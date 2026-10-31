"""Encode bounded budget fixtures and independently audit applied-state replays.

No model-derived color decoding is used. GPU verdicts and complete accepted raw
payloads come from the unmodified production TSMPDecoder in the Native harness.
"""
from __future__ import annotations

import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import subprocess
import zlib

FRAMES, FPS = 120, 30
FIELDS = (("critical", 64, 1), ("medium", 512, 3), ("bulk", 2048, 6))
RPC_METHOD = b"ReceiveBudgetEvent"


def require(condition, text):
    if not condition:
        raise ValueError(text)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def csv_rows(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def save(path, data):
    Path(path).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def generation(ordinal, period):
    return 1 + (ordinal - 1) // period * period


def value(field, gen, size):
    state = (0x9e3779b9 ^ (gen * 0x85ebca6b) ^ ((field + 1) * 0xc2b2ae35)) & 0xffffffff
    result = bytearray(size)
    for index in range(size):
        state ^= (state << 13) & 0xffffffff
        state ^= state >> 17
        state ^= (state << 5) & 0xffffffff
        result[index] = state & 0xff
    struct.pack_into("<I", result, 0, gen)
    return bytes(result)


def fnv(data):
    result = 2166136261
    for byte in data:
        result = ((result ^ byte) * 16777619) & 0xffffffff
    return result


def packet(data, field_hashes):
    require(len(data) >= 8 and data[0] == 1, "Invalid outgoing network header")
    messages = struct.unpack_from("<H", data, 2)[0]
    cursor, fields, events = 8, {}, []
    for _ in range(messages):
        require(cursor + 8 <= len(data), "Invalid outgoing message")
        network, kind, _, _, length = struct.unpack_from("<HBBHH", data, cursor)
        require(network == 1, "Unexpected source network ID")
        end = cursor + 8 + length
        require(end <= len(data), "Message outside source payload")
        cursor += 8
        if kind == 1:
            count = struct.unpack_from("<H", data, cursor)[0]
            cursor += 2
            for _ in range(count):
                require(cursor + 7 <= end, "Invalid outgoing field")
                key, field_type, size = struct.unpack_from("<IBH", data, cursor)
                cursor += 7
                require(key in field_hashes and field_type == 8 and cursor + size <= end, "Unknown source field/schema")
                field = field_hashes[key]
                require(field not in fields, "Source repeated field")
                body = data[cursor:cursor + size]
                require(size == FIELDS[field][1], "Wrong source field size")
                gen = struct.unpack_from("<I", body)[0]
                require(body == value(field, gen, size), "Source field differs from independent semantic schedule")
                fields[field] = gen
                cursor += size
        elif kind == 2:
            rpc_hash, count = struct.unpack_from("<IB", data, cursor)
            cursor += 5
            require(rpc_hash == fnv(RPC_METHOD) and count == 2, "Unexpected source RPC")
            args = []
            for _ in range(count):
                require(cursor + 3 <= end, "Invalid source RPC argument")
                arg_type, size = struct.unpack_from("<BH", data, cursor)
                cursor += 3
                require(cursor + size <= end, "RPC argument outside source message")
                args.append((arg_type, data[cursor:cursor + size]))
                cursor += size
            require(args[0] == (7, RPC_METHOD) and args[1][0] == 2 and len(args[1][1]) == 4, "Wrong source RPC values")
            events.append(struct.unpack("<i", args[1][1])[0])
        else:
            raise ValueError("Unknown source message type")
        require(cursor == end, "Trailing bytes in source message")
    require(cursor == len(data), "Trailing bytes in source payload")
    return fields, events


def audit_sources(root):
    require((root / "result.txt").read_text(encoding="utf-8-sig").splitlines()[0] == "PASS", "Source generator did not pass")
    definitions = csv_rows(root / "fields.csv")
    require([row["field"] for row in definitions] == [item[0] for item in FIELDS], "Field definitions changed")
    require([int(row["hash"]) for row in definitions] == [fnv(f"BudgetDeliveryProbe.delivery.{name}".encode()) for name, _, _ in FIELDS], "Field hashes differ from independent stable-key calculation")
    hashes = {int(row["hash"]): index for index, row in enumerate(definitions)}
    manifest = load(root / "fixtures.json")
    require(len(manifest["profiles"]) == 6, "Expected six source profiles")
    source_packets, receipts, sizes, header_templates = {}, [], {}, {}
    require({p["name"] for p in manifest["profiles"]} == {f"{codec}-t{target}" for codec in ("luma4", "color256") for target in (0, 1024, 2304)}, "Unexpected source policy/profile set")
    for profile in manifest["profiles"]:
        folder = Path(profile["fixture"])
        rows = csv_rows(folder / "source-frames.csv")
        require(len(rows) == FRAMES, "Wrong source frame count")
        sizes[profile["name"]] = []
        for ordinal, row in enumerate(rows, 1):
            require(int(row["ordinal"]) == ordinal, "Source mapping is not contiguous")
            stem = f"frame-{ordinal:06d}"
            header_path, payload_path, png_path = (folder / "header" / f"{stem}.bin", folder / "payload" / f"{stem}.bin", folder / "input" / f"{stem}.png")
            header, data = header_path.read_bytes(), payload_path.read_bytes()
            require(len(header) == 56 and zlib.crc32(header[:52]) == struct.unpack_from("<I", header, 52)[0], "Invalid source header CRC")
            require(struct.unpack_from("<I", header, 24)[0] == ordinal and struct.unpack_from("<H", header, 42)[0] == len(data), "Wrong source header ID/length")
            require(struct.unpack_from("<I", header, 20)[0] == 20261031, "Source StreamId differs from frozen shared value")
            require(struct.unpack_from("<H", header, 32)[0] == {"Luma4": 0, "Color256": 3}[profile["codec"]], "Wrong source codec ID")
            require(struct.unpack_from("<HHHH", header, 12) == (8, 160, 90, 0) and header[50] == 4, "Source block/geometry/layout/sample differs")
            canonical_header = bytearray(header)
            for start, count in ((24, 8), (42, 6), (52, 4)):
                canonical_header[start:start + count] = bytes(count)
            template = header_templates.setdefault(profile["codec"], bytes(canonical_header))
            require(bytes(canonical_header) == template, "Codec/options/shared header fields vary across same-codec budgets")
            require(zlib.crc32(data) == struct.unpack_from("<I", header, 44)[0], "Invalid source payload CRC")
            require(struct.unpack_from("<H", header, 10)[0] & 1 and header[5] >= 2, "Unprotected source packet")
            require(sha(header_path) == row["headerSha256"] and sha(payload_path) == row["payloadSha256"] and sha(png_path) == row["pngSha256"], "Source hash mismatch")
            fields, events = packet(data, hashes)
            for index, gen in fields.items():
                require(gen == generation(ordinal, FIELDS[index][2]), "Encoder sent a stale or future desired value")
            for event in events:
                require(1 <= event <= ordinal and (event - 1) % 15 == 0 and ordinal - event <= 3, "Unexpected repeat schedule")
            target = profile["target"]
            require(target == 0 or len(data) <= target, "Source exceeds target")
            gpu = row["gpuLuma4"].lower() == "true"
            require(gpu == (profile["codec"] == "Luma4"), "Actual encoder GPU/fallback path differs from frozen scope")
            if gpu:
                require(min(int(row[key]) for key in ("gpuSymbolWidth", "gpuSymbolHeight", "gpuUploadBytes")) > 0, "GPU path lacks recorded resources")
            source_packets[(profile["name"], ordinal)] = (data, fields, events)
            sizes[profile["name"]].append(len(data))
            receipts.append({"profile": profile["name"], "ordinal": ordinal, "headerSha256": sha(header_path), "payloadSha256": sha(payload_path), "pngSha256": sha(png_path)})
    return manifest, source_packets, receipts, sizes


def execute(command, log):
    with Path(log).open("w", encoding="utf-8") as handle:
        result = subprocess.run(command, stdout=handle, stderr=subprocess.STDOUT, text=True, timeout=900)
    require(result.returncode == 0, f"Command failed; retain {log}")


def encode(args):
    root, target = args.fixtures.resolve(), args.output.resolve()
    require(not target.exists(), "Use a new encoding output directory")
    target.mkdir(parents=True)
    manifest, _, receipts, sizes = audit_sources(root)
    save(target / "source-audit.json", {"status": "PASS", "receipts": receipts, "sizes": sizes})
    plans, profiles, observations = [], [], []
    for source in manifest["profiles"]:
        folder = target / source["name"]
        folder.mkdir()
        decoded = folder / "decoded"
        decoded.mkdir()
        video = folder / "video.mp4"
        command = [args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-framerate", str(FPS), "-start_number", "1", "-i", str(Path(source["fixture"]) / "input/frame-%06d.png"),
                   "-frames:v", str(FRAMES), "-an", "-vf", "scale=in_range=full:out_range=limited:out_color_matrix=bt709,format=yuv420p", "-c:v", "libx264", "-preset", "veryfast", "-g", "60", "-bf", "0", "-pix_fmt", "yuv420p", "-r", str(FPS), "-fps_mode", "cfr",
                   "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv", "-keyint_min", "60", "-sc_threshold", "0", "-b:v", "3500000", "-minrate", "3500000", "-maxrate", "3500000", "-bufsize", "7000000", "-x264-params", "nal-hrd=cbr:force-cfr=1:filler=1", str(video)]
        decode = [args.ffmpeg, "-n", "-hide_banner", "-loglevel", "info", "-i", str(video), "-map", "0:v:0", "-vf", "scale=in_range=limited:out_range=full:in_color_matrix=bt709,format=rgb24", "-fps_mode", "passthrough", "-start_number", "1", str(decoded / "frame-%06d.png")]
        plans.append({"source": source, "encode": command, "decode": decode})
        save(target / "commands.json", plans)
        execute(command, folder / "encode.log")
        execute(decode, folder / "decode.log")
        require({path.name for path in decoded.glob("*.png")} == {f"frame-{i:06d}.png" for i in range(1, FRAMES + 1)}, "Decoded frames not exactly mapped")
        info = json.loads(subprocess.check_output([args.ffprobe, "-v", "error", "-show_streams", "-show_frames", "-show_packets", "-of", "json", str(video)], text=True))
        save(folder / "ffprobe.json", info)
        packet_rows = info.get("packets", [r for r in info.get("packets_and_frames", []) if r.get("type") == "packet"])
        frame_rows = info.get("frames", [r for r in info.get("packets_and_frames", []) if r.get("type") == "frame"])
        require(len(frame_rows) == FRAMES, "Video frame count mismatch")
        total_packet_bytes = sum(int(row["size"]) for row in packet_rows if row.get("codec_type") == "video")
        observations.append({"name": source["name"], "videoSha256": sha(video), "decodedFrames": len(frame_rows), "packetBytes": total_packet_bytes, "packetMbps": total_packet_bytes * 8 / (FRAMES / FPS) / 1e6,
                             "decodedHashes": [{"file": path.name, "sha256": sha(path)} for path in sorted(decoded.glob("*.png"))]})
        png = dict(source, name=source["name"] + "-png")
        cbr = dict(source, name=source["name"] + "-x264-cbr3.5", frames=str(decoded))
        profiles += [png, cbr]
    held = next(source for source in manifest["profiles"] if source["name"] == "color256-t2304")
    profiles.append(dict(held, name="color256-t2304-png-burst-hold", burstHold=True))
    save(target / "replay-plan.json", {"profiles": profiles, "encodingSummaryPath": str(target / "encoding-summary.json")})
    save(target / "encoding-summary.json", {"status": "PASS", "finishedUtc": datetime.now(timezone.utc).isoformat(), "profiles": observations,
                                             "scope": "Finite offline video encode/decode; not a streaming service or live delivery"})
    print(json.dumps({"status": "PASS", "videos": len(observations), "replayProfiles": len(profiles)}))


def distribution(numbers):
    if not numbers:
        return None
    numbers = sorted(numbers)
    return {"samples": len(numbers), "mean": sum(numbers) / len(numbers), "p50": numbers[math.ceil(.5 * len(numbers)) - 1], "p95": numbers[math.ceil(.95 * len(numbers)) - 1], "min": numbers[0], "max": numbers[-1]}


def source_receipt_check(folder):
    before, after = (csv_rows(folder / name) for name in ("source-hashes-before.csv", "source-hashes-after.csv"))
    require(before == after, "Package or harness changed during run")
    for row in after:
        require(sha(row["path"]) == row["sha256"], "Recorded source no longer matches")
    log = (folder / "editor.log").read_text(encoding="utf-8-sig", errors="replace")
    require(not re.search(r"error\s+CS\d+|scripts have compiler errors|script compilation failed", log, re.I), "Compiler error in final run")
    return len(after)


def analyze(args):
    source, replay = args.fixtures.resolve(), args.replay.resolve()
    manifest, packets, receipts, _ = audit_sources(source)
    require((replay / "result.txt").read_text(encoding="utf-8-sig").splitlines()[0] == "PASS", "Replay failed")
    plan = load(replay / "plan-used.json")
    encoding_path = Path(plan["encodingSummaryPath"])
    encoding = load(encoding_path)
    require(encoding["status"] == "PASS", "Encoding did not pass")
    encoded_hashes = {}
    for encoded_profile in encoding["profiles"]:
        folder = encoding_path.parent / encoded_profile["name"]
        require(sha(folder / "video.mp4") == encoded_profile["videoSha256"], "Encoded video hash differs")
        for row in encoded_profile["decodedHashes"]:
            image = folder / "decoded" / row["file"]
            require(sha(image) == row["sha256"], "Restored video image hash differs")
            encoded_hashes[str(image)] = row["sha256"]
    source_comparisons = source_receipt_check(source) + source_receipt_check(replay)
    summaries, accepted_audit = [], []
    for profile in plan["profiles"]:
        rows = csv_rows(replay / f"{profile['name']}.csv")
        require(len(rows) == FRAMES, "Replay count mismatch")
        source_name = Path(profile["fixture"]).name
        state = [0, 0, 0]
        seen = [set(), set(), set()]
        events, expected_event_rows = set(), []
        metrics = [{"sourceAgeMs": [], "generationLagMs": [], "unseenFrames": 0, "currentFrames": 0} for _ in FIELDS]
        exact, rejected, duplicates, retained = 0, 0, 0, 0
        for ordinal, row in enumerate(rows, 1):
            require(int(row["ordinal"]) == ordinal, "Replay is not contiguous")
            source_ordinal = 44 if profile.get("burstHold") and 45 <= ordinal <= 56 else ordinal
            require(int(row["sourceOrdinal"]) == source_ordinal, "Burst/source mapping mismatch")
            image = Path(profile["frames"]) / f"frame-{source_ordinal:06d}.png"
            require(sha(image) == row["inputSha256"], "Replayed image differs from captured input")
            if "x264" in profile["name"]:
                require(encoded_hashes.get(str(image)) == row["inputSha256"], "Replay image not bound to encoded video evidence")
            data, fields, outgoing_events = packets[(source_name, source_ordinal)]
            expected_added = 0
            if row["status"] == "EXACT":
                require(int(row["frameIndex"]) == source_ordinal and int(row["bytes"]) == len(data), "Accepted ID/length mismatch")
                actual = replay / f"{profile['name']}-accepted/frame-{ordinal:06d}.bin"
                require(actual.read_bytes() == data and sha(actual) == row["decodedSha256"], "Accepted raw bytes differ from outgoing original")
                for index, gen in fields.items():
                    state[index] = gen
                    seen[index].add(gen)
                for event in outgoing_events:
                    if event not in events:
                        events.add(event)
                        expected_added += 1
                        expected_event_rows.append({"eventId": str(event), "deliveryOrdinal": str(ordinal)})
                exact += 1
                retained += len(data)
                accepted_audit.append({"profile": profile["name"], "ordinal": ordinal, "sha256": sha(actual)})
            elif row["status"] == "REJECTED":
                rejected += 1
            else:
                require(row["status"] == "DUPLICATE" and profile.get("burstHold"), "Unexpected replay status")
                duplicates += 1
            require([int(row[f"{field[0]}Generation"]) for field in FIELDS] == state, "Applied field generations differ from independent packet model")
            require(int(row["eventsAdded"]) == expected_added, "RPC application/dedup differs from independent model")
            for index, field in enumerate(FIELDS):
                desired = generation(ordinal, field[2])
                if not state[index]:
                    metrics[index]["unseenFrames"] += 1
                else:
                    metrics[index]["sourceAgeMs"].append((ordinal - state[index]) / FPS * 1000)
                    metrics[index]["generationLagMs"].append((desired - state[index]) / FPS * 1000)
                    metrics[index]["currentFrames"] += int(desired == state[index])
        require(csv_rows(replay / f"{profile['name']}-events.csv") == expected_event_rows, "RPC event trace mismatch")
        if profile["name"].endswith("-png"):
            require((exact, rejected, duplicates) == (FRAMES, 0, 0), "Pristine GPU control failed; cannot label it video loss")
        if profile.get("burstHold"):
            require((exact, rejected, duplicates) == (FRAMES - 12, 0, 12), "Held-image GPU control failed")
            require(events == set(range(1, FRAMES + 1, 15)) - {46}, "Held-image event coverage differs from deliberate complete repeat loss")
        fields_summary = {}
        for index, (name, size, period) in enumerate(FIELDS):
            fields_summary[name] = {"desiredUpdates": FRAMES // period, "uniqueAppliedUpdates": len(seen[index]), "appliedUniqueBytes": len(seen[index]) * size,
                                    "unseenFrames": metrics[index]["unseenFrames"], "currentStateFrames": metrics[index]["currentFrames"],
                                    "sourceAgeMs": distribution(metrics[index]["sourceAgeMs"]), "desiredGenerationLagMs": distribution(metrics[index]["generationLagMs"])}
        valid_timing = [row for row in rows if row["warm"].lower() == "false"]
        source_rows = csv_rows(Path(profile["fixture"]) / "source-frames.csv")
        summaries.append({"profile": profile["name"], "codec": profile["codec"], "target": profile["target"], "observations": FRAMES,
                          "exact": exact, "rejected": rejected, "duplicates": duplicates, "retainedCompletePayloadBytes": retained,
                          "sourcePayloadBytes": sum(int(row["bytes"]) for row in source_rows),
                          "desiredUniqueStateBytes": sum(FRAMES // period * size for _, size, period in FIELDS),
                          "appliedUniqueStateBytes": sum(len(seen[index]) * field[1] for index, field in enumerate(FIELDS)),
                          "fields": fields_summary, "desiredEvents": 8, "deliveredEvents": len(events), "missedEventIds": sorted(set(range(1, FRAMES + 1, 15)) - events),
                          "rpcMediaDelayMs": distribution([(int(row["deliveryOrdinal"]) - int(row["eventId"])) / FPS * 1000 for row in expected_event_rows]),
                          "sourceGpuLuma4Frames": sum(row["gpuLuma4"].lower() == "true" for row in source_rows),
                          "sourceGpuSymbolWidths": sorted({int(row["gpuSymbolWidth"]) for row in source_rows}),
                          "sourceGpuSymbolHeights": sorted({int(row["gpuSymbolHeight"]) for row in source_rows}),
                          "sourceGpuUploadByteCapacities": sorted({int(row["gpuUploadBytes"]) for row in source_rows}),
                          "encodeCpuSubmitMs": distribution([float(row["encodeMs"]) for row in source_rows if row["warm"].lower() == "false"]),
                          "decodeCpuSubmitMs": distribution([float(row["submitMs"]) for row in valid_timing]),
                          "decodeCompletionWallMs": distribution([float(row["completionWallMs"]) for row in valid_timing])})
    output = {"status": "PASS", "finishedUtc": datetime.now(timezone.utc).isoformat(), "sourceComparisons": source_comparisons,
              "sourceFrames": len(receipts), "acceptedByteRechecks": len(accepted_audit), "summaries": summaries,
              "limitations": ["Native Unity Editor on this device; no Udon or live VRChat performance claim.", "Offline media ordinal age excludes capture, network and player buffering latency.", "Encode and decode submission CPU scopes exclude PNG/file I/O and GPU completion; completion wall time includes asynchronous waits and host scheduling.", "First ten observations excluded from timing; no paired statistical significance claim.", "Fixed priority and atomic fields can starve bulk updates at target1024; no adaptive budget or fragmentation added.", "Source binary references are captured from actual Encoder output before video compression, not reconstructed from compressed images.", "Field values/events, StreamId, frame index and codec options match across budgets; normal Encoder host timestamps vary, so complete protected headers are not bit-identical paired controls."]}
    save(args.output, output)
    save(args.output.with_name("accepted-byte-audit.json"), {"status": "PASS", "cases": accepted_audit})
    print(json.dumps({"status": "PASS", "profiles": len(summaries), "acceptedByteRechecks": len(accepted_audit), "sourceComparisons": source_comparisons}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="mode", required=True)
    encoder = subs.add_parser("encode")
    encoder.add_argument("--fixtures", type=Path, required=True)
    encoder.add_argument("--output", type=Path, required=True)
    encoder.add_argument("--ffmpeg", default="C:/Windows/ffmpeg.exe")
    encoder.add_argument("--ffprobe", default="C:/Windows/ffprobe.exe")
    analyzer = subs.add_parser("analyze")
    analyzer.add_argument("--fixtures", type=Path, required=True)
    analyzer.add_argument("--replay", type=Path, required=True)
    analyzer.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    (encode if args.mode == "encode" else analyze)(args)


if __name__ == "__main__":
    main()
