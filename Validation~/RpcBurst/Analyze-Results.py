"""Independently recount raw RPC packets/CRCs/queue state and application/timing rows."""
import argparse
from collections import Counter, defaultdict
import csv
import hashlib
import json
import math
import re
from pathlib import Path
import struct
import zlib

p = argparse.ArgumentParser()
p.add_argument('--results', type=Path, required=True)
a = p.parse_args()
root = a.results
def rows(name):
    with (root/name).open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
def f(row, key): return float(row[key])
def n(row, key): return int(row[key])
def pct(values):
    values = sorted(values)
    if not values: return dict(count=0)
    return dict(count=len(values), mean=sum(values)/len(values),
                p95=values[math.ceil(len(values)*.95)-1],
                p99=values[math.ceil(len(values)*.99)-1], maximum=values[-1])
def same(x, y, eps=1e-6): assert abs(x-y) <= eps, (x, y)
def apply_time(row, origin):
    # Early probe CSVs used .NET's seven-significant-digit float formatting.
    # Latency is a separately retained double computed from the original float.
    # Recover its exact IEEE754 float, verify the latency and the known display
    # rounding bound. New probe CSVs use round-trip formatting directly.
    exact = struct.unpack('<f', struct.pack('<f', origin + f(row,'latencyMs')/1000))[0]
    same(f(row,'latencyMs'), (exact-origin)*1000)
    printed=f(row,'apply')
    bound=.5001*10**(math.floor(math.log10(abs(exact)))-6) if exact else 0
    assert abs(printed-exact)<=max(bound,1e-9), (printed,exact,bound)
    return exact
def u16(data, pos): return struct.unpack_from('<H', data, pos)[0]
def u32(data, pos): return struct.unpack_from('<I', data, pos)[0]
def fnv(value):
    result=2166136261
    for byte in value.encode(): result=((result^byte)*16777619)&0xffffffff
    return result
def packet(row):
    data=(root/row['packet']).read_bytes(); header=(root/row['header']).read_bytes()
    assert len(header)==56 and len(data)==n(row,'bytes')==u16(header,42)
    assert u16(header,10)&1 and zlib.crc32(data)==u32(header,44)
    assert zlib.crc32(header[:52])==u32(header,52)
    assert u32(header,24)==n(row,'frameIndex')
    assert data[:2]==b'\x01\x00'
    cursor=8; ids=[]; markers=[]
    for _ in range(u16(data,2)):
        assert cursor+8<=len(data)
        assert u16(data,cursor)==1
        end=cursor+8+u16(data,cursor+6); assert end<=len(data)
        if data[cursor+2]==2:
            assert u32(data,cursor+8)==fnv('ReceiveEvent') and data[cursor+12]==2
            arg=cursor+13; assert data[arg]==7
            length=u16(data,arg+1); assert data[arg+3:arg+3+length]==b'ReceiveEvent'
            arg+=3+length; assert data[arg]==2 and u16(data,arg+1)==4
            ids.append(struct.unpack_from('<i',data,arg+3)[0]); assert arg+7==end
        else:
            assert data[cursor+2]==1
            count=u16(data,cursor+8); arg=cursor+10
            for _ in range(count):
                assert data[arg+4]==2 and u16(data,arg+5)==4
                markers.append(struct.unpack_from('<i',data,arg+7)[0]); arg+=11
            assert arg==end
        cursor=end
    assert cursor==len(data)
    reported=[int(i) for i in row['rpcIds'].split(';') if i]
    assert ids==reported
    return ids,markers

result_text=(root/'result.txt').read_text(encoding='utf-8-sig')
assert result_text.splitlines()[0]=='PASS'
frequency=int(re.search(r'StopwatchFrequency=(\d+)',result_text).group(1))
plan=json.loads((root/'plan-used.json').read_text(encoding='utf-8-sig'))
source_before={r['path']:r['sha256'] for r in rows('source-before.csv')}
source_after={r['path']:r['sha256'] for r in rows('source-after.csv')}
assert source_before and source_before==source_after
config_before=json.loads((root/'configuration-before.json').read_text(encoding='utf-8-sig'))
config_after=json.loads((root/'configuration-after.json').read_text(encoding='utf-8-sig'))
assert config_before==config_after
artifacts=rows('artifact-hashes.csv')
assert [r['sha256'] for r in artifacts if r['kind']=='plan']==[hashlib.sha256((root/'plan-used.json').read_bytes()).hexdigest()]
bytecode={r['path']:r['sha256'] for r in artifacts if r['kind']=='bytecode'}
assert len(bytecode)==4 and len(artifacts)==1+len(plan['cases'])*5
enqueues=rows('enqueue.csv'); packets=rows('packets.csv'); frames=rows('frames.csv')
events=rows('events.csv'); markers=rows('markers.csv'); vm=rows('vm-events.csv'); summaries=rows('summary.csv')
assert [r['case'] for r in summaries]==[r['name'] for r in plan['cases']]
output=dict(status='PASS', plan_sha256=hashlib.sha256((root/'plan-used.json').read_bytes()).hexdigest(),
            product_sources=len(source_before), project_configurations=len(config_before),
            bytecode=bytecode, preflight_marker_zero='Retained as control; excluded from latency and age performance',
            method='Fresh compiled client VM and real VRC GPU callbacks; raw packet/queue/timing independent recount; no real VRChat/CDN/FPS claim',
            host_limit='Packet serialization and file writes are outside VM timings but affect scheduling, render rate and sender deadlines',
            receiver_timestamp_audit='Original float reconstructed from separately retained double latency, constrained to IEEE754float32 and seven-significant-digit CSV rounding; later probe versions print roundtrip floats', cases={})
for config,summary in zip(plan['cases'],summaries):
    name=config['name']; selected=lambda allrows:[r for r in allrows if r['case']==name]
    e,q,fr,ap,ma,vs=map(selected,(enqueues,packets,frames,events,markers,vm))
    accepted=[n(r,'id') for r in e if r['accepted']=='True']
    rejected=[n(r,'id') for r in e if r['accepted']=='False']
    assert [n(r,'id') for r in ap]==accepted
    assert len(accepted)==n(summary,'accepted')==n(summary,'applied')
    assert len(rejected)==n(summary,'rejected') and len(q)==n(summary,'published')
    copies=Counter(); first={}; state_sends={}
    timeline=sorted([(f(r,'time'),0,r) for r in e]+[(f(r,'time'),1,r) for r in q])
    queue=[]; repeats={}
    for stamp,kind,row in timeline:
        if kind==0:
            assert len(queue)==n(row,'queueBefore')
            if row['accepted']=='True':
                assert len(queue)<32; queue.append(n(row,'id')); repeats[n(row,'id')]=plan['repeats']
            else: assert len(queue)==32
            assert len(queue)==n(row,'queueAfter')
            continue
        assert len(queue)==n(row,'queueBefore')
        assert (repeats[queue[0]] if queue else 0)==n(row,'headBefore')
        ids,wire_markers=packet(row)
        assert ids==queue[:len(ids)] and len(ids)==len(set(ids))
        for event_id in ids:
            copies[event_id]+=1; repeats[event_id]-=1; first.setdefault(event_id,stamp)
        while queue and repeats[queue[0]]==0: queue.pop(0)
        assert len(queue)==n(row,'queueAfter')
        assert (repeats[queue[0]] if queue else 0)==n(row,'headAfter')
        for marker in wire_markers:
            assert marker==n(row,'marker')
            if marker>0:
                assert marker not in state_sends
                state_sends[marker]=stamp
    assert not queue and set(copies)==set(accepted)
    assert all(copies[i]==plan['repeats'] for i in accepted)
    enqueue_time={n(r,'id'):f(r,'time') for r in e if r['accepted']=='True'}
    for row in ap:
        event_id=n(row,'id'); same(f(row,'enqueue'),enqueue_time[event_id]); same(f(row,'firstSend'),first[event_id])
        actual_apply=apply_time(row,enqueue_time[event_id])
        assert n(row,'copies')==plan['repeats'] and actual_apply>=first[event_id]-.0001
    state_ids=[]
    for row in ma:
        marker=n(row,'id')
        if marker==0: continue
        assert marker in state_sends
        same(f(row,'send'),state_sends[marker]); apply_time(row,state_sends[marker])
        state_ids.append(marker)
    assert state_ids==sorted(set(state_ids))
    begin,end=f(summary,'begin'),f(summary,'end'); duration=end-begin
    vs=[r for r in vs if begin<=f(r,'time')<end]
    measured_frames={n(r,'frame') for r in fr if begin<=f(r,'time')<end}
    per_frame=defaultdict(float,{i:0. for i in measured_frames})
    for row in vs:
        same(f(row,'vmMs'),(n(row,'endTicks')-n(row,'startTicks'))*1000/frequency)
        per_frame[n(row,'frame')]+=f(row,'vmMs')
    previous=-1
    for row in sorted(vs,key=lambda r:n(r,'startTicks')):
        assert n(row,'startTicks')>=previous; previous=n(row,'endTicks')
    latency=[f(r,'latencyMs') for r in ap]
    first_latency=[(first[i]-enqueue_time[i])*1000 for i in accepted]
    result=dict(accepted=len(accepted), rejected=len(rejected), applied=len(ap), protected_packets=len(q),
                actual_frame_hz=len(measured_frames)/duration, sender_deadline_misses=n(summary,'deadlineMisses'),
                enqueue_to_first_send_ms=pct(first_latency), enqueue_to_apply_ms=pct(latency),
                full_vm_cpu_ms_per_second=sum(f(r,'vmMs') for r in vs)/duration,
                vm_cpu_per_frame_ms=pct(list(per_frame.values())),
                encoder_call_ms=pct([f(r,'vmMs') for r in vs if r['owner']=='Encoder' and r['event']=='EncodeNow']),
                state_apply_latency_ms=pct([f(r,'latencyMs') for r in ma if n(r,'id')>0]),
                max_queue=n(summary,'maxQueue'), blocked_opportunities=n(summary,'blockedOpportunities'),
                first_to_last_event_send_ms=(max(first.values())-min(first.values()))*1000 if first else 0)
    if config.get('blocked'):
        assert result['blocked_opportunities']>0
        assert all(not r['rpcIds'] for r in q if n(r,'budget')==29)
        assert min(first.values())>=begin+1.2
    output['cases'][name]=result
output['counts']=dict(enqueues=len(enqueues), packets=len(packets), frames=len(frames), applications=len(events), markers=len(markers), vm_events=len(vm))
(root/'analysis.json').write_text(json.dumps(output,indent=2)+'\n',encoding='utf-8')
print(json.dumps(output,indent=2))
