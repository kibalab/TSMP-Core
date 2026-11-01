"""Retained-fixture end-to-end checks for the conditional analysis CLI."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import zlib


def write_csv(path, rows):
    with path.open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    manifest, production = [], []
    # A contiguous burst is intentionally favorable to round-robin across four
    # codewords.  It is a synthetic construction, not a video measurement.
    for name, length, wrong in [('short', 32, [2]), ('burst', 900, list(range(20))),
                                ('bad-header', 239, [1, 90])]:
        expected = bytes((index * 17 + 3) & 255 for index in range(length))
        actual = bytearray(expected)
        for index in wrong:
            actual[index] ^= 111
        header = bytearray(56)
        header[12:14] = (8).to_bytes(2, 'little')
        header[14:16] = (160).to_bytes(2, 'little')
        header[32:34] = (3).to_bytes(2, 'little')
        header[42:44] = length.to_bytes(2, 'little')
        header[44:48] = zlib.crc32(expected).to_bytes(4, 'little')
        header[52:56] = zlib.crc32(header[:52]).to_bytes(4, 'little')
        for suffix, data in [('expected', expected), ('decoded', actual), ('header', header)]:
            (args.results / (name + '.' + suffix + '.bin')).write_bytes(data)
        profile = name + '__x264-cbr3.5M'
        manifest.append(dict(profile=profile, file=name + '.png', frame=1,
            variant='codec3-mode2', mode='Payload', input='',
            header=str((args.results / (name + '.header.bin')).resolve()),
            reference=str((args.results / (name + '.expected.bin')).resolve()),
            decoded=str((args.results / (name + '.decoded.bin')).resolve()),
            decodedSha256=hashlib.sha256(actual).hexdigest()))
        production.append(dict(profile=profile, file=name + '.png', variant='Recovery',
            status='REJECTED', headerValid=str(name != 'bad-header')))
    write_csv(args.results / 'raw-dumps.csv', manifest)
    write_csv(args.results / 'production.csv', production)
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1')
    command = [sys.executable, str(Path(__file__).with_name('analyze_residuals.py')),
        '--manifest', str(args.results / 'raw-dumps.csv'),
        '--production-csv', str(args.results / 'production.csv'),
        '--results', str(args.results / 'analysis')]
    result = subprocess.run(command, text=True, capture_output=True, env=env, check=True)
    (args.results / 'analysis.stdout.txt').write_text(result.stdout, encoding='utf-8')
    with (args.results / 'analysis' / 'conditional-parity.csv').open() as stream:
        rows = list(csv.DictReader(stream))
    def find(name, parity, layout):
        return next(row for row in rows if row['profile'].startswith(name + '__')
                    and row['parity'] == str(parity) and row['layout'] == layout)
    assert len(rows) == 18
    assert find('short', 16, 'contiguous')['conditionalWithinErrorBound'] == 'True'
    assert find('burst', 16, 'contiguous')['conditionalWithinErrorBound'] == 'False'
    assert find('burst', 16, 'round-robin')['conditionalWithinErrorBound'] == 'True'
    assert find('bad-header', 16, 'contiguous')['productionHeaderValid'] == 'False'
    summary = json.loads((args.results / 'analysis' / 'summary.json').read_text())
    assert summary['actualEncodedFecPathRuns'] == 0 and summary['replayChecks'] == 18
    # Tamper detection is required: source provenance must not silently pass.
    manifest[0]['decodedSha256'] = '0' * 64
    write_csv(args.results / 'tampered-dumps.csv', manifest)
    tampered_command = command[:]
    tampered_command[3] = str(args.results / 'tampered-dumps.csv')
    tampered_command[-1] = str(args.results / 'tampered-analysis')
    failed = subprocess.run(tampered_command, text=True, capture_output=True, env=env)
    assert failed.returncode != 0 and 'Changed GPU dump' in failed.stderr
    print(json.dumps(dict(status='PASS', assertions=7, conditionalRows=len(rows),
                          tamperedDumpRejected=True), indent=2))


if __name__ == '__main__':
    main()
