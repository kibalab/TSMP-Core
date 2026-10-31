"""Recount wall-clock Encoder evidence without calling production C# helpers."""
import argparse
import csv
import hashlib
import json
import re
import struct
import zlib
from collections import defaultdict
from pathlib import Path


def fnv(text):
    value = 2166136261
    for byte in text.encode("utf-8"):
        value = ((value ^ byte) * 16777619) & 0xFFFFFFFF
    return value


FIELDS = ("high", "medium", "slow")
HASHES = {fnv("RefreshRecoveryProbe.refresh." + name): i for i, name in enumerate(FIELDS)}
INTERVALS = [struct.unpack("<f", struct.pack("<f", n))[0] for n in (.1, .4, 1.5)]


def packet(root, name, header_name):
    raw, header = (root / name).read_bytes(), (root / header_name).read_bytes()
    u16 = lambda data, at: struct.unpack_from("<H", data, at)[0]
    u32 = lambda data, at: struct.unpack_from("<I", data, at)[0]
    assert len(header) == 56 and header[:4] == b"TSMP" and header[4] == 0 and header[5] >= 2
    assert u16(header, 8) == 56 and u16(header, 10) & 1
    assert zlib.crc32(header[:52]) == u32(header, 52)
    assert u16(header, 40) == 0x100 and u16(header, 42) == len(raw)
    assert zlib.crc32(raw) == u32(header, 44)
    assert raw[:2] == b"\x01\x00" and len(raw) >= 8
    frame, stream = u32(header, 24), u32(header, 20)
    assert u32(raw, 4) == frame
    values, rpcs, cursor = {}, [], 8
    for _ in range(u16(raw, 2)):
        assert cursor + 8 <= len(raw) and u16(raw, cursor) == 1
        end = cursor + 8 + u16(raw, cursor + 6)
        assert end <= len(raw) and u16(raw, cursor + 4) == (frame & 65535)
        kind, body = raw[cursor + 2], cursor + 8
        if kind == 1:
            count, at = u16(raw, body), body + 2
            for _ in range(count):
                assert at + 7 <= end and raw[at + 4] == 8
                key, length = u32(raw, at), u16(raw, at + 5)
                assert key in HASHES and HASHES[key] not in values and at + 7 + length <= end
                value = raw[at + 7:at + 7 + length]
                assert len(value) == 32 and len(set(value)) == 1
                values[HASHES[key]] = value
                at += 7 + length
            assert at == end
        else:
            assert kind == 2 and u32(raw, body) == fnv("ReceiveEvent") and raw[body + 4] == 2
            at = body + 5
            args = []
            for expected_type in (7, 2):
                assert at + 3 <= end and raw[at] == expected_type
                length = u16(raw, at + 1)
                assert at + 3 + length <= end
                args.append(raw[at + 3:at + 3 + length])
                at += 3 + length
            assert at == end and args[0] == b"ReceiveEvent" and args[1] == struct.pack("<i", 501)
            rpcs.append((stream, 501))
        cursor = end
    assert cursor == len(raw)
    return dict(values=values, rpcs=rpcs, frame=frame, stream=stream,
                bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest(),
                header_sha256=hashlib.sha256(header).hexdigest())


def analyze(root):
    result = (root / "result.txt").read_text(encoding="utf-8-sig")
    assert result.splitlines()[0] == "PASS"
    origin = float(re.search(r"ClockOrigin=([^;]+)", result)[1])
    rows = list(csv.DictReader((root / "realtime-trace.csv").open(encoding="utf-8-sig", newline="")))
    by_case, groups, packets = defaultdict(list), defaultdict(list), {}
    for row in rows:
        by_case[row["case"]].append(row)
        groups[int(row["deadline_index"])].append(row)
        if row["packet"]:
            assert row["packet"] not in packets
            packets[row["packet"]] = packet(root, row["packet"], row["header"])
    names = {"drop-0.1", "drop-0.4", "drop-1.5", "hold-0.4", "hold-1.5-restart", "drop-0.1-restart-dedup", "no-loss-restart-dedup"}
    assert set(by_case) == names
    previous_index, previous_entry, total_missed, max_gap = -1, origin, 0, 0.0
    for index, group in sorted(groups.items()):
        assert {r["case"] for r in group} == names and len(group) == len(names)
        entry = float(group[0]["tick_entry"])
        assert {float(r["tick_entry"]) for r in group} == {entry}
        deadline = origin + index / 30.0
        missed = index - previous_index - 1
        assert all(abs(float(r["deadline"]) - deadline) < 1e-8 and int(r["missed_deadlines"]) == missed for r in group)
        assert entry + 1e-9 >= deadline and entry >= previous_entry
        max_gap = max(max_gap, entry - previous_entry)
        total_missed += missed
        previous_index, previous_entry = index, entry
    summaries = {}
    for name, samples in by_case.items():
        last_sent, previous_value = [-1.] * 3, [-1] * 3
        states, events, visible, head, source_changed, restarted = [0] * 3, set(), None, 0, False, False
        sends, output_count, restored, first_send = [0] * 3, 0, [-1.] * 3, [-1.] * 3
        latencies, rpc_copy_times = [], []
        before_restart_events = None
        for row in samples:
            start, now, end = map(float, (row["call_start"], row["send_time"], row["call_end"]))
            assert start <= now <= end
            latencies.append((end - start) * 1000)
            changed, loss_end, restart_time = map(float, (row["source_changed"], row["loss_end"], row["restarted"]))
            if changed >= 0 and not source_changed:
                assert changed >= origin + .5 and changed <= now
                source_changed, head = True, head + 4
            if restart_time >= 0 and not restarted:
                assert restart_time >= origin + 4. and restart_time <= now
                before_restart_events = len(events)
                restarted, head, last_sent, previous_value = True, head + 4, [-1.] * 3, [-1] * 3
            value = 77 if restarted else 42 if source_changed else 0
            mask = 0
            for f in range(3):
                due = last_sent[f] < 0 or now < last_sent[f] or now - last_sent[f] >= INTERVALS[f]
                changed_or_refresh = last_sent[f] < 0 or value != previous_value[f] or now < last_sent[f] or now - last_sent[f] >= 1.
                if due and changed_or_refresh:
                    mask |= 1 << f
            assert mask == int(row["sent_field_mask"])
            output = bool(int(row["output"]))
            assert output == bool(mask or head)
            assert int(row["rpc_written"]) == int(head > 0)
            drop = source_changed and changed <= now < loss_end
            held = drop and name.startswith("hold-")
            assert bool(int(row["dropped"])) == drop and bool(int(row["held"])) == held
            if output:
                output_count += 1
                p = packets[row["packet"]]
                assert p["bytes"] == int(row["payload_bytes"]) and p["frame"] == int(row["frame"]) and p["stream"] == int(row["stream"])
                assert sum(1 << f for f in p["values"]) == mask and len(p["rpcs"]) == int(head > 0)
                for f, raw in p["values"].items():
                    assert raw == bytes([value]) * 32
                    sends[f] += 1
                    if value == 42 and now >= loss_end and first_send[f] < 0:
                        first_send[f] = now
                if head:
                    rpc_copy_times.append(dict(time=now - origin, stream=p["stream"], dropped=drop))
                    head -= 1
                if not drop:
                    visible = row["packet"]
            else:
                assert not row["packet"] and int(row["payload_bytes"]) == 0
            for f, field in enumerate(FIELDS):
                successful = float(row[field + "_last_send"])
                if mask & 1 << f:
                    assert now <= successful <= end
                    last_sent[f], previous_value[f] = successful, value
                else:
                    assert successful == last_sent[f]
            assert int(row["queue_count"]) == int(head > 0) and int(row["head_repeats"]) == head
            apply = (output and not drop) or (held and visible is not None)
            if apply:
                assert row["applied_packet"] == visible
                p = packets[visible]
                apply_start, apply_end = float(row["apply_start"]), float(row["apply_end"])
                assert apply_start >= end and apply_end >= apply_start
                for f, raw in p["values"].items():
                    states[f] = raw[0]
                events.update(p["rpcs"])
                for f in range(3):
                    if states[f] == 42 and restored[f] < 0:
                        restored[f] = apply_end
            else:
                assert not row["applied_packet"] and float(row["apply_start"]) == -1 and float(row["apply_end"]) == -1
            assert states == [int(row[f]) for f in FIELDS]
            assert int(row["rpc_calls"]) == len(events) == int(row["expected_rpc_calls"])
        assert len(samples) - output_count > 100 and output_count < 50
        assert all(earliest >= loss_end and actual >= earliest for earliest, actual in zip(first_send, restored))
        assert all(time <= loss_end + max(1., interval) + max_gap + .1 for time, interval in zip(first_send, INTERVALS))
        if restarted:
            assert states == [77, 77, 77] and len(events) == before_restart_events + 1
        if name == "no-loss-restart-dedup":
            assert before_restart_events == 1 and len(events) == 2
        summaries[name] = dict(observations=len(samples), outputs=output_count, emitted=sends,
                               first_post_loss_send_s=[v - origin for v in first_send],
                               first_apply_s=[v - origin for v in restored], events=len(events),
                               rpc_copies=rpc_copy_times, encode_call_max_ms=max(latencies))
    for group in ("product", "config"):
        before = json.loads((root / (group + "-before.json")).read_text(encoding="utf-8-sig"))
        after = json.loads((root / (group + "-after.json")).read_text(encoding="utf-8-sig"))
        assert before == after
    return dict(status="PASS", observations=len(rows), nonempty_packets=len(packets), headers=len(packets),
                unique_deadlines=len(groups), missed_deadlines=total_missed, max_tick_gap_s=max_gap,
                actual_clock_origin=origin, scenarios=summaries,
                boundary="Actual Native EncodeNow/default clock and GPU submissions; injected protected Decoder completion. No GPU readback/header duplicate/network/service/live-client/FPS measurement.")


if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("--results", type=Path, required=True)
    args = cli.parse_args()
    summary = analyze(args.results)
    (args.results / "realtime-audit.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: summary[k] for k in ("status", "observations", "nonempty_packets", "unique_deadlines", "missed_deadlines")}))
