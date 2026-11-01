"""Recount actual applications and top-level CPU/render frames from raw evidence."""
import argparse
import csv
import hashlib
import json
import math
import re
from collections import Counter, defaultdict
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--results', type=Path, required=True)
a = p.parse_args()
root = a.results
status_text=(root/'result.txt').read_text(encoding='utf-8-sig')
assert status_text.startswith('PASS')
frequency=int(re.search(r'StopwatchFrequency=(\d+)',status_text).group(1))

def read(name):
    return list(csv.DictReader((root/name).open(encoding='utf-8-sig', newline='')))

def quantile(values, q):
    values = sorted(values)
    return values[min(len(values)-1, math.ceil(len(values)*q)-1)] if values else None

def stats(values):
    return dict(count=len(values), mean=sum(values)/len(values) if values else None,
                p95=quantile(values,.95), p99=quantile(values,.99), maximum=max(values) if values else None)

summaries = read('summary.csv')
events = read('vm-events.csv')
frames = read('frames.csv')
pubs = read('publications.csv')
apps = read('applications.csv')
plan = json.loads((root/'plan-used.json').read_text(encoding='utf-8-sig'))
assert [s['case'] for s in summaries] == [c['name'] for c in plan['cases']]
assert all(int(e['endTicks']) >= int(e['startTicks']) and float(e['vmMs']) >= 0 for e in events)
for e in events:
    assert abs(float(e['grossVmMs'])-float(e['nestedObservationMs'])-float(e['vmMs'])) < 1e-7
    assert abs((int(e['endTicks'])-int(e['startTicks']))*1000/frequency-float(e['grossVmMs'])) < 1e-7
    assert e['owner'] in ('Encoder','Decoder'), 'Nested work must not be counted twice'
before = {r['path']:r['sha256'] for r in read('source-before.csv')}
after = {r['path']:r['sha256'] for r in read('source-after.csv')}
assert before == after and before
for h in read('artifact-hashes.csv'):
    if h['kind'] in ('plan','stress'):
        assert hashlib.sha256(Path(h['path']).read_bytes()).hexdigest() == h['sha256']

result = {}
for s in summaries:
    name = s['case']; lo,hi = float(s['begin']),float(s['end']); seconds = float(s['seconds'])
    es = [e for e in events if e['case']==name and lo <= float(e['time']) < hi]
    fs = [f for f in frames if f['case']==name and f['measured']=='True']
    assert len(fs)==int(s['renderTicks']) and len({int(f['frame']) for f in fs})==len(fs)
    all_times = [float(f['time']) for f in fs]
    frame_times = [(b-a)*1000 for a,b in zip(all_times,all_times[1:])]
    per_frame = defaultdict(lambda:dict(cpu=0,decoder=0,encoder=0,callbacks=0,host=0,gross=0,nested=0))
    for f in fs: per_frame[int(f['frame'])]
    for e in es:
        v=per_frame[int(e['frame'])]; ms=float(e['vmMs']); v['cpu']+=ms; v[e['owner'].lower()]+=ms
        v['callbacks']+=int('readback' in e['event'].lower()); v['host']+=float(e['observationMs']); v['gross']+=float(e['grossVmMs']); v['nested']+=float(e['nestedObservationMs'])
    ages=[float(f['sourceAgeMs']) for f in fs if float(f['sourceAgeMs'])>=0]
    for f in fs:
        if 'ageSampleTime' in f and float(f['sourceAgeMs'])>=0:
            assert abs((float(f['ageSampleTime'])-float(f['latestSentTime']))*1000-float(f['sourceAgeMs']))<1e-6
    matching_pubs=[r for r in pubs if r['case']==name and r['measured']=='True']
    matching_apps=[r for r in apps if r['case']==name and r['measured']=='True']
    if s['regime']=='changing-clean':
        assert len(matching_pubs)==int(s['published'])
        assert len(matching_apps)==int(s['receivedAfterDrain'])
        ids=[int(r['id']) for r in matching_apps]; assert ids==sorted(set(ids))
        assert set(ids)<=set(int(r['id']) for r in matching_pubs)
        assert int(s['corrupt'])==0
    measured_apps=[r for r in matching_apps if float(r['applied'])<hi]
    if s['regime']=='changing-clean': assert len(measured_apps)==int(s['appliedInWindow'])
    gc = {f'gen{g}': int(fs[-1][f'gc{g}'])-int(fs[0][f'gc{g}']) for g in range(3)} if fs else {}
    result[name]=dict(regime=s['regime'], codec=s['codec'], schedule=s['schedule'], budget=int(s['budget']),
        target_render_hz=int(s['renderTarget']), achieved_render_hz=len(fs)/seconds,
        event_frame_union_count=len(per_frame), measured_update_frame_count=len(fs), frame_time_ms=stats(frame_times), tsmp_cpu_per_frame_ms=stats([v['cpu'] for v in per_frame.values()]),
        decoder_cpu_per_frame_ms=stats([v['decoder'] for v in per_frame.values()]),
        encoder_cpu_per_frame_ms=stats([v['encoder'] for v in per_frame.values()]),
        encoder_event_cpu_ms=stats([float(e['vmMs']) for e in es if e['owner']=='Encoder' and e['event']=='EncodeNow']),
        callbacks_per_frame=stats([v['callbacks'] for v in per_frame.values()]),
        callback_events_ms=stats([float(e['vmMs']) for e in es if 'readback' in e['event'].lower()]),
        total_vm_cpu_ms=sum(float(e['vmMs']) for e in es), gross_vm_cpu_ms=sum(float(e['grossVmMs']) for e in es),
        vm_cpu_ms_per_second=sum(float(e['vmMs']) for e in es)/seconds,
        host_observation_ms=stats([float(e['observationMs']) for e in es]),
        nested_host_ms=sum(float(e['nestedObservationMs']) for e in es), gc_collections=gc,
        actual_published=len(matching_pubs), unique_received_after_drain=len(matching_apps) if s['regime']=='changing-clean' else None,
        cohort_applied_before_end_hz=len(measured_apps)/seconds if s['regime']=='changing-clean' else None,
        arrival_window_applied_hz=sum(r['case']==name and lo<=float(r['applied'])<hi for r in apps)/seconds if s['regime']=='changing-clean' else None,
        missed_published_ids=int(s['published'])-int(s['receivedAfterDrain']) if s['regime']=='changing-clean' else None,
        source_age_ms=stats(ages) if s['regime']=='changing-clean' else None,
        application_latency_ms=stats([float(r['latencyMs']) for r in matching_apps]),
        sender_deadline_misses=int(s['senderMisses']), busy_occupancy_percent=100*int(s['busyTicks'])/len(fs),
        actual_busy_decode_skips=int(s['busyDecodeSkips']), duplicates=int(s['duplicates']), retries=int(s['retries']),
        proven_admissions_lower_bound=int(s['provenAdmissions']), ambiguous_associations=int(s['ambiguousAssociations']))

out=dict(status='PASS', stopwatch_frequency=frequency, percentile='nearest-rank ceil(n*q)-1', cases=result, source_comparisons=len(before), raw_events=len(events), render_rows=len(frames),
         event_note='Net CPU subtracts measured nested receiver host checks; gross Interpret and host observations are retained. Render times include harness and Editor overhead; no liveclientFPS inference.',
         fixed_stress_note='Damaged repeated-ID cases disable duplicates to measure saturation; no unique delivery rate, source age, or new event delivery claim.',
         polling_note='Manual validation-only30Hz polling, noRPCworkload; actualframe-arrival signal unavailable. Product default must be retained unless separate delivery guards cover production scenarios.')
(root/'analysis.json').write_text(json.dumps(out,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:out[k] for k in ('status','source_comparisons','raw_events','render_rows')}))
