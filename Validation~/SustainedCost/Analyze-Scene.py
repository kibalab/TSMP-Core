"""Recount SustainedCost scene timings and independently validate archived packets.

Usage: python Analyze-Scene.py --results RUN [--out OUTPUT.json]
Only reads product/fixture evidence and writes the requested analysis JSON.
"""
import argparse
from bisect import bisect_right
import csv
import hashlib
import json
import math
import re
import struct
import zlib
from collections import Counter, defaultdict
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_csv(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def quantile(values, q):
    return sorted(values)[min(len(values) - 1, math.ceil(len(values) * q) - 1)] if values else None


def stats(values):
    values = list(values)
    return dict(count=len(values), mean=sum(values) / len(values) if values else None,
                median=quantile(values, .5), p95=quantile(values, .95), p99=quantile(values, .99),
                maximum=max(values) if values else None,
                p99_equals_maximum_by_rank=bool(values) and math.ceil(len(values) * .99) == len(values))


def fnv(text):
    value = 2166136261
    for byte in text.encode('utf-8'):
        value = ((value ^ byte) * 16777619) & 0xffffffff
    return value


def check_packet(header, payload, packet_id):
    assert len(header) == 56 and header[:4] == b'TSMP'
    assert struct.unpack_from('<H', header, 8)[0] == 56
    assert struct.unpack_from('<H', header, 10)[0] & 1, 'Payload CRC flag must remain enabled'
    assert struct.unpack_from('<I', header, 24)[0] == packet_id
    assert struct.unpack_from('<H', header, 40)[0] == 0x100
    assert struct.unpack_from('<H', header, 42)[0] == len(payload)
    assert zlib.crc32(header[:52]) == struct.unpack_from('<I', header, 52)[0]
    assert zlib.crc32(payload) == struct.unpack_from('<I', header, 44)[0]
    assert payload[:2] == b'\x01\x00'
    message_count = struct.unpack_from('<H', payload, 2)[0]
    fields = {}
    cursor = 8
    network_ids = []
    empty_variable_messages = 0
    for _ in range(message_count):
        network_id, kind, flags, sequence, body_bytes = struct.unpack_from('<HBBHH', payload, cursor)
        assert kind == 1 and flags == 0
        network_ids.append(network_id)
        cursor += 8
        body_end = cursor + body_bytes
        count = struct.unpack_from('<H', payload, cursor)[0]
        empty_variable_messages += count == 0
        cursor += 2
        for _ in range(count):
            field_hash, value_type, value_bytes = struct.unpack_from('<IBH', payload, cursor)
            cursor += 7
            value = payload[cursor:cursor + value_bytes]
            assert len(value) == value_bytes
            cursor += value_bytes
            key = (network_id, field_hash)
            assert key not in fields
            fields[key] = (value_type, value)
        assert cursor == body_end
    assert cursor == len(payload) and sorted(set(network_ids)) == list(range(1, 15)), (packet_id, cursor, len(payload), message_count, network_ids)
    assert len(fields) == 17
    for network_id in range(1, 13):
        value_type, value = fields[network_id, fnv('transform.packed')]
        assert value_type == 8 and len(value) == 31
    value_type, value = fields[13, fnv('humanoid.pose')]
    assert value_type == 8 and len(value) == 327
    value_type, value = fields[14, fnv('SceneCostState.showcase.sequence')]
    assert value_type == 2 and len(value) == 4 and struct.unpack('<i', value)[0] == packet_id
    value_type, value = fields[14, fnv('SceneCostState.showcase.seconds')]
    assert value_type == 3 and len(value) == 4 and math.isfinite(struct.unpack('<f', value)[0])
    value_type, value = fields[14, fnv('SceneCostState.showcase.message')]
    assert value_type == 7 and value.decode('utf-8') == 'HELLO TSMP'
    value_type, value = fields[14, fnv('SceneCostState.showcase.bytes')]
    assert value_type == 8 and len(value) == 256
    # Reproduce the fixture's deterministic byte source independently.
    pattern = (packet_id * 2654435761 + 2246822519) & 0xffffffff
    expected = bytearray()
    for _ in range(256):
        pattern ^= (pattern << 13) & 0xffffffff
        pattern ^= pattern >> 17
        pattern ^= (pattern << 5) & 0xffffffff
        expected.append(pattern & 0xff)
    assert value == expected
    return {'header_crc': True, 'payload_crc': True, 'messages': message_count, 'empty_variable_messages': empty_variable_messages,
            'fields': len(fields), 'bytes': len(payload)}


def packet_archive(path, publications, applications):
    blob = path.read_bytes()
    version, sent_count, received_count = struct.unpack_from('<iii', blob)
    assert version == 1 and sent_count == len(publications) and received_count == len(applications)
    cursor = 12
    sent, received = {}, {}
    source_messages, source_empty_messages = Counter(), Counter()
    for destination, count, rows in ((sent, sent_count, publications), (received, received_count, applications)):
        for index in range(count):
            packet_id, length = struct.unpack_from('<ii', blob, cursor)
            cursor += 8
            header = blob[cursor:cursor + 56]
            cursor += 56
            payload = blob[cursor:cursor + length]
            cursor += length
            assert packet_id == int(rows[index]['id']) and length == int(rows[index]['bytes'])
            assert packet_id not in destination
            checked = check_packet(header, payload, packet_id)
            if destination is sent:
                source_messages[checked['messages']] += 1
                source_empty_messages[checked['empty_variable_messages']] += 1
            destination[packet_id] = (header, payload)
    assert cursor == len(blob)
    assert all(sent[packet_id] == value for packet_id, value in received.items())
    return {'source_packets': sent_count, 'received_packets': received_count,
            'independent_header_and_payload_crc_checks': sent_count + received_count,
            'received_packets_exactly_equal_source': True, 'fields_per_packet': 17,
            'source_message_count_distribution': dict(source_messages),
            'source_empty_variable_message_distribution': dict(source_empty_messages),
            'source_and_received_schema_and_state_bytes_validated': True,
            'pose_target_scope': 'Actual target pose/transform equality is checked by fixture assertions after drain; raw target poses are not exported for independent rechecking.'}


def analyze(root):
    result_text = (root / 'result.txt').read_text(encoding='utf-8-sig')
    assert result_text.startswith('PASS')
    frequency = int(re.search(r'StopwatchFrequency=(\d+)', result_text).group(1))
    plan = json.loads((root / 'plan-used.json').read_text(encoding='utf-8-sig'))
    before = {r['path']: r['sha256'].lower() for r in read_csv(root / 'source-before.csv')}
    after = {r['path']: r['sha256'].lower() for r in read_csv(root / 'source-after.csv')}
    assert before and before == after
    # Current-file comparison is useful while the isolated source is unchanged.
    current = {'equal': 0, 'changed_since_run': [], 'missing': []}
    for path, expected in before.items():
        source = Path(path)
        if not source.is_file():
            current['missing'].append(path)
        elif digest(source) != expected:
            current['changed_since_run'].append(path)
        else:
            current['equal'] += 1
    artifacts = read_csv(root / 'artifact-hashes.csv')
    plan_receipts = [row for row in artifacts if row['kind'] == 'plan']
    assert len(plan_receipts) == 1 and digest(root / 'plan-used.json') == plan_receipts[0]['sha256']
    bytecode = {row['path']: (int(row['bytes']), row['sha256']) for row in read_csv(root / 'bytecode-sizes.csv')}
    assert bytecode and all(size > 0 for size, _ in bytecode.values())
    for row in artifacts:
        if row['kind'] == 'bytecode':
            assert bytecode[row['path']][1] == row['sha256']
    config_equal = None
    if (root / 'configuration-before.json').is_file():
        config_equal = json.loads((root / 'configuration-before.json').read_text(encoding='utf-8-sig')) == json.loads((root / 'configuration-after.json').read_text(encoding='utf-8-sig'))
        assert config_equal
    output = {'status': 'PASS', 'scope': 'Independent raw evidence recount; SDK Editor VM only. No logged-in VRChat, rendered avatar, OBS/CDN or GPU execution-time result.',
              'quantile': 'nearest-rank ceil(n*q)-1; sample counts retained',
              'source_before_after_equal': True, 'source_file_count': len(before),
              'current_source_comparison': current, 'configuration_before_after_equal': config_equal,
              'plan_sha256': digest(root / 'plan-used.json'), 'bytecode_receipts': bytecode,
              'bytecode_scope': 'Loaded bytecode receipt lengths/hash cross-check only; raw bytecode files are not exported.',
              'cases': {}}
    for case in plan['cases']:
        assert case['scene']
        prefix = root / (case['name'] + '-scene-')
        scope_text = Path(str(prefix) + 'scope.txt').read_text(encoding='utf-8-sig')
        scope = dict(line.split('=', 1) for line in scope_text.splitlines() if '=' in line and ' ' not in line.split('=', 1)[0])
        low, high = float(scope['begin']), float(scope['end'])
        seconds = high - low
        events = read_csv(Path(str(prefix) + 'vm.csv'))
        frames = read_csv(Path(str(prefix) + 'frames.csv'))
        pubs = read_csv(Path(str(prefix) + 'publications.csv'))
        apps = read_csv(Path(str(prefix) + 'applications.csv'))
        assert [int(p['id']) for p in pubs] == list(range(1, len(pubs) + 1))
        app_ids = [int(a['id']) for a in apps]
        assert app_ids == sorted(set(app_ids)) and set(app_ids) <= set(range(1, len(pubs) + 1))
        for p in pubs:
            assert int(p['id']) == int(p['encodedFrame']) and int(p['variables']) == 17 and p['gpu'] == 'True'
            assert (p['measured'] == 'True') == (low <= float(p['sent']) < high)
        for index, a in enumerate(apps):
            p = pubs[int(a['id']) - 1]
            assert int(a['ordinal']) == index and a['fields'] == '17' and a['crc'] == 'True'
            assert a['encodedFrame'] == p['encodedFrame'] and a['bytes'] == p['bytes']
            assert abs(float(a['sent']) - float(p['sent'])) < 1e-8
            assert abs((float(a['applied']) - float(a['sent'])) * 1000 - float(a['latencyMs'])) < 1e-6
            assert (a['measuredSource'] == 'True') == (p['measured'] == 'True')
        has_nested_rows = bool(events) and 'top' in events[0]
        top_events = [e for e in events if e.get('top', 'True') == 'True']
        nested_events = [e for e in events if e.get('top', 'True') != 'True']
        assert all(e['role'] in ('Encoder', 'Decoder') for e in top_events)
        for e in events:
            assert e['role'] in ('Encoder', 'Decoder', 'SceneSender', 'SceneTarget', 'SceneState')
            gross = (int(e['endTicks']) - int(e['startTicks'])) * 1000 / frequency
            assert gross >= 0 and float(e['netVmMs']) >= 0 and 0 <= float(e['nestedHostMs']) <= gross
            assert abs(gross - float(e['grossVmMs'])) < 1e-6
            assert abs(gross - float(e['nestedHostMs']) - float(e['netVmMs'])) < 1e-6
        ordered_top = sorted(top_events, key=lambda parent: int(parent['startTicks']))
        parent_starts = [int(parent['startTicks']) for parent in ordered_top]
        parent_stops = [int(parent['endTicks']) for parent in ordered_top]
        assert all(stop <= following for stop, following in zip(parent_stops, parent_starts[1:])), 'Top-level events must not overlap'
        for child in nested_events:
            start, stop = int(child['startTicks']), int(child['endTicks'])
            index = bisect_right(parent_starts, start) - 1
            assert index >= 0 and stop <= parent_stops[index], 'Every component row must fit exactly one top-level parent'
            parent = ordered_top[index]
            assert int(parent['frame']) == int(child['frame'])
            assert parent['role'] == ('Encoder' if child['role'] == 'SceneSender' else 'Decoder')
        for f in frames:
            assert (f['measured'] == 'True') == (low <= float(f['clock']) < high)
            if int(f['received']) > 0:
                p = pubs[int(f['received']) - 1]
                assert abs(float(f['receivedSentClock']) - float(p['sent'])) < 1e-8
                assert abs((float(f['ageSampleClock']) - float(p['sent'])) * 1000 - float(f['ageMs'])) < 1e-6
        es = [e for e in top_events if low <= float(e['clock']) < high]
        nested_es = [e for e in nested_events if low <= float(e['clock']) < high]
        fs = [f for f in frames if f['measured'] == 'True']
        ps = [p for p in pubs if p['measured'] == 'True']
        cohort = {int(p['id']) for p in ps}
        aa = [a for a in apps if int(a['id']) in cohort]
        arrivals = [a for a in apps if low <= float(a['applied']) < high]
        frame_cpu = defaultdict(lambda: dict(net=0, gross=0, nested=0, encoder=0, decoder=0))
        for f in fs:
            frame_cpu[int(f['frame'])]
        grouped = defaultdict(list)
        for e in es:
            value = frame_cpu[int(e['frame'])]
            value['net'] += float(e['netVmMs'])
            value['gross'] += float(e['grossVmMs'])
            value['nested'] += float(e['nestedHostMs'])
            value[e['role'].lower()] += float(e['netVmMs'])
            grouped[e['role'] + '/' + e['event']].append(float(e['netVmMs']))
        component_groups = defaultdict(list)
        for e in nested_es:
            component = int(e['component'])
            family = 'cube' if component < 12 else 'humanoid' if component == 12 else 'state'
            component_groups[e['role'] + '/' + family].append(float(e['netVmMs']))
        cpu = [v['net'] for v in frame_cpu.values()]
        encoder_mapping_mismatches = []
        publications_by_frame = {int(p['frame']): int(p['id']) for p in pubs}
        for e in es:
            if e['role'] == 'Encoder' and int(e['source']) != publications_by_frame.get(int(e['frame'])):
                encoder_mapping_mismatches.append({'frame': int(e['frame']), 'eventSource': int(e['source']), 'publicationId': publications_by_frame.get(int(e['frame']))})
        missing = sorted(cohort - set(app_ids))
        all_missing = sorted(set(range(1, len(pubs) + 1)) - set(app_ids))
        callback_clock = [float(e['clock']) for e in es if e['role'] == 'Decoder' and e['event'] == '_onAsyncGpuReadbackComplete']
        callback_rows = [e for e in es if e['role'] == 'Decoder' and e['event'] == '_onAsyncGpuReadbackComplete']
        observed_wait_rows = [e for e in callback_rows if float(e.get('requestWaitLowerMs', '-1')) >= 0 and int(e.get('requestAmbiguity', '1')) == 0]
        for e in observed_wait_rows:
            assert int(e['callbackRequest']) > 0 and int(e['callbackSlot']) in (0, 1)
            assert 0 <= float(e['requestWaitLowerMs']) <= float(e['requestWaitUpperMs'])
        callbacks_per_frame = Counter(int(e['frame']) for e in es if e['role'] == 'Decoder' and e['event'] == '_onAsyncGpuReadbackComplete')
        publications_by_id = {int(p['id']): p for p in pubs}
        missing_details = []
        for packet_id in missing:
            p = publications_by_id[packet_id]
            newer = next((a for a in apps if int(a['id']) > packet_id), None)
            missing_details.append({'id': packet_id, 'sent': float(p['sent']), 'frame': int(p['frame']),
                                    'publication_duration_ms': (float(p['published']) - float(p['sent'])) * 1000,
                                    'first_later_applied_id': int(newer['id']) if newer else None,
                                    'first_later_applied_after_missing_source_ms': (float(newer['applied']) - float(p['sent'])) * 1000 if newer else None})
        output['cases'][case['name']] = {
            'seconds': seconds, 'warm_seconds': float(scope['warmSeconds']), 'safety_mode': int(scope['safetyMode']),
            'render_target_hz': int(scope['renderTargetHz']), 'achieved_render_hz': len(fs) / seconds,
            'source_target_hz': int(scope['sourceTargetHz']), 'achieved_publication_hz': len(ps) / seconds,
            'measured_update_frames': len(fs), 'observed_cpu_frame_union_count': len(frame_cpu),
            'measured_publications': len(ps), 'cohort_received_after_drain': len(aa),
            'cohort_delivery_percent': 100 * len(aa) / len(ps) if ps else None,
            'cohort_applied_before_end': sum(float(a['applied']) < high for a in aa),
            'arrival_window_applications': len(arrivals), 'arrival_window_application_hz': len(arrivals) / seconds,
            'all_publications': len(pubs), 'all_applications': len(apps), 'all_missing_ids': all_missing,
            'missing_cohort_ids': missing, 'missing_cohort_details': missing_details,
            'sender_deadline_misses': int(scope['senderDeadlineMisses']),
            'event_cpu_ms': {key: stats(value) for key, value in sorted(grouped.items())},
            'component_rows_present': has_nested_rows,
            'nested_component_cpu_ms_included_in_parent': {key: {**stats(value), 'total_ms': sum(value)} for key, value in sorted(component_groups.items())},
            'tsmp_net_cpu_per_observed_frame_ms': stats(cpu),
            'tsmp_gross_cpu_per_observed_frame_ms': stats(v['gross'] for v in frame_cpu.values()),
            'encoder_cpu_per_observed_frame_ms': stats(v['encoder'] for v in frame_cpu.values()),
            'decoder_cpu_per_observed_frame_ms': stats(v['decoder'] for v in frame_cpu.values()),
            'net_cpu_ms_per_second': sum(cpu) / seconds,
            'nested_host_ms': sum(v['nested'] for v in frame_cpu.values()),
            'cpu_budget_exceedance': [{'budget_ms': budget, 'frames': sum(v > budget for v in cpu),
                                       'denominator': len(cpu), 'percent': 100 * sum(v > budget for v in cpu) / len(cpu)} for budget in (1000 / 60, 1000 / 90)],
            'slowest_cpu_frames': [{'frame': frame, **value} for frame, value in sorted(frame_cpu.items(), key=lambda pair: pair[1]['net'], reverse=True)[:10]],
            'source_age_ms': stats(float(f['ageMs']) for f in fs if float(f['ageMs']) >= 0),
            'cohort_application_latency_ms': stats(float(a['latencyMs']) for a in aa),
            'publication_duration_ms': stats((float(p['published']) - float(p['sent'])) * 1000 for p in ps),
            'wall_frame_interval_ms': stats((float(right['clock']) - float(left['clock'])) * 1000 for left, right in zip(fs, fs[1:])),
            'callback_interval_ms': stats((right - left) * 1000 for left, right in zip(callback_clock, callback_clock[1:])),
            'request_to_callback_observed_wall_bound': {
                'scope': 'Bounds from observed slot-request issuing Interpret end/start to callback Interpret start. Includes GPU queue, frame pacing and main-thread callback scheduling; not GPU execution time. Only unambiguous observed callbacks included.',
                'callback_count': len(callback_rows), 'unambiguous_observed_count': len(observed_wait_rows),
                'ambiguous_or_unobserved_count': len(callback_rows) - len(observed_wait_rows),
                'lower_ms': stats(float(e['requestWaitLowerMs']) for e in observed_wait_rows),
                'upper_ms': stats(float(e['requestWaitUpperMs']) for e in observed_wait_rows)},
            'gpu_callback_events_per_observed_frame': stats(callbacks_per_frame[frame] for frame in frame_cpu),
            'host_step_ms': stats(float(f['stepMs']) for f in fs),
            'host_preparation_ms': stats(float(p['preparationMs']) for p in ps),
            'host_source_snapshot_ms': stats(float(p['snapshotMs']) for p in ps),
            'gc_collections': {f'gen{generation}': int(fs[-1][f'gc{generation}']) - int(fs[0][f'gc{generation}']) for generation in range(3)} if fs else {},
            'encoder_source_mapping_mismatch_count': len(encoder_mapping_mismatches),
            'encoder_source_mapping_mismatches_first10': encoder_mapping_mismatches[:10],
            'archive': packet_archive(Path(str(prefix) + 'packets.bin'), pubs, apps),
            'whole_session_counters_not_measurement_window': {key: int(value) for key, value in scope.items() if key.startswith('counterWholeSession.')},
            'source_motion_evidence': {key: float(value) for key, value in scope.items() if key.startswith('sourceMax') or key in ('sourceCube0DescendingSamples', 'sourceFinalDynamicGravityBodies')},
            'limits': ['Application time is the end of the complete 17-field cohort before Decoder final cleanup.',
                       'Source timestamp precedes native source preparation and EncodeNow; publication completion is separately recorded.',
                       'Request-to-callback observed bounds require the optional request identity columns; missing/ambiguous callbacks are excluded. Callback intervals and source-to-application latency are not GPU execution time.',
                       'Native Playables/release preparation is timed; automatic FixedUpdate PhysX, rendering and overall Editor CPU are not individually measured.',
                       'A fixture PASS validates applied content and target equality, not delivery of every published frame. Missing publication IDs are reported explicitly.']}
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--out', type=Path)
    args = parser.parse_args()
    result = analyze(args.results)
    out = args.out or args.results / 'independent-analysis.json'
    if out.exists():
        parser.error('Fresh output name required; prior evidence is preserved')
    out.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'status': result['status'], 'out': str(out),
                      'cases': {name: {key: value[key] for key in ('measured_publications', 'cohort_received_after_drain', 'cohort_delivery_percent', 'achieved_render_hz', 'net_cpu_ms_per_second')} for name, value in result['cases'].items()}}))


if __name__ == '__main__':
    main()
