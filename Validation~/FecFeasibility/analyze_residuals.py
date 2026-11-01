"""Conditional FEC screen from actual GPU-decoded bytes, not an encoder test.

The observed old-format payload corruption is replayed with *perfect* new RS
parity.  Interleaving here reassigns old payload error positions to codewords;
it does not predict corruption in a newly arranged H.264 image.  Every result
is therefore a conditional feasibility bound, never delivered-goodput evidence.
"""
from __future__ import annotations
import argparse
import collections
import csv
import hashlib
import json
import math
from pathlib import Path
import time
import zlib

from fec_reference import CorrectionFailure, decode, encode


def read_csv(path):
    with Path(path).open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def word_indices(length, parity, layout):
    count = math.ceil(length / (255 - parity))
    if layout == 'contiguous':
        return [list(range(start, min(start + 255 - parity, length)))
                for start in range(0, length, 255 - parity)]
    return [list(range(word, length, count)) for word in range(count)]


def longest_run(indices):
    best = current = 0
    previous = -2
    for index in indices:
        current = current + 1 if index == previous + 1 else 1
        best = max(best, current)
        previous = index
    return best


def symbol_positions(expected, actual, bits):
    result = set()
    for index, (left, right) in enumerate(zip(expected, actual)):
        difference = left ^ right
        for bit in range(8):
            if difference & (1 << bit):
                result.add((index * 8 + bit) // bits)
    return sorted(result)


def summarize(rows):
    totals = collections.defaultdict(lambda: dict(observations=0, payloadBytes=0,
        encodedBytes=0, conditionalExact=0, conditionalExactBytes=0,
        headerValidObservations=0, headerValidConditionalExact=0,
        replayChecks=0))
    for row in rows:
        keys = [('ALL', row['parity'], row['layout']),
                (row['dataset'] + '/' + row['category'], row['parity'], row['layout'])]
        for key in keys:
            group = totals[key]
            group['observations'] += 1
            group['payloadBytes'] += row['payloadBytes']
            group['encodedBytes'] += row['encodedBytes']
            group['conditionalExact'] += row['conditionalWithinErrorBound']
            group['conditionalExactBytes'] += row['payloadBytes'] * row['conditionalWithinErrorBound']
            group['headerValidObservations'] += row['productionHeaderValid']
            group['headerValidConditionalExact'] += row['productionHeaderValid'] * row['conditionalWithinErrorBound']
            group['replayChecks'] += row['replayChecked']
    return [dict(group=key[0], parity=key[1], layout=key[2], **value,
                 parityOverheadPerSourceByte=value['encodedBytes'] / value['payloadBytes'] - 1)
            for key, value in sorted(totals.items())]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--production-csv', type=Path, action='append', required=True)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--parity', default='16,32,48')
    parser.add_argument('--variant-suffix', default='mode2')
    parser.add_argument('--replay-checks-per-group', type=int, default=8)
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    start = time.perf_counter()
    parity_values = [int(value) for value in args.parity.split(',')]
    if any(value < 2 or value > 128 or value % 2 for value in parity_values):
        raise ValueError('Use an even parity count in 2..128')
    production = {}
    for path in args.production_csv:
        for row in read_csv(path):
            if row['status'] != 'REJECTED' or row['variant'] not in ('Recovery', 'Recovery2'):
                continue
            key = (row['profile'], row['file'])
            if key in production and production[key] != row:
                raise ValueError('Ambiguous production variants: ' + repr(key))
            production[key] = row
    manifest = [row for row in read_csv(args.manifest)
                if row['mode'] == 'Payload' and row['variant'].endswith(args.variant_suffix)]
    keys = [(row['profile'], row['file']) for row in manifest]
    if not keys:
        raise ValueError('No selected GPU dumps')
    if len(keys) != len(set(keys)):
        raise ValueError('Duplicate selected observation')
    rows, sources = [], []
    replay_counts = collections.Counter()
    for ordinal, item in enumerate(manifest):
        key = (item['profile'], item['file'])
        prior = production[key]
        expected = Path(item['reference']).read_bytes()
        actual = Path(item['decoded']).read_bytes()
        header = Path(item['header']).read_bytes()
        actual_sha = hashlib.sha256(actual).hexdigest()
        if actual_sha.lower() != item['decodedSha256'].lower():
            raise ValueError('Changed GPU dump: ' + repr(key))
        if len(expected) != len(actual) or len(header) != 56:
            raise ValueError('Length mismatch: ' + repr(key))
        if int.from_bytes(header[42:44], 'little') != len(expected):
            raise ValueError('Independent header length mismatch')
        if zlib.crc32(header[:52]) != int.from_bytes(header[52:56], 'little'):
            raise ValueError('Independent header CRC mismatch')
        if zlib.crc32(expected) != int.from_bytes(header[44:48], 'little'):
            raise ValueError('Independent source payload CRC mismatch')
        if int.from_bytes(header[12:14], 'little') != 8:
            raise ValueError('Spatial diagnostics require the measured blockSize=8')
        errors = [index for index, (a, b) in enumerate(zip(expected, actual)) if a != b]
        profile = item['profile']
        dataset = 'holdout' if 'heldout' in profile else 'main'
        category = ('CBR' if '-cbr' in profile else 'CRF18' if '-crf18' in profile
                    else 'CRF28' if '-crf28' in profile else 'other')
        header_valid = prior['headerValid'].lower() == 'true'
        codec = int.from_bytes(header[32:34], 'little')
        if codec == 3:
            bits, calibration = 8, 256
        elif codec == 5:
            channel_bits = list(header[35:38])
            bits, calibration = sum(channel_bits), sum(1 << value for value in channel_bits)
        else:
            raise ValueError('Unsupported codec in spatial diagnostic')
        width = int.from_bytes(header[14:16], 'little')
        payload_row = 5 + math.ceil(calibration / width)
        symbols = symbol_positions(expected, actual, bits)
        xy = [(index % width, payload_row + index // width) for index in symbols]
        source = dict(profile=profile, file=item['file'], dataset=dataset, category=category,
            payloadBytes=len(expected), wrongBytes=len(errors), wrongBits=sum((a ^ b).bit_count() for a, b in zip(expected, actual)),
            longestWrongByteRun=longest_run(errors), wrongSymbols=len(symbols),
            damagedPayload64ByteRanges=len({index // 64 for index in errors}),
            damagedRaster16PixelTiles=len({(x // 2, y // 2) for x, y in xy}),
            damagedRaster32PixelTiles=len({(x // 4, y // 4) for x, y in xy}),
            productionHeaderValid=header_valid,
            reference=item['reference'], decoded=item['decoded'],
            referenceSha256=hashlib.sha256(expected).hexdigest(), decodedSha256=actual_sha)
        sources.append(source)
        for parity in parity_values:
            for layout in ('contiguous', 'round-robin'):
                indices = word_indices(len(expected), parity, layout)
                error_set = set(errors)
                per_word = [sum(index in error_set for index in word) for word in indices]
                within_bound = all(count <= parity // 2 for count in per_word)
                checked = False
                group = (dataset, category, parity, layout, within_bound)
                if replay_counts[group] < args.replay_checks_per_group:
                    restored = bytearray(len(expected))
                    success = True
                    for word in indices:
                        original_data = bytes(expected[index] for index in word)
                        observed_data = bytes(actual[index] for index in word)
                        # New parity is assumed perfect.  Never edit a saved
                        # source/dump or assert that an encoder preserves it.
                        damaged = observed_data + encode(original_data, parity)[-parity:]
                        try:
                            decoded = decode(damaged, parity)
                        except CorrectionFailure:
                            success = False
                            break
                        for index, value in zip(word, decoded):
                            restored[index] = value
                    full_exact = success and bytes(restored) == expected
                    crc_accepted_wrong = success and bytes(restored) != expected and zlib.crc32(restored) == zlib.crc32(expected)
                    if crc_accepted_wrong or (within_bound and not full_exact):
                        raise AssertionError('Reference correction invariant failed')
                    checked = True
                    replay_counts[group] += 1
                rows.append(dict(profile=profile, file=item['file'], dataset=dataset, category=category,
                    parity=parity, layout=layout, payloadBytes=len(expected),
                    encodedBytes=len(expected) + len(indices) * parity,
                    wrongBytes=len(errors), wordCount=len(indices), maxWrongBytesPerWord=max(per_word),
                    conditionalWithinErrorBound=within_bound, productionHeaderValid=header_valid,
                    replayChecked=checked))
        if (ordinal + 1) % 250 == 0:
            print(f'Analyzed {ordinal + 1}/{len(manifest)} GPU dumps', flush=True)
    for name, values in [('observations.csv', sources), ('conditional-parity.csv', rows),
                         ('summary.csv', summarize(rows))]:
        with (args.results / name).open('w', newline='', encoding='utf-8') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(values[0]))
            writer.writeheader()
            writer.writerows(values)
    summary = dict(status='CONDITIONAL_PERFECT_PARITY_REPLAY_ONLY', observations=len(sources),
        actualEncodedFecPathRuns=0, parityBytes=parity_values,
        replayChecks=sum(replay_counts.values()), sourceManifest=str(args.manifest),
        sourceManifestSha256=hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
        sourceScriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        referenceScriptSha256=hashlib.sha256(Path(__file__).with_name('fec_reference.py').read_bytes()).hexdigest(),
        seconds=time.perf_counter() - start,
        assumptions=['New parity bytes are perfect.',
            'Original observed payload error values/positions are reused.',
            'Round-robin redistributes those old errors across codewords.',
            'Changed raster/palette/entropy, parity corruption and header protection are unmeasured.',
            'Raw payload probes with invalid received headers are diagnostic only.',
            'Spatial tiles assume the measured blockSize=8 fixtures.'])
    (args.results / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2), flush=True)


if __name__ == '__main__':
    main()
