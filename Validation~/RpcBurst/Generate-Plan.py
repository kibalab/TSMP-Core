"""Freeze RPC burst workloads before any measurement output."""
import argparse
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
if a.output.exists():
    raise SystemExit('Use a fresh plan path')
cases = [
    dict(name='burst1', burst=1, seconds=2),
    dict(name='burst8', burst=8, seconds=3),
    dict(name='burst32', burst=32, seconds=6),
    dict(name='queue-full33', burst=33, seconds=6),
    dict(name='sustained2', inputHz=2, inputSeconds=3, seconds=4),
    dict(name='sustained10', inputHz=10, inputSeconds=4, seconds=8),
    dict(name='burst32-mixed', burst=32, seconds=6, mixed=True),
    dict(name='burst8-budget-recovery', burst=8, seconds=5, mixed=True,
         blocked=True),
]
plan = dict(schema=1, sourceHz=30, renderHz=90, repeats=4, cases=cases,
            method='Actual post-Play client VM Encoder/Luma4/Decoder/receiver and VRC GPU callbacks',
            boundaries='One-second pre-observation warmup; cases begin after successful state GPU preflight; actual clocks; all emitted packet/header bytes retained',
            correctness='Every accepted event exactly once in FIFO ID order; four protected on-wire copies per accepted event; rejected IDs never emitted; every applied marker corresponds to a sent value; source deadlines and queue-full outcomes retained',
            budget='Blocked case enqueues at budget256, then budget29 (state fits, RPC43 does not) from measured start until +1.2s; restore256; no queue/repeat consumption while blocked')
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(plan, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(cases=len(cases), seconds=sum(c['seconds'] for c in cases),
                      sha256=hashlib.sha256(a.output.read_bytes()).hexdigest())))
