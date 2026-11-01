"""Compare all formal pairs, preserving first-call/rebuild timing separately."""
import argparse
import csv
import hashlib
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--manifest', required=True, type=Path)
a = p.parse_args()
manifest = json.loads(a.manifest.read_text(encoding='utf-8-sig'))
groups = defaultdict(dict)
source_sets = []
codes = defaultdict(dict)
for run in manifest['runs']:
    directory = Path(run['results'])
    analysis = json.loads((directory/'analysis.json').read_text())
    assert analysis['status'] == 'PASS' and analysis['plan_sha256'] == manifest['plan']['sha256']
    receipts = {r['path']:r['sha256'] for r in csv.DictReader((directory/'source-before.csv').open(encoding='utf-8-sig', newline=''))}
    encoder = [path for path in receipts if path.replace('\\','/').endswith('/Runtime/Encoder/TSMPEncoder.cs')]
    assert len(encoder) == 1 and receipts[encoder[0]] == manifest['sources'][run['variant']]['sha256']
    source_sets.append({path:digest for path,digest in receipts.items() if path != encoder[0]})
    archive = directory/'Editor/BindingCostValidation.cs'
    frozen = next(r for r in manifest['harness'] if r['path'].replace('\\','/').endswith('/Editor/BindingCostValidation.cs'))
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == frozen['sha256']
    for row in csv.DictReader((directory/'artifact-hashes.csv').open(encoding='utf-8-sig', newline='')):
        if row['kind'] == 'client-bytecode':
            codes[run['variant']][row['path']] = dict(bytes=int(row['bytes']), sha256=row['sha256'])
    groups[run['pair']][run['variant']] = analysis
assert all(source == source_sets[0] for source in source_sets)
comparisons = {}
for name in groups[0]['original']['cases']:
    rows = []
    for pair, results in sorted(groups.items()):
        old = results['original']['cases'][name]
        new = results['candidate']['cases'][name]
        rows.append(dict(pair=pair, old_ms=old['mean'], new_ms=new['mean'], reduction_percent=100*(1-new['mean']/old['mean']),
                         old_p95=old['p95'], new_p95=new['p95'], old_first_ms=old['first_encode_ms'], new_first_ms=new['first_encode_ms']))
    comparisons[name] = dict(pairs=rows, pooled_old_ms=statistics.mean(r['old_ms'] for r in rows), pooled_new_ms=statistics.mean(r['new_ms'] for r in rows))
    comparisons[name]['pooled_reduction_percent'] = 100*(1-comparisons[name]['pooled_new_ms']/comparisons[name]['pooled_old_ms'])
for path in codes['original']:
    if '/Runtime/Encoder/TSMPEncoder.asset' not in path:
        assert codes['original'][path] == codes['candidate'][path]
report = dict(status='PASS', formal_runs=len(manifest['runs']), source_files_unchanged_other_than_encoder=len(source_sets[0]),
              total_encode_calls=sum(r['samples'] for pair in groups.values() for r in pair.values()),
              total_packets=sum(r['packets'] for pair in groups.values() for r in pair.values()),
              total_float_values=sum(r['float_values'] for pair in groups.values() for r in pair.values()),
              cases=comparisons, bytecode=codes,
              scope='Refresh=0, warmed per-call full actual Encoder VM elapsed with actual GPU output. Protected completion injection validates all values; no receiver GPU timing/live client/FPS/source-rate claim. First encode/rebuild cost retained separately. Initial profiling run excluded.')
output = a.manifest.parent/'ab-analysis.json'
output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report, indent=2))
