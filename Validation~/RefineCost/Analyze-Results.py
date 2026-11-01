"""Independently audit and summarize controlled Refine-cost VM invocations."""
from __future__ import annotations
import argparse
from collections import defaultdict
import csv
import hashlib
import json
import math
from pathlib import Path
from statistics import mean


def read_csv(path):
    with path.open(encoding='utf-8-sig', newline='') as source:
        return list(csv.DictReader(source))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def percentile(values, quantile):
    return sorted(values)[max(0, math.ceil(len(values) * quantile) - 1)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    root = args.results
    assert (root / 'result.txt').read_text(encoding='utf-8-sig').splitlines()[0] == 'PASS'
    plan = json.loads((root / 'plan-used.json').read_text(encoding='utf-8-sig'))
    cases = {item['name']: item for item in plan['cases']}
    samples, events = read_csv(root / 'samples.csv'), read_csv(root / 'vm-events.csv')
    expected_rows = len(cases) * 3 * (plan['warmup'] + plan['samples'])
    assert len(samples) == expected_rows
    assert len({(row['case'], row['budget'], row['sample']) for row in samples}) == expected_rows
    event_groups = defaultdict(list)
    for row in events:
        event_groups[row['case'], row['budget'], row['sample']].append(row)
    artifacts = read_csv(root / 'artifact-hashes.csv')
    for item in cases.values():
        for field in ('input', 'header', 'payload'):
            path = Path(item[field])
            assert digest(path) == item[field + 'Sha256'], path
            assert any(row['kind'] == 'case' and Path(row['path']) == path and row['sha256'] == digest(path) for row in artifacts), path
    before = {row['path']: row['sha256'] for row in read_csv(root / 'source-before.csv')}
    after = {row['path']: row['sha256'] for row in read_csv(root / 'source-after.csv')}
    assert before and before == after
    assert all(digest(Path(path)) == expected for path, expected in after.items())
    bytecodes = defaultdict(set)
    for row in artifacts:
        if row['kind'] == 'bytecode':
            bytecodes[row['path']].add(row['sha256'])
    assert bytecodes and all(len(hashes) == 1 for hashes in bytecodes.values())
    groups = defaultdict(list)
    accepted = 0
    for row in samples:
        case, budget, sample = cases[row['case']], int(row['budget']), int(row['sample'])
        warmup = sample < plan['warmup']
        assert (row['warmup'] == 'True') == warmup
        expected = case['minimumAttempts'] >= 0 and budget >= case['minimumAttempts']
        assert (row['exact'] == 'True') == expected
        assert int(row['bytes']) == 5375 and int(row['fieldBytes']) == 5350
        assert int(row['headerAttempts']) == 0
        assert int(row['payloadAttempts']) == (budget if case['minimumAttempts'] < 0 else min(budget, case['minimumAttempts']))
        assert int(row['notifications']) == int(expected)
        accepted += int(expected)
        calls = event_groups[row['case'], row['budget'], row['sample']]
        assert all((call['warmup'] == 'True') == warmup for call in calls)
        submit = sum(float(call['vmMs']) for call in calls if call['event'] == 'DecodeNow')
        callbacks = [call for call in calls if 'readback' in call['event'].lower()]
        callback_ms = sum(float(call['vmMs']) for call in callbacks)
        total = sum(float(call['vmMs']) for call in calls)
        assert len(callbacks) == int(row['callbacks']) and len(callbacks) > 0
        assert abs(submit - float(row['submitVmMs'])) < 1e-7
        assert abs(callback_ms - float(row['callbackVmMs'])) < 1e-7
        assert abs(total - float(row['totalVmMs'])) < 1e-7
        assert 0 < total <= float(row['wallLatencyMs']) + .01
        if not warmup:
            groups[row['case'], budget].append(row)
    summary = []
    for (name, budget), values in sorted(groups.items()):
        assert len(values) == plan['samples']
        item = dict(case=name, codec=cases[name]['codec'], budget=budget, samples=len(values),
                    exact=sum(row['exact'] == 'True' for row in values), bytes=5375,
                    callbacks=sorted({int(row['callbacks']) for row in values}),
                    predictedReadbacks=sum(int(row['predictedReadbacks']) for row in values),
                    fallbacks=sum(int(row['fallbacks']) for row in values))
        for field in ('submitVmMs', 'callbackVmMs', 'totalVmMs', 'wallLatencyMs'):
            numbers = [float(row[field]) for row in values]
            item[field] = dict(mean=mean(numbers), p50=percentile(numbers, .5), p95=percentile(numbers, .95), maximum=max(numbers))
        callback_numbers = [float(event['vmMs']) for row in values
                            for event in event_groups[row['case'], row['budget'], row['sample']]
                            if 'readback' in event['event'].lower()]
        item['individualCallbackVmMs'] = dict(events=len(callback_numbers), mean=mean(callback_numbers),
                                              p50=percentile(callback_numbers, .5), p95=percentile(callback_numbers, .95), maximum=max(callback_numbers))
        summary.append(item)
    result = dict(status='PASS', inputs=len(cases), observations=len(samples), measured=len(groups)*plan['samples'],
                  acceptedIncludingWarmup=accepted, sourceFiles=len(before), eventRows=len(events), summary=summary,
                  limitations=[
                      'Selected repeated fixed inputs; duplicate suppression disabled to measure complete application.',
                      'Actual client-target Udon programs in SDK editor VM, not a real VRChat client.',
                      'Decoder VM Interpret CPU includes nested codec/receiver and synchronous driver calls; it is not isolated CRC time or GPU execution time.',
                      'Wall latency ends at final callback VM Interpret return, before remaining outer event dispatcher bookkeeping, and includes asynchronous waits; no total FPS or useful-update-Hz claim.',
                      'Only the selected 5375-byte workloads and source sizes are measured; no transport/service or full-corpus replay.',
                      'Total VM CPU sums several callbacks; concurrent slots and callback overlap in a render frame are not measured.',
                  ])
    (root / 'independent-summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('status','inputs','observations','measured','acceptedIncludingWarmup','sourceFiles','eventRows')}, indent=2))
    for item in summary:
        print(item['case'], item['budget'], item['exact'], round(item['totalVmMs']['mean'], 4), round(item['totalVmMs']['p95'], 4), round(item['wallLatencyMs']['p95'], 4))


if __name__ == '__main__':
    main()
