"""Pin all formal pair orders, exact sources, harness and plan before A/B output."""
import argparse
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--root', type=Path, required=True)
p.add_argument('--plan', type=Path, required=True)
a = p.parse_args()
root = a.root.resolve()
here = Path(__file__).resolve().parent
manifest = root/'ab-manifest.json'
if manifest.exists():
    raise SystemExit('Fresh manifest required')

def receipt(path):
    return dict(path=str(path.resolve()), sha256=hashlib.sha256(path.read_bytes()).hexdigest())

sources = {kind: receipt(root/'sources'/f'TSMPEncoder-{kind}.cs') for kind in ('original', 'candidate')}
plan = receipt(a.plan)
harness = [receipt(path) for path in sorted(here.rglob('*')) if path.is_file() and path.suffix in ('.cs', '.py', '.ps1', '.md')]
runs = []
for pair, order in enumerate((('original', 'candidate'), ('candidate', 'original'), ('original', 'candidate'))):
    for kind in order:
        path = root/'ab'/f'pair{pair}-{kind}'
        assert not path.exists(), path
        runs.append(dict(pair=pair, variant=kind, results=str(path)))
document = dict(schema=1, sources=sources, plan=plan, harness=harness, runs=runs,
                profiling_baseline_excluded=True,
                method='Six fresh SDK Editor processes, three alternating complete-matrix source pairs. Only dedicated-project Encoder source changes. Restore candidate finally. Refresh zero isolates changed/unchanged serialization; no live client/source-rate/FPS claim.')
manifest.write_text(json.dumps(document, indent=2)+'\n', encoding='utf-8')
print(json.dumps(dict(path=str(manifest), sha256=hashlib.sha256(manifest.read_bytes()).hexdigest(), runs=len(runs))))
