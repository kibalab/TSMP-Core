"""Preselect an ON/OFF/OFF/ON codec VM observer control, no product overlay."""
import argparse
import hashlib
import json
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
if a.output.exists(): raise SystemExit('Use a fresh plan path')
cases=[]
for i,disabled in enumerate((False,True,True,False)):
    cases.append(dict(name=f'wrapper-control-{i}-'+('off' if disabled else 'on'),
        codec='Color256',bytes=5120,renderHz=90,sourceHz=30,seconds=8,budget=2,
        stress=False,schedule='every-render',pollHz=0,measuredStartId=1024,
        rpcEverySeconds=1.3,disableCodecTiming=disabled))
plan=dict(schema=1,cases=cases,
    purpose='Rule out codec VM observer changing the execution/timing path. Same fresh process, ON/OFF/OFF/ON, new VMs for each case. No source/polling/default change.',
    observer='OFF supplies role:null to the codec Program constructor, completely removing its TimedVm wrapper while outer Encoder/Decoder and receiver byte checks remain identical. ON retains nested stage timing with measured bookkeeping subtracted.',
    limits='This cannot attribute a historical different-run absolute timing change. A large current ON/OFF effect invalidates using observed codec stages as production timing evidence.')
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(json.dumps(plan,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(cases=len(cases),measuredSeconds=32,sha256=hashlib.sha256(a.output.read_bytes()).hexdigest())))
