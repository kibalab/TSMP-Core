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
case_options = {c['name']:c for c in plan['cases']}
assert [s['case'] for s in summaries] == [c['name'] for c in plan['cases']]
assert all(int(e['endTicks']) >= int(e['startTicks']) and float(e['vmMs']) >= 0 for e in events)
for e in events:
    assert abs(float(e['grossVmMs'])-float(e['nestedObservationMs'])-float(e['vmMs'])) < 1e-7
    assert abs((int(e['endTicks'])-int(e['startTicks']))*1000/frequency-float(e['grossVmMs'])) < 1e-7
    assert e['owner'] in ('Encoder','Decoder'), 'Nested work must not be counted twice'
before = {r['path']:r['sha256'] for r in read('source-before.csv')}
after = {r['path']:r['sha256'] for r in read('source-after.csv')}
assert before == after and before
for h in read('harness-receipts.csv'):
    assert hashlib.sha256((root/h['path']).read_bytes()).hexdigest()==h['sha256']
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

stages = read('codec-stages.csv')
rpc_enqueues = read('rpc-enqueues.csv')
rpc_apps = read('rpc-applications.csv')
timer_control=read('timer-calibration.csv')
assert len(timer_control)==1025
for c in timer_control:
    assert abs((int(c['endTicks'])-int(c['startTicks']))*1000/frequency-float(c['deltaMs']))<1e-7
assert float(timer_control[-1]['deltaMs'])>=1
for s in summaries:
    name=s['case']; lo,hi=float(s['begin']),float(s['end'])
    encoder_events=[e for e in events if e['case']==name and e['owner']=='Encoder' and e['event']=='EncodeNow' and lo<=float(e['time'])<hi]
    measured_stages=[]; codec_totals=[]; remainder=[]
    for e in encoder_events:
        children=[c for c in stages if c['case']==name and c['topOwner']=='Encoder' and c['topEvent']=='EncodeNow' and int(e['startTicks'])<=int(c['startTicks']) and int(c['endTicks'])<=int(e['endTicks'])]
        if case_options[name].get('disableCodecTiming',False):
            assert not children, 'Codec wrapper must be absent in explicit calibration-off cases'
        else:
            assert sum(c['event']=='OnTSMPEncoderWritePayload' for c in children)==1, 'Exactly one measured Color256 payload-write event per successful EncodeNow required'
        for c in children:
            assert abs((int(c['endTicks'])-int(c['startTicks']))*1000/frequency-float(c['nestedObservationMs'])-float(c['vmMs']))<1e-7
        for x,y in zip(children,children[1:]): assert int(x['endTicks'])<=int(y['startTicks'])
        total=sum(float(c['vmMs']) for c in children)
        assert total<=float(e['vmMs'])+1e-6
        codec_totals.append(total); remainder.append(float(e['vmMs'])-total); measured_stages+=children
    by_event={label:stats([float(c['vmMs']) for c in measured_stages if c['event']==label]) for label in sorted({c['event'] for c in measured_stages})}
    enqueues=[r for r in rpc_enqueues if r['case']==name]
    received=[r for r in rpc_apps if r['case']==name]
    sent_keys=[(r['stream'],r['eventId']) for r in enqueues if r['accepted']=='True']
    received_keys=[(r['stream'],r['eventId']) for r in received]
    assert len(sent_keys)==len(set(sent_keys)) and len(received_keys)==len(set(received_keys))
    assert set(received_keys)<=set(sent_keys)
    times={(r['stream'],r['eventId']):float(r['time']) for r in enqueues}
    assert all(float(r['time'])>=times[(r['stream'],r['eventId'])] for r in received)
    result[name]['rpc']=dict(accepted=len(sent_keys), executed=len(received_keys), missing=sorted(set(sent_keys)-set(received_keys)),
        delay_ms=stats([(float(r['time'])-times[(r['stream'],r['eventId'])])*1000 for r in received]),
        source_note='Finite four-copy real client VM RPCs through protected raster and real GPU; local loopback, not ACK delivery guarantee.')
    if case_options[name].get('disableCodecTiming',False):
        result[name]['encoder_attribution']=dict(enabled=False,note='Explicit calibration-off case: codec uses the original unwrapped VM, so no codec-stage attribution is available. Outer Encoder remains timed.')
    else:
        result[name]['encoder_attribution']=dict(enabled=True,codec_custom_events_ms_per_encode=stats(codec_totals),
            remaining_ms_per_encode=stats(remainder), by_codec_event=by_event,
            codec_fraction=sum(codec_totals)/sum(float(e['vmMs']) for e in encoder_events),
            note='Nonoverlapping nested codec custom events only; remainder combines serialization/change comparison/CRC/base pixels/header/bridge/upload and other Encoder work. Do not add codec to whole Encoder again.')
    result[name]['delivery_guard_pass']=not result[name]['rpc']['missing'] and result[name]['missed_published_ids']==0 and result[name]['sender_deadline_misses']==0

pairs=[]
for pair in range(3):
    old_name=f'pair{pair}-every-render'; new_name=f'pair{pair}-poll60'
    if old_name not in result or new_name not in result: continue
    old,new=result[old_name],result[new_name]
    old_ids={int(r['id']) for r in pubs if r['case']==old_name and r['measured']=='True'}
    new_ids={int(r['id']) for r in pubs if r['case']==new_name and r['measured']=='True'}
    pairs.append(dict(pair=pair, total_cpu_change_percent=100*(new['vm_cpu_ms_per_second']/old['vm_cpu_ms_per_second']-1),
                      same_published_ids=old_ids==new_ids,
                      both_delivery_guards_pass=old['delivery_guard_pass'] and new['delivery_guard_pass'],
                      source_age_p95_change_ms=new['source_age_ms']['p95']-old['source_age_ms']['p95'],
                      frame_cpu_p99_change_ms=new['tsmp_cpu_per_frame_ms']['p99']-old['tsmp_cpu_per_frame_ms']['p99']))

out=dict(status='PASS', stopwatch_frequency=frequency, percentile='nearest-rank ceil(n*q)-1', cases=result, paired_poll60=pairs, source_comparisons=len(before), raw_events=len(events), render_rows=len(frames),
         adjacent_clock_read_ms=stats([float(c['deltaMs']) for c in timer_control[:-1]]), clock_positive_control_ms=float(timer_control[-1]['deltaMs']),
         event_note='Net CPU subtracts measured nested receiver host checks; gross Interpret and host observations are retained. Render times include harness and Editor overhead; no liveclientFPS inference.',
         fixed_stress_note='Damaged repeated-ID cases disable duplicates to measure saturation; no unique delivery rate, source age, or new event delivery claim.',
         polling_note='Validation-only explicit30/60Hz polling with finite four-copy RPCs, phase/jitter/hold/restart guards; actual video frame-arrival signal unavailable. PASS means valid measurement, not every schedule passes adoption guards.')
(root/'analysis.json').write_text(json.dumps(out,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:out[k] for k in ('status','source_comparisons','raw_events','render_rows')}))
