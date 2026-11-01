"""Freeze repository and dedicated-project inputs before workload experiments."""
import argparse
import datetime
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args], text=True).strip()


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


p = argparse.ArgumentParser()
p.add_argument('--results', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[2]
a.results.mkdir(parents=True, exist_ok=True)
output = a.results / 'baseline.json'
if output.exists():
    raise SystemExit('Preserve the existing baseline; use a fresh results directory.')
repos = [('Core', root)] + [(name, Path('F:/Unity/TSMP/TSMPCodec-' + name))
                          for name in ['Luma4', 'Color256', 'RGB16', 'RGB20']]
result = dict(created_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
              repos=[], configurations=[])
for name, repo in repos:
    if git(repo, 'diff', '--name-only', 'HEAD', '--', 'Packages'):
        raise SystemExit('Uncommitted package changes must be preserved: ' + str(repo))
    archive = a.results / (name + '-baseline-packages.zip')
    if archive.exists():
        raise SystemExit('Existing archive: ' + str(archive))
    sources = []
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        paths = subprocess.check_output(['git', '-C', str(repo), 'ls-files', '-z', 'Packages']).decode().split('\0')
        for relative in filter(None, paths):
            path = repo / relative
            data = path.read_bytes()
            z.writestr(relative, data)
            sources.append(dict(path=relative, sha256=hashlib.sha256(data).hexdigest()))
    result['repos'].append(dict(name=name, path=str(repo), head=git(repo, 'rev-parse', 'HEAD'),
                               branch=git(repo, 'branch', '--show-current'),
                               main=git(repo, 'rev-parse', 'main'),
                               origin_main=git(repo, 'rev-parse', 'origin/main'),
                               archive=str(archive), archive_sha256=sha(archive), sources=sources))
for project_name in ['Native', 'VRC']:
    project = Path('F:/Unity/TSMP/Validation-Results/payload-crc-20261028/projects') / project_name
    paths = [project / 'Packages/manifest.json', project / 'Packages/packages-lock.json']
    paths += sorted((project / 'ProjectSettings').glob('*'))
    for path in paths:
        if path.is_file():
            result['configurations'].append(dict(project=project_name,
                                                 path=str(path), sha256=sha(path)))
output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
audit = dict(repositories=[dict(name=repo['name'], repository=repo['path'],
                               baseline=repo['head'], main=repo['main'],
                               archive=repo['archive'], sha256=repo['archive_sha256'])
                          for repo in result['repos']])
(a.results / 'audit-baseline.json').write_text(json.dumps(audit, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(baseline=str(output), repositories=len(result['repos']),
                      archived_files=sum(len(repo['sources']) for repo in result['repos']),
                      configurations=len(result['configurations']))))
