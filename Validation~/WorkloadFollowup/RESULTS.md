# Workload follow-up, 2026-11-01

The product change removes repeated Udon BeforeEncode target searches. Stable
bindings cache each target's group during the existing send-state rebuild, then
use indexed visited flags per attempt. The Runtime change is **one file,
29 added/9 removed lines**. Callback semantics, serialization, CRC, packet layout,
sending/recovery defaults and Native encoding remain unchanged.

## Measured product improvement

Three fresh-process source pairs run in AB/BA/AB order, with twelve complete
workloads per process. Each table row pools **96 actual measured EncodeNow calls
per version**. These are per-call client VM elapsed times in an SDK Editor,
including nested product work once; they are not whole-frame costs or FPS.

| Float fields / distinct targets / changes | Original mean | Modified mean | Reduction | Original / modified p95 |
| --- | ---: | ---: | ---: | ---: |
| 32 / 32 / all | 3.399 ms | 2.468 ms | **27.39%** | 3.578 / 2.625 ms |
| 128 / 128 / all | 26.461 ms | 12.423 ms | **53.05%** | 27.664 / 13.007 ms |
| 128 / 128 / none | 23.319 ms | 9.445 ms | **59.49%** | 23.864 / 9.714 ms |
| 128 / 128 / quarter | 24.314 ms | 10.602 ms | **56.39%** | 24.674 / 11.145 ms |

The changing 128-target mean reductions are 53.49%, 51.87% and 53.80% in the
three pairs. All many-target cases improve in every pair. Single-target controls
are near unchanged: one 32-field pair becomes 0.017 ms slower and its p95 rises
1.978 to 2.095 ms. That reversal and every outlier remain in the results.

All six processes pass 126,583 assertions each. Independent audits recount
**2,880 calls, 1,944 protected packet/header pairs and 99,360 Float32 values**.
All **972 corresponding payload pairs are byte-identical**. Header timestamps
may differ. Callback order/once-only behavior, priority rotation, intervals,
ReceiveOnly fields, same-length target replacement, and callback-time binding,
direction and sendMode changes pass 114 edge rows plus 54 original controls.
Only Encoder source/bytecode changes; 391 other source receipts, 23 project
configurations and four other loaded programs stay identical across variants.

The client Encoder program shrinks **139,520 to 138,324 B**. Group-index and
visited-array bodies occupy 640 B at 128 bindings, excluding object overhead,
and replace the old target-reference array; the sendMode array already existed.
This does not establish measured allocation or zero allocation.

Group construction remains quadratic when existing send state is rebuilt. The
first 128-target changing call is 25.675–27.141 ms original versus
25.956–26.846 ms modified. No startup improvement is claimed. Frequent binding
changes can keep paying rebuild cost. The matrix uses refresh=0 to isolate
changed/unchanged work, with actual GPU Luma4 output and protected Decoder
completion injection. It does not establish default-refresh pacing, receiver
GPU cost, many concurrent streams, pose/string mixtures or live-client FPS.

Full methods, raw controls, retained preliminary failures and reproduction:
[BindingCost](../BindingCost/RESULTS.md).

## RPC delay confirmed; queue policy retained

The actual client Encoder to GPU Luma4 to VRC GPU Decoder/receiver path passes
11,107 assertions across eight cases. All **159 accepted events execute once in
FIFO order**, with exactly **636 protected copies** and **813 protected packet
pairs** independently verified. The intentional 33rd call into a full queue is
rejected. Thirty-six reduced-budget opportunities preserve pending repeats,
while small state continues; restoring the budget resumes delivery.

A 32-event burst takes **4.132 seconds** from first to last initial send, and the
last event applies about 4.177 seconds after enqueue. Ten events/second for four
seconds produces a maximum queue of eleven, mean application delay 668 ms and
maximum 1.333 seconds. Its finite input stops so the queue can drain; this is not
a sustained-overload guarantee. The mixed burst still delivers 180/180 changing
state markers with application-latency p95 22.730 ms.

This is waiting caused by the existing one-head/four-repeat policy, with low
frame VM cost in these cases. Automatic batching would change RPC/state capacity
allocation and require correct mixed-repeat, late-enqueue and failure commit
accounting. No batching policy or new setting is introduced in this work.
English/Korean/Japanese RPC guides now correctly show the bool return type,
explain queue acceptance versus delivery and explain local side effects before
a failed All enqueue. Persistent values should use periodically refreshed state;
finite event copies can all be lost.

Full results and preserved observer failure: [RpcBurst](../RpcBurst/RESULTS.md).

## Current Color256 polling and remaining cost

Fifteen preselected cases measure 144 seconds on the existing optimized Color256
and original Core. Three alternating 30Hz-source/90Hz-render pairs show **5.73%
to 7.29% lower total TSMP VM time per second** with validation-only 60Hz polling.
All 2,160 paired field publications and 54 paired RPCs arrive; paired source-age
p95 improves in these local schedules. Across all fifteen cases, 108/108 RPCs
execute, while higher-rate and intentional under-poll controls retain missing
field IDs. No universal polling cap or Runtime polling feature is adopted.

Nested codec VM events account for about **60% of current measured encoding
time**, and about 65% in the subsequent observer control. The remainder combines
serialization, comparison, CRC, header/base-image/bridge and upload work; this
does not separately measure those private stages or GPU execution. A future
Color256 GPU writer is a candidate requiring its own pixel and whole-path A/B
evidence, not an implemented or quantified gain.

The new measurements have materially different absolute timings from earlier
results despite identical product sources and bytecode. This is not a new
Color256 improvement. The final Core candidate is separately checked with a
32-second ON/OFF/OFF/ON codec-observer control: **960/960 measured 5KB fields and
24/24 RPCs** arrive with no sender deadline misses. Mean EncodeNow is
6.5633 ms with the observer and 6.5258 ms without it, a 0.575% difference, far
smaller than the historical timing gap. The host/run variation remains
unexplained. These controls are not pooled with the earlier source experiment.

Full methods, all losses, program hashes and measurement limits:
[Color256Followup](../Color256Followup/RESULTS.md).

## Final regression and ownership checks

- Actual freshly compiled client Encoder budget/array/rotation/retained-repeat
  regression: **131 assertions PASS**, using final Encoder bytecode
  `428c70ce2917eed0a77ff7f78e646a0445e2900732b4a6c97e28048168c7d144`.
- Existing product client compilation: **16 programs, Bindings=True PASS**.
- Five repository main/origin-main refs preserved, no added merges, committed
  package sources. Dedicated Native/VRC copies match authoritative sources:
  **708 source comparisons and 320 preserved existing GUIDs PASS**.
- **46** original project configuration files and **600** byte-exact baseline
  archive files verified. Unrelated Unity PID11456 is left running; every owned
  measurement process has ended.

Evidence root:
`F:/Unity/TSMP/Validation-Results/workload-followup-20261101`.
It retains frozen sources/plans/harnesses, raw packets/timings, complete logs,
failed preliminary fixtures, independent audits and final source/configuration
receipts. Recount final ownership checks with:

```powershell
python ./Validation~/WorkloadFollowup/Audit-Final.py --results '<evidence root>'
```

All work stays on codex/tsmp-reliability, committed by work unit with no main
merge, push or release. This is automated local SDK Editor evidence on
i9-13900K/RTX4090/D3D11/Gamma. It does not validate actual VRChat, Quest,
populated worlds or OBS/VRCDN transport. CRC, the two payload recovery attempts,
default unlimited target budget and finite RPC delivery contract are retained.
