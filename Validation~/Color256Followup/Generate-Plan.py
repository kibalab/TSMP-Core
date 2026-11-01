"""Freeze current-code polling pairs and delivery guards before measurement."""
import argparse
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
if a.output.exists():
    raise SystemExit('Use a fresh plan path')
cases = []
def case(name, schedule='every-render', poll=0, source=30, seconds=12, render=90, **extra):
    return dict(name=name, codec='Color256', bytes=5120, renderHz=render,
                sourceHz=source, seconds=seconds, budget=2, stress=False,
                schedule=schedule, pollHz=poll, measuredStartId=1024,
                rpcEverySeconds=1.3, **extra)
for pair in range(3):
    for poll in ((0, 60) if pair % 2 == 0 else (60, 0)):
        schedule = 'every-render' if poll == 0 else f'poll{poll}'
        cases.append(case(f'pair{pair}-{schedule}', schedule, poll))
for poll in (0, 30):
    schedule = 'every-render' if poll == 0 else f'poll{poll}'
    cases.append(case(f'jitter-{schedule}', schedule, poll, seconds=8,
                      decodePhaseSeconds=.01, sourceJitterSeconds=.006))
for poll in (0, 30, 60):
    schedule = 'every-render' if poll == 0 else f'poll{poll}'
    cases.append(case(f'source60-{schedule}', schedule, poll, source=60, seconds=8))
for poll in (0, 30):
    schedule = 'every-render' if poll == 0 else f'poll{poll}'
    cases.append(case(f'render60-{schedule}', schedule, poll, render=60, seconds=8))
for poll in (0, 60):
    schedule = 'every-render' if poll == 0 else f'poll{poll}'
    cases.append(case(f'hold-restart-{schedule}', schedule, poll, seconds=8,
                      decodePhaseSeconds=.01, holdStartSeconds=2,
                      holdSeconds=.5, restartSeconds=4))
plan = dict(schema=1, cases=cases,
    purpose='Current optimized Color256 actual client VM, real GPU, explicit polling independent of source rate; finite four-copy RPCs and stream restart guards.',
    stage_timing='Nested codec custom-event VM intervals, no product source overlay. Outer Encoder includes codec work once. Measured nested host observer overhead is subtracted. Remaining time does not separately identify CRC, serialization or upload.',
    adoption='Performance win requires repeatable total CPU reduction with same actually published field IDs received, no new sender misses, no RPC missing/duplicate executions, and no material source-age/p99 worsening. Source60/poll30 is an intentional under-polling control. No real video arrival API or default change follows automatically.',
    timing='Two seconds warmup, measured cases, one-second ongoing publication observation tail; all failures retained. One fresh process with alternating paired case order; no claim of independent-process repetitions.')
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(plan, indent=2)+'\n', encoding='utf-8')
print(json.dumps(dict(cases=len(cases), measuredSeconds=sum(c['seconds'] for c in cases),
                      sha256=hashlib.sha256(a.output.read_bytes()).hexdigest())))
