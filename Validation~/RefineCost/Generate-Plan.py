"""Select a small fixed current-Refine timing matrix from existing audited inputs.

Selection is explicit and fixed before new VM performance measurements. These are
representative diagnostics, not an independent transport accuracy holdout.
"""
from __future__ import annotations
import argparse
from collections import Counter
import csv
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence-parent', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    previous = args.evidence_parent / 'refine-robustness-20261031/default-production'
    plan_file, csv_file = previous / 'plan-used.json', previous / 'production-frames.csv'
    plan = json.loads(plan_file.read_text(encoding='utf-8-sig'))
    profiles = {profile['name']: profile for profile in plan['profiles']}
    with csv_file.open(encoding='utf-8-sig', newline='') as source:
        rows = list(csv.DictReader(source))
    records = {(row['profile'], row['file']): row for row in rows}
    cases = []

    def add(name, profile_name, frame, minimum):
        file = f'frame-{frame:06}.png'
        profile, row = profiles[profile_name], records[profile_name, file]
        assert row['expectedBytes'] == '5375'
        assert (row['status'] == 'EXACT') == (minimum >= 0)
        if minimum >= 0:
            assert int(row['payloadAttempts']) == minimum
        else:
            assert int(row['payloadAttempts']) == 2 and row['headerValid'] == 'True'
        mapping = next(mapping for mapping in profile['frameMappings'] if mapping['file'] == file)
        header_root = Path(profile.get('expectedHeaderDirectory', str(Path(profile['expectedDirectory']).parent / 'header')))
        cases.append(dict(name=name, codec='Color256' if profile_name.startswith('color256') else 'RGB16',
                          input=str(Path(profile['framesDirectory']) / mapping['file']),
                          header=str(header_root / mapping.get('headerFile', f"frame-{mapping['frameIndex']:06}.bin")),
                          payload=str(Path(profile['expectedPayloadDirectory']) / mapping['payloadFile']),
                          minimumAttempts=minimum))

    luma = args.evidence_parent / 'obs-transport-20261030/fixtures/720p-random'
    cases.append(dict(name='Luma4-clean-5375', codec='Luma4', input=str(luma / 'input/frame-000003.png'),
                      header=str(luma / 'header/frame-000003.bin'), payload=str(luma / 'payload/frame-000003.bin'), minimumAttempts=0))
    add('Color256-clean-5375', 'color256-720p-random__png-control', 3, 0)
    add('RGB16-clean-5375', 'rgb16-720p-random__png-control', 3, 0)
    add('Color256-first-mode-5375', 'color256-720p-random__x264-veryfast-cbr6M-709limited', 9, 1)
    add('Color256-second-mode-5375', 'color256-720p-random__x264-veryfast-cbr6M-709limited', 6, 2)
    add('Color256-reject-5375', 'color256-720p-random__x264-veryfast-cbr3.5M-709limited', 3, -1)
    add('RGB16-first-mode-5375', 'rgb16-720p-random__x264-veryfast-cbr6M-709limited', 27, 1)
    add('RGB16-second-mode-5375', 'rgb16-720p-random__nvenc-p4-cbr3.5M-709limited', 195, 2)
    add('RGB16-reject-5375', 'rgb16-720p-random__x264-veryfast-cbr3.5M-709limited', 3, -1)
    for item in cases:
        for key in ('input', 'header', 'payload'):
            path = Path(item[key]); assert path.is_file(), path
            item[key + 'Sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(dict(warmup=5, samples=30, cases=cases), indent=2), encoding='utf-8')
    recount = {}
    for name, subset in [('all', rows), ('CBR', [row for row in rows if 'cbr' in row['profile'].lower()]),
                         ('OBS', [row for row in rows if row['profile'].startswith('obs__')])]:
        recount[name] = dict(observations=len(subset), exact=sum(row['status'] == 'EXACT' for row in subset),
                             exactByAttempts=dict(sorted(Counter(int(row['payloadAttempts']) for row in subset if row['status'] == 'EXACT').items())),
                             attemptedRows=dict(sorted(Counter(int(row['payloadAttempts']) for row in subset).items())),
                             extraPayloadReadbacks=sum(int(row['payloadAttempts']) for row in subset))
    artifact = dict(status='PASS', sourceCsv=str(csv_file), sourceCsvSha256=hashlib.sha256(csv_file.read_bytes()).hexdigest(),
                    sourcePlan=str(plan_file), sourcePlanSha256=hashlib.sha256(plan_file.read_bytes()).hexdigest(),
                    historicalRecount=recount, selectedCases=len(cases), scope='Existing Native observations; not a new Udon execution or transport holdout.')
    args.output.with_name('selection-and-historical-recount.json').write_text(json.dumps(artifact, indent=2), encoding='utf-8')
    print(json.dumps(dict(status='PASS', cases=len(cases), historicalRecount=recount), indent=2))


if __name__ == '__main__':
    main()
