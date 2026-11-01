"""Independently recount protected RPC packets and FIFO delivery from raw CSVs."""
import argparse
from collections import Counter
import csv
from decimal import Decimal
import json
from pathlib import Path
import zlib


def rows(folder, name):
    with (folder / name).open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def half_unit(value):
    # Convert.ToString(float) uses a shorter decimal than its binary value.
    # Bound that recorded rounding explicitly rather than assuming exact times.
    return float(Decimal(10) ** Decimal(Decimal(value).as_tuple().exponent)) / 2


p = argparse.ArgumentParser()
p.add_argument('--results', type=Path, required=True)
a = p.parse_args()
root = a.results
assert (root / 'result.txt').read_text(encoding='utf-8-sig').startswith('PASS')
plan = json.loads((root / 'plan-used.json').read_text(encoding='utf-8-sig'))
enqueues, packets, applications, summaries = [rows(root, name) for name in
                                            ['enqueue.csv', 'packets.csv', 'events.csv', 'summary.csv']]
reports = []
for case in plan['cases']:
    name = case['name']
    accepted = [int(row['id']) for row in enqueues if row['case'] == name and row['accepted'] == 'True']
    rejected = [int(row['id']) for row in enqueues if row['case'] == name and row['accepted'] != 'True']
    counts, first = Counter(), {}
    case_packets = [row for row in packets if row['case'] == name]
    for row in case_packets:
        data, header = [(root / row[key]).read_bytes() for key in ['packet', 'header']]
        assert len(header) == 56 and header[:4] == b'TSMP'
        assert zlib.crc32(header[:52]) == int.from_bytes(header[52:56], 'little')
        assert len(data) == int(row['bytes']) == int.from_bytes(header[42:44], 'little')
        assert zlib.crc32(data) == int.from_bytes(header[44:48], 'little')
        assert int.from_bytes(header[24:28], 'little') == int(row['frameIndex'])
        assert int.from_bytes(data[4:8], 'little') == int(row['frameIndex'])
        at, ids = 8, []
        for _ in range(int.from_bytes(data[2:4], 'little')):
            network, kind, end = int.from_bytes(data[at:at+2], 'little'), data[at+2], at+8+int.from_bytes(data[at+6:at+8], 'little')
            assert network == 1 and at+8 <= end <= len(data)
            body = data[at+8:end]
            if kind == 2:
                assert body[4] == 2
                cursor, arguments = 5, []
                for _ in range(body[4]):
                    length = int.from_bytes(body[cursor+1:cursor+3], 'little')
                    arguments.append((body[cursor], body[cursor+3:cursor+3+length]))
                    cursor += 3+length
                assert cursor == len(body) and arguments[0][1] == b'ReceiveEvent'
                assert arguments[1][0] == 2 and len(arguments[1][1]) == 4
                event = int.from_bytes(arguments[1][1], 'little', signed=True)
                assert event in accepted and event not in rejected
                ids.append(event)
                counts[event] += 1
                first.setdefault(event, float(row['time']))
            else:
                assert kind == 1 and int.from_bytes(body[:2], 'little') == 1
                assert body[6] == 2 and int.from_bytes(body[7:9], 'little') == 4
                marker = int.from_bytes(body[9:13], 'little', signed=True)
                assert len(body) == 13 and marker == int(row['marker'])
            at = end
        assert at == len(data)
        assert ids == [int(v) for v in row['rpcIds'].split(';') if v]
        assert len(ids) <= 1, 'This audit describes the frozen single-head baseline.'
        expected_queue = int(row['queueBefore']) - int(bool(ids) and int(row['headBefore']) == 1)
        assert int(row['queueAfter']) == expected_queue
    assert list(first) == accepted and set(counts) == set(accepted)
    assert all(count == plan['repeats'] for count in counts.values())
    received = [row for row in applications if row['case'] == name]
    assert [int(row['id']) for row in received] == accepted
    for row in received:
        event = int(row['id'])
        assert float(row['firstSend']) == first[event]
        error = abs((float(row['apply']) - float(row['enqueue']))*1000 - float(row['latencyMs']))
        bound = (half_unit(row['apply'])+half_unit(row['enqueue']))*1000 + half_unit(row['latencyMs']) + 1e-8
        assert error <= bound, (name, event, error, bound)
    summary = next(row for row in summaries if row['case'] == name)
    assert int(summary['accepted']) == int(summary['applied']) == len(accepted)
    assert int(summary['rejected']) == len(rejected) and int(summary['published']) == len(case_packets)
    reports.append(dict(case=name, accepted=len(accepted), rejected=len(rejected),
                        copies=sum(counts.values()), packets=len(case_packets),
                        first_to_last_send_seconds=max(first.values())-min(first.values()),
                        sender_deadline_misses=int(summary['deadlineMisses'])))
result = dict(status='PASS', accepted=sum(row['accepted'] for row in reports),
              protected_copies=sum(row['copies'] for row in reports),
              packets=sum(row['packets'] for row in reports), cases=reports,
              timestamp_note='Latency agrees within the explicit half-decimal-unit rounding bounds of each recorded number.')
(root / 'root-packet-audit.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
print(json.dumps(result))
