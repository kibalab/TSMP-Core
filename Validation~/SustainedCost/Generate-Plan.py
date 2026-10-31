"""Freeze the entire sustained matrix before any output; no fixture selection by timing."""
import argparse
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--refine-plan', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
if a.output.exists():
    raise SystemExit('Use a new plan path')
references = json.loads(a.refine_plan.read_text(encoding='utf-8-sig'))['cases']
cases = []
for codec in ('Luma4', 'Color256'):
    for size in (1024, 5120):
        for render in (60, 90):
            cases.append(dict(name=f'{codec}-{size}-30-at-{render}', codec=codec,
                              bytes=size, renderHz=render, sourceHz=30, seconds=12,
                              budget=2, stress=False, schedule='every-render'))
for codec in ('Color256', 'RGB16'):
    ref = next(c for c in references if c['name'] == f'{codec}-second-mode-5375')
    for kind in ('input', 'header', 'payload'):
        assert hashlib.sha256(Path(ref[kind]).read_bytes()).hexdigest() == ref[kind+'Sha256']
    for budget in range(3):
        cases.append(dict(name=f'{codec}-fixed-damage-b{budget}', codec=codec,
                          bytes=5350, renderHz=90, sourceHz=30, seconds=12,
                          budget=budget, stress=True, schedule='every-render',
                          **{k: ref[k] for k in ('input', 'header', 'payload')}))
cases.append(dict(cases[7], name='Color256-5120-90-confirm60', seconds=60))
cases.append(dict(cases[10], name='Color256-fixed-damage-b2-confirm60', seconds=60))
# Preselect three paired A/B repetitions, alternating order, plus60s polling confirmation.
for repeat in range(3):
    for schedule in (('every-render', 'poll30') if repeat % 2 == 0 else ('poll30', 'every-render')):
        cases.append(dict(cases[7], name=f'Color256-5120-90-{schedule}-pair{repeat}', schedule=schedule))
cases.append(dict(cases[7], name='Color256-5120-90-poll30-confirm60', schedule='poll30', seconds=60))
for condition, extra in (('phase-jitter', dict(decodePhaseSeconds=0.01, sourceJitterSeconds=0.006)), ('phase-held', dict(decodePhaseSeconds=0.01, holdStartSeconds=4, holdSeconds=1.5))):
    for schedule in ('every-render', 'poll30'):
        cases.append(dict(cases[7], name=f'Color256-5120-90-{condition}-{schedule}', schedule=schedule, **extra))
plan = dict(schema=2, cases=cases,
            method='CompiledUdon SDK Editor; real GPU callbacks; clean unique IDs versus separately labeled fixed damaged repeats',
            correctness='PASS requires ordered unique clean IDs with everyapplication fullpayload xorshift checks from received ID in receiver host observations; nested host time separately recorded/subtracted, and exact accepted stress bytes; missing published IDs remain delivery observations',
            polling='Validation-only experimental30Hz polling, using existing applyEveryFrame and _update; retain default unless CPU benefit repeats and delivery/age guards pass',
            payload_pattern='IDseeded xorshift32; seed=(ID*2654435761+2246822519)mod2^32; byte4onward usesxorshift13/17/5 low8bits; hostgenerationoutsideEncoderVM',
            frozen_refine_plan=str(a.refine_plan.resolve()),
            frozen_refine_plan_sha256=hashlib.sha256(a.refine_plan.read_bytes()).hexdigest())
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(plan, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'cases': len(cases), 'measured_seconds': sum(c['seconds'] for c in cases),
                  'sha256': hashlib.sha256(a.output.read_bytes()).hexdigest()}))
