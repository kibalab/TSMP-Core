"""Audit final loss-reduction source ownership against the saved Git baseline.

Reads repositories and dedicated validation projects; writes only the requested
JSON evidence file. Pass project roots whose TSMP packages are embedded copies.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile


SELECTED = {'.cs', '.shader', '.cginc', '.meta'}
PROTECTED = {'.prefab', '.mat', '.asset'}


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def git(repo: Path, *args: str) -> str:
    return subprocess.check_output(
        ['git', '-C', str(repo), *args], encoding='utf-8', errors='replace'
    ).strip()


def declaration(data: bytes, pattern: str) -> str | None:
    found = re.search(pattern, data.decode('utf-8-sig'), re.MULTILINE)
    return found.group(1) if found else None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--project', type=Path, action='append', default=[])
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    initial = json.loads(args.baseline.read_text(encoding='utf-8-sig'))
    failures, repositories, files, guid_owners = [], [], [], {}
    preserved_guids = 0
    for item in initial['repositories']:
        repo, archive = Path(item['repository']), Path(item['archive'])
        baseline = item['baseline']
        if sha(archive.read_bytes()) != item['sha256']:
            failures.append(f"{item['name']}: baseline archive hash mismatch")
        with zipfile.ZipFile(archive) as frozen:
            old_files = {name: frozen.read(name) for name in frozen.namelist()
                         if not name.endswith('/')}
        package_names = {name.split('/')[1] for name in old_files
                         if name.startswith('Packages/') and name.endswith('/package.json')}
        if len(package_names) != 1:
            raise RuntimeError(f'Ambiguous package in {archive}: {package_names}')
        package_name = next(iter(package_names))
        package = repo / 'Packages' / package_name
        head, branch, main_ref = (git(repo, 'rev-parse', 'HEAD'),
                                  git(repo, 'branch', '--show-current'),
                                  git(repo, 'rev-parse', 'main'))
        changes = git(repo, 'diff', '--name-status', baseline, '--', 'Packages').splitlines()
        changed_paths = {line.split('\t')[-1] for line in changes}
        status = git(repo, 'status', '--porcelain', '--', 'Packages').splitlines()
        merges = git(repo, 'rev-list', '--merges', f'{baseline}..{head}').splitlines()
        ancestor = subprocess.run(['git', '-C', str(repo), 'merge-base', '--is-ancestor',
                                   baseline, head], capture_output=True).returncode == 0
        checks = {'mainPreserved': main_ref == item['main'],
                  'branchSeparated': branch == 'codex/tsmp-reliability',
                  'baselineAncestor': ancestor, 'noAddedMerges': not merges,
                  'committedPackages': not status}
        for label, passed in checks.items():
            if not passed:
                failures.append(f"{item['name']}: {label}")
        repositories.append(dict(name=item['name'], repository=str(repo), package=package_name,
                                 baseline=baseline, head=head, branch=branch, main=main_ref,
                                 checks=checks, changes=changes, packageStatus=status,
                                 newCommits=int(git(repo, 'rev-list', '--count', f'{baseline}..{head}'))))
        for old_name, data in old_files.items():
            path = Path(old_name)
            if path.suffix in PROTECTED or path.name == 'package.json':
                current = repo / path
                # Archives contain Git's canonical LF bytes; working trees may
                # use CRLF. Git's diff applies the repository's text filters.
                if not current.is_file() or old_name in changed_paths:
                    failures.append(f'{item["name"]}: protected asset changed: {old_name}')
            if path.suffix == '.meta':
                current = repo / path
                old_guid = declaration(data, r'^guid:\s*([0-9a-f]+)')
                new_guid = declaration(current.read_bytes(), r'^guid:\s*([0-9a-f]+)') if current.is_file() else None
                if old_guid and old_guid == new_guid:
                    preserved_guids += 1
                elif old_guid:
                    failures.append(f'{item["name"]}: existing GUID changed: {old_name}')
            if path.suffix == '.shader':
                current = repo / path
                if not current.is_file() or declaration(data, r'\bShader\s+"([^"]+)"') != declaration(current.read_bytes(), r'\bShader\s+"([^"]+)"'):
                    failures.append(f'{item["name"]}: shader name changed: {old_name}')
        selected = {}
        for scope in ('Runtime', 'Editor'):
            for source in (package / scope).rglob('*'):
                if not source.is_file() or source.suffix not in SELECTED:
                    continue
                relative = source.relative_to(package).as_posix()
                data = source.read_bytes()
                selected[relative] = sha(data)
                if source.suffix == '.meta':
                    guid = declaration(data, r'^guid:\s*([0-9a-f]+)')
                    if guid:
                        guid_owners.setdefault(guid, []).append(str(source))
        for project in args.project:
            target_package = project / 'Packages' / package_name
            for relative, digest in selected.items():
                target = target_package / relative
                actual = sha(target.read_bytes()) if target.is_file() else None
                matched = actual == digest
                files.append(dict(project=str(project), package=package_name, file=relative,
                                  sourceSha256=digest, targetSha256=actual, matches=matched))
                if not matched:
                    failures.append(f'{project.name}/{package_name}: source mismatch: {relative}')
            for scope in ('Runtime', 'Editor'):
                for target in (target_package / scope).rglob('*'):
                    if target.is_file() and target.suffix in SELECTED and target.relative_to(target_package).as_posix() not in selected:
                        failures.append(f'{project.name}/{package_name}: extra selected file: {target}')
    for guid, owners in guid_owners.items():
        if len(owners) > 1:
            failures.append(f'Duplicate GUID {guid}: {owners}')
    result = dict(status='FAIL' if failures else 'PASS',
                  finishedUtc=datetime.now(timezone.utc).isoformat(),
                  baseline=str(args.baseline), repositories=repositories,
                  existingGuidsPreserved=preserved_guids, comparisons=len(files),
                  sourceFiles=files, failures=failures)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('status', 'existingGuidsPreserved', 'comparisons', 'failures')}))
    if failures:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
