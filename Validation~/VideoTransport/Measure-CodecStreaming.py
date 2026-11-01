#!/usr/bin/env python3
"""Encode finite native-resolution codec fixtures; GPU verdicts are a later step.

Requires NumPy and Pillow. Sources must have independent pre-raster header and
payload bins. No color-symbol CPU decoder, Unity, OBS or network service runs.
MEASURED means process completion, never byte-integrity success.
"""

import argparse
from collections import Counter
import csv
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import traceback
import zlib

from PIL import Image


HERE = Path(__file__).resolve().parent
HELPER_PATH = HERE / "Measure-Streaming.py"
SPEC = importlib.util.spec_from_file_location("tsmp_stream_measurement_helpers", HELPER_PATH)
HELPERS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HELPERS)
FPS = 30
write_json = HELPERS.write_json
sha256 = HELPERS.sha256
utc_now = HELPERS.utc_now
run = HELPERS.run


def require(condition, message):
    if not condition:
        raise ValueError(message)


def u16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def inspect_fixture(fixture):
    """Validate raw references without recovering bytes from measured pixels."""
    directory = fixture / "input"
    require(directory.is_dir(), f"Fixture input directory missing: {directory}")
    paths = sorted(directory.glob("*.png"))
    require(paths, f"Fixture contains no PNGs: {directory}")
    rows, dimensions, descriptor, frame_ids = [], None, None, set()
    for index, path in enumerate(paths, 1):
        require(path.name == f"frame-{index:06d}.png", "Require a contiguous frame-000001..N.png sequence")
        header_path = fixture / "header" / f"frame-{index:06d}.bin"
        payload_path = fixture / "payload" / f"frame-{index:06d}.bin"
        header, payload = header_path.read_bytes(), payload_path.read_bytes()
        require(len(header) == 56, f"Expected 56-byte independent header: {header_path}")
        require(u32(header, 0) == 0x504D5354 and u16(header, 8) == 56, f"Invalid header prefix: {header_path}")
        require(header[4] == 0 and header[5] >= 2 and u16(header, 10) & 1,
                f"Protected protocol >=0.2 required: {header_path}")
        require(zlib.crc32(header[:52]) == u32(header, 52), f"Header CRC mismatch: {header_path}")
        require(len(payload) == u16(header, 42) and zlib.crc32(payload) == u32(header, 44),
                f"Independent payload length/CRC mismatch: {payload_path}")
        require(u16(header, 12) == 8 and header[50] == 4 and u16(header, 18) == 0,
                f"Expected native block8/sample4/layout0: {header_path}")
        require(u16(header, 40) == 0x0100 and header[34] <= 5,
                f"Expected NetworkFrame and valid codec options: {header_path}")
        frame_id = u32(header, 24)
        require(frame_id not in frame_ids, f"Repeated fixture source ID: {frame_id}")
        frame_ids.add(frame_id)
        require(len(payload) >= 25 and payload[:2] == b"\x01\x00" and u16(payload, 2) == 1
                and u32(payload, 4) == frame_id and u16(payload, 12) == (frame_id & 0xffff),
                f"NetworkFrame sequence/reference mismatch: {payload_path}")
        with Image.open(path) as image:
            image.verify()
        with Image.open(path) as image:
            size = image.size
        require(size == (u16(header, 14) * 8, u16(header, 16) * 8), f"PNG/header geometry mismatch: {path}")
        current = (u16(header, 32), header[7], header[34], header[35:40].hex())
        if dimensions is None:
            dimensions, descriptor = size, current
        require(size == dimensions and current == descriptor, "Fixture codec/options/dimensions must be constant")
        rows.append(dict(sourceIndex=index, sourceFile=path.name, sourcePath=str(path),
                         sha256=sha256(path), frameIndex=frame_id, payloadBytes=len(payload),
                         timestampMs=u32(header, 28),
                         headerFile=header_path.name, headerSha256=sha256(header_path),
                         payloadFile=payload_path.name, payloadSha256=sha256(payload_path),
                         width=size[0], height=size[1]))
    expected_bins = {Path(row["sourceFile"]).with_suffix(".bin").name for row in rows}
    for name in ("header", "payload"):
        require({path.name for path in (fixture / name).glob("*.bin")} == expected_bins,
                f"Unexpected or missing independent {name} bins")
    provenance_csv = fixture / "source-frames.csv"
    if provenance_csv.exists():
        with provenance_csv.open("r", encoding="utf-8-sig", newline="") as handle:
            reader = csv.DictReader(handle)
            required = {"frame", "timestampMs", "bytes", "pngSHA256", "payloadSHA256", "headerSHA256"}
            require(required <= set(reader.fieldnames or []), f"Fixture provenance columns missing: {provenance_csv}")
            recorded = {}
            for entry in reader:
                frame = int(entry["frame"])
                require(frame not in recorded, f"Duplicate fixture provenance frame ID: {frame}")
                recorded[frame] = entry
        require(set(recorded) == frame_ids, f"Fixture provenance frame IDs/count differ: {provenance_csv}")
        for source in rows:
            entry = recorded[source["frameIndex"]]
            require(int(entry["timestampMs"]) == source["timestampMs"] and int(entry["bytes"]) == source["payloadBytes"],
                    f"Fixture provenance timestamp/length mismatch: {source['sourceFile']}")
            for field, actual in (("pngSHA256", source["sha256"]), ("payloadSHA256", source["payloadSha256"]),
                                  ("headerSHA256", source["headerSha256"])):
                require(entry[field].strip().lower() == actual.lower(),
                        f"Fixture provenance {field} mismatch: {source['sourceFile']}")
    return rows, dimensions, dict(codecId=descriptor[0], symbolMode=descriptor[1],
                                  codecOptionLength=descriptor[2], codecOptionsHex=descriptor[3])


def package_hashes(paths):
    hashes = {str(Path(__file__).resolve()): sha256(Path(__file__)), str(HELPER_PATH): sha256(HELPER_PATH)}
    # Importing the helpers loads this module, but no model function is called.
    model_path = Path(HELPERS.MODEL.__file__).resolve()
    hashes[str(model_path)] = sha256(model_path)
    allowed = {".cs", ".shader", ".cginc", ".prefab", ".mat"}
    for root in paths:
        if root is None:
            continue
        root = root.resolve()
        require(root.is_dir(), f"Package provenance directory missing: {root}")
        selected = sorted(path for path in root.rglob("*")
                          if path.is_file() and (path.suffix.lower() in allowed or path.name == "package.json"))
        require(selected, f"No package provenance files found: {root}")
        hashes.update((str(path), sha256(path)) for path in selected)
    return hashes


def profiles_for(dimensions, include_crf, selected):
    base = HELPERS.profile_matrix(dimensions)
    width, height = dimensions
    ffv1 = dict(name="ffv1-rgb", encoder="ffv1", colorRange="full", pixelFormat="bgr0",
                width=width, height=height, fps=FPS, kind="rgb-lossless-control")
    crf = [dict(name=f"x264-veryfast-crf{value}-709limited", encoder="libx264", bitrate=None,
                colorRange="limited", gop=60 if height == 720 else 30, width=width,
                height=height, fps=FPS, bFrames=0, pixelFormat="yuv420p", preset="veryfast",
                crf=value, kind="crf") for value in (18, 28)]
    candidates = [ffv1] + base + crf
    if selected:
        names = selected.split(",")
        require(len(names) == len(set(names)), "Duplicate profile selection")
        unknown = set(names) - {profile["name"] for profile in candidates}
        require(not unknown, f"Unknown profiles: {sorted(unknown)}; available: {[p['name'] for p in candidates]}")
        return [profile for profile in candidates if profile["name"] in names]
    return [ffv1] + base + (crf if include_crf else [])


def encode_command(ffmpeg, expanded, video, count, profile):
    if profile["encoder"] == "ffv1":
        return [ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-framerate", str(FPS),
                "-start_number", "1", "-i", str(expanded / "frame-%06d.png"), "-frames:v", str(count),
                "-an", "-c:v", "ffv1", "-pix_fmt", "bgr0", "-r", str(FPS), "-fps_mode", "cfr", str(video)]
    command = HELPERS.encode_command(ffmpeg, expanded, video, count, profile)
    if "crf" in profile:
        qp = command.index("-qp")
        command[qp:qp + 2] = ["-crf", str(profile["crf"])]
    return command


def transport_profile(name, frames, fixture, codec_prefab, mapping):
    return dict(name=name, codecPrefabPath=codec_prefab, framesDirectory=str(frames),
                expectedDirectory=str(fixture / "input"), expectedPayloadDirectory=str(fixture / "payload"),
                frameMappings=[dict(file=row["file"], expectedFile=row["expectedFile"],
                                    payloadFile=row["payloadFile"], frameIndex=row["frameIndex"],
                                    frameIndexSpecified=True) for row in mapping])


def write_csv(path, rows):
    require(rows, f"Cannot write empty CSV: {path}")
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture", type=Path, required=True, help="input/, header/, payload/ fixture root")
    parser.add_argument("--codec-prefab-path", required=True, help="Unity Packages/...prefab used by later GPU decoding")
    parser.add_argument("--results", type=Path, required=True, help="New or empty evidence directory")
    parser.add_argument("--profiles", help="Comma-separated profile names; PNG control is always included")
    parser.add_argument("--include-crf", action="store_true", help="Add CRF18/28 to the default CBR/conversion matrix")
    parser.add_argument("--ffmpeg", default=r"C:\Windows\ffmpeg.exe")
    parser.add_argument("--ffprobe", help="Defaults to ffprobe alongside ffmpeg")
    parser.add_argument("--core-package", type=Path)
    parser.add_argument("--codec-package", type=Path)
    parser.add_argument("--timeout", type=float, default=300)
    parser.add_argument("--plan-only", action="store_true")
    args = parser.parse_args()
    require(math.isfinite(args.timeout) and args.timeout > 0, "--timeout must be finite and positive")
    prefab = args.codec_prefab_path.replace("\\", "/")
    require(prefab.startswith("Packages/") and prefab.endswith(".prefab") and ".." not in prefab.split("/"),
            "--codec-prefab-path must be a package-relative prefab asset path")
    fixture, destination = args.fixture.resolve(), args.results.resolve()
    require(not destination.exists() or (destination.is_dir() and not any(destination.iterdir())),
            "Choose a new, empty results directory; existing evidence is preserved")
    destination.mkdir(parents=True, exist_ok=True)
    summary = dict(status="PREPARING", startedUtc=utc_now(), fixture=str(fixture), results=str(destination),
                   codecPrefabPath=prefab, scope="Finite offline FFmpeg file encode/decode; subsequent GPU validation required",
                   profiles={})
    try:
        require(zlib.crc32(b"123456789") == 0xCBF43926, "CRC32 standard-vector check failed")
        sources, dimensions, descriptor = inspect_fixture(fixture)
        profiles = profiles_for(dimensions, args.include_crf, args.profiles)
        count = len(sources)
        source_hashes = package_hashes([args.core_package, args.codec_package])
        for name in ("source-frames.csv", "fixture.json"):
            path = fixture / name
            if path.exists():
                require(path.is_file(), f"Fixture provenance path is not a file: {path}")
                source_hashes[str(path)] = sha256(path)
        mapping = [dict(file=f"frame-{index:06d}.png", expectedFile=source["sourceFile"],
                        payloadFile=source["payloadFile"], frameIndex=source["frameIndex"],
                        expectedPts=index - 1, expectedTimeBase="1/30", expectedPtsSeconds=(index - 1) / FPS)
                   for index, source in enumerate(sources, 1)]
        expanded = destination / "expanded-input"
        ffprobe = args.ffprobe or str(Path(args.ffmpeg).with_name("ffprobe.exe" if os.name == "nt" else "ffprobe"))
        summary.update(dimensions=list(dimensions), fps=FPS, frameCount=count, requestedSeconds=count / FPS,
                       sourceFrameCount=count, uniqueSourceIds=count, repeatedSourceIds=False,
                       codec=descriptor, sources=sources,
                       sourceHashes=source_hashes,
                       cpuModel="NONE: no color-symbol model or Luma4 symbol classifier is run",
                       expectedPayloads=dict(origin="supplied-independent-fixture-raw-bins",
                                             directory=str(fixture / "payload")),
                       limitations=["MEASURED means process completion; byte integrity requires the actual GPU decoder.",
                                    "Raw header/payload CRC checks do not prove raster correctness; PNG GPU controls test that separately.",
                                    "This finite file experiment does not test OBS, Spout, network delivery, VRChat or live latency.",
                                    "Nominal CBR and two-second VBV do not impose a strict instantaneous bitrate cap.",
                                    "NVENC p4/lookahead-0 is the explicit FFmpeg profile, not an OBS preset claim.",
                                    "QP0 YUV420 controls include color/range conversion and are not RGB-lossless controls."],
                       bufferSeconds=2, encodeConversion=HELPERS.ENCODE_FILTER,
                       decodeConversion=HELPERS.DECODE_FILTER)
        write_json(destination / "expected-frame-mapping.json", mapping)
        plans = []
        for profile in profiles:
            directory = destination / profile["name"]
            video = directory / "encoded.mkv"
            decode_filter = "format=rgb24" if profile["encoder"] == "ffv1" else HELPERS.DECODE_FILTER.format(range=profile["colorRange"])
            plans.append(dict(profile=profile, encodeCommand=encode_command(args.ffmpeg, expanded, video, count, profile),
                              decodeCommand=[args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-i", str(video),
                                             "-map", "0:v:0", "-vf", decode_filter, "-fps_mode", "passthrough",
                                             "-start_number", "1", str(directory / "decoded" / "frame-%06d.png")]))
        write_json(destination / "plan.json", plans)
        manifest = dict(profiles=[transport_profile("png-control", fixture / "input", fixture, prefab, mapping)])
        write_json(destination / "transport-manifest.json", manifest)
        summary["profiles"]["png-control"] = dict(status="AWAITING_GPU", frames=count)
        if args.plan_only:
            summary["status"] = "PLAN_ONLY"
            return 0
        run([args.ffmpeg, "-version"], destination, "ffmpeg-version", args.timeout)
        run([ffprobe, "-version"], destination, "ffprobe-version", args.timeout)
        expanded.mkdir()
        materialization = Counter()
        for source in sources:
            original, linked = fixture / "input" / source["sourceFile"], expanded / source["sourceFile"]
            try:
                os.link(original, linked)
                materialization["hardlinks"] += 1
            except OSError:
                shutil.copy2(original, linked)
                materialization["copies"] += 1
        summary["inputMaterialization"] = dict(materialization)
        for plan in plans:
            profile = plan["profile"]
            name = profile["name"]
            directory = destination / name
            directory.mkdir()
            (directory / "decoded").mkdir()
            write_json(directory / "expected-frame-mapping.json", mapping)
            result = dict(configuration=profile, status="RUNNING", integrityStatus="AWAITING_GPU")
            summary["profiles"][name] = result
            stage = "ENCODE"
            print(f"{name}: encode", flush=True)
            try:
                run(plan["encodeCommand"], directory, "encode", args.timeout)
                stage = "PROBE"
                video = directory / "encoded.mkv"
                probe = json.loads(run([ffprobe, "-v", "error", "-select_streams", "v:0", "-show_streams", "-show_format",
                                        "-show_frames", "-of", "json", str(video)], directory, "ffprobe-frames", args.timeout))
                packets = json.loads(run([ffprobe, "-v", "error", "-select_streams", "v:0", "-show_packets", "-of", "json", str(video)],
                                          directory, "ffprobe-packets", args.timeout))
                result["transport"], frame_rows = HELPERS.packet_measurements(probe, packets, video, count)
                require((int(result["transport"]["stream"]["width"]), int(result["transport"]["stream"]["height"])) == dimensions,
                        "Encoded dimensions differ from the native fixture")
                write_csv(directory / "timestamps-and-frame-sizes.csv", frame_rows)
                stage = "DECODE"
                print(f"{name}: decode", flush=True)
                run(plan["decodeCommand"], directory, "decode", args.timeout)
                decoded = directory / "decoded"
                require({path.name for path in decoded.glob("*.png")} == {row["file"] for row in mapping},
                        "Decoded filenames/count differ from independent ordinal mapping")
                manifest["profiles"].append(transport_profile(name, decoded, fixture, prefab, mapping))
                write_json(destination / "transport-manifest.json", manifest)
                result.update(status="MEASURED", decodedFrames=count)
            except Exception as error:
                result.update(status=f"FAILED_{stage}", error=str(error))
                (directory / "failure.txt").write_text(traceback.format_exc(), encoding="utf-8")
            write_json(directory / "result.json", result)
            write_json(destination / "result.json", summary)
        summary["status"] = "FAILED" if any(item["status"].startswith("FAILED") for item in summary["profiles"].values()) else "MEASURED"
        print(json.dumps(dict(status=summary["status"], integrityStatus="AWAITING_GPU",
                              profiles={name: result["status"] for name, result in summary["profiles"].items()}), indent=2))
        return 1 if summary["status"] == "FAILED" else 0
    except Exception as error:
        summary.update(status="FAILED_PREPARATION", error=str(error))
        (destination / "failure.txt").write_text(traceback.format_exc(), encoding="utf-8")
        raise
    finally:
        summary["finishedUtc"] = utc_now()
        write_json(destination / "result.json", summary)


if __name__ == "__main__":
    sys.exit(main())
