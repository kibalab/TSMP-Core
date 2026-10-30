#!/usr/bin/env python3
"""Generate finite Luma4/block8/sample4 measurement fixtures without running Unity.

Requires numpy and Pillow. Existing protected VideoTransport PNGs provide the
binary layout templates. A central-sample/calibrated-nearest-symbol CPU model
checks all templates and selected generated PNGs; this is not GPU certification.
"""

import argparse
import csv
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import platform
import re
import struct
import sys
import zlib

import numpy as np
from PIL import Image, __version__ as pillow_version


BLOCK = 8
HEADER_BYTES = 56
HEADER_ROW = 2
PAYLOAD_ROW = 5
VALUE_OFFSET = 25
LENGTHS = (32, 2495, 5375)
PALETTE = np.array([24 + 14 * i if i < 15 else 232 for i in range(16)], dtype=np.uint8)
FRAME_NAME = re.compile(r"^frame-(\d{3}|\d{6})\.png$", re.IGNORECASE)
CORE_FILES = (
    "Runtime/Codecs/Luma4/Scripts/Luma4Raster.cs",
    "Runtime/Core/Protocol/ProtocolConstants.cs",
    "Runtime/Core/Protocol/FrameHeader.cs",
    "Runtime/Core/Protocol/FrameHeaderWriter.cs",
    "Runtime/Core/Protocol/FrameHeaderReader.cs",
    "Runtime/Core/Protocol/NetworkFrameProtocol.cs",
    "Runtime/Core/Protocol/ProtocolTypes.cs",
    "Runtime/Encoder/Helpers/NetworkFrameWriter.cs",
    "Runtime/Encoder/Helpers/NetworkValueEntryWriter.cs",
    "Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc",
)
CODEC_FILES = (
    "Runtime/Scripts/TSMPCodecLuma4.cs",
    "Runtime/Shaders/TSMPDecodeLuma4Bytes.shader",
    "Runtime/Shaders/TSMPPrepareLuma4Calibration.shader",
    "Runtime/Shaders/Luma4Calibration.cginc",
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def u16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def samples_and_symbols(path):
    with Image.open(path) as image:
        rgb = np.asarray(image.convert("RGB"))
    height, width, _ = rgb.shape
    require(width % BLOCK == 0 and height % BLOCK == 0, f"Invalid block8 dimensions: {path}")
    require(width // BLOCK >= 38 and height // BLOCK >= 7, f"Image is too small: {path}")
    blocks = rgb.astype(np.float64).reshape(height // BLOCK, BLOCK, width // BLOCK, BLOCK, 3)
    luma = blocks[:, 2:6, :, 2:6, :].mean(axis=(1, 3, 4))
    calibration = luma[1, :32].reshape(16, 2).mean(axis=1)
    require(np.all(np.diff(calibration) >= 4), f"Non-monotonic/degenerate calibration: {path}")
    symbols = np.abs(luma[:, :, None] - calibration).argmin(axis=2).astype(np.uint8)
    return rgb, calibration, symbols


def bytes_at(symbols, start, count):
    require(count >= 0 and start >= 0 and start + count * 2 <= symbols.size, "Encoded bytes exceed image capacity")
    nibbles = symbols.ravel()[start:start + count * 2]
    return (nibbles[::2] | (nibbles[1::2] << 4)).astype(np.uint8).tobytes()


def decode(path):
    rgb, calibration, symbols = samples_and_symbols(path)
    width_blocks = symbols.shape[1]
    header = bytes_at(symbols, HEADER_ROW * width_blocks, HEADER_BYTES)
    require(u32(header, 0) == 0x504D5354 and u16(header, 8) == HEADER_BYTES, f"Invalid TSMP header: {path}")
    require(header[4] == 0 and header[5] >= 2 and u16(header, 10) & 1, f"Protected protocol 0.2 header required: {path}")
    require(zlib.crc32(header[:52]) == u32(header, 52), f"Header CRC mismatch: {path}")
    require(u16(header, 12) == BLOCK and header[50] == 4, f"Expected block8/sample4: {path}")
    require((u16(header, 14), u16(header, 16)) == (symbols.shape[1], symbols.shape[0]), f"Header geometry mismatch: {path}")
    payload = bytes_at(symbols, PAYLOAD_ROW * width_blocks, u16(header, 42))
    require(zlib.crc32(payload) == u32(header, 44), f"Payload CRC mismatch: {path}")
    return rgb, calibration, header, payload


def validate_payload(payload, frame):
    require(len(payload) >= VALUE_OFFSET, "NetworkFrame fixture is too short")
    require(payload[:2] == b"\x01\x00" and u16(payload, 2) == 1, "Expected one NetworkFrame message")
    require(u32(payload, 4) == frame and u16(payload, 12) == (frame & 0xffff), "Network/message sequence mismatch")
    require(u16(payload, 8) == 1 and payload[10] == 1 and payload[11] == 0, "Expected original VariableState message")
    require(u16(payload, 14) == len(payload) - 16 and u16(payload, 16) == 1, "VariableState body/count mismatch")
    require(u32(payload, 18) == 100 and payload[22] == 8 and u16(payload, 23) == len(payload) - VALUE_OFFSET,
            "Expected the original single raw-byte field")


def render(width, height, header, payload):
    width_blocks, height_blocks = width // BLOCK, height // BLOCK
    require(len(header) == HEADER_BYTES and len(header) * 2 <= width_blocks * 3, "Header exceeds three rows")
    require(len(payload) * 2 <= width_blocks * (height_blocks - PAYLOAD_ROW - 1), "Payload exceeds image capacity")
    blocks = np.full((height_blocks, width_blocks), 128, dtype=np.uint8)
    marker = min(8, width_blocks)
    for i in range(marker):
        blocks[0, i] = PALETTE[15 if i % 2 == 0 else 0]
        blocks[0, width_blocks - marker + i] = PALETTE[0 if i % 2 == 0 else 15]
        blocks[-1, i] = PALETTE[15 if (i // 2) % 2 == 0 else 0]
        blocks[-1, width_blocks - marker + i] = PALETTE[0 if (i // 2) % 2 == 0 else 15]
    blocks[1, :32] = np.repeat(PALETTE, 2)
    for start_row, data in ((HEADER_ROW, header), (PAYLOAD_ROW, payload)):
        raw = np.frombuffer(data, dtype=np.uint8)
        nibbles = np.empty(len(raw) * 2, dtype=np.uint8)
        nibbles[::2], nibbles[1::2] = raw & 15, raw >> 4
        start = start_row * width_blocks
        blocks.ravel()[start:start + len(nibbles)] = PALETTE[nibbles]
    # FrameRaster.WriteEndMarker overwrites only the FIRST 16 blocks, after finder/payload.
    for x in range(min(16, width_blocks)):
        blocks[-1, x] = PALETTE[15 if x % 2 == 0 else 0]
    return np.repeat(np.repeat(blocks, BLOCK, axis=0), BLOCK, axis=1)


def source_paths(args):
    core = (args.core_package or Path(__file__).resolve().parents[2] / "Packages/com.kibalab.tsmp.core").resolve()
    codec = args.codec_package
    if codec is None:
        candidates = [core.parent / "com.kibalab.tsmp.codec.luma4"]
        candidates.extend(parent / "projects/Native/Packages/com.kibalab.tsmp.codec.luma4" for parent in args.template.resolve().parents)
        codec = next((path for path in candidates if path.is_dir()), None)
    require(codec is not None, "Cannot locate codec source; supply --codec-package")
    codec = codec.resolve()
    paths = [Path(__file__).resolve()]
    paths.extend(core / relative for relative in CORE_FILES)
    paths.extend(codec / relative for relative in CODEC_FILES)
    for path in paths:
        require(path.is_file(), f"Source provenance file missing: {path}")
    return core, codec, {str(path): sha256(path) for path in paths}


def templates(directory):
    paths = {}
    for path in directory.glob("*.png"):
        match = FRAME_NAME.fullmatch(path.name)
        require(match is not None, f"Unexpected template PNG: {path}")
        index = int(match.group(1))
        require(index not in paths, f"Duplicate template numeric ID: {index}")
        paths[index] = path
    require(sorted(paths) == list(range(1, 13)), "Expected exactly the original 12 template frames")
    decoded, hashes, rows = {}, {}, []
    for frame, path in sorted(paths.items()):
        rgb, calibration, header, payload = decode(path)
        require(rgb.shape == (720, 1280, 3), f"Expected original 1280x720 template: {path}")
        require(u32(header, 24) == frame and u16(header, 40) == 0x0100, "Unexpected template frame ID/type")
        require(len(payload) == LENGTHS[(frame - 1) % 3], "Unexpected original payload length cycle")
        validate_payload(payload, frame)
        require(np.array_equal(calibration, PALETTE), "Original palette differs from current Luma4 palette")
        require(np.array_equal(rgb, np.repeat(render(1280, 720, header, payload)[:, :, None], 3, axis=2)),
                f"Python raster does not reproduce original template pixels: {path}")
        decoded[frame] = (header, payload)
        hashes[str(path.resolve())] = sha256(path)
        rows.append(dict(frame=frame, bytes=len(payload), headerCrc32=f"{u32(header, 52):08X}", payloadCrc32=f"{u32(header, 44):08X}", pixelModelExact=True))
    return decoded, hashes, rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--template", required=True, type=Path, help="Original VideoTransport input directory")
    parser.add_argument("--results", required=True, type=Path, help="New empty destination")
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--frames", type=int, default=300)
    parser.add_argument("--fps", default="30", help="Positive rational, e.g. 30 or 30000/1001")
    parser.add_argument("--pattern", choices=("structured", "random"), default="structured")
    parser.add_argument("--seed", type=int, default=20261030)
    parser.add_argument("--core-package", type=Path)
    parser.add_argument("--codec-package", type=Path)
    args = parser.parse_args()
    fps = Fraction(args.fps)
    require(fps > 0 and 1 <= args.frames <= 999999, "Require positive fps and 1..999999 frames")
    require(args.width > 0 and args.height > 0 and args.width % BLOCK == 0 and args.height % BLOCK == 0, "Dimensions must be positive block8 multiples")
    require(38 <= args.width // BLOCK <= 65535 and 7 <= args.height // BLOCK <= 65535, "Geometry exceeds header/raster limits")
    require(args.seed >= 0, "Seed must be nonnegative")
    destination = args.results.resolve()
    require(not destination.exists() or (destination.is_dir() and not any(destination.iterdir())), "Choose a new, empty results directory")
    require(zlib.crc32(b"123456789") == 0xCBF43926, "CRC32 standard vector failed")
    core, codec, hashes = source_paths(args)
    originals, template_hashes, template_checks = templates(args.template.resolve())
    # Check capacity before writing any evidence.
    render(args.width, args.height, originals[3][0], originals[3][1])
    for name in ("input", "payload", "header"):
        (destination / name).mkdir(parents=True, exist_ok=True)
    rng = np.random.Generator(np.random.PCG64(args.seed))
    rows = []
    for frame in range(1, args.frames + 1):
        template_header, template_payload = originals[(frame - 1) % 3 + 1]
        header, payload = bytearray(template_header), bytearray(template_payload)
        struct.pack_into("<I", payload, 4, frame)
        struct.pack_into("<H", payload, 12, frame & 0xffff)
        value_length = len(payload) - VALUE_OFFSET
        if args.pattern == "random":
            value = rng.integers(0, 256, size=value_length, dtype=np.uint8)
        else:
            indices = np.arange(value_length, dtype=np.uint64)
            value = ((frame * 29 + indices * 73 + (indices >> 8)) & 255).astype(np.uint8)
        payload[VALUE_OFFSET:] = value.tobytes()
        timestamp = ((frame - 1) * 1000 * fps.denominator // fps.numerator) & 0xffffffff
        struct.pack_into("<HH", header, 14, args.width // BLOCK, args.height // BLOCK)
        struct.pack_into("<II", header, 24, frame, timestamp)
        struct.pack_into("<I", header, 44, zlib.crc32(payload))
        struct.pack_into("<I", header, 52, zlib.crc32(header[:52]))
        validate_payload(payload, frame)
        name = f"frame-{frame:06d}"
        png = destination / "input" / (name + ".png")
        Image.fromarray(render(args.width, args.height, header, payload)).convert("RGB").save(png, format="PNG")
        (destination / "payload" / (name + ".bin")).write_bytes(payload)
        (destination / "header" / (name + ".bin")).write_bytes(header)
        rows.append(dict(frame=frame, timestampMs=timestamp, bytes=len(payload), headerCrc32=f"{u32(header, 52):08X}", payloadCrc32=f"{u32(header, 44):08X}", pngSHA256=sha256(png), payloadSHA256=hashlib.sha256(payload).hexdigest(), headerSHA256=hashlib.sha256(header).hexdigest()))
    selected = sorted({1, min(2, args.frames), min(3, args.frames), max(1, args.frames // 2), args.frames})
    checks = []
    for frame in selected:
        name = f"frame-{frame:06d}"
        _, _, header, payload = decode(destination / "input" / (name + ".png"))
        require(header == (destination / "header" / (name + ".bin")).read_bytes(), f"Generated PNG header mismatch: {frame}")
        require(payload == (destination / "payload" / (name + ".bin")).read_bytes(), f"Generated PNG payload mismatch: {frame}")
        validate_payload(payload, frame)
        checks.append(dict(frame=frame, headerCrcValid=True, payloadCrcValid=True, fullBytesExact=True))
    with (destination / "source-frames.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    manifest = dict(profiles=[dict(name="png-control", framesDirectory=str(destination / "input"), expectedDirectory=str(destination / "input"), expectedPayloadDirectory=str(destination / "payload"))])
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    summary = dict(status="PASS", scope="CPU fixture generation/model checks only; no GPU, OBS, stream, live pacing, state/RPC or client certification", width=args.width, height=args.height, blockSize=BLOCK, sampleSize=4, frames=args.frames, fps=str(fps), durationSeconds=float(Fraction(args.frames, 1) / fps), pattern=args.pattern, seed=args.seed, randomGenerator="NumPy PCG64", python=sys.version, numpy=np.__version__, pillow=pillow_version, platform=platform.platform(), corePackage=str(core), codecPackage=str(codec), templateDirectory=str(args.template.resolve()), sourceHashes=hashes, templateHashes=template_hashes, templateChecks=template_checks, generatedSampleChecks=checks, limitations="Model uses float64 central4x4 means and calibrated nearest symbols; generated PNG checks are selected samples, not all-frame GPU validation. Timestamp is sender-relative and must not be subtracted from receiver time. A finite image sequence has no live timing.")
    (destination / "fixture.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps({key: summary[key] for key in ("status", "width", "height", "frames", "fps", "pattern", "generatedSampleChecks")}, indent=2))


if __name__ == "__main__":
    main()
