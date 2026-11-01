"""Independently audit raw samples, all captured floats, CRCs, and source receipts."""
import argparse
import csv
import hashlib
import json
import math
import re
import statistics
import struct
import zlib
from collections import defaultdict
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--results', required=True, type=Path)
a = p.parse_args()
root = a.results
result = (root / 'result.txt').read_text(encoding='utf-8-sig')
assert result.splitlines()[0] == 'PASS'
frequency = int(re.search(r'StopwatchFrequency=(\d+)', result).group(1))
plan = json.loads((root / 'plan-used.json').read_text(encoding='utf-8-sig'))
assert hashlib.sha256((root/'Runtime/BindingCostProbe.cs').read_bytes()).hexdigest() == plan['probe_sha256']
cases = {c['name']: c for c in plan['cases']}
rows = list(csv.DictReader((root / 'samples.csv').open(encoding='utf-8-sig', newline='')))
groups = defaultdict(list)
packets = 0
values = 0

def fnv(name):
    result = 2166136261
    for b in name.encode():
        result = ((result ^ b) * 16777619) & 0xffffffff
    return result

def expected_iteration(case, iteration, index):
    if case['regime'] == 'changing':
        return iteration
    if case['regime'] == 'unchanged':
        return 0
    prior = iteration - (iteration - index % 4) % 4
    return max(prior, 0)

for row in rows:
    case = cases[row['case']]
    iteration = int(row['sample'])
    assert int(row['fields']) == case['fields'] and int(row['targets']) == case['targets']
    assert int(row['beforeCallbacks']) == case['targets']
    expected_count = case['fields'] if iteration == 0 or case['regime'] == 'changing' else 0 if case['regime'] == 'unchanged' else case['fields'] // 4
    assert int(row['expectedFields']) == int(row['encodedFields']) == expected_count
    elapsed = (int(row['endTicks']) - int(row['startTicks'])) * 1000 / frequency
    assert math.isclose(elapsed, float(row['encodeMs']), rel_tol=1e-11, abs_tol=1e-9)
    if expected_count:
        data = (root / row['packet']).read_bytes()
        header = (root / row['header']).read_bytes()
        assert len(header) == 56 and struct.unpack_from('<H', header, 42)[0] == len(data)
        assert zlib.crc32(header[:52]) == struct.unpack_from('<I', header, 52)[0]
        assert struct.unpack_from('<H', header, 10)[0] & 1
        assert zlib.crc32(data) == struct.unpack_from('<I', header, 44)[0]
        assert row['usedGpu'] == 'True' and len(data) == int(row['payloadBytes'])
        assert data[:2] == b'\x01\x00'
        messages = struct.unpack_from('<H', data, 2)[0]
        cursor = 8
        found = {}
        all_hashes = {fnv(f'f{i:03}'): i for i in range(case['fields'])}
        for _ in range(messages):
            network, kind, _, _, length = struct.unpack_from('<HBBHH', data, cursor)
            end = cursor + 8 + length
            assert kind == 1 and end <= len(data)
            count = struct.unpack_from('<H', data, cursor + 8)[0]
            cursor += 10
            for _ in range(count):
                hashed, kind, length = struct.unpack_from('<IBH', data, cursor)
                assert kind == 3 and length == 4
                index = all_hashes[hashed] if case['targets'] == 1 else network - 1
                assert hashed == fnv(f'f{index if case["targets"] == 1 else 0:03}')
                assert network == index % case['targets'] + 1
                assert index not in found
                expected = expected_iteration(case, iteration, index) * 1024 + index + 1
                value = struct.unpack_from('<f', data, cursor + 7)[0]
                assert value == expected
                found[index] = value
                cursor += 11
                values += 1
            assert cursor == end
        assert cursor == len(data) and len(found) == expected_count
        if iteration and case['regime'] == 'quarter-changing':
            assert set(found) == {i for i in range(case['fields']) if i % 4 == iteration % 4}
        packets += 1
    else:
        assert not row['packet'] and not row['header'] and int(row['payloadBytes']) == 0
    if row['measured'] == 'True':
        groups[row['case']].append(elapsed)

def metrics(times):
    times = sorted(times)
    return dict(count=len(times), mean=statistics.mean(times), p95=times[math.ceil(len(times)*.95)-1], p99=times[math.ceil(len(times)*.99)-1], maximum=times[-1])

summary = {}
for name, case in cases.items():
    all_case_rows = [r for r in rows if r['case'] == name]
    assert [int(r['sample']) for r in all_case_rows] == list(range(case['warm'] + case['samples']))
    assert len({int(r['frame']) for r in all_case_rows}) == len(all_case_rows)
    assert [int(r['sample']) for r in all_case_rows if r['measured'] == 'True'] == list(range(case['warm'], case['warm']+case['samples']))
    assert len(groups[name]) == case['samples']
    summary[name] = metrics(groups[name])
    first = next(r for r in rows if r['case'] == name and int(r['sample']) == 0)
    summary[name]['first_encode_ms'] = float(first['encodeMs'])
    summary[name]['warm_encode_ms'] = [float(r['encodeMs']) for r in rows if r['case'] == name and r['measured'] == 'False']
assert len(rows) == sum(c['warm']+c['samples'] for c in cases.values())
helper_groups = defaultdict(list)
for row in csv.DictReader((root / 'helper.csv').open(encoding='utf-8-sig', newline='')):
    elapsed = (int(row['endTicks']) - int(row['startTicks'])) * 1000 / frequency
    assert math.isclose(elapsed, float(row['milliseconds']), rel_tol=1e-11, abs_tol=1e-9)
    helper_groups[f'{row["targets"]}-{row["method"]}'].append(elapsed)
sources = []
for name in ('source-before.csv', 'source-after.csv'):
    sources.append({r['path']: r['sha256'] for r in csv.DictReader((root/name).open(encoding='utf-8-sig', newline=''))})
assert sources[0] == sources[1] and len(sources[0]) > 300
configs = [json.loads((root/f'configuration-{phase}.json').read_text(encoding='utf-8-sig')) for phase in ('before', 'after')]
assert configs[0] == configs[1]
semantic_rows = list(csv.DictReader((root/'semantics.csv').open(encoding='utf-8-sig', newline='')))
assert len(semantic_rows) == 19
assert {r['case'] for r in semantic_rows} == {'mixed-priority-rotation', 'undue-or-receiveonly-first-field', 'cross-callback-sendmode', 'callback-target-substitution', 'callback-direction-mutation'}
for row in semantic_rows:
    assert row['expectedOrder'] == row['actualOrder']
    assert row['expectedFields'] == row['encodedFields']
    assert int(row['actualCallbacks']) == len(set(row['actualOrder'].split('/')))
    elapsed = (int(row['endTicks']) - int(row['startTicks'])) * 1000 / frequency
    assert math.isclose(elapsed, float(row['encodeMs']), rel_tol=1e-11, abs_tol=1e-9)
report = dict(status='PASS', samples=len(rows), packets=packets, float_values=values, sources=len(sources[0]), configuration_files=len(configs[0]), stopwatch_frequency=frequency,
              plan_sha256=hashlib.sha256((root/'plan-used.json').read_bytes()).hexdigest(), cases=summary,
              helper={name:metrics(times) for name,times in helper_groups.items()}, semantic_edge_rows=len(semantic_rows),
              scope='Actual client VM EncodeNow elapsed in SDK Editor, actual GPU Encoder output. Decoder accepts original protected bytes by injected completion; no receive GPU timing, live client/FPS or source-rate claim.')
(root/'analysis.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report, indent=2))
