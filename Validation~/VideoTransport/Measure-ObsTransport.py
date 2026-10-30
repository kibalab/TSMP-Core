#!/usr/bin/env python3
"""Measure owned portable OBS with a local recording and timed media playhead.

Windows/Python + websocket-client. stream-ready.json signals API preparation
and active recording, not verified pixel freeze;
create results/go.signal to restart the source. This script never reads the
user's OBS configuration or accepts an external publishing destination.
Only --record-only is currently enabled; relay measurement is deferred.

Protocol: https://github.com/obsproject/obs-websocket/blob/5.5.4/docs/generated/protocol.md
Media settings: obsproject/obs-studio, tag 31.0.1, plugins/obs-ffmpeg/obs-ffmpeg-source.c
OBS31 uses separate global.ini/user.ini; see UI/obs-app.cpp in that same tag.
"""

import argparse
import base64
import csv
import ctypes
from datetime import datetime, timezone
import hashlib
import ipaddress
import json
import math
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import time
from urllib.parse import urlsplit
import uuid


PROFILE = "TSMP Measurement"
PROFILE_DIR = "TSMP-Measurement"
SCENE = "TSMP Measurement Scene"
INPUT = "TSMP Measurement Source"
WS_PORT = 4456


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def write_json(path, data):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(data, indent=2), encoding="utf-8")
    temporary.replace(path)


def validate_destination(server, stream_key):
    parsed = urlsplit(server)
    try:
        loopback = ipaddress.ip_address(parsed.hostname or "").is_loopback
        port = parsed.port
    except ValueError:
        loopback, port = False, None
    if (parsed.scheme != "rtmp" or not loopback or not port or parsed.username
            or parsed.password or parsed.query or parsed.fragment
            or not re.fullmatch(r"/[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*", parsed.path)):
        raise ValueError("Server must be a literal loopback RTMP URL with a port and plain local path")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", stream_key):
        raise ValueError("Use one plain local stream-key component")
    return server.rstrip("/")


def hidden_startup():
    info = subprocess.STARTUPINFO()
    info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    info.wShowWindow = subprocess.SW_HIDE
    return info


def assert_no_obs():
    response = subprocess.run(["tasklist.exe", "/FO", "CSV", "/NH"],
                              capture_output=True, text=True, errors="replace",
                              check=True, startupinfo=hidden_startup(),
                              creationflags=subprocess.CREATE_NO_WINDOW)
    for row in csv.reader(response.stdout.splitlines()):
        if row and row[0].lower() in ("obs64.exe", "obs32.exe", "obs.exe"):
            raise RuntimeError("An existing OBS process is running; leave it untouched and stop this measurement")


def prepare_config(args, secret, run_id):
    root = args.obs_root
    exe = root / "bin" / "64bit" / "obs64.exe"
    if not exe.is_file() or not (root / "portable_mode.txt").is_file():
        raise ValueError("Use the prepared private portable OBS root with bin/64bit/obs64.exe and portable_mode.txt")
    for parent in (Path(os.environ.get("APPDATA", "C:/invalid")) / "obs-studio",
                   Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "obs-studio"):
        if root == parent.resolve():
            raise ValueError("The private portable root must differ from the installed/user OBS directories")
    config = root / "config"
    # Archive only this harness's previous config, preserving its raw logs.
    # A junction or unknown config is never replaced or traversed for writes.
    if config.resolve() != config or config.is_symlink():
        raise ValueError("Private config must be an ordinary directory inside the portable root")
    if config.exists():
        if not (config / "tsmp-owned-measurement.json").is_file():
            raise ValueError("Refusing to replace an unowned portable config")
        history = root / "history"
        if history.resolve() != history:
            raise ValueError("Private history must stay inside the portable root")
        history.mkdir(exist_ok=True)
        config.rename(history / (run_id + "-previous-config"))
    private = config / "obs-studio"
    profile = private / "basic" / "profiles" / PROFILE_DIR
    websocket_config = private / "plugin_config" / "obs-websocket"
    profile.mkdir(parents=True)
    websocket_config.mkdir(parents=True)
    write_json(config / "tsmp-owned-measurement.json", {"owner": "Measure-ObsTransport.py", "runId": run_id})
    # LastVersion is OBS's major<<24 | minor<<16 | patch encoding (31.0.1).
    (private / "global.ini").write_text("[General]\nLastVersion=520093697\nPre31Migrated=true\nEnableAutoUpdates=false\n", encoding="utf-8")
    (private / "user.ini").write_text(
        f"[General]\nFirstRun=true\n\n[Basic]\nProfile={PROFILE}\nProfileDir={PROFILE_DIR}\n"
        "SceneCollection=TSMP Measurement\nSceneCollectionFile=TSMP-Measurement\nConfigOnNewProfile=false\n"
        "\n[BasicWindow]\nWarnBeforeStartingStream=false\nWarnBeforeStoppingStream=false\n"
        "RecordWhenStreaming=false\nKeepRecordingWhenStreamStops=false\nSysTrayEnabled=true\n", encoding="utf-8")
    (profile / "basic.ini").write_text(
        f"[General]\nName={PROFILE}\n\n[Output]\nMode=Advanced\nReconnect=false\n"
        "FilenameFormatting=TSMP-%CCYY-%MM-%DD-%hh-%mm-%ss\nOverwriteIfExists=false\n"
        "\n[AdvOut]\nEncoder=obs_nvenc_h264_tex\nApplyServiceSettings=false\nUseRescale=false\n"
        "TrackIndex=1\nVodTrackEnabled=false\nRecEncoder=obs_nvenc_h264_tex\nRecType=Standard\n"
        f"RecFormat2=mkv\nRecFilePath={args.results / 'recording'}\nRecTracks=1\nRecRescale=false\n"
        "RecRB=false\n\n[Video]\n"
        f"BaseCX={args.width}\nBaseCY={args.height}\nOutputCX={args.width}\nOutputCY={args.height}\n"
        "FPSType=0\nFPSCommon=30\nFPSInt=30\nFPSNum=30\nFPSDen=1\n"
        f"ScaleType=bilinear\nColorFormat=NV12\nColorSpace=709\nColorRange={args.color_range}\n"
        "\n[Audio]\nSampleRate=48000\nChannelSetup=Stereo\n", encoding="utf-8")
    write_json(profile / "streamEncoder.json", {"rate_control": "CBR", "bitrate": args.bitrate, "keyint_sec": 1, "bf": 0})
    write_json(profile / "recordEncoder.json", {"rate_control": "CBR", "bitrate": args.bitrate, "keyint_sec": 1, "bf": 0})
    # The random authentication secret is written only here and sent in the
    # authentication handshake. It is never included in argv, telemetry or stdout.
    write_json(websocket_config / "config.json", {
        "first_load": False, "server_enabled": True, "server_port": WS_PORT,
        "alerts_enabled": False, "auth_required": True, "server_password": secret})
    return exe, private


class ObsRequestError(RuntimeError):
    def __init__(self, kind, status):
        self.code = status.get("code") if isinstance(status, dict) else None
        super().__init__(f"OBS request {kind} failed: {status}")


class ObsClient:
    def __init__(self, password, process, journal):
        import websocket
        self.connection = None
        self.journal = journal
        self.sequence = 0
        deadline = time.perf_counter() + 60
        while time.perf_counter() < deadline:
            if process.poll() is not None:
                raise RuntimeError("Owned OBS exited before the authenticated WebSocket became ready")
            connection = None
            try:
                connection = websocket.create_connection(
                    f"ws://127.0.0.1:{WS_PORT}", timeout=3,
                    http_no_proxy=["127.0.0.1", "localhost"])
                hello = json.loads(connection.recv())
                if hello.get("op") != 0 or not hello.get("d", {}).get("authentication"):
                    connection.close()
                    raise RuntimeError("Owned WebSocket must announce authentication")
                auth = hello["d"]["authentication"]
                key = base64.b64encode(hashlib.sha256((password + auth["salt"]).encode()).digest()).decode()
                answer = base64.b64encode(hashlib.sha256((key + auth["challenge"]).encode()).digest()).decode()
                connection.send(json.dumps({"op": 1, "d": {"rpcVersion": 1, "eventSubscriptions": 0, "authentication": answer}}))
                identified = json.loads(connection.recv())
                if identified.get("op") != 2:
                    connection.close()
                    raise RuntimeError("Owned WebSocket authentication was not acknowledged")
                self.connection = connection
                return
            except (OSError, websocket.WebSocketException):
                if connection:
                    try:
                        connection.close()
                    except Exception:
                        pass
                time.sleep(0.25)
        raise TimeoutError("Owned OBS WebSocket startup timed out")

    def request(self, kind, fields=None):
        self.sequence += 1
        identifier = str(self.sequence)
        before = time.perf_counter()
        started_utc = utc_now()
        self.journal.write(json.dumps({"event": "request-begin", "requestType": kind,
                                      "requestId": identifier, "perfCounterBefore": before,
                                      "utcBefore": started_utc}) + "\n")
        self.journal.flush()
        self.connection.send(json.dumps({"op": 6, "d": {
            "requestType": kind, "requestId": identifier, "requestData": fields or {}}}))
        while True:
            response = json.loads(self.connection.recv())
            if response.get("op") == 7 and response.get("d", {}).get("requestId") == identifier:
                data = response["d"]
                break
        after = time.perf_counter()
        record = {"event": "request-end", "requestType": kind, "requestId": identifier,
                  "perfCounterBefore": before, "perfCounterAfter": after,
                  "utcBefore": started_utc, "utcAfter": utc_now(),
                  "requestStatus": data.get("requestStatus"), "responseData": data.get("responseData", {})}
        self.journal.write(json.dumps(record) + "\n")
        self.journal.flush()
        if not data.get("requestStatus", {}).get("result"):
            raise ObsRequestError(kind, data.get("requestStatus"))
        return data.get("responseData", {}), record

    def action(self, name):
        return self.request("TriggerMediaInputAction", {
            "inputName": INPUT, "mediaAction": "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_" + name})

    def close(self):
        if self.connection:
            self.connection.close()


def wait_for_version(client, timeout=60, retry_interval=0.2):
    # Authentication can finish while OBS is still loading its first scene.
    # Only frontend NotReady (207) on this initial probe is transient here.
    deadline = time.perf_counter() + timeout
    while True:
        try:
            return client.request("GetVersion")
        except ObsRequestError as error:
            remaining = deadline - time.perf_counter()
            if error.code != 207 or remaining <= 0:
                raise
            time.sleep(min(retry_interval, remaining))
            if time.perf_counter() >= deadline:
                raise


def wait_for_source_transform(client, item_id, width, height, timeout=15, retry_interval=0.1):
    deadline = time.perf_counter() + timeout
    while True:
        response, _ = client.request("GetSceneItemTransform", {"sceneName": SCENE, "sceneItemId": item_id})
        transform = response["sceneItemTransform"]
        actual = (transform.get("sourceWidth"), transform.get("sourceHeight"))
        if actual == (width, height):
            return transform
        # Zero is an asynchronous media-size readiness state. A known wrong
        # size is a real no-scaling violation and must never be retried away.
        if any(value not in (0, expected) for value, expected in zip(actual, (width, height))):
            raise RuntimeError("Source media dimensions differ from OBS canvas; scaling is not allowed")
        remaining = deadline - time.perf_counter()
        if remaining <= 0:
            raise TimeoutError("Source media dimensions remained zero after 15 seconds")
        time.sleep(min(retry_interval, remaining))
        if time.perf_counter() >= deadline:
            raise TimeoutError("Source media dimensions remained zero after 15 seconds")


def close_owned(process):
    if process.poll() is not None:
        return False
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_ssize_t)
    user32.EnumWindows.argtypes = [callback_type, ctypes.c_ssize_t]
    user32.PostMessageW.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_size_t, ctypes.c_ssize_t]
    user32.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]

    @callback_type
    def close_window(window, _):
        owner = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(window, ctypes.byref(owner))
        if owner.value == process.pid:
            user32.PostMessageW(window, 0x0010, 0, 0)  # WM_CLOSE, owned PID only.
        return True

    user32.EnumWindows(close_window, 0)
    try:
        process.wait(timeout=20)
        return False
    except subprocess.TimeoutExpired:
        process.kill()  # The Popen process handle, never a name-based global kill.
        process.wait(timeout=10)
        return True


def sample(client, phase, samples, record_only):
    result = {"phase": phase, "samplePerfCounterBefore": time.perf_counter(), "utc": utc_now()}
    output_request = ("GetRecordStatus", "record") if record_only else ("GetStreamStatus", "stream")
    for request, key in [("GetMediaInputStatus", "media"), ("GetStats", "stats"), output_request]:
        data, timing = client.request(request, {"inputName": INPUT} if key == "media" else {})
        result[key] = data
        result[key + "PerfCounterBefore"] = timing["perfCounterBefore"]
        result[key + "PerfCounterAfter"] = timing["perfCounterAfter"]
    result["samplePerfCounterAfter"] = time.perf_counter()
    samples.write(json.dumps(result) + "\n")
    samples.flush()
    status = result[output_request[1]]
    if not status.get("outputActive") or status.get("outputReconnecting") or status.get("outputPaused"):
        raise RuntimeError("Owned output stopped, paused or reconnected during the measurement")
    if result["media"].get("mediaState") == "OBS_MEDIA_STATE_ERROR":
        raise RuntimeError("OBS media source reported an error")
    return result


def run(args):
    if os.name != "nt":
        raise RuntimeError("This harness requires Windows portable OBS")
    args.server = validate_destination(args.server, args.stream_key)
    if not args.record_only:
        raise RuntimeError("Only --record-only is enabled; network relay measurement is deferred")
    for name in ("width", "height"):
        if getattr(args, name) < 16 or getattr(args, name) > 16384 or getattr(args, name) % 2:
            raise ValueError("Use positive even video dimensions between 16 and 16384")
    for name in ("duration", "drain"):
        if not math.isfinite(getattr(args, name)) or not 0 <= getattr(args, name) <= 3600:
            raise ValueError("Duration and drain must be finite seconds in 0..3600")
    if args.duration <= 0 or not 1 <= args.bitrate <= 1000000:
        raise ValueError("Duration and bitrate must be positive")
    args.obs_root = args.obs_root.resolve()
    args.source_video = args.source_video.resolve(strict=True)
    args.results = args.results.resolve()
    args.results.mkdir(parents=True, exist_ok=True)
    if any(args.results.iterdir()):
        raise ValueError("Choose a new empty results directory; prior evidence is preserved")
    assert_no_obs()
    with socket.socket() as port_check:
        port_check.bind(("127.0.0.1", WS_PORT))
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]
    secret = secrets.token_urlsafe(32)
    exe, private = prepare_config(args, secret, run_id)
    result = {"runId": run_id, "status": "INCOMPLETE", "startedUtc": utc_now(),
              "outputMode": "local-recording" if args.record_only else "loopback-stream",
              "clock": "Python time.perf_counter (Windows QPC); mediaCursor is milliseconds, not a synchronized capture timestamp",
              "sourceVideo": str(args.source_video), "sourceSha256": hashlib.sha256(args.source_video.read_bytes()).hexdigest(),
              "obsExecutable": str(exe), "privateConfigAndRawLogs": str(private),
              "encoderSettings": {"encoder": "obs_nvenc_h264_tex",
                            "rateControl": "CBR", "bitrateKbps": args.bitrate, "keyframeSeconds": 1, "bframes": 0,
                            "width": args.width, "height": args.height, "fps": 30,
                            "colorFormat": "NV12", "colorSpace": "709", "colorRange": args.color_range,
                            "remainingEncoderOptions": "OBS 31.0.1 defaults; no preset/tuning value is inferred"},
              "requestedPlaybackSeconds": args.duration, "requestedDrainSeconds": args.drain,
              "limitations": "Owned local OBS recording only; no RTMP/RTSP/HLS relay, VRCDN, receiver payload accuracy, VRChat, or end-to-end latency is certified."}
    write_json(args.results / "result.json", result)
    process, client, started = None, None, False
    journal = (args.results / "requests.jsonl").open("w", encoding="utf-8", buffering=1)
    samples = (args.results / "samples.jsonl").open("w", encoding="utf-8", buffering=1)
    output = (private / "process-output.log").open("wb")
    try:
        command = [str(exe), "--portable", "--minimize-to-tray", "--disable-updater", "--profile", PROFILE]
        result["launchPerfCounterBefore"] = time.perf_counter()
        process = subprocess.Popen(command, cwd=exe.parent, stdout=output, stderr=subprocess.STDOUT,
                                   startupinfo=hidden_startup(), creationflags=subprocess.CREATE_NO_WINDOW)
        result["launchPerfCounterAfter"] = time.perf_counter()
        result["ownedPid"] = process.pid
        client = ObsClient(secret, process, journal)
        version, _ = wait_for_version(client)
        result["obsVersion"] = version
        if version.get("obsVersion") != "31.0.1":
            raise RuntimeError("This measurement profile requires installed OBS 31.0.1")
        required = {"GetMediaInputStatus", "TriggerMediaInputAction", "SetMediaInputCursor", "GetStats",
                    "GetInputDefaultSettings", "GetInputList", "CreateInput", "CreateScene", "SetCurrentProgramScene", "SetInputMute",
                    "SetSceneItemTransform", "GetSceneItemTransform", "GetVideoSettings"}
        required.update({"SetRecordDirectory", "GetRecordStatus", "StartRecord", "StopRecord"} if args.record_only
                        else {"GetStreamStatus", "SetStreamServiceSettings", "StartStream", "StopStream"})
        if not required.issubset(set(version.get("availableRequests", []))):
            raise RuntimeError("OBS lacks required measurement requests")
        video, _ = client.request("GetVideoSettings")
        if any(video.get(key) != value for key, value in {
                "baseWidth": args.width, "baseHeight": args.height, "outputWidth": args.width,
                "outputHeight": args.height, "fpsNumerator": 30, "fpsDenominator": 1}.items()):
            raise RuntimeError("Effective OBS video settings differ from requested no-scale profile")
        result["videoSettings"] = video
        defaults, _ = client.request("GetInputDefaultSettings", {"inputKind": "ffmpeg_source"})
        result["mediaInputDefaults"] = defaults
        client.request("CreateScene", {"sceneName": SCENE})
        client.request("SetCurrentProgramScene", {"sceneName": SCENE})
        settings = {"is_local_file": True, "local_file": str(args.source_video), "looping": False,
                    "restart_on_activate": False, "close_when_inactive": False, "clear_on_media_end": False,
                    "hw_decode": False, "speed_percent": 100, "log_changes": True}
        created, _ = client.request("CreateInput", {"sceneName": SCENE, "inputName": INPUT,
                                                    "inputKind": "ffmpeg_source", "inputSettings": settings,
                                                    "sceneItemEnabled": True})
        client.request("SetInputMute", {"inputName": INPUT, "inputMuted": True})
        inputs, _ = client.request("GetInputList")
        if [item.get("inputName") for item in inputs.get("inputs", [])] != [INPUT]:
            raise RuntimeError("Owned profile contains unexpected inputs; do not record ambient sources")
        item_id = created["sceneItemId"]
        client.request("SetSceneItemTransform", {"sceneName": SCENE, "sceneItemId": item_id,
                       "sceneItemTransform": {"positionX": 0, "positionY": 0, "scaleX": 1, "scaleY": 1,
                                              "rotation": 0, "alignment": 5, "boundsType": "OBS_BOUNDS_NONE",
                                              "cropLeft": 0, "cropRight": 0, "cropTop": 0, "cropBottom": 0}})
        deadline = time.perf_counter() + 20
        while time.perf_counter() < deadline:
            media, _ = client.request("GetMediaInputStatus", {"inputName": INPUT})
            if media.get("mediaState") == "OBS_MEDIA_STATE_ERROR":
                raise RuntimeError("Source media could not be opened")
            if media.get("mediaDuration", 0) > 0 and media.get("mediaState") in ("OBS_MEDIA_STATE_PLAYING", "OBS_MEDIA_STATE_PAUSED"):
                break
            time.sleep(0.1)
        else:
            raise TimeoutError("Media did not reach a playable state")
        client.action("PAUSE")
        client.request("SetMediaInputCursor", {"inputName": INPUT, "mediaCursor": 0})
        deadline = time.perf_counter() + 10
        while time.perf_counter() < deadline:
            media, _ = client.request("GetMediaInputStatus", {"inputName": INPUT})
            if media.get("mediaState") == "OBS_MEDIA_STATE_PAUSED" and 0 <= media.get("mediaCursor", -1) <= 100:
                break
            time.sleep(0.1)
        else:
            raise TimeoutError("Could not hold the first media frame before streaming")
        if media.get("mediaDuration", 0) < args.duration * 1000 - 100:
            raise RuntimeError("Source media duration is shorter than the requested measurement")
        transform = wait_for_source_transform(client, item_id, args.width, args.height)
        result["sourceTransform"] = transform
        if args.record_only:
            (args.results / "recording").mkdir()
            client.request("SetRecordDirectory", {"recordDirectory": str(args.results / "recording")})
        else:
            client.request("SetStreamServiceSettings", {"streamServiceType": "rtmp_custom", "streamServiceSettings": {
                "server": args.server, "key": args.stream_key, "use_auth": False}})
        output_status_request = "GetRecordStatus" if args.record_only else "GetStreamStatus"
        stop_request = "StopRecord" if args.record_only else "StopStream"
        started = True
        client.request("StartRecord" if args.record_only else "StartStream")
        deadline = time.perf_counter() + 20
        while time.perf_counter() < deadline:
            stream, _ = client.request(output_status_request)
            if stream.get("outputActive") and not stream.get("outputReconnecting"):
                break
            time.sleep(0.1)
        else:
            raise TimeoutError("Owned output did not become active")
        result["readyPerfCounter"] = time.perf_counter()
        write_json(args.results / "stream-ready.json", {
            "runId": run_id, "ownedPid": process.pid, "perfCounter": result["readyPerfCounter"], "utc": utc_now(),
            "outputMode": result["outputMode"], "outputActive": True, "mediaState": media,
            "goSignal": str(args.results / "go.signal"), "waitLimitSeconds": 90})
        write_json(args.results / "result.json", result)
        deadline = time.perf_counter() + 90
        while not (args.results / "go.signal").is_file():
            if time.perf_counter() >= deadline:
                raise TimeoutError("No go.signal arrived within 90 seconds")
            sample(client, "waiting-for-go", samples, args.record_only)
            time.sleep(0.5)
        result["goObservedPerfCounter"] = time.perf_counter()
        _, restart = client.action("RESTART")
        result["sourceRestart"] = restart
        write_json(args.results / "source-restarted.json", restart)
        stop_at = restart["perfCounterAfter"] + args.duration
        playback = []
        while time.perf_counter() < stop_at:
            current = sample(client, "playback", samples, args.record_only)
            playback.append(current)
            if process.poll() is not None:
                raise RuntimeError("Owned OBS exited during playback")
            time.sleep(min(0.25, max(0, stop_at - time.perf_counter())))
        if not playback or max(row["media"].get("mediaCursor", 0) for row in playback) < min(1000, args.duration * 500):
            raise RuntimeError("Media playhead did not advance after restart")
        last_media, _ = client.request("GetMediaInputStatus", {"inputName": INPUT})
        if last_media.get("mediaState") == "OBS_MEDIA_STATE_PLAYING":
            client.action("PAUSE")
        result["drainPerfCounterBefore"] = time.perf_counter()
        drain_until = result["drainPerfCounterBefore"] + args.drain
        while time.perf_counter() < drain_until:
            sample(client, "drain", samples, args.record_only)
            time.sleep(min(0.25, max(0, drain_until - time.perf_counter())))
        result["finalSample"] = sample(client, "before-stop", samples, args.record_only)
        stopped, _ = client.request(stop_request)
        if args.record_only:
            result["recordingPath"] = stopped.get("outputPath")
        deadline = time.perf_counter() + 20
        while time.perf_counter() < deadline:
            stream, _ = client.request(output_status_request)
            if not stream.get("outputActive"):
                started = False
                break
            time.sleep(0.1)
        else:
            raise TimeoutError("Owned output did not stop")
        if args.record_only:
            recording = Path(result.get("recordingPath") or "").resolve()
            if recording.parent != (args.results / "recording") or not recording.is_file() or recording.stat().st_size == 0:
                raise RuntimeError("Owned MKV recording was not saved in the requested results directory")
            result["recordingBytes"] = recording.stat().st_size
        result["status"] = "PASS"
    except BaseException as error:
        result["status"] = "FAIL"
        result["error"] = (type(error).__name__ + ": " + str(error)).replace(secret, "[redacted]")
    finally:
        if client and started:
            try:
                stopped, _ = client.request("StopRecord" if args.record_only else "StopStream")
                if args.record_only:
                    result["recordingPath"] = stopped.get("outputPath")
            except Exception as error:
                result["cleanupError"] = (type(error).__name__ + ": " + str(error)).replace(secret, "[redacted]")
        if client:
            try:
                client.close()
            except Exception as error:
                result["websocketCloseError"] = (type(error).__name__ + ": " + str(error)).replace(secret, "[redacted]")
                result["status"] = "FAIL"
        if process:
            try:
                result["forcedOwnedProcessTermination"] = close_owned(process)
                result["ownedExitCode"] = process.returncode
                if result["forcedOwnedProcessTermination"] or process.returncode != 0:
                    result["status"] = "FAIL"
            except Exception as error:
                result["processCloseError"] = (type(error).__name__ + ": " + str(error)).replace(secret, "[redacted]")
                result["status"] = "FAIL"
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=10)
                    result["forcedOwnedProcessTermination"] = True
                    result["ownedExitCode"] = process.returncode
        result["finishedUtc"] = utc_now()
        result["finishedPerfCounter"] = time.perf_counter()
        journal.close()
        samples.close()
        output.close()
        write_json(args.results / "result.json", result)
    print(json.dumps({"status": result["status"], "results": str(args.results), "error": result.get("error")}, indent=2))
    return 0 if result["status"] == "PASS" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--obs-root", required=True, type=Path)
    parser.add_argument("--record-only", action="store_true", help="Required: record locally without starting any relay publisher")
    parser.add_argument("--source-video", required=True, type=Path)
    parser.add_argument("--results", required=True, type=Path)
    parser.add_argument("--server", default="rtmp://127.0.0.1:19350/live")
    parser.add_argument("--stream-key", default="tsmp", help="Non-secret local test path component")
    parser.add_argument("--width", required=True, type=int)
    parser.add_argument("--height", required=True, type=int)
    parser.add_argument("--bitrate", default=10000, type=int, help="CBR kbps")
    parser.add_argument("--duration", default=20, type=float)
    parser.add_argument("--drain", default=8, type=float)
    parser.add_argument("--color-range", default="Full", choices=["Full", "Partial"])
    args = parser.parse_args()
    try:
        return run(args)
    except Exception as error:
        print(type(error).__name__ + ": " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
