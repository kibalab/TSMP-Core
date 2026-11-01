"""Check final workload evidence, configurations, source copies and branch ownership."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args], text=True).strip()


p = argparse.ArgumentParser()
p.add_argument('--results', type=Path, required=True)
a = p.parse_args()
root = a.results
workspace = Path(__file__).resolve().parents[2]
baseline = read(root / 'baseline.json')
configurations = baseline['configurations']
assert len(configurations) == 46
assert all(digest(Path(row['path'])) == row['sha256'] for row in configurations)
expected_configs = {str(Path(row['path'])).lower() for row in configurations}
actual_configs = set()
for name in ['Native', 'VRC']:
    project = Path('F:/Unity/TSMP/Validation-Results/payload-crc-20261028/projects') / name
    paths = [project / 'Packages/manifest.json', project / 'Packages/packages-lock.json']
    paths += list((project / 'ProjectSettings').glob('*'))
    actual_configs.update(str(path).lower() for path in paths if path.is_file())
assert actual_configs == expected_configs
allowed = {'Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.cs',
           'Packages/com.kibalab.tsmp.core/CHANGELOG.md'}
archives = 0
for repo in baseline['repos']:
    path, archive = Path(repo['path']), Path(repo['archive'])
    assert digest(archive) == repo['archive_sha256']
    with zipfile.ZipFile(archive) as frozen:
        assert set(frozen.namelist()) == {row['path'] for row in repo['sources']}
        for row in repo['sources']:
            assert hashlib.sha256(frozen.read(row['path'])).hexdigest() == row['sha256']
            archives += 1
    assert git(path, 'rev-parse', 'main') == repo['main']
    assert git(path, 'rev-parse', 'origin/main') == repo['origin_main']
    assert git(path, 'branch', '--show-current') == 'codex/tsmp-reliability'
    changed = set(git(path, 'diff', '--name-only', repo['head'], '--', 'Packages').splitlines())
    assert changed == (allowed if repo['name'] == 'Core' else set()), changed
    assert not git(path, 'status', '--porcelain', '--', 'Packages')
subprocess.run([sys.executable, str(workspace / 'Validation~/LossReduction/Audit-Final.py'),
                '--baseline', str(root / 'audit-baseline.json'),
                '--project', 'F:/Unity/TSMP/Validation-Results/payload-crc-20261028/projects/Native',
                '--project', 'F:/Unity/TSMP/Validation-Results/payload-crc-20261028/projects/VRC',
                '--output', str(root / 'source-audit-final.json')], check=True)
ab = read(root / 'binding-cost/ab-analysis.json')
assert ab['status'] == 'PASS' and ab['formal_runs'] == 6
assert ab['total_encode_calls'] == 2880 and ab['total_packets'] == 1944 and ab['total_float_values'] == 99360
assert read(root / 'binding-cost/independent-ab-audit.json')['status'] == 'PASS'
assert read(root / 'rpc-burst/baseline-corrected/root-packet-audit.json')['status'] == 'PASS'
assert read(root / 'color256-followup/vrc-initial/independent-stage-audit.json')['status'] == 'PASS'
assert read(root / 'color256-followup/vrc-wrapper-control/independent-observer-audit.json')['status'] == 'PASS'
for name in ['vrc-initial', 'vrc-wrapper-control']:
    assert read(root / 'color256-followup' / name / 'analysis.json')['status'] == 'PASS'
budget = (root / 'regression-budget-vm/result.txt').read_text(encoding='utf-8-sig')
assert budget.splitlines()[0] == 'PASS' and 'Assertions=131' in budget
compiled = list((root / 'regression-client-compile').glob('*-Udon-result.txt'))
assert len(compiled) == 1
compile_text = compiled[0].read_text(encoding='utf-8-sig')
assert compile_text.splitlines()[0] == 'PASS' and 'TSMPPrograms=16' in compile_text and 'Bindings=True' in compile_text
report = dict(status='PASS', baseline_archive_files=archives, configurations_preserved=len(configurations),
              repositories=5, source_audit=read(root / 'source-audit-final.json')['status'],
              formal_encode_calls=ab['total_encode_calls'], protected_pairs=ab['total_packets'],
              float_values=ab['total_float_values'], default_budget_vm_assertions=131,
              compiled_product_programs=16, core_head=git(workspace, 'rev-parse', 'HEAD'),
              scope='Automated SDK Editor evidence; no main merge, push, release, live VRChat or CDN claim.')
(root / 'root-final-audit.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps(report))
