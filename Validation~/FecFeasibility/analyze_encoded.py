"""Gate actual GPU headers, correct encoded FEC bytes, verify original payloads.

This is a measurement-side Python receiver, not TSMP runtime acceptance or Udon
performance.  Raw shaders use reference geometry; accepting only an identical
CRC-valid received header makes that geometry identical for accepted cases.
"""
import argparse
import collections
import csv
import hashlib
import json
from pathlib import Path
import struct
import time
import zlib

from fec_envelope import (LAYOUT_ID, PAYLOAD_TYPE, PROTECTED_LAYOUT_ID,
                          PROTECTED_PAYLOAD_TYPE, unwrap)
from fec_reference import CorrectionFailure


def csv_rows(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def read_dump(row):
    data = Path(row['decoded']).read_bytes()
    if hashlib.sha256(data).hexdigest() != row['decodedSha256']:
        raise ValueError('Changed GPU output ' + row['decoded'])
    return data


def write_csv(path, rows):
    with path.open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--original-fixture', type=Path, required=True)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--current-production-csv', type=Path)
    parser.add_argument('--current-variant', default='SparseOn')
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    groups = collections.defaultdict(list)
    for row in csv_rows(args.manifest):
        groups[(row['profile'], row['file'])].append(row)
    observations, wrong_accepted = [], 0
    for (profile, filename), rows in groups.items():
        is_fec = '-rs' in profile
        protected_metadata = '-meta16-' in profile
        parity = int(profile.split('-rs')[1].split('-')[0]) if is_fec else 0
        codec = 'color256' if profile.startswith('color256') else 'rgb16'
        condition = profile.split('__', 1)[1]
        headers = [row for row in rows if row['mode'] == 'Header']
        payloads = [row for row in rows if row['mode'] == 'Payload']
        assert len(headers) == 1 and len(payloads) == 3
        reference_header = Path(headers[0]['reference']).read_bytes()
        reference_packet = Path(payloads[0]['reference']).read_bytes()
        actual_header = read_dump(headers[0])
        original_path = args.original_fixture / 'payload' / (Path(filename).stem + '.bin')
        original = original_path.read_bytes()
        assert len(reference_header) == 56
        assert zlib.crc32(reference_header[:52]) == struct.unpack_from('<I', reference_header, 52)[0]
        assert zlib.crc32(reference_packet) == struct.unpack_from('<I', reference_header, 44)[0]
        if is_fec:
            assert unwrap(reference_packet, protected_metadata) == (original, reference_packet)
        else:
            assert reference_packet == original
        header_crc = (len(actual_header) == 56 and actual_header[:4] == b'TSMP'
            and struct.unpack_from('<H', actual_header, 8)[0] == 56 and actual_header[4] == 0
            and zlib.crc32(actual_header[:52]) == struct.unpack_from('<I', actual_header, 52)[0])
        header_exact = header_crc and actual_header == reference_header
        if header_crc and not header_exact:
            raise AssertionError('Wrong received header passed CRC')
        if header_exact:
            expected_type = PROTECTED_PAYLOAD_TYPE if protected_metadata else PAYLOAD_TYPE if is_fec else 0x0100
            expected_layout = PROTECTED_LAYOUT_ID if protected_metadata else LAYOUT_ID if is_fec else 0
            assert struct.unpack_from('<H', actual_header, 40)[0] == expected_type
            assert struct.unpack_from('<H', actual_header, 18)[0] == expected_layout
        accepted, mode, attempts, errors = False, -1, 0, []
        normal_exact = False
        decode_ms = 0.0
        for item in sorted(payloads, key=lambda row: int(row['variant'].rsplit('mode', 1)[1])):
            raw = read_dump(item)
            candidate_mode = int(item['variant'].rsplit('mode', 1)[1])
            if candidate_mode == 0:
                normal_exact = raw == reference_packet
            if not header_exact or accepted:
                continue
            assert len(raw) == struct.unpack_from('<H', actual_header, 42)[0]
            attempts += 1
            tick = time.perf_counter()
            try:
                if is_fec:
                    candidate, reconstructed = unwrap(raw, protected_metadata)
                else:
                    candidate, reconstructed = raw, raw
                if zlib.crc32(reconstructed) != struct.unpack_from('<I', actual_header, 44)[0]:
                    raise CorrectionFailure('Protected outer payload CRC mismatch')
                if candidate != original or reconstructed != reference_packet:
                    wrong_accepted += 1
                    raise AssertionError('Wrong original or packet passed full acceptance checks')
                accepted, mode = True, candidate_mode
            except CorrectionFailure as error:
                errors.append(str(error))
            decode_ms += (time.perf_counter() - tick) * 1000
        observations.append(dict(profile=profile, file=filename, codec=codec, condition=condition,
            parity=parity, metadataParity=16 if protected_metadata else 0,
            originalBytes=len(original), transmittedBytes=len(reference_packet),
            receivedHeaderValid=header_exact, normalPacketExact=normal_exact,
            originalExact=accepted, acceptedMode=mode, attemptedCandidates=attempts,
            pythonFecAndCrcMs=decode_ms, failure=errors[-1] if errors else ('Header rejection' if not header_exact else ''),
            originalSha256=hashlib.sha256(original).hexdigest(), referencePacketSha256=hashlib.sha256(reference_packet).hexdigest()))
    baseline = {(row['codec'], row['condition'], row['file']): row for row in observations if row['parity'] == 0}
    summaries = []
    current = None
    if args.current_production_csv:
        current = {(row['profile'], row['file']): row for row in csv_rows(args.current_production_csv)
                   if row['variant'] == args.current_variant}
        if not current:
            raise ValueError('No current-production rows for selected variant')
    for profile in sorted({row['profile'] for row in observations}):
        cases = [row for row in observations if row['profile'] == profile]
        comparisons = [(row, baseline[(row['codec'], row['condition'], row['file'])]) for row in cases]
        summary = dict(profile=profile, codec=cases[0]['codec'], condition=cases[0]['condition'],
            parity=cases[0]['parity'], metadataParity=cases[0]['metadataParity'], observations=len(cases),
            originalExact=sum(row['originalExact'] for row in cases),
            receivedHeaderValid=sum(row['receivedHeaderValid'] for row in cases),
            originalBytes=sum(row['originalBytes'] for row in cases),
            exactOriginalBytes=sum(row['originalBytes'] * row['originalExact'] for row in cases),
            transmittedBytes=sum(row['transmittedBytes'] for row in cases),
            addedExactVsPairedBaseline=sum(row['originalExact'] and not old['originalExact'] for row, old in comparisons),
            lostExactVsPairedBaseline=sum(old['originalExact'] and not row['originalExact'] for row, old in comparisons))
        if current is not None:
            paired = []
            for row, old in comparisons:
                actual = current[(old['profile'], row['file'])]
                if int(actual['expectedBytes']) != row['originalBytes'] or 'WRONG' in actual['status']:
                    raise AssertionError('Current production reference mismatch')
                paired.append((row, actual['status'] == 'EXACT'))
            summary.update(pairedCurrentExact=sum(exact for _, exact in paired),
                currentExactOriginalBytes=sum(row['originalBytes'] * exact for row, exact in paired),
                addedExactVsCurrent=sum(row['originalExact'] and not exact for row, exact in paired),
                lostExactVsCurrent=sum(exact and not row['originalExact'] for row, exact in paired))
        summaries.append(summary)
    assert len(groups) == 1620 and len(summaries) == 18
    assert all(row['originalExact'] == 90 for row in summaries if row['condition'] == 'png-control')
    write_csv(args.results / 'observations.csv', observations)
    write_csv(args.results / 'profile-comparison.csv', summaries)
    result = dict(status='ACTUAL_ENCODED_RASTER_REFERENCE_FEC_MEASURED', observations=len(observations),
        wrongAccepted=wrong_accepted, sourceManifest=str(args.manifest),
        sourceManifestSha256=hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
        scriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        baseline='Frozen previous decoder global modes zero/one/two; no new sparse mixing.',
        currentProductionCsv=str(args.current_production_csv) if args.current_production_csv else None,
        currentProductionVariant=args.current_variant if args.current_production_csv else None,
        currentProductionSha256=hashlib.sha256(args.current_production_csv.read_bytes()).hexdigest() if args.current_production_csv else None,
        limitations=['Actual production raster and compression, actual GPU bytes, Python reference FEC only.',
            'No TSMP runtime FEC integration or application/state/RPC acceptance.',
            'No Udon/client/live-service performance, latency or reliability measurement.',
            'Python correction duration is not Udon performance.',
            'Same nominal CBR does not mean identical exact packet rate or instantaneous peaks.'])
    (args.results / 'summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2), flush=True)


if __name__ == '__main__':
    main()
