"""Recount structural regression results, source receipts and VM microtimings."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import statistics


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    root = args.results
    checks = {}
    for run, assertions in (('native-final', 1257), ('vrc-final', 3858), ('core-safety-gamma', 910)):
        lines = (root / run / 'result.txt').read_text(encoding='utf-8-sig').splitlines()
        assert lines[0] == 'PASS' and f'Assertions={assertions}' in lines, run
        log = (root / run / 'editor.log').read_text(encoding='utf-8-sig', errors='replace')
        assert not re.search(r'\berror\s+CS\d+\b|scripts have compiler errors', log, re.I), run
        checks[run] = assertions
    for run in ('native-final', 'vrc-final'):
        directory = root / run
        for receipt in json.loads((directory / 'harness-source-hashes.json').read_text(encoding='utf-8-sig')):
            path = receipt['Path'].replace('\\', '/')
            relative = path.split('/FrameStructure/', 1)[1]
            actual = hashlib.sha256((directory / relative).read_bytes()).hexdigest()
            assert actual.upper() == receipt['Hash'].upper(), relative
    summaries = list(csv.DictReader((root / 'vrc-final/timings.csv').open(encoding='utf-8-sig', newline='')))
    raw = list(csv.DictReader((root / 'vrc-final/samples.csv').open(encoding='utf-8-sig', newline='')))
    assert len(summaries) == 12 and len(raw) == 360
    timing = []
    for row in summaries:
        values = sorted(float(sample['milliseconds']) for sample in raw
                        if (sample['mode'], sample['bytes'], sample['messages'])
                        == (row['mode'], row['bytes'], row['messages']))
        assert len(values) == int(row['samples']) == 30
        for label, actual in (('mean_ms', statistics.mean(values)),
                              ('p95_ms', values[math.ceil(.95 * len(values)) - 1]),
                              ('max_ms', max(values))):
            assert abs(actual - float(row[label])) <= .000002, (row, label, actual)
        timing.append(dict(mode=row['mode'], bytes=int(row['bytes']),
                           entries=int(row['messages']), meanMs=statistics.mean(values),
                           p95Ms=values[28], maxMs=max(values)))
    compile_results = list((root / 'udon-compile').glob('*-Udon-result.txt'))
    assert len(compile_results) == 1
    compiled = compile_results[0].read_text(encoding='utf-8-sig').splitlines()
    assert compiled[0] == 'PASS' and 'TSMPPrograms=16' in compiled and 'Bindings=True' in compiled
    source = json.loads((root / 'source-configuration-audit.json').read_text(encoding='utf-8-sig'))
    assert source['status'] == 'PASS'
    assert len(source['configurations']) == 6 and len(source['sources']) == 4
    assert all(item['matches'] for item in source['configurations'] + source['sources'])
    result = dict(status='PASS', correctness=checks, udonPrograms=16,
                  frozenHarnessReceiptsVerified=True, configurationsPreserved=6,
                  runtimeCopyComparisons=4, timingSamples=360, timings=timing,
                  rawTimingColumnNote='messages denotes variable-entry count; every timed packet has one message',
                  limitation='Targeted Native/SDK VM completion tests and isolated preflight cost; not GPU or live-client timing')
    (root / 'independent-audit.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('status', 'correctness', 'udonPrograms', 'timingSamples')}))


if __name__ == '__main__':
    main()
