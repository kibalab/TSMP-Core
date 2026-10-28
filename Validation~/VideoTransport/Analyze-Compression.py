#!/usr/bin/env python3
"""Offline Luma4 compression characterization; never starts or modifies Unity.

Requires Python, numpy, Pillow and ffmpeg. The supported fixture is the existing
12-frame 1280x720/block8/sample4 VideoTransportValidation PNG sequence. The CPU
model mirrors the shader's RGB mean, central samples, two-block calibration and
nearest-symbol classification. It is a numerical model, not a GPU test.
"""

import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import zlib

import numpy as np
from PIL import Image


PALETTE = np.array([24 + 14 * i if i < 15 else 232 for i in range(16)])
SOURCE_FILES = [
    "Runtime/Shaders/TSMPDecodeLuma4Bytes.shader",
    "Runtime/Shaders/Luma4Calibration.cginc",
    "Runtime/Shaders/TSMPPrepareLuma4Calibration.shader",
    "Runtime/Scripts/TSMPCodecLuma4.cs",
]


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, log):
    result = subprocess.run(command, capture_output=True, text=True, timeout=120)
    log.write_text(json.dumps(command) + "\n" + result.stdout + result.stderr,
                   encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {log}")


def image_and_samples(path):
    image = np.asarray(Image.open(path).convert("RGB"))
    if image.shape != (720, 1280, 3):
        raise ValueError(f"Unsupported fixture dimensions: {path}: {image.shape}")
    # Shader samples RGB/255, then averages channels and the central 4x4 pixels.
    # Expressing values as 0..255 preserves the same nearest-centre decision.
    blocks = image.astype(np.float64).reshape(90, 8, 160, 8, 3)
    luma = blocks[:, 2:6, :, 2:6, :].mean(axis=(1, 3, 4))
    calibration = luma[1, :32].reshape(16, 2).mean(axis=1)
    symbols = np.abs(luma[:, :, None] - calibration).argmin(axis=2)
    nominal = np.abs(luma[:, :, None] - PALETTE).argmin(axis=2)
    return image, luma, calibration, symbols, nominal


def bytes_at(symbols, start_block, count):
    nibbles = symbols.ravel()[start_block:start_block + count * 2]
    return (nibbles[::2] | (nibbles[1::2] << 4)).astype(np.uint8).tobytes()


def write_csv(path, rows):
    if rows:
        with path.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=rows[0].keys())
            writer.writeheader()
            writer.writerows(rows)


def analyze(profile, directory, originals, errors):
    rows = []
    for frame, original in enumerate(originals, 1):
        path = directory / f"frame-{frame:03d}.png"
        rgb, luma, calibration, symbols, nominal = image_and_samples(path)
        source_rgb, _, _, source_symbols, _ = original
        header = bytes_at(symbols, 2 * 160, 56)
        expected_header = bytes_at(source_symbols, 2 * 160, 56)
        count = struct.unpack_from("<H", expected_header, 42)[0]
        expected = bytes_at(source_symbols, 5 * 160, count)
        payload = bytes_at(symbols, 5 * 160, count)
        valid_header_crc = zlib.crc32(header[:52]) == struct.unpack_from("<I", header, 52)[0]
        valid_payload_crc = zlib.crc32(payload) == struct.unpack_from("<I", header, 44)[0]
        row = dict(profile=profile, frame=frame, payloadBytes=count,
                   pixelMaxAbsError=int(np.abs(rgb.astype(int) - source_rgb).max()),
                   rgbChannelMaxSpread=int(np.ptp(rgb.astype(int), axis=2).max()),
                   calibrationMinGap=float(np.diff(calibration).min()),
                   calibrationMaxAbsError=float(np.abs(calibration - PALETTE).max()),
                   headerCrcValid=bool(valid_header_crc),
                   payloadCrcMatchesDecodedHeader=bool(valid_payload_crc),
                   headerStoredCrc32=struct.unpack_from("<I", header, 52)[0],
                   headerComputedCrc32=zlib.crc32(header[:52]),
                   payloadStoredCrc32=struct.unpack_from("<I", header, 44)[0],
                   payloadComputedCrc32=zlib.crc32(payload),
                   headerByteErrors=sum(a != b for a, b in zip(header, expected_header)),
                   payloadByteErrors=sum(a != b for a, b in zip(payload, expected)),
                   headerSymbolErrors=0, payloadSymbolErrors=0,
                   headerNominalSymbolErrors=0, payloadNominalSymbolErrors=0,
                   modelStatus="")
        for region, start, length in [("header", 320, 56), ("payload", 800, count)]:
            expected_symbols = source_symbols.ravel()[start:start + length * 2]
            recovered = symbols.ravel()[start:start + length * 2]
            nominal_recovered = nominal.ravel()[start:start + length * 2]
            wrong = np.flatnonzero(expected_symbols != recovered)
            row[region + "SymbolErrors"] = len(wrong)
            row[region + "NominalSymbolErrors"] = int(np.count_nonzero(expected_symbols != nominal_recovered))
            for position in wrong:
                index = start + int(position)
                wanted = int(expected_symbols[position])
                actual = int(recovered[position])
                measured = float(luma.ravel()[index])
                boundary = float((calibration[wanted] + calibration[actual]) / 2)
                errors.append(dict(profile=profile, frame=frame, region=region,
                                   byteOffset=int(position // 2), nibble="low" if position % 2 == 0 else "high",
                                   blockX=index % 160, blockY=index // 160,
                                   expectedSymbol=wanted, actualSymbol=actual,
                                   sampleLuma=measured,
                                   expectedCalibration=float(calibration[wanted]),
                                   actualCalibration=float(calibration[actual]),
                                   crossedMidpoint=boundary,
                                   boundaryOvershoot=abs(measured - boundary),
                                   rawLumaShift=measured - float(PALETTE[wanted])))
        row["modelStatus"] = ("HEADER_REJECT" if not valid_header_crc else
                              "PAYLOAD_REJECT" if not valid_payload_crc else
                              "EXACT" if payload == expected and header == expected_header else "CHANGED")
        rows.append(row)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--results", required=True, type=Path,
                        help="New, empty directory; existing evidence is never overwritten")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--codec-package", required=True, type=Path,
                        help="The actual com.kibalab.tsmp.codec.luma4 used by the saved GPU run")
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=True)
    if any(args.results.iterdir()):
        raise ValueError("Choose an empty results directory")
    inputs = sorted((args.evidence / "input").glob("frame-*.png"))
    if [path.name for path in inputs] != [f"frame-{i:03d}.png" for i in range(1, 13)]:
        raise ValueError("Expected exactly frame-001.png through frame-012.png")
    originals = [image_and_samples(path) for path in inputs]
    if any(np.ptp(item[0].astype(int), axis=2).max() != 0 for item in originals):
        raise ValueError("This experiment expects grayscale Luma4 inputs")
    for item in originals:
        header = bytes_at(item[3], 320, 56)
        count = struct.unpack_from("<H", header, 42)[0]
        if not (zlib.crc32(header[:52]) == struct.unpack_from("<I", header, 52)[0]
                and zlib.crc32(bytes_at(item[3], 800, count)) == struct.unpack_from("<I", header, 44)[0]):
            raise ValueError("CPU model failed original protected-frame CRC self-check")
    run([args.ffmpeg, "-version"], args.results / "ffmpeg-version.txt")
    # Lossless YUV controls isolate RGB<->YUV conversion from lossy AVC coding.
    experiments = {
        "ffv1-yuv420": ["-c:v", "ffv1", "-pix_fmt", "yuv420p"],
        "h264-qp0-yuv420": ["-c:v", "libx264", "-preset", "veryfast", "-qp", "0", "-pix_fmt", "yuv420p"],
        "h264-crf28-yuv444": ["-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-pix_fmt", "yuv444p"],
        "h264-crf28-yuv420-nodeblock": ["-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-pix_fmt", "yuv420p", "-x264-params", "no-deblock=1"],
    }
    profiles = {"png-control": args.evidence / "input"}
    for name in ["ffv1-rgb", "h264-crf18", "h264-crf28"]:
        profiles[name] = args.evidence / name / "decoded"
    for name, options in experiments.items():
        directory = args.results / name
        decoded = directory / "decoded"
        decoded.mkdir(parents=True)
        video = directory / "encoded.mkv"
        run([args.ffmpeg, "-hide_banner", "-loglevel", "info", "-framerate", "30",
             "-i", str(args.evidence / "input" / "frame-%03d.png"), "-frames:v", "12",
             *options, str(video)], directory / "encode.log")
        run([args.ffmpeg, "-hide_banner", "-loglevel", "error", "-i", str(video),
             "-frames:v", "12", str(decoded / "frame-%03d.png")], directory / "decode.log")
        profiles[name] = decoded
    rows, errors = [], []
    for name, directory in profiles.items():
        rows.extend(analyze(name, directory, originals, errors))
    write_csv(args.results / "frames.csv", rows)
    write_csv(args.results / "symbol-errors.csv", errors)
    gpu_rows = list(csv.DictReader((args.evidence / "frames.csv").open(encoding="utf-8-sig")))
    agreements = []
    for saved in gpu_rows:
        model = next(row for row in rows if row["profile"] == saved["profile"] and row["frame"] == int(saved["expectedFrame"]))
        agreements.append((model["modelStatus"] == "EXACT") == (saved["status"] == "EXACT")
                          and model["headerCrcValid"] == (saved["headerValid"] == "True"))
    crc_comparisons = []
    gpu_details = (args.evidence / "result.txt").read_text(encoding="utf-8-sig")
    for frame, region, stored, computed in re.findall(
            r"h264-crf28 frame (\d+): (Payload|Header) CRC mismatch\. expected=(\d+) actual=(\d+)", gpu_details):
        model = next(row for row in rows if row["profile"] == "h264-crf28" and row["frame"] == int(frame))
        prefix = region.lower()
        crc_comparisons.append(model[prefix + "StoredCrc32"] == int(stored)
                               and model[prefix + "ComputedCrc32"] == int(computed))
    summary = dict(
        scope="Offline CPU symbol model and controlled FFmpeg roundtrips; no Unity or live transport run",
        model="RGB mean; central4x4 per8x8; mean of2 calibration blocks per symbol; nearest measured centre; low nibble first",
        limitations="Float64 CPU means approximate GPU float32 arithmetic; exact decision-boundary ties can differ. CRC results are compared with saved GPU outcomes, not a new GPU decode. Controls do not uniquely identify an individual x264 quantized coefficient.",
        savedGpuOutcomeComparisons=len(agreements), savedGpuOutcomeAgreements=sum(agreements),
        savedGpuErrorCrcComparisons=len(crc_comparisons), savedGpuErrorCrcAgreements=sum(crc_comparisons),
        savedGpuModelReproductionPass=(len(agreements) == 48 and sum(agreements) == 48
                                      and len(crc_comparisons) == 12 and sum(crc_comparisons) == 12),
        profiles={}, evidence=str(args.evidence.resolve()), results=str(args.results.resolve()),
        sourceHashes={str(path.resolve()): sha256(path) for path in inputs})
    summary["sourceHashes"][str(Path(__file__).resolve())] = sha256(Path(__file__))
    for relative in SOURCE_FILES:
        path = args.codec_package / relative
        summary["sourceHashes"][str(path.resolve())] = sha256(path)
    core = args.codec_package.parent / "com.kibalab.tsmp.core"
    for relative in ["Runtime/Core/Protocol/ProtocolConstants.cs", "Runtime/Codecs/Luma4/Scripts/Luma4Raster.cs",
                     "Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"]:
        path = core / relative
        summary["sourceHashes"][str(path.resolve())] = sha256(path)
    for name in profiles:
        group = [row for row in rows if row["profile"] == name]
        summary["profiles"][name] = dict(
            frames=len(group), exact=sum(row["modelStatus"] == "EXACT" for row in group),
            headerReject=sum(row["modelStatus"] == "HEADER_REJECT" for row in group),
            payloadReject=sum(row["modelStatus"] == "PAYLOAD_REJECT" for row in group),
            headerSymbolErrors=sum(row["headerSymbolErrors"] for row in group),
            payloadSymbolErrors=sum(row["payloadSymbolErrors"] for row in group),
            headerByteErrors=sum(row["headerByteErrors"] for row in group),
            payloadByteErrors=sum(row["payloadByteErrors"] for row in group),
            headerNominalSymbolErrors=sum(row["headerNominalSymbolErrors"] for row in group),
            payloadNominalSymbolErrors=sum(row["payloadNominalSymbolErrors"] for row in group),
            pixelMaxAbsError=max(row["pixelMaxAbsError"] for row in group),
            calibrationMinGap=min(row["calibrationMinGap"] for row in group))
    (args.results / "result.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps({key: summary[key] for key in ["savedGpuOutcomeComparisons", "savedGpuOutcomeAgreements",
                                                  "savedGpuErrorCrcComparisons", "savedGpuErrorCrcAgreements",
                                                  "savedGpuModelReproductionPass", "profiles"]}, indent=2))
    # Rejected compression profiles are expected characterization outcomes. A
    # numerical model that does not reproduce the saved GPU evidence is an error.
    if not summary["savedGpuModelReproductionPass"]:
        raise RuntimeError("CPU model must reproduce all 48 GPU outcomes and all 12 error CRC pairs")


if __name__ == "__main__":
    main()
