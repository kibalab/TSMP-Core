"""Freeze seven fresh-process runs before any Color256 A/B timing output."""
import argparse
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--baseline-source', type=Path, required=True)
p.add_argument('--candidate-source', type=Path, required=True)
p.add_argument('--output-root', type=Path, required=True)
a = p.parse_args()
if a.output_root.exists() and any(a.output_root.iterdir()):
    raise SystemExit('Use a new empty A/B directory')
source = {}
for variant, path in [('baseline',a.baseline_source),('candidate',a.candidate_source)]:
    source[variant] = dict(path=str(path.resolve()), sha256=hashlib.sha256(path.read_bytes()).hexdigest())
assert source['baseline']['sha256'] != source['candidate']['sha256']
a.output_root.mkdir(parents=True, exist_ok=True)
runs=[]
def add(name,variant,pair,seconds,sizes):
    cases=[]
    for size in sizes:
        cases.append(dict(name=f'Color256-{size}-90-ab',codec='Color256',bytes=size,
                          sourceHz=30,renderHz=90,seconds=seconds,budget=2,
                          stress=False,schedule='every-render',measuredStartId=1024))
    path=a.output_root/(name+'-plan.json')
    path.write_text(json.dumps(dict(schema=3,cases=cases),indent=2)+'\n',encoding='utf-8')
    runs.append(dict(name=name,variant=variant,pair=pair,plan=str(path.resolve()),
                     plan_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                     results=str((a.output_root/name).resolve())))
for pair in range(3):
    order=('baseline','candidate') if pair%2==0 else ('candidate','baseline')
    for variant in order:
        add(f'pair{pair}-{variant}',variant,pair,12,[1024,5120])
add('candidate-confirm60','candidate',None,60,[5120])
manifest=dict(schema=1,runs=runs,sources=source,
    method='Seven fresh Unity processes, six paired runs each1KB+5KB12s pluscandidate5KB60s. Alternatingvariantorder. Actual compiledUdon VM+realGPU callbacks.',
    inputs='IDseeded xorshift pattern; first measured publication startsID1024. Common measured IDs sharefieldbytes; outerframeIDs/hosttimestamps/warmcountdiffer andarenotbitidentical.',
    primary='EncodeNow CPUperactualsuccessfulcall, fullfieldcorrectness, publishedHz/senderdeadline misses, sourceage, frameCPU andCPU/s. ComparethroughputaswellasCPU becausecandidatecanproduce moreframes.',
    source_flip='Only dedicatedVRC Color256codec.cs betweenprocesses; pinnedsourcehashbeforeeachrun andunchangedwithinrun. NoGPUshader/API/protocol/retrypolicychange.')
(a.output_root/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(runs=len(runs),measured_seconds=204,
                     manifest_sha256=hashlib.sha256((a.output_root/'manifest.json').read_bytes()).hexdigest())))
