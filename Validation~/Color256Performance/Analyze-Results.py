"""Reconcile frozen Color256 source pairs after all seven fresh processes exit."""
import argparse
import csv
import hashlib
import json
import math
import re
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--root',type=Path,required=True)
a=p.parse_args()
root=a.root
assert (root/'controller-result.txt').read_text(encoding='utf-8-sig').startswith('PASS7')
manifest=json.loads((root/'manifest.json').read_text(encoding='utf-8-sig'))
assert len(manifest['runs'])==7
receipts=json.loads((root/'source-copy-receipts.json').read_text(encoding='utf-8-sig'))
assert [r['run'] for r in receipts]==[r['name'] for r in manifest['runs']]
installed_sources=None
variants={}
all_metrics={}
bytecodes={}
encoder_samples={}
all_publications=all_applications=warm_missing=tail_missing=0
byte_checks=0
for run,receipt in zip(manifest['runs'],receipts):
    expected=manifest['sources'][run['variant']]['sha256'].lower()
    assert receipt['sha256'].lower()==expected
    output=Path(run['results'])
    assert hashlib.sha256(Path(run['plan']).read_bytes()).hexdigest()==run['plan_sha256']
    assert Path(run['plan']).read_bytes()==(output/'plan-used.json').read_bytes()
    analysis=json.loads((output/'analysis.json').read_text(encoding='utf-8-sig'))
    assert analysis['status']=='PASS'
    before={r['path']:r['sha256'] for r in csv.DictReader((output/'source-before.csv').open(encoding='utf-8-sig'))}
    after={r['path']:r['sha256'] for r in csv.DictReader((output/'source-after.csv').open(encoding='utf-8-sig'))}
    assert before==after
    codec=next(path for path in before if path.endswith('TSMPCodecColor256.cs'))
    assert before[codec].lower()==expected
    if installed_sources is None:
        installed_sources=before
    else:
        assert before.keys()==installed_sources.keys()
        assert all(value==installed_sources[path] for path,value in before.items() if path!=codec)
    configs=[json.loads((output/f'configuration-{phase}.json').read_text(encoding='utf-8-sig')) for phase in ('before','after')]
    assert configs[0]==configs[1] and len(configs[0])>=3
    config_count=len(configs[0])
    metrics=analysis['cases']; all_metrics[run['name']]=metrics
    for name,metric in metrics.items():
        assert metric['missed_published_ids']==0
        assert metric['encoder_event_cpu_ms']['count']==metric['actual_published']
        pubs=[r for r in csv.DictReader((output/'publications.csv').open(encoding='utf-8-sig')) if r['case']==name and r['measured']=='True']
        assert pubs and int(pubs[0]['id'])==1024
        assert [int(r['id']) for r in pubs]==list(range(1024,1024+len(pubs)))
    bytecode={}
    for row in csv.DictReader((output/'artifact-hashes.csv').open(encoding='utf-8-sig')):
        if row['kind']=='bytecode':
            if row['path'] in bytecode: assert bytecode[row['path']]==row['sha256']
            bytecode[row['path']]=row['sha256']
    bytecodes[run['name']]=bytecode
    if run['pair'] is not None:
        bounds={s['case']:(float(s['begin']),float(s['end'])) for s in csv.DictReader((output/'summary.csv').open(encoding='utf-8-sig'))}
        for e in csv.DictReader((output/'vm-events.csv').open(encoding='utf-8-sig')):
            lo,hi=bounds[e['case']]
            if e['owner']=='Encoder' and e['event']=='EncodeNow' and lo<=float(e['time'])<hi:
                encoder_samples.setdefault((run['variant'],e['case']),[]).append(float(e['vmMs']))
    raw_pubs=list(csv.DictReader((output/'publications.csv').open(encoding='utf-8-sig')))
    raw_apps=list(csv.DictReader((output/'applications.csv').open(encoding='utf-8-sig')))
    all_publications+=len(raw_pubs);all_applications+=len(raw_apps)
    summary_rows={s['case']:s for s in csv.DictReader((output/'summary.csv').open(encoding='utf-8-sig'))}
    observed={(s['case'],s['id']) for s in raw_apps}
    for q in raw_pubs:
        if (q['case'],q['id']) not in observed:
            lo,hi=float(summary_rows[q['case']]['begin']),float(summary_rows[q['case']]['end'])
            if float(q['sent'])<lo:warm_missing+=1
            elif float(q['sent'])>=hi:tail_missing+=1
            else:raise AssertionError('Measured publication missing')
    run_checks=sum(int(v) for v in re.findall(r'accepted payload byte checks=(\d+)',(output/'result.txt').read_text(encoding='utf-8-sig')))
    assert run_checks==len(raw_apps);byte_checks+=run_checks
    variants.setdefault(run['variant'],[]).append(run['name'])

comparisons=[]
for pair in range(3):
    baseline=all_metrics[f'pair{pair}-baseline']
    candidate=all_metrics[f'pair{pair}-candidate']
    for name in baseline:
        b,c=baseline[name],candidate[name]
        comparisons.append(dict(pair=pair,case=name,
            baseline_encode_mean_ms=b['encoder_event_cpu_ms']['mean'],candidate_encode_mean_ms=c['encoder_event_cpu_ms']['mean'],
            encode_mean_reduction_percent=100*(1-c['encoder_event_cpu_ms']['mean']/b['encoder_event_cpu_ms']['mean']),
            baseline_encode_p95_ms=b['encoder_event_cpu_ms']['p95'],candidate_encode_p95_ms=c['encoder_event_cpu_ms']['p95'],
            baseline_cpu_frame_p95_ms=b['tsmp_cpu_per_frame_ms']['p95'],candidate_cpu_frame_p95_ms=c['tsmp_cpu_per_frame_ms']['p95'],
            baseline_vm_cpu_ms_per_second=b['vm_cpu_ms_per_second'],candidate_vm_cpu_ms_per_second=c['vm_cpu_ms_per_second'],
            baseline_source_hz=b['actual_published']/12,candidate_source_hz=c['actual_published']/12,
            baseline_render_hz=b['achieved_render_hz'],candidate_render_hz=c['achieved_render_hz'],
            baseline_sender_misses=b['sender_deadline_misses'],candidate_sender_misses=c['sender_deadline_misses'],
            baseline_source_age_p95_ms=b['source_age_ms']['p95'],candidate_source_age_p95_ms=c['source_age_ms']['p95'],
            baseline_measured_received=b['unique_received_after_drain'],candidate_measured_received=c['unique_received_after_drain']))
confirm=all_metrics['candidate-confirm60']['Color256-5120-90-ab']
assert confirm['actual_published']>0 and confirm['missed_published_ids']==0
last=json.loads((root/'final-project-source.json').read_text(encoding='utf-8-sig'))
assert last['sha256'].lower()==manifest['sources']['candidate']['sha256'].lower()
before_bytecodes=bytecodes['pair0-baseline']
changed_bytecode_paths=sorted({path for name,b in bytecodes.items() for path,value in b.items() if path in before_bytecodes and before_bytecodes[path]!=value})
pooled={}
for name in all_metrics['pair0-baseline']:
    samples={variant:encoder_samples[(variant,name)] for variant in ('baseline','candidate')}
    stats={}
    for variant,values in samples.items():
        ordered=sorted(values);stats[variant]=dict(count=len(values),mean_ms=sum(values)/len(values),p95_ms=ordered[math.ceil(len(values)*.95)-1],maximum_ms=max(values))
    pooled[name]=dict(**stats,mean_reduction_percent=100*(1-stats['candidate']['mean_ms']/stats['baseline']['mean_ms']))
summary=dict(status='PASS',manifest_sha256=hashlib.sha256((root/'manifest.json').read_bytes()).hexdigest(),
    fresh_processes=7, measured_seconds=204, paired_comparisons=comparisons,pooled_encode_calls=pooled,candidate_confirm60=confirm,
    product_sources_compared_per_run=len(installed_sources),other_product_source_changes=0,configuration_files_per_run=config_count,
    raw_trace_counts=dict(publications=all_publications,applications=all_applications,applications_with_complete_field_byte_checks=byte_checks,warm_missing=warm_missing,tail_missing=tail_missing),
    bytecode_paths_changed_vs_baseline=changed_bytecode_paths,
    input_note='Common measured field IDs begin1024 with identical deterministic fieldbytes. Outerframeindices/hosttimestamps/warmupcounts differ; fullpackets arenotbitidentical.',
    workload_note='CompareCPUperEncodeNow aswellastotalCPU/s andthroughput: a fastercandidatecanpublish anddecode moreupdates andrender moreframes.',
    counts=dict(measured_published=sum(m['actual_published'] for ms in all_metrics.values() for m in ms.values()),
                measured_received=sum(m['unique_received_after_drain'] for ms in all_metrics.values() for m in ms.values())))
(root/'ab-analysis.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(status=summary['status'],fresh_processes=7,**summary['counts'])))
