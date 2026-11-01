#!/usr/bin/env python3
"""Decode an OBS recording and compare it with independent pre-raster fixtures.

Requires NumPy, Pillow and FFmpeg/ffprobe. This is an offline CPU symbol model,
not a GPU test or a network-latency measurement. A GPU comparison manifest is
written for TransportMeasurementValidation. No OBS/Unity process is started.
"""

import argparse
from collections import Counter
import csv
import importlib.util
import json
from pathlib import Path
import re
import struct
import sys
import zlib

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("streaming_measurement", HERE / "Measure-Streaming.py")
STREAM = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(STREAM)


def load_fixture(root):
    fixture = json.loads((root / "fixture.json").read_text(encoding="utf-8-sig"))
    if fixture.get("status") != "PASS" or (fixture.get("blockSize"), fixture.get("sampleSize")) != (8, 4):
        raise ValueError("Require a generated PASS fixture with block8/sample4")
    dimensions = (fixture["width"], fixture["height"])
    with (root / "source-frames.csv").open(encoding="utf-8-sig", newline="") as handle:
        source_rows = list(csv.DictReader(handle))
    ids = [int(row["frame"]) for row in source_rows]
    if ids != list(range(1, fixture["frames"] + 1)):
        raise ValueError("Fixture requires the complete unique source-ID sequence 1..N")
    references, provenance = {}, []
    for row in source_rows:
        identifier = int(row["frame"])
        stem = f"frame-{identifier:06d}"
        image = root / "input" / (stem + ".png")
        header_path, payload_path = [root / kind / (stem + ".bin") for kind in ("header", "payload")]
        header, payload = header_path.read_bytes(), payload_path.read_bytes()
        if len(header) != 56 or header[:4] != b"TSMP" or header[4] != 0 or struct.unpack_from("<H", header, 8)[0] != 56:
            raise ValueError(f"Invalid independent source header: {header_path}")
        if (struct.unpack_from("<I", header, 24)[0] != identifier
                or struct.unpack_from("<I", header, 28)[0] != int(row["timestampMs"])
                or zlib.crc32(header[:52]) != struct.unpack_from("<I", header, 52)[0]
                or len(payload) != struct.unpack_from("<H", header, 42)[0]
                or len(payload) != int(row["bytes"])
                or zlib.crc32(payload) != struct.unpack_from("<I", header, 44)[0]
                or not struct.unpack_from("<H", header, 10)[0] & 1 or header[5] < 2):
            raise ValueError(f"Source raw frame identity/CRC self-check failed: {identifier}")
        for path, hash_key in [(image, "pngSHA256"), (header_path, "headerSHA256"), (payload_path, "payloadSHA256")]:
            if STREAM.sha256(path) != row[hash_key]:
                raise ValueError(f"Independent source provenance mismatch: {path}")
        if struct.unpack_from("<HHH", header, 12) != (8, dimensions[0] // 8, dimensions[1] // 8) or header[50] != 4:
            raise ValueError("Fixture header layout does not match its native dimensions")
        references[identifier] = dict(header=header, payload=payload, expectedFile=image.name,
                                      payloadFile=payload_path.name, timestampMs=int(row["timestampMs"]))
        provenance.append(dict(sourceId=identifier, expectedFile=image.name, payloadFile=payload_path.name,
                               headerSha256=row["headerSHA256"], payloadSha256=row["payloadSHA256"],
                               inputSha256=row["pngSHA256"], payloadBytes=len(payload)))
    return fixture, dimensions, references, provenance


def differences(actual, expected):
    return abs(len(actual) - len(expected)) + sum(left != right for left, right in zip(actual, expected))


def inspect_frame(path, dimensions, references, header_only=False):
    rgb, _, calibration, symbols, _ = STREAM.samples(path)
    width, height = dimensions
    if rgb.shape != (height, width, 3):
        raise ValueError(f"Decoded recording changed native dimensions: {path}")
    columns = width // 8
    header = STREAM.MODEL.bytes_at(symbols, columns * 2, 56)
    if len(header) != 56:
        raise ValueError("Native header region does not contain 56 bytes")
    stored_header_crc, computed_header_crc = struct.unpack_from("<I", header, 52)[0], zlib.crc32(header[:52])
    raw_id, raw_timestamp = struct.unpack_from("<II", header, 24)
    base_metadata = header[:4] == b"TSMP" and header[4] == 0 and struct.unpack_from("<H", header, 8)[0] == 56
    header_crc_valid = stored_header_crc == computed_header_crc
    checked_id = raw_id if base_metadata and header_crc_valid else None
    reference = references.get(checked_id)
    unknown = reference is None
    row = dict(file=path.name, expectedFrameUnknown=unknown,
               rawHeaderFrameIndex=raw_id, rawHeaderTimestampMs=raw_timestamp,
               crcCheckedHeaderFrameIndex=checked_id, resolvedSourceId=checked_id if reference else None,
               expectedFile=reference["expectedFile"] if reference else "",
               expectedTimestampMs=reference["timestampMs"] if reference else None,
               headerBasicMetadataValid=bool(base_metadata), cpuHeaderCrcValid=bool(header_crc_valid),
               cpuHeaderStoredCrc32=stored_header_crc, cpuHeaderComputedCrc32=computed_header_crc,
               sourceHeaderByteErrors=differences(header, reference["header"]) if reference else None,
               expectedPayloadBytes=len(reference["payload"]) if reference else None,
               decodedPayloadBytes=None, cpuPayloadCrcPresent=bool(struct.unpack_from("<H", header, 10)[0] & 1),
               cpuPayloadCrcChecked=False, cpuPayloadCrcVerified=False,
               cpuPayloadStoredCrc32=None, cpuPayloadComputedCrc32=None,
               payloadByteErrors=None, calibrationMinGap=float(STREAM.np.diff(calibration).min()),
               status="HEADER_REJECT", reason="")
    mapping = dict(file=path.name, expectedFrameUnknown=unknown, frameIndexSpecified=True,
                   frameIndex=checked_id if reference else 0)
    if reference:
        mapping.update(expectedFile=reference["expectedFile"], payloadFile=reference["payloadFile"])
    if not base_metadata:
        row["reason"] = "Header magic, major version or byte size is invalid"
        return row, mapping
    if not header_crc_valid:
        row["reason"] = "Header CRC mismatch; source ID and payload length are untrusted"
        return row, mapping
    if not row["cpuPayloadCrcPresent"] or header[5] < 2:
        row["reason"] = "Protected >=0.2 payload checksum is required"
        return row, mapping
    block, active_width, active_height, layout = struct.unpack_from("<HHHH", header, 12)
    if (block, active_width, active_height, layout, header[50]) != (8, columns, height // 8, 0, 4):
        row["reason"] = "Header layout is outside this block8/sample4 CPU model"
        return row, mapping
    if header_only:
        # Color codecs share the grayscale base-header raster, but have distinct
        # payload palettes, calibration rows and bit packing. Never apply the
        # Luma4 payload model to them. The production GPU validates full bytes.
        row.update(status="HEADER_MATCH_PAYLOAD_UNMEASURED" if reference and not row["sourceHeaderByteErrors"]
                   else "UNKNOWN_REFERENCE" if unknown else "HEADER_CHANGED",
                   reason="Base header association only; complete payload requires the production GPU decoder")
        return row, mapping
    count = struct.unpack_from("<H", header, 42)[0]
    if count * 2 > columns * (height // 8 - 6):
        row["reason"] = "Header payload size exceeds native raster capacity"
        return row, mapping
    payload = STREAM.MODEL.bytes_at(symbols, columns * 5, count)
    if len(payload) != count:
        raise ValueError("CPU model produced fewer bytes than the declared bounded payload")
    stored_payload_crc, computed_payload_crc = struct.unpack_from("<I", header, 44)[0], zlib.crc32(payload)
    row.update(decodedPayloadBytes=count, cpuPayloadCrcChecked=True,
               cpuPayloadStoredCrc32=stored_payload_crc, cpuPayloadComputedCrc32=computed_payload_crc,
               cpuPayloadCrcVerified=stored_payload_crc == computed_payload_crc)
    if reference:
        row["payloadByteErrors"] = differences(payload, reference["payload"])
    if stored_payload_crc != computed_payload_crc:
        row.update(status="PAYLOAD_REJECT", reason="Payload CRC mismatch")
    elif unknown:
        row.update(status="UNKNOWN_REFERENCE", reason="CRC-valid frame has no independent known source ID; accuracy cannot be established")
    elif row["sourceHeaderByteErrors"] or row["payloadByteErrors"]:
        row.update(status="CHANGED", reason="CRC checks pass but independent source bytes differ")
    else:
        row.update(status="EXACT", reason="Header and complete payload match independent source raw bins")
    return row, mapping


def write_csv(path, rows):
    if not rows:
        raise ValueError("No recording frames were analyzed")
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def analyze(args):
    recording = args.recording.resolve(strict=True)
    fixture_root = args.source_fixture.resolve(strict=True)
    results = args.results.resolve()
    results.mkdir(parents=True, exist_ok=True)
    if any(results.iterdir()):
        raise ValueError("Choose a new empty results directory; existing evidence is preserved")
    if args.header_only_association and not args.codec_prefab_path:
        raise ValueError("Header-only association requires --codec-prefab-path for the production GPU check")
    summary = dict(status="INCOMPLETE", startedUtc=STREAM.utc_now(), recording=str(recording),
                   sourceFixture=str(fixture_root),
                   scope=("Offline base-header association of actual OBS recording; payload accuracy UNMEASURED until GPU validation"
                          if args.header_only_association else
                          "Offline CPU model of actual OBS-rendered/NVENC recording; independent source-bin comparison, not new GPU validation"),
                   limitations=["Recording may include pre-restart playback, repeated frames, source transitions and a held tail. A PAUSED API response does not certify frozen output pixels.",
                                "Recording PTS and frame rate do not establish network latency, source age, live delivery rate or applied Hz.",
                                "Float64 CPU means can differ from GPU float32 classification at exact decision boundaries.",
                                "CRC-valid frame identity is not cryptographic authentication; independent source bytes are required for EXACT.",
                                "No RTMP/RTSP/HLS relay, VRCDN, Udon, VRChat client, state or RPC application is measured here."])
    STREAM.write_json(results / "summary.json", summary)
    try:
        fixture, dimensions, references, provenance = load_fixture(fixture_root)
        codec_ids = {struct.unpack_from("<H", reference["header"], 32)[0] for reference in references.values()}
        if not args.header_only_association and codec_ids != {0}:
            raise ValueError("The payload CPU model supports Luma4 only; use --header-only-association and the actual codec prefab")
        summary["referenceOrigin"] = "Generated pre-raster payload/header bins, SHA256 checked against source-frames.csv; never recovered from recording outputs"
        summary["fixture"] = {key: fixture[key] for key in ("width", "height", "frames", "fps", "durationSeconds", "pattern")}
        summary["provenance"] = dict(recordingSha256=STREAM.sha256(recording),
                                      scriptSha256=STREAM.sha256(Path(__file__)),
                                      streamingModelSha256=STREAM.sha256(HERE / "Measure-Streaming.py"),
                                      baseModelSha256=STREAM.sha256(HERE / "Analyze-Compression.py"),
                                      sourceCsvSha256=STREAM.sha256(fixture_root / "source-frames.csv"),
                                      references=provenance)
        for command, name in [(args.ffmpeg, "ffmpeg-version"), (args.ffprobe, "ffprobe-version")]:
            STREAM.run([command, "-version"], results, name, args.timeout)
        probe_text = STREAM.run([args.ffprobe, "-v", "error", "-select_streams", "v:0", "-show_streams",
                                 "-show_format", "-show_frames", "-count_frames", "-of", "json", str(recording)],
                                results, "ffprobe", args.timeout)
        probe = json.loads(probe_text)
        STREAM.write_json(results / "probe.json", probe)
        streams = probe.get("streams", [])
        if len(streams) != 1 or streams[0].get("codec_type") != "video":
            raise ValueError("Expected one selected video stream")
        stream = streams[0]
        if (stream.get("width"), stream.get("height")) != dimensions:
            raise ValueError("Recording dimensions differ from source fixture; native-size comparison is required")
        declared_range = stream.get("color_range")
        if declared_range not in ("pc", "tv"):
            raise ValueError("Recording lacks an explicit pc/tv range; do not guess the RGB conversion range")
        if stream.get("color_space") != "bt709":
            raise ValueError("This profile requires declared BT.709; do not guess a color matrix")
        decoded = results / "decoded"
        decoded.mkdir()
        color_range = "full" if declared_range == "pc" else "limited"
        decode_filter = STREAM.DECODE_FILTER.format(range=color_range)
        STREAM.run([args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-noautorotate", "-i", str(recording),
                    "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", decode_filter, "-fps_mode", "passthrough",
                    "-start_number", "1", str(decoded / "frame-%06d.png")], results, "decode", args.timeout)
        files = sorted(decoded.glob("frame-*.png"))
        if not files or [path.name for path in files] != [f"frame-{i:06d}.png" for i in range(1, len(files) + 1)]:
            raise ValueError("Decoded frame sequence is empty or noncontiguous")
        frames = probe.get("frames", [])
        if len(files) != len(frames) or int(stream.get("nb_read_frames", -1)) != len(files):
            raise ValueError("ffprobe frame count and passthrough PNG count differ")
        rows, mappings = [], []
        for ordinal, (path, frame) in enumerate(zip(files, frames), 1):
            row, mapping = inspect_frame(path, dimensions, references, args.header_only_association)
            row = dict(recordingFrame=ordinal,
                       pts=frame.get("pts"), ptsTime=frame.get("pts_time"),
                       bestEffortTimestamp=frame.get("best_effort_timestamp"),
                       bestEffortTimestampTime=frame.get("best_effort_timestamp_time"),
                       durationTime=frame.get("duration_time", frame.get("pkt_duration_time")),
                       keyFrame=frame.get("key_frame"), pictureType=frame.get("pict_type"), **row)
            rows.append(row)
            mappings.append(mapping)
        write_csv(results / "frames.csv", rows)
        manifest = dict(profiles=[dict(name=args.name, fixture=str(fixture_root), framesDirectory=str(decoded),
                                      expectedDirectory=str(fixture_root / "input"),
                                      expectedPayloadDirectory=str(fixture_root / "payload"), frameMappings=mappings)])
        if args.codec_prefab_path:
            manifest["profiles"][0]["codecPrefabPath"] = args.codec_prefab_path
        STREAM.write_json(results / "gpu-manifest.json", manifest)
        known = [row["resolvedSourceId"] for row in rows if row["resolvedSourceId"] is not None]
        exact = [row["resolvedSourceId"] for row in rows if row["status"] == "EXACT"]
        expected_ids = set(references)
        statuses = Counter(row["status"] for row in rows)
        leading = 0
        for row in rows:
            if row["resolvedSourceId"] != min(expected_ids):
                break
            leading += 1
        trailing = 0
        for row in reversed(rows):
            if row["resolvedSourceId"] != max(expected_ids):
                break
            trailing += 1
        pts = [STREAM.numeric(frame.get("best_effort_timestamp_time", frame.get("pts_time"))) for frame in frames]
        if any(value is None for value in pts):
            raise ValueError("A decoded frame lacks a finite presentation timestamp")
        intervals = [right - left for left, right in zip(pts, pts[1:])]
        summary.update(status="COMPLETE", finishedUtc=STREAM.utc_now(),
                       cpuAccuracyVerdict=("UNMEASURED_GPU_REQUIRED" if args.header_only_association else
                                           "PASS" if statuses.get("EXACT", 0) == len(rows) else "FAIL"),
                       exactSourceCoverageVerdict=("UNMEASURED_GPU_REQUIRED" if args.header_only_association else
                                                  "PASS" if set(exact) == expected_ids else "FAIL"),
                       observedRecordingFrames=len(rows), expectedUniqueSourceFrames=len(references),
                       countsByCpuStatus=dict(statuses), expectedFrameUnknownObservations=sum(row["expectedFrameUnknown"] for row in rows),
                       crcCheckedKnownHeaderObservations=len(known), uniqueKnownSourceIDs=sorted(set(known)),
                       duplicateKnownSourceObservations=len(known) - len(set(known)),
                       uniqueExactSourceIDs=None if args.header_only_association else sorted(set(exact)),
                       duplicateExactSourceObservations=None if args.header_only_association else len(exact) - len(set(exact)),
                       missingExpectedIDs=sorted(expected_ids - set(known)),
                       missingExactExpectedIDs=None if args.header_only_association else sorted(expected_ids - set(exact)),
                       leadingContiguousFirstSourceIDObservations=leading, trailingContiguousLastSourceIDObservations=trailing,
                       holdCountInterpretation="Content-based contiguous counts include the original first/last frame; request-time phases are not inferred from them.",
                       recordingVideo=dict(width=dimensions[0], height=dimensions[1], codec=stream.get("codec_name"),
                                           pixelFormat=stream.get("pix_fmt"), declaredColorRange=declared_range,
                                           declaredColorSpace=stream.get("color_space"), timeBase=stream.get("time_base"),
                                           rFrameRate=stream.get("r_frame_rate"), averageFrameRate=stream.get("avg_frame_rate"),
                                           firstPTSSeconds=pts[0], lastPTSSeconds=pts[-1],
                                           observedOutputFramesPerPTSSecond=(len(pts) - 1) / (pts[-1] - pts[0]) if pts[-1] > pts[0] else None,
                                           ptsIntervalSeconds=STREAM.distribution(intervals), nonIncreasingPTSIntervals=sum(value <= 0 for value in intervals)),
                       decodeFilter=decode_filter, gpuManifest=str(results / "gpu-manifest.json"),
                       crcDiagnostics="Stored/computed header CRC is recorded even for malformed metadata; payload CRC is computed only after base-header CRC, protected flag and supported-layout checks.")
        STREAM.write_json(results / "summary.json", summary)
        print(json.dumps({key: summary[key] for key in ("status", "cpuAccuracyVerdict", "exactSourceCoverageVerdict",
                         "observedRecordingFrames", "countsByCpuStatus", "expectedFrameUnknownObservations",
                         "missingExpectedIDs", "missingExactExpectedIDs", "gpuManifest")}, indent=2))
        return 0  # Rejected frames are characterization results, not process errors.
    except BaseException as error:
        summary.update(status="ERROR", finishedUtc=STREAM.utc_now(), error=type(error).__name__ + ": " + str(error))
        STREAM.write_json(results / "summary.json", summary)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--recording", required=True, type=Path)
    parser.add_argument("--source-fixture", required=True, type=Path)
    parser.add_argument("--results", required=True, type=Path)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    parser.add_argument("--name", default="obs-recording")
    parser.add_argument("--header-only-association", action="store_true",
                        help="Associate shared base-header IDs only; leave color payload accuracy to the GPU")
    parser.add_argument("--codec-prefab-path", help="Production codec prefab asset path for the GPU manifest")
    parser.add_argument("--timeout", default=600, type=float, help="Maximum seconds per FFmpeg/ffprobe command")
    args = parser.parse_args()
    try:
        return analyze(args)
    except Exception as error:
        print(type(error).__name__ + ": " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
