#!/usr/bin/env python3
"""Read saved protected TSMP images; screen header samplers with a CPU model.

This does not modify original evidence or runtime code, and is not a GPU test.
It verifies the existing central4 CPU model against saved GPU CRCs/outcomes.
"""
import argparse
import concurrent.futures
import csv
import hashlib
import json
from pathlib import Path
import struct
import zlib

import numpy as np
from PIL import Image

SAMPLERS = ["mean4", "mean1", "mean2", "mean3", "mean5", "mean6", "mean8",
            "weighted4", "weighted6", "trim4", "median4", "trim6"]


def sample_blocks(rgb, method):
    size = int(''.join(c for c in method if c.isdigit()))
    start = (8 - size) // 2
    # Only rows containing calibration/header are needed. PNG decode still reads
    # its full image; processing and storage remain bounded to its top 24 rows.
    blocks = rgb[:24].astype(np.float64).reshape(3, 8, rgb.shape[1] // 8, 8, 3)
    values = blocks[:, start:start + size, :, start:start + size].mean(axis=4)
    if method.startswith("weighted"):
        axis = np.arange(size)
        weights = np.minimum(axis + 1, size - axis).astype(np.float64)
        weights = weights[:, None] * weights[None, :]
        return (values * weights[None, :, None, :]).sum(axis=(1, 3)) / weights.sum()
    if method.startswith("trim") or method.startswith("median"):
        ordered = np.sort(values.transpose(0, 2, 1, 3).reshape(3, -1, size * size), axis=2)
        if method.startswith("median"):
            return np.median(ordered, axis=2)
        trim = size * size // 4
        return ordered[:, :, trim:-trim].mean(axis=2)
    return values.mean(axis=(1, 3))


def recover(header_samples, calibration_samples):
    calibration = calibration_samples[1, :32].reshape(16, 2).mean(axis=1)
    observed = header_samples[2, :112]
    distances = np.abs(observed[:, None] - calibration[None, :])
    symbols = distances.argmin(axis=1)
    data = (symbols[::2] | (symbols[1::2] << 4)).astype(np.uint8).tobytes()
    valid = zlib.crc32(data[:52]) == struct.unpack_from('<I', data, 52)[0]
    return data, valid, symbols, calibration, observed


def process(task):
    row, path = task
    rgb = np.asarray(Image.open(path).convert('RGB'))
    expected_path = Path(row['expectedFile']).parent.parent / 'header' / Path(row['expectedFile']).with_suffix('.bin').name
    expected = expected_path.read_bytes()
    expected_symbols = np.frombuffer(expected, dtype=np.uint8)
    expected_symbols = np.stack((expected_symbols & 15, expected_symbols >> 4), axis=1).ravel()
    samples = {name: sample_blocks(rgb, name) for name in SAMPLERS}
    output = []
    for name in SAMPLERS:
        # Same-sampler calibration is the existing shader's behavior. Hybrid
        # calibration4 variants isolate changes to header samples alone.
        calibrations = [name] + (["mean4"] if name != "mean4" else [])
        for cal_name in calibrations:
            data, valid, symbols, centers, observed = recover(samples[name], samples[cal_name])
            baseline = name == cal_name == 'mean4'
            crc = zlib.crc32(data[:52])
            stored = struct.unpack_from('<I', data, 52)[0]
            distances = np.abs(observed[:, None] - centers[None, :])
            ranks = (distances < distances[np.arange(112), expected_symbols, None]).sum(axis=1) + 1
            output.append(dict(profile=row['profile'], file=row['file'], expectedFrame=row['expectedFrame'],
                               gpuHeaderValid=row['headerValid'] == 'True', gpuStage=row['rejectionStage'],
                               sampler=name, calibrationSampler=cal_name, headerCrcValid=bool(valid),
                               headerExact=data == expected, headerByteErrors=sum(a != b for a, b in zip(data, expected)),
                               headerSymbolErrors=int(np.count_nonzero(symbols != expected_symbols)),
                               expectedCandidateMaxRank=int(ranks.max()), headerCrcActual=f'{crc:08X}',
                               headerCrcExpected=f'{stored:08X}',
                               gpuOutcomeAgreement=(not baseline or valid == (row['headerValid'] == 'True')),
                               gpuComputedCrcAgreement=(not baseline or not row['headerCrcActual'] or f'{crc:08X}' == row['headerCrcActual']),
                               gpuStoredCrcAgreement=(not baseline or not row['headerCrcExpected'] or f'{stored:08X}' == row['headerCrcExpected']),
                               input=str(path), reference=str(expected_path)))
    return output


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evidence', type=Path, required=True)
    p.add_argument('--results', type=Path, required=True)
    p.add_argument('--profile-filter', default='crf28')
    p.add_argument('--per-profile', type=int, default=0)
    p.add_argument('--workers', type=int, default=4)
    args = p.parse_args()
    args.results.mkdir(parents=True, exist_ok=True)
    if any(args.results.iterdir()):
        raise ValueError('Choose a fresh results directory')
    manifest = json.loads((args.evidence / 'matrix-gpu-manifest.json').read_text(encoding='utf-8-sig'))
    profiles = {item['name']: item for item in manifest['profiles']}
    rows = list(csv.DictReader((args.evidence / 'gpu-matrix' / 'frames.csv').open(encoding='utf-8-sig')))
    counts, tasks = {}, []
    for row in rows:
        if args.profile_filter not in row['profile']:
            continue
        counts[row['profile']] = counts.get(row['profile'], 0) + 1
        if args.per_profile and counts[row['profile']] > args.per_profile:
            continue
        tasks.append((row, Path(profiles[row['profile']]['framesDirectory']) / row['file']))
    results = []
    with concurrent.futures.ProcessPoolExecutor(max_workers=args.workers) as pool:
        for i, output in enumerate(pool.map(process, tasks, chunksize=8), 1):
            results.extend(output)
            if i % 100 == 0:
                print(f'PROGRESS {i}/{len(tasks)}', flush=True)
    with (args.results / 'headers.csv').open('w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=list(results[0]))
        writer.writeheader()
        writer.writerows(results)
    grouped = {}
    for row in results:
        key = row['profile'] + '__' + row['sampler'] + '__cal_' + row['calibrationSampler']
        group = grouped.setdefault(key, dict(frames=0, crcValid=0, exact=0, recovered=0, lost=0, falseCrcPass=0, symbolErrors=0))
        group['frames'] += 1
        group['crcValid'] += row['headerCrcValid']
        group['exact'] += row['headerExact']
        group['recovered'] += row['headerExact'] and not row['gpuHeaderValid']
        group['lost'] += not row['headerExact'] and row['gpuHeaderValid']
        group['falseCrcPass'] += row['headerCrcValid'] and not row['headerExact']
        group['symbolErrors'] += row['headerSymbolErrors']
    baseline = [r for r in results if r['sampler'] == r['calibrationSampler'] == 'mean4']
    summary = dict(scope='CPU numerical screening only; no new GPU validation', inputs=len(tasks),
                   profileFilter=args.profile_filter, perProfile=args.per_profile,
                   baselineGpuOutcomeAgreements=sum(r['gpuOutcomeAgreement'] for r in baseline),
                   baselineGpuComputedCrcAgreements=sum(r['gpuComputedCrcAgreement'] for r in baseline),
                   baselineGpuStoredCrcAgreements=sum(r['gpuStoredCrcAgreement'] for r in baseline),
                   scriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), groups=grouped)
    (args.results / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in summary.items() if k != 'groups'}, indent=2))
    totals = {}
    for key, group in grouped.items():
        name = key.split('__', 2)[-1]
        total = totals.setdefault(name, {metric: 0 for metric in group})
        for metric, value in group.items():
            total[metric] += value
    print(json.dumps(totals, indent=2))


if __name__ == '__main__':
    main()
