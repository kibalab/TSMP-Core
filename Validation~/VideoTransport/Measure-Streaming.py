#!/usr/bin/env python3
"""Finite FFmpeg CBR measurements; never starts Unity, OBS or a network service.

Requires numpy and Pillow. PNGs must be native block8/sample4 protected Luma4
fixtures. Repeated source IDs are associated by file ordinal, not live delivery.
Compression rejection is a measured result; process/model failures exit nonzero.
"""

import argparse
from collections import Counter, OrderedDict
import csv
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import time
import traceback
import zlib

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("compression_model", HERE / "Analyze-Compression.py")
MODEL = importlib.util.module_from_spec(spec)
spec.loader.exec_module(MODEL)
FPS = 30
ENCODE_FILTER = "scale=in_range=full:out_range={range}:out_color_matrix=bt709,format=yuv420p"
DECODE_FILTER = "scale=in_range={range}:out_range=full:in_color_matrix=bt709,format=rgb24"


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False, allow_nan=False) + "\n", encoding="utf-8")


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def run(command, directory, name, timeout):
    """Preserve stdout/stderr and an exit record, including launch/timeout errors."""
    record = dict(command=[str(item) for item in command], startedUtc=utc_now(), timeoutSeconds=timeout)
    started = time.monotonic()
    stdout, stderr = b"", b""
    failure = None
    try:
        result = subprocess.run(record["command"], capture_output=True, timeout=timeout,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        stdout, stderr = result.stdout, result.stderr
        record["exitCode"] = result.returncode
        if result.returncode:
            failure = f"Command exited {result.returncode}: {directory / (name + '.process.json')}"
    except subprocess.TimeoutExpired as error:
        stdout, stderr = error.stdout or b"", error.stderr or b""
        record.update(exitCode=None, timedOut=True, error=str(error))
        failure = f"Command timed out: {directory / (name + '.process.json')}"
    except OSError as error:
        record.update(exitCode=None, error=str(error))
        failure = f"Command could not start: {directory / (name + '.process.json')}"
    record.update(finishedUtc=utc_now(), wallSeconds=time.monotonic() - started)
    (directory / (name + ".stdout.txt")).write_bytes(stdout)
    (directory / (name + ".stderr.txt")).write_bytes(stderr)
    write_json(directory / (name + ".process.json"), record)
    if failure:
        raise RuntimeError(failure)
    return stdout.decode("utf-8-sig")


def samples(path):
    # Preserve the existing, GPU-compared CPU model verbatim for 720p inputs.
    with Image.open(path) as image:
        size = image.size
    if size == (1280, 720):
        return MODEL.image_and_samples(path)
    image = np.asarray(Image.open(path).convert("RGB"))
    height, width, _ = image.shape
    if width % 8 or height % 8 or width < 256 or height < 56:
        raise ValueError(f"Expected native block8 dimensions: {path}: {width}x{height}")
    luma = image.reshape(height // 8, 8, width // 8, 8, 3)[:, 2:6, :, 2:6, :].mean(axis=(1, 3, 4))
    calibration = luma[1, :32].reshape(16, 2).mean(axis=1)
    symbols = np.abs(luma[:, :, None] - calibration).argmin(axis=2)
    nominal = np.abs(luma[:, :, None] - MODEL.PALETTE).argmin(axis=2)
    return image, luma, calibration, symbols, nominal


def inspect_sources(directory):
    numbered = []
    for path in directory.glob("frame-*.png"):
        match = re.fullmatch(r"frame-(\d+)\.png", path.name)
        if not match:
            raise ValueError(f"Unexpected fixture filename: {path.name}")
        numbered.append((int(match[1]), path))
    numbered.sort()
    if not numbered or [number for number, _ in numbered] != list(range(1, len(numbered) + 1)):
        raise ValueError("Input must contain one contiguous frame-1..N PNG sequence, starting at 1")
    metadata = []
    dimensions = None
    for index, path in numbered:
        item = samples(path)
        rgb, _, calibration, symbols, _ = item
        height, width, _ = rgb.shape
        if dimensions is None:
            dimensions = (width, height)
        if dimensions != (width, height) or np.any(rgb[:, :, 0] != rgb[:, :, 1]) or np.any(rgb[:, :, 0] != rgb[:, :, 2]):
            raise ValueError(f"Sources must have identical dimensions and grayscale Luma4 pixels: {path}")
        columns = width // 8
        header = MODEL.bytes_at(symbols, columns * 2, 56)
        if len(header) != 56 or zlib.crc32(header[:52]) != struct.unpack_from("<I", header, 52)[0]:
            raise ValueError(f"Original header CRC self-check failed: {path}")
        flags = struct.unpack_from("<H", header, 10)[0]
        if not flags & 1 or header[5] < 2:
            raise ValueError(f"Protected protocol >=0.2 source required: {path}")
        block, active_width, active_height, layout = struct.unpack_from("<HHHH", header, 12)
        if (block, active_width, active_height, layout, header[50]) != (8, width // 8, height // 8, 0, 4):
            raise ValueError(f"CPU model requires native block8/sample4, layout0 fixture: {path}")
        length = struct.unpack_from("<H", header, 42)[0]
        if length * 2 > columns * (height // 8 - 6):
            raise ValueError(f"Payload exceeds native raster capacity: {path}")
        payload = MODEL.bytes_at(symbols, columns * 5, length)
        if zlib.crc32(payload) != struct.unpack_from("<I", header, 44)[0] or np.diff(calibration).min() <= 0:
            raise ValueError(f"Original payload CRC/calibration self-check failed: {path}")
        metadata.append(dict(sourceIndex=index, sourceFile=path.name, sourcePath=str(path.resolve()),
                             sha256=sha256(path), frameIndex=struct.unpack_from("<I", header, 24)[0],
                             payloadBytes=length, payloadSha256=hashlib.sha256(payload).hexdigest(),
                             width=width, height=height))
    return metadata, dimensions


def prepare_expected_payloads(input_directory, results_directory, sources):
    """Use independent fixture bins when supplied; otherwise recover originals.

    The fallback reads only protected source PNGs, never compressed/decoded
    outputs. Its model-derived provenance remains explicit in result.json.
    """
    supplied = input_directory.parent / "payload"
    use_supplied = supplied.exists()
    directory = supplied if use_supplied else results_directory / "expected-payload"
    if use_supplied:
        if not directory.is_dir():
            raise ValueError(f"Expected payload path is not a directory: {directory}")
    else:
        directory.mkdir()
    files = []
    for source in sources:
        filename = Path(source["sourceFile"]).with_suffix(".bin").name
        path = directory / filename
        if use_supplied:
            payload = path.read_bytes()
        else:
            original = samples(input_directory / source["sourceFile"])
            payload = MODEL.bytes_at(original[3], source["width"] // 8 * 5, source["payloadBytes"])
        digest = hashlib.sha256(payload).hexdigest()
        if len(payload) != source["payloadBytes"] or digest != source["payloadSha256"]:
            raise ValueError(f"Expected raw payload differs from protected original PNG: {path}")
        if not use_supplied:
            with path.open("xb") as handle:
                handle.write(payload)
        files.append(dict(file=filename, path=str(path.resolve()), sha256=digest, bytes=len(payload)))
    return directory.resolve(), dict(
        origin="supplied-fixture-raw-bins" if use_supplied else "cpu-model-recovered-from-protected-source-pngs",
        directory=str(directory.resolve()), files=files,
        note="Compared with protected original PNG payloads; never derived from compressed/decoded measurement outputs."
        if use_supplied else "Fallback byte reference is recovered from original PNGs by the CPU model; independent pre-raster raw bytes were unavailable.")


def transport_profile(name, frames_directory, input_directory, payload_directory, mapping, expanded=False):
    return dict(name=name, framesDirectory=str(frames_directory), expectedDirectory=str(input_directory),
                expectedPayloadDirectory=str(payload_directory),
                frameMappings=[dict(file=row["expandedInputFile"] if expanded else row["file"],
                                    expectedFile=row["expectedFile"], payloadFile=row["payloadFile"],
                                    frameIndex=row["frameIndex"], frameIndexSpecified=True) for row in mapping])


class MappedOriginals:
    """Keep long, unique 1440p fixtures bounded instead of retaining all RGBs."""
    def __init__(self, directory, mapping):
        self.directory = directory
        self.mapping = mapping

    def __iter__(self):
        cache = OrderedDict()
        for row in self.mapping:
            name = row["expectedFile"]
            if name not in cache:
                if len(cache) >= 4:
                    cache.popitem(last=False)
                cache[name] = samples(self.directory / name)
            cache.move_to_end(name)
            yield cache[name]


class ErrorCsv:
    """Analyze-Compression only needs append(); stream symbol errors to disk."""
    def __init__(self, path):
        self.handle = path.open("w", newline="", encoding="utf-8")
        self.writer = None
        self.count = 0

    def append(self, row):
        if self.writer is None:
            self.writer = csv.DictWriter(self.handle, fieldnames=list(row))
            self.writer.writeheader()
        self.writer.writerow(row)
        self.count += 1

    def close(self):
        self.handle.close()


def analyze_generic(profile, decoded, originals, errors):
    """Same RGB/centre/calibration model, with raster offsets derived from width.

    This extension is a CPU approximation, not an additional GPU validation.
    """
    rows = []
    for frame, original in enumerate(originals, 1):
        rgb, luma, calibration, symbols, nominal = samples(decoded / f"frame-{frame:03d}.png")
        source_rgb, _, _, source_symbols, _ = original
        if rgb.shape != source_rgb.shape:
            raise ValueError(f"Decoded dimensions changed: {profile}, frame {frame}")
        columns = rgb.shape[1] // 8
        header_start, payload_start = columns * 2, columns * 5
        header = MODEL.bytes_at(symbols, header_start, 56)
        expected_header = MODEL.bytes_at(source_symbols, header_start, 56)
        count = struct.unpack_from("<H", expected_header, 42)[0]
        expected = MODEL.bytes_at(source_symbols, payload_start, count)
        payload = MODEL.bytes_at(symbols, payload_start, count)
        header_crc = zlib.crc32(header[:52])
        payload_crc = zlib.crc32(payload)
        valid_header = header_crc == struct.unpack_from("<I", header, 52)[0]
        valid_payload = payload_crc == struct.unpack_from("<I", header, 44)[0]
        row = dict(profile=profile, frame=frame, payloadBytes=count,
                   pixelMaxAbsError=int(np.abs(rgb.astype(int) - source_rgb).max()),
                   rgbChannelMaxSpread=int(np.ptp(rgb.astype(int), axis=2).max()),
                   calibrationMinGap=float(np.diff(calibration).min()),
                   calibrationMaxAbsError=float(np.abs(calibration - MODEL.PALETTE).max()),
                   headerCrcValid=bool(valid_header), payloadCrcMatchesDecodedHeader=bool(valid_payload),
                   headerStoredCrc32=struct.unpack_from("<I", header, 52)[0], headerComputedCrc32=header_crc,
                   payloadStoredCrc32=struct.unpack_from("<I", header, 44)[0], payloadComputedCrc32=payload_crc,
                   headerByteErrors=sum(a != b for a, b in zip(header, expected_header)),
                   payloadByteErrors=sum(a != b for a, b in zip(payload, expected)),
                   headerSymbolErrors=0, payloadSymbolErrors=0,
                   headerNominalSymbolErrors=0, payloadNominalSymbolErrors=0, modelStatus="")
        for region, start, length in [("header", header_start, 56), ("payload", payload_start, count)]:
            wanted_symbols = source_symbols.ravel()[start:start + length * 2]
            recovered = symbols.ravel()[start:start + length * 2]
            wrong = np.flatnonzero(wanted_symbols != recovered)
            row[region + "SymbolErrors"] = len(wrong)
            row[region + "NominalSymbolErrors"] = int(np.count_nonzero(wanted_symbols != nominal.ravel()[start:start + length * 2]))
            for position in wrong:
                index = start + int(position)
                wanted, actual = int(wanted_symbols[position]), int(recovered[position])
                measured = float(luma.ravel()[index])
                boundary = float((calibration[wanted] + calibration[actual]) / 2)
                errors.append(dict(profile=profile, frame=frame, region=region, byteOffset=int(position // 2),
                                   nibble="low" if position % 2 == 0 else "high", blockX=index % columns,
                                   blockY=index // columns, expectedSymbol=wanted, actualSymbol=actual,
                                   sampleLuma=measured, expectedCalibration=float(calibration[wanted]),
                                   actualCalibration=float(calibration[actual]), crossedMidpoint=boundary,
                                   boundaryOvershoot=abs(measured - boundary), rawLumaShift=measured - float(MODEL.PALETTE[wanted])))
        row["modelStatus"] = ("HEADER_REJECT" if not valid_header else "PAYLOAD_REJECT" if not valid_payload else
                              "EXACT" if payload == expected and header == expected_header else "CHANGED")
        rows.append(row)
    return rows


def profile_matrix(dimensions):
    width, height = dimensions
    if (width, height) == (1280, 720):
        entries = [("x264-veryfast-qp0-709limited", "libx264", None, "limited", 60)]
        for encoder, label in [("libx264", "x264-veryfast"), ("h264_nvenc", "nvenc-p4")]:
            entries.extend((f"{label}-cbr{rate}-709limited", encoder, bitrate, "limited", 60)
                           for rate, bitrate in [("3.5M", 3500000), ("6M", 6000000)])
    elif (width, height) == (2560, 1440):
        entries = [(f"x264-veryfast-qp0-709{color}", "libx264", None, color, 30) for color in ["full", "limited"]]
        entries += [(f"nvenc-p4-cbr10M-709{color}", "h264_nvenc", 10000000, color, 30) for color in ["full", "limited"]]
    else:
        raise ValueError(f"No default rate matrix for {width}x{height}; use native 720p or 1440p fixtures")
    return [dict(name=name, encoder=encoder, bitrate=rate, colorRange=color, gop=gop, width=width, height=height,
                 fps=FPS, bFrames=0, pixelFormat="yuv420p", preset="veryfast" if encoder == "libx264" else "p4")
            for name, encoder, rate, color, gop in entries]


def encode_command(ffmpeg, expanded, video, count, profile):
    color, encoder, rate = profile["colorRange"], profile["encoder"], profile["bitrate"]
    command = [ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-framerate", str(FPS),
               "-start_number", "1", "-i", str(expanded / "frame-%06d.png"), "-frames:v", str(count),
               "-an", "-vf", ENCODE_FILTER.format(range=color), "-c:v", encoder, "-preset", profile["preset"],
               "-g", str(profile["gop"]), "-bf", "0", "-pix_fmt", "yuv420p", "-r", str(FPS),
               "-fps_mode", "cfr", "-color_primaries", "bt709", "-color_trc", "bt709",
               "-colorspace", "bt709", "-color_range", "pc" if color == "full" else "tv"]
    if encoder == "libx264":
        command += ["-keyint_min", str(profile["gop"]), "-sc_threshold", "0"]
    if rate is None:
        command += ["-qp", "0"]
    else:
        command += ["-b:v", str(rate), "-minrate", str(rate), "-maxrate", str(rate), "-bufsize", str(rate * 2)]
        if encoder == "libx264":
            command += ["-x264-params", "nal-hrd=cbr:force-cfr=1:filler=1"]
        else:
            command += ["-rc", "cbr", "-rc-lookahead", "0"]
    return command + [str(video)]


def numeric(value):
    try:
        result = float(value)
        return result if math.isfinite(result) else None
    except (TypeError, ValueError):
        return None


def distribution(values):
    if not values:
        return None
    return dict(min=min(values), mean=sum(values) / len(values), p95=float(np.percentile(values, 95)), max=max(values))


def packet_measurements(probe, packets_probe, video, count):
    streams = [stream for stream in probe.get("streams", []) if stream.get("codec_type") == "video"]
    if len(streams) != 1:
        raise ValueError("Expected exactly one video stream")
    stream = streams[0]
    frames = probe.get("frames", [])
    packets = packets_probe.get("packets", [])
    frame_rows = []
    for ordinal, frame in enumerate(frames, 1):
        frame_rows.append(dict(frame=ordinal, pts=frame.get("pts"), ptsTime=frame.get("pts_time"),
                               bestEffortTimestamp=frame.get("best_effort_timestamp"),
                               bestEffortTimestampTime=frame.get("best_effort_timestamp_time"),
                               packetDts=frame.get("pkt_dts"), packetDtsTime=frame.get("pkt_dts_time"),
                               duration=frame.get("duration", frame.get("pkt_duration")),
                               durationTime=frame.get("duration_time", frame.get("pkt_duration_time")),
                               packetBytes=frame.get("pkt_size"), keyFrame=frame.get("key_frame"),
                               pictureType=frame.get("pict_type"), colorRange=frame.get("color_range"),
                               colorSpace=frame.get("color_space")))
    pts = [numeric(frame.get("pts_time", frame.get("best_effort_timestamp_time"))) for frame in frames]
    times = [numeric(packet.get("pts_time", packet.get("dts_time"))) for packet in packets]
    if len(frames) != count or len(packets) != count or any(value is None for value in pts + times):
        raise ValueError(f"Frame/packet count or timestamp mismatch: expected {count}, frames={len(frames)}, packets={len(packets)}")
    if any(b <= a for a, b in zip(pts, pts[1:])):
        raise ValueError("Decoded presentation timestamps are not strictly increasing")
    sizes = [int(packet["size"]) for packet in packets]
    start = min(times)
    end = max(at + (numeric(packet.get("duration_time")) or 1 / FPS) for at, packet in zip(times, packets))
    duration = end - start
    windows = []
    for index in range(math.ceil(duration - 1e-6)):
        interval = min(1.0, duration - index)
        size = sum(size for at, size in zip(times, sizes) if index <= at - start < index + 1)
        windows.append(dict(startSeconds=index, intervalSeconds=interval, packetBytes=size, bitsPerSecond=size * 8 / interval))
    return dict(stream=stream, format=probe.get("format", {}), observedFrames=len(frames),
                packetCount=len(packets), packetBytes=sum(sizes), presentationDurationSeconds=duration,
                measuredVideoPacketBitsPerSecond=sum(sizes) * 8 / duration,
                measuredContainerBitsPerSecond=video.stat().st_size * 8 / duration,
                framePacketBytes=distribution(sizes), oneSecondPacketBitrate=distribution([row["bitsPerSecond"] for row in windows]),
                oneSecondWindows=windows, timestampBasis="ffprobe packet PTS through final packet duration; fallback 1/30s if absent",
                bFrameCount=sum(frame.get("pict_type") == "B" for frame in frames)), frame_rows


def cpu_summary(rows, symbol_errors):
    statuses = Counter(row["modelStatus"] for row in rows)
    return dict(frames=len(rows), statuses=dict(statuses), symbolErrorRows=symbol_errors,
                headerByteErrors=sum(row["headerByteErrors"] for row in rows),
                payloadByteErrors=sum(row["payloadByteErrors"] for row in rows),
                pixelMaxAbsError=max(row["pixelMaxAbsError"] for row in rows),
                calibrationMinGap=min(row["calibrationMinGap"] for row in rows))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path, help="Protected native-resolution frame-NNN.png directory")
    parser.add_argument("--results", required=True, type=Path, help="New or empty evidence directory")
    parser.add_argument("--ffmpeg", default=r"C:\Windows\ffmpeg.exe")
    parser.add_argument("--ffprobe", help="Defaults to ffprobe.exe alongside ffmpeg")
    parser.add_argument("--seconds", type=float, default=8, help="Finite 30fps run length; default 8 seconds")
    parser.add_argument("--profiles", help="Comma-separated matrix profile names; default all for source dimensions")
    parser.add_argument("--codec-package", type=Path, help="Optional Luma4 package whose source hashes are recorded")
    parser.add_argument("--timeout", type=float, default=300, help="Per-process timeout seconds")
    parser.add_argument("--plan-only", action="store_true", help="Validate sources and save commands/mapping without encoding")
    args = parser.parse_args()
    if not math.isfinite(args.seconds) or args.seconds <= 0 or not math.isclose(args.seconds * FPS, round(args.seconds * FPS)):
        parser.error("--seconds must specify a positive whole number of 30fps frames")
    if not math.isfinite(args.timeout) or args.timeout <= 0:
        parser.error("--timeout must be positive")
    count = round(args.seconds * FPS)
    args.input = args.input.resolve()
    args.results = args.results.resolve()
    args.results.mkdir(parents=True, exist_ok=True)
    if any(args.results.iterdir()):
        raise ValueError("Choose an empty results directory; existing evidence is preserved")
    summary = dict(scope="Finite offline FFmpeg encode/decode and CPU Luma4 model; no Unity, OBS, network or live-client execution",
                   startedUtc=utc_now(), status="PREPARING", input=str(args.input), results=str(args.results), profiles={})
    try:
        sources, dimensions = inspect_sources(args.input)
        profiles = profile_matrix(dimensions)
        if args.profiles:
            selected = args.profiles.split(",")
            unknown = set(selected) - {profile["name"] for profile in profiles}
            if unknown or len(selected) != len(set(selected)):
                raise ValueError(f"Unknown or duplicate profile names: {selected}; available: {[p['name'] for p in profiles]}")
            profiles = [profile for profile in profiles if profile["name"] in selected]
        mapping = [dict(frame=frame, file=f"frame-{frame:03d}.png", expandedInputFile=f"frame-{frame:06d}.png",
                        expectedFile=sources[(frame - 1) % len(sources)]["sourceFile"],
                        payloadFile=Path(sources[(frame - 1) % len(sources)]["sourceFile"]).with_suffix(".bin").name,
                        sourceIndex=(frame - 1) % len(sources) + 1,
                        frameIndex=sources[(frame - 1) % len(sources)]["frameIndex"],
                        expectedPts=frame - 1, expectedTimeBase="1/30", expectedPtsSeconds=(frame - 1) / FPS)
                   for frame in range(1, count + 1)]
        expanded = args.results / "expanded-input"
        ffprobe = args.ffprobe or str(Path(args.ffmpeg).with_name("ffprobe.exe" if os.name == "nt" else "ffprobe"))
        hashes = {str(Path(__file__).resolve()): sha256(Path(__file__)), str(Path(MODEL.__file__).resolve()): sha256(Path(MODEL.__file__))}
        if args.codec_package:
            for relative in MODEL.SOURCE_FILES:
                path = args.codec_package / relative
                hashes[str(path.resolve())] = sha256(path)
            core = args.codec_package.parent / "com.kibalab.tsmp.core"
            for relative in ["Runtime/Codecs/Luma4/Scripts/Luma4Raster.cs", "Runtime/Core/Protocol/FrameHeader.cs"]:
                path = core / relative
                hashes[str(path.resolve())] = sha256(path)
        summary.update(dimensions=list(dimensions), fps=FPS, frameCount=count, requestedSeconds=args.seconds,
                       sourceFrameCount=len(sources), sources=sources, sourceHashes=hashes,
                       repeatedSourceIds=len({row["frameIndex"] for row in mapping}) != count,
                       sourceAssociation="Explicit file ordinal mapping; cyclic source reuse is not a unique live-delivery or age measurement",
                       cpuModel="Existing Analyze-Compression.py at 720p; same block8/central4x4/two-block-calibration math generalized at 1440p",
                       limitations=["Float64 CPU arithmetic can differ from GPU float32 near symbol boundaries; no new GPU verdict is claimed.",
                                    "Fixed CBR targets and 2-second VBV buffers do not imply equal frame size or exactly constant one-second bitrate.",
                                    "NVENC p4 is an explicit FFmpeg model preset, not a claim about the observed OBS preset.",
                                    "QP0 controls retain RGB/YUV420 and range conversion; they are not RGB-lossless controls.",
                                    "Decoding is offline, without real-time pacing; process wall time is not network latency."],
                       bufferSeconds=2, encodeConversion=ENCODE_FILTER, decodeConversion=DECODE_FILTER)
        payload_directory, summary["expectedPayloads"] = prepare_expected_payloads(args.input, args.results, sources)
        write_json(args.results / "expected-frame-mapping.json", mapping)
        plans = []
        for profile in profiles:
            directory = args.results / profile["name"]
            video = directory / "encoded.mkv"
            plans.append(dict(profile=profile, encodeCommand=encode_command(args.ffmpeg, expanded, video, count, profile),
                              decodeCommand=[args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-i", str(video),
                                             "-map", "0:v:0", "-vf", DECODE_FILTER.format(range=profile["colorRange"]),
                                             "-fps_mode", "passthrough", "-start_number", "1", str(directory / "decoded" / "frame-%03d.png")]))
        write_json(args.results / "plan.json", plans)
        if args.plan_only:
            summary["status"] = "PLAN_ONLY"
            return 0
        run([args.ffmpeg, "-version"], args.results, "ffmpeg-version", args.timeout)
        run([ffprobe, "-version"], args.results, "ffprobe-version", args.timeout)
        expanded.mkdir()
        materialization = Counter()
        for row in mapping:
            source = args.input / row["expectedFile"]
            destination = expanded / row["expandedInputFile"]
            try:
                os.link(source, destination)
                materialization["hardlinks"] += 1
            except OSError:
                shutil.copy2(source, destination)
                materialization["copies"] += 1
        summary["inputMaterialization"] = dict(materialization)
        expanded_originals = MappedOriginals(args.input, mapping)
        manifest = dict(profiles=[transport_profile("png-control", expanded, args.input, payload_directory, mapping, expanded=True)])
        write_json(args.results / "transport-manifest.json", manifest)
        summary["profiles"]["png-control"] = dict(status="SOURCE_CRC_SELF_CHECK_PASS", frames=count,
                                                    note="Source CPU CRC self-check only; GPU control is supplied in transport-manifest.json")
        for plan in plans:
            profile = plan["profile"]
            name = profile["name"]
            directory = args.results / name
            directory.mkdir()
            (directory / "decoded").mkdir()
            write_json(directory / "expected-frame-mapping.json", mapping)
            result = dict(configuration=profile, status="RUNNING")
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
                result["transport"], frame_rows = packet_measurements(probe, packets, video, count)
                MODEL.write_csv(directory / "timestamps-and-frame-sizes.csv", frame_rows)
                stage = "DECODE"
                print(f"{name}: decode", flush=True)
                run(plan["decodeCommand"], directory, "decode", args.timeout)
                decoded = directory / "decoded"
                if {path.name for path in decoded.glob("*.png")} != {row["file"] for row in mapping}:
                    raise ValueError("Decoded PNG filenames/count do not exactly match the expected ordinal mapping")
                manifest["profiles"].append(transport_profile(name, decoded, args.input, payload_directory, mapping))
                write_json(args.results / "transport-manifest.json", manifest)
                stage = "CPU_MODEL"
                print(f"{name}: CPU model", flush=True)
                errors = ErrorCsv(directory / "symbol-errors.csv")
                try:
                    analyze = MODEL.analyze if dimensions == (1280, 720) else analyze_generic
                    rows = analyze(name, decoded, expanded_originals, errors)
                finally:
                    errors.close()
                MODEL.write_csv(directory / "frames.csv", rows)
                result.update(status="MEASURED", cpu=cpu_summary(rows, errors.count))
            except Exception as error:
                result.update(status=f"FAILED_{stage}", error=str(error))
                (directory / "failure.txt").write_text(traceback.format_exc(), encoding="utf-8")
            write_json(directory / "result.json", result)
            write_json(args.results / "result.json", summary)
        summary["status"] = "FAILED" if any(result["status"].startswith("FAILED") for result in summary["profiles"].values()) else "MEASURED"
        print(json.dumps(dict(status=summary["status"], profiles={name: result["status"] for name, result in summary["profiles"].items()}), indent=2))
        return 1 if summary["status"] == "FAILED" else 0
    except Exception as error:
        summary.update(status="FAILED_PREPARATION", error=str(error))
        (args.results / "failure.txt").write_text(traceback.format_exc(), encoding="utf-8")
        raise
    finally:
        summary["finishedUtc"] = utc_now()
        write_json(args.results / "result.json", summary)


if __name__ == "__main__":
    sys.exit(main())
