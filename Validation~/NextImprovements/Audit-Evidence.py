"""Recount final follow-up evidence without launching Unity or changing product code."""
import argparse
import csv
import hashlib
import json
import math
import re
import struct
import subprocess
import zlib
from collections import defaultdict
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def rows(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def receipts(path):
    return {item['Path']: item['Hash'].lower() for item in read(path)}


def percentile(values, q):
    return sorted(values)[max(0, math.ceil(len(values) * q) - 1)]


def audit_realtime(folder):
    assert (folder / 'result.txt').read_text().startswith('PASS')
    trace = rows(folder / 'realtime-trace.csv')
    grouped = defaultdict(list)
    for row in trace:
        grouped[row['case']].append(row)
    assert len(grouped) == 7 and len(trace) == 1785
    fields = ['high', 'medium', 'slow']
    hashes = {}
    for index, field in enumerate(fields):
        digest = 2166136261
        for byte in ('RefreshRecoveryProbe.refresh.' + field).encode():
            digest = ((digest ^ byte) * 16777619) & 0xffffffff
        hashes[digest] = index
    packet_values, packet_events = {}, {}
    for row in trace:
        if not row['packet']:
            continue
        raw, header = (folder / row['packet']).read_bytes(), (folder / row['header']).read_bytes()
        assert len(header) == 56 and header[:4] == b'TSMP'
        assert zlib.crc32(header[:52]) == int.from_bytes(header[52:56], 'little')
        assert zlib.crc32(raw) == int.from_bytes(header[44:48], 'little')
        assert int.from_bytes(header[42:44], 'little') == len(raw) == int(row['payload_bytes'])
        assert int.from_bytes(header[24:28], 'little') == int.from_bytes(raw[4:8], 'little') == int(row['frame'])
        values, event_ids, cursor = {}, [], 8
        for _ in range(int.from_bytes(raw[2:4], 'little')):
            end = cursor + 8 + int.from_bytes(raw[cursor + 6:cursor + 8], 'little')
            at = cursor + 8
            if raw[cursor + 2] == 1:
                count = int.from_bytes(raw[at:at + 2], 'little')
                at += 2
                for _ in range(count):
                    key, value_type, length = struct.unpack_from('<IBH', raw, at)
                    assert key in hashes and value_type == 8 and length == 32
                    assert hashes[key] not in values
                    value = raw[at + 7:at + 7 + length]
                    assert len(value) == 32 and len(set(value)) == 1
                    values[hashes[key]] = value[0]
                    at += 7 + length
                assert at == end
            else:
                assert raw[cursor + 2] == 2 and int.from_bytes(raw[end - 4:end], 'little') == 501
                event_ids.append((int.from_bytes(header[20:24], 'little'), 501))
            assert cursor + 8 <= end <= len(raw)
            cursor = end
        assert cursor == len(raw)
        assert sum(1 << field for field in values) == int(row['sent_field_mask'])
        packet_values[row['packet']], packet_events[row['packet']] = values, event_ids
    assert len(packet_values) == 115
    intervals = [struct.unpack('<f', struct.pack('<f', value))[0] for value in [.1, .4, 1.5]]
    for name, samples in grouped.items():
        assert len(samples) == 255
        assert [int(row['deadline_index']) for row in samples] == list(range(255))
        states, seen, last_send, previous_value = [0, 0, 0], set(), [-1.] * 3, [-1] * 3
        restarted = False
        for row in samples:
            now = float(row['send_time'])
            assert float(row['call_start']) <= now <= float(row['call_end'])
            assert int(row['missed_deadlines']) == 0
            if float(row['restarted']) >= 0 and not restarted:
                restarted, last_send, previous_value = True, [-1.] * 3, [-1] * 3
            value = 77 if restarted else 42 if float(row['source_changed']) >= 0 else 0
            mask = 0
            for index, field in enumerate(fields):
                due = last_send[index] < 0 or now - last_send[index] >= intervals[index]
                changed_or_refresh = previous_value[index] != value or now - last_send[index] >= 1
                if due and changed_or_refresh:
                    mask |= 1 << index
                    assert now <= float(row[field + '_last_send']) <= float(row['call_end'])
                else:
                    assert float(row[field + '_last_send']) == last_send[index]
                last_send[index] = float(row[field + '_last_send'])
                if mask & 1 << index:
                    previous_value[index] = value
            assert mask == int(row['sent_field_mask'])
            if row['applied_packet']:
                for field, applied in packet_values[row['applied_packet']].items():
                    states[field] = applied
                seen.update(packet_events[row['applied_packet']])
            assert states == [int(row[field]) for field in fields]
            assert len(seen) == int(row['rpc_calls'])
        if name == 'no-loss-restart-dedup':
            assert len(seen) == 2
    for kind in ['product', 'config']:
        assert receipts(folder / (kind + '-before.json')) == receipts(folder / (kind + '-after.json'))
    return dict(observations=len(trace), packet_header_pairs=len(packet_values),
                actual_30hz_opportunities=255, missed_deadlines=0)


def audit_color_performance(folder):
    manifest = read(folder / 'ab/manifest.json')
    independent = read(folder / 'independent-performance-audit.json')
    assert independent['status'] == independent['candidate_adoption_delivery_gate'] == 'PASS'
    assert len(manifest['runs']) == 7
    assert sha(folder / 'ab/manifest.json') == independent['manifest_sha256']
    pooled = defaultdict(lambda: defaultdict(list))
    total_publications = total_applications = 0
    for run in manifest['runs']:
        directory = Path(run['results'])
        assert (directory / 'result.txt').read_text().startswith('PASS')
        assert sha(Path(run['plan'])) == run['plan_sha256']
        assert sha(Path(manifest['sources'][run['variant']]['path'])) == manifest['sources'][run['variant']]['sha256']
        summary = rows(directory / 'summary.csv')
        events = rows(directory / 'vm-events.csv')
        publications = rows(directory / 'publications.csv')
        applications = rows(directory / 'applications.csv')
        total_publications += len(publications)
        total_applications += len(applications)
        detail = read(directory / 'analysis.json')
        for case in summary:
            name = case['case']
            lo, hi = float(case['begin']), float(case['end'])
            calls = [float(event['vmMs']) for event in events if event['case'] == name and
                     event['owner'] == 'Encoder' and event['event'] == 'EncodeNow' and lo <= float(event['time']) < hi]
            measured_pubs = [pub for pub in publications if pub['case'] == name and pub['measured'] == 'True']
            measured_apps = [app for app in applications if app['case'] == name and app['measured'] == 'True']
            assert len(calls) == len(measured_pubs) == len(measured_apps)
            assert int(measured_pubs[0]['id']) == 1024
            assert [pub['id'] for pub in measured_pubs] == [app['id'] for app in measured_apps]
            stats = detail['cases'][name]['encoder_event_cpu_ms']
            assert abs(sum(calls) / len(calls) - stats['mean']) < 1e-7
            assert abs(percentile(calls, .95) - stats['p95']) < 1e-7
            if run['pair'] is not None:
                pooled[name][run['variant']].extend(calls)
            if run['variant'] == 'candidate':
                assert int(case['senderMisses']) == 0
                assert int(case['corrupt']) == 0
    assert total_publications == total_applications == 6884
    improvements = {}
    for case, variants in pooled.items():
        before = sum(variants['baseline']) / len(variants['baseline'])
        after = sum(variants['candidate']) / len(variants['candidate'])
        assert after < before
        improvements[case] = dict(before_mean_ms=before, after_mean_ms=after,
                                  reduction_percent=100 * (before - after) / before,
                                  baseline_calls=len(variants['baseline']), candidate_calls=len(variants['candidate']))
    return dict(runs=7, all_publications=total_publications, all_applications=total_applications,
                improvements=improvements)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--sustained-run', default='sustained/vrc-final')
    args = parser.parse_args()
    root = args.results
    baseline = read(root / 'baseline.json')
    for repo in baseline['repos']:
        assert sha(Path(repo['archive'])) == repo['archiveSha256'].lower()
        for ref, key in [('main', 'main'), ('origin/main', 'originMain')]:
            actual = subprocess.check_output(['git', '-C', repo['path'], 'rev-parse', ref], text=True).strip()
            assert actual == repo[key], (repo['name'], ref)
    for config in read(root / 'configuration-before.json'):
        assert sha(Path(config['path'])) == config['sha256'].lower(), config['path']
    ownership = read(root / 'source-audit-final.json')
    assert ownership['status'] == 'PASS' and not ownership['failures']

    allocation = root / 'allocation/native'
    assert (allocation / 'result.txt').read_text(encoding='utf-8-sig').startswith('PASS')
    counter = dict(line.split('=', 1) for line in (allocation / 'allocation-counter.txt').read_text().splitlines())
    assert int(counter['PositiveControlBytes']) == 8192 and int(counter['Calls']) == 1000
    valid = int(counter['PositiveControlDelta']) >= 8192
    assert (counter['CounterValid'] == 'True') == valid
    assert int(counter['VerifiedAllocatedBytes']) == (int(counter['RawDelta']) if valid else -1)

    cold_draws = cold_exact = 0
    for order in ('fixed-first', 'broad-first'):
        folder = root / 'rgb16-preparation' / order
        assert (folder / 'result.txt').read_text().startswith('PASS')
        assert receipts(folder / 'source-before.json') == receipts(folder / 'source-after.json')
        assert receipts(folder / 'configuration-before.json') == receipts(folder / 'configuration-after.json')
        draws = rows(folder / 'samples.csv')
        assert len(draws) == 54
        for draw in draws:
            flushed = draw['flushed'] == 'True'
            total = float(draw['prepareMs']) + float(draw['blitSubmitMs'])
            if flushed:
                total += float(draw['flushAndCompareMs'])
                assert draw['payload'] == 'EXACT'
                cold_exact += 1
            else:
                assert draw['payload'] == 'NOT_READ'
            assert abs(total - float(draw['totalMs'])) < 1e-6
        cold_draws += len(draws)

    budget = read(root / 'budget-independent-audit.json')
    assert budget['status'] == 'PASS'
    assert budget['trace_rows'] == 1446 and budget['packet_header_pairs'] == 92
    for relative, digest in budget['artifact_sha256'].items():
        assert sha(root / 'budget-recovery/native-final' / relative) == digest
    realtime = audit_realtime(root / 'budget-recovery/realtime-native-playmode')
    color_folder = root / 'color256-optimization'
    color = read(color_folder / 'color256-independent-correctness.json')
    assert color['status'] == 'PASS' and color['unchanged_other_sources'] == 391
    assert color['inserted_lines'] == 51 and color['deleted_lines'] == 0
    for stage, directory in [('baseline', 'baseline-correctness'), ('candidate', 'candidate-correctness')]:
        vector_rows = rows(color_folder / directory / 'vectors.csv')
        assert len(vector_rows) == color['stages'][stage]['vectors'] == 25
        assert sum(row['vm'] == 'True' for row in vector_rows) == 19
        for vector in vector_rows:
            for kind in ['managed', 'oracle', 'payload'] + (['vm'] if vector['vm'] == 'True' else []):
                path = color_folder / directory / (vector['name'] + '.' + kind + '.bin')
                key = {'managed': 'nativeSha256', 'oracle': 'oracleSha256', 'payload': 'payloadSha256', 'vm': 'vmSha256'}[kind]
                assert sha(path) == vector[key]
                if stage == 'candidate':
                    assert path.read_bytes() == (color_folder / 'baseline-correctness' / path.name).read_bytes()
    color_performance = audit_color_performance(color_folder)

    sustained = root / args.sustained_run
    assert (sustained / 'result.txt').read_text().startswith('PASS')
    detail = read(sustained / 'analysis.json')
    assert detail['status'] == 'PASS'
    notes = (sustained / 'result.txt').read_text()
    frequency = int(re.search(r'StopwatchFrequency=(\d+)', notes).group(1))
    event_rows = rows(sustained / 'vm-events.csv')
    event_ids = [int(event['eventId']) for event in event_rows]
    assert len(event_ids) == len(set(event_ids))
    for event in event_rows:
        assert event['owner'] in ('Encoder', 'Decoder')
        ticks_ms = (int(event['endTicks']) - int(event['startTicks'])) * 1000 / frequency
        assert abs(ticks_ms - float(event['grossVmMs'])) < 1e-7
        assert float(event['vmMs']) >= 0
        assert abs(ticks_ms - float(event['nestedObservationMs']) - float(event['vmMs'])) < 1e-7
    summaries = rows(sustained / 'summary.csv')
    publications = rows(sustained / 'publications.csv')
    applications = rows(sustained / 'applications.csv')
    frames = rows(sustained / 'frames.csv')
    checks = {}
    for summary in summaries:
        name = summary['case']
        first, last = float(summary['begin']), float(summary['end'])
        measured_events = [event for event in event_rows if event['case'] == name and first <= float(event['time']) < last]
        frame_sums = defaultdict(float)
        for frame in frames:
            if frame['case'] == name and frame['measured'] == 'True':
                frame_sums[int(frame['frame'])] = 0
        for event in measured_events:
            frame_sums[int(event['frame'])] += float(event['vmMs'])
        analysis = detail['cases'][name]
        assert abs(sum(frame_sums.values()) - analysis['total_vm_cpu_ms']) < 1e-6
        stats = analysis['tsmp_cpu_per_frame_ms']
        assert len(frame_sums) == stats['count']
        assert abs(percentile(list(frame_sums.values()), .95) - stats['p95']) < 1e-7
        assert abs(percentile(list(frame_sums.values()), .99) - stats['p99']) < 1e-7
        cohort_pubs = [pub for pub in publications if pub['case'] == name and pub['measured'] == 'True']
        cohort_apps = [app for app in applications if app['case'] == name and app['measured'] == 'True']
        if summary['regime'] == 'changing-clean':
            pub_ids = {int(pub['id']) for pub in cohort_pubs}
            app_ids = [int(app['id']) for app in cohort_apps]
            assert app_ids == sorted(set(app_ids)) and set(app_ids) <= pub_ids
            assert len(pub_ids) == int(summary['published'])
            assert len(app_ids) == int(summary['receivedAfterDrain'])
            assert analysis['missed_published_ids'] == len(pub_ids) - len(app_ids)
            assert int(summary['corrupt']) == 0
        checks[name] = dict(measured_events=len(measured_events), cpu_frames=len(frame_sums),
                            source_publications=len(cohort_pubs),
                            accepted_unique=len(cohort_apps) if summary['regime'] == 'changing-clean' else None)

    result = dict(status='PASS', repositories=len(baseline['repos']),
                  source_comparisons=ownership['comparisons'], existing_guids=ownership['existingGuidsPreserved'],
                  allocation_counter_valid=valid, verified_allocated_bytes=int(counter['VerifiedAllocatedBytes']),
                  cold_draws=cold_draws, cold_flushed_exact_flags=cold_exact,
                  budget_trace_rows=budget['trace_rows'], budget_packet_header_pairs=budget['packet_header_pairs'],
                  realtime=realtime,
                  color_pixel_vectors_per_stage=25, color_client_vm_vectors_per_stage=19,
                  color_performance=color_performance,
                  sustained_cases=checks, sustained_raw_events=len(event_rows),
                  scope='Independent arithmetic/trace/provenance checks; saved GPU verdicts are recounted, not independently re-read. No live client or service measurement.')
    (root / 'root-independent-audit.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key != 'sustained_cases'}, indent=2))


if __name__ == '__main__':
    main()
