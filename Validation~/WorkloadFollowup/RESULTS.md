# Workload follow-up, 2026-11-01

## Maximum-latency implementation and guarded decision

This round retains a lower-interference validation harness, calibrated opt-in
GC diagnostics and variable-length/ownership regressions. It ships **no new
Runtime optimization**: a small private snapshot-capacity prototype passes
correctness but fails the frozen mean/upper-tail guards and is restored byte
for byte in all four source locations. No retry selects away unfavorable data.
CRC, recovery, two slots, scheduling defaults and the Float32/Timeline fixes
remain intact. All work stays on codex/tsmp-optimization, without main merge,
push, release or actual VRChat validation.

The measurement-method comparison runs six fresh processes on identical
Runtime. Four focused cases now have 3,072 measured calls per harness. Removing
quadratic packet identity verification and temporary injection/file work reduces
the 128-field/one-target median post-Encode verification gap from 2.8427 to
0.0470 ms. Encode maxima fall 17.2543 -> 10.7576 ms for one target and
23.0927 -> 15.6050 ms for 128 changing targets. These are observer effects,
**not shipped Runtime speedups**. The one-target Decode maximum instead rises
40.0211 -> 45.7173 ms; only four of 3,072 calls exceed 33.33 ms, but all
3,072 full 128-field applications exceed 16.67 ms. Repeated batch work and
occasional spikes need different treatment.

Calibrated off/on/on/off telemetry cannot attribute all spikes to GC. Scoped
Mono allocation remains unavailable. The initial stopped-recorder experiment
is preserved; the separate corrected version restarts and positively verifies
live counters, with all 16,656 observations passing validity/running checks.
Profiler samples lack exact VM-call/frame attribution, and instrumentation has
measurable overhead. No zero-allocation or real-world incidence claim follows.

Eight separately frozen product processes compare four synthetic variable
conditions and twelve scalar controls. Alternating 1,024/1,041-byte data shows
2,048 -> 0 measured sender snapshot replacements per case, while exact-length
receivers and producer byte-array creation remain unchanged. However, pooled-producer
Encode maximum rises 1.4947 -> 1.8614 ms and combined maximum 2.4958 ->
3.1925 ms; fixed-changing Encode mean rises 5.69%, and scalar controls also
fail upper-tail guards. Fifteen metric guards fail overall. This does not prove
the candidate caused every spike, but allocation removal alone does not justify
its retained peak capacity or adoption for maximum-latency improvement.

The final restored product passes fresh Native/current/minimum-SDK scheduling,
Native GPU loopback (11 frames), 16 current-SDK and 13 minimum-SDK client program
compilations, with automatic bindings. Prior candidate contracts also pass real
capacity-tail checks; those are distinguished from final exact-length results.

Next priority is measuring an actual representative packed-pose/field mix and
binding changes with this harness, then profiling repeated Decoder batch work
and real per-frame completion counts. Existing MinSendInterval and packed
fields can reduce due work; OnChange still serializes due fields, payload bytes
are not a CPU budget, and lowering call rate does not bound one call. Do not
add an automatic CPU budget, new queue/mode or recovery compromise without
evidence that its tradeoff improves the relevant workload.

See [complete experiments, limitations and decisions](../BindingCost/RESULTS.md).
Raw evidence and independent audits:
`F:/Unity/TSMP/Validation-Results/tail-latency-implementation-20261101`.

## Current maximum-latency review

This historical read-only Float32 review does not show every maximum increasing. Across its
equally sampled 12 conditions, measured Encode maximum is 17.5793 ms original
versus 15.5909 ms current; including warm calls it is 51.8879/53.3732 ms. These
are different scopes and neither establishes a real VRChat maximum or frequency.
Earlier rejected duplicate-check/Color256 row-copy trials remain withdrawn.

The 6.4337/15.5909 ms spikes each occur once in 96 measured opportunities above
1.5x the original same-case median. However, the 128-target unchanged condition
has 19/32 calls above the old maximum in one process; that slower period must
not be dismissed. Applying 128 independent fields costs current SDK Decoder
medians 24.881/27.275 ms for one/many targets, above 16.67 ms in all 96/96 samples
of each. Shipped poses already pack data; 128 bones are not 128 scalar bindings.

The harness performs an O(N squared) identity search, temporary allocations and
file writes between timed calls. At 128 fields/one target, a complete packet
causes 16,384 search predicates. No per-sample GC/phase trace proves the spike
cause. Next priority is lower-overhead measurement with valid counters, then
realistic length-changing pose payloads and per-frame completion counts. Private
buffer reuse and fewer due field checks are conditional candidates; smaller
payload/rate settings alone do not bound one call. Keep CRC, recovery and slots.

Reports only this round: no product/fixture edits, new timing runs, merge or push.
See [frequency, limits and ranked proposals](../BindingCost/RESULTS.md#current-tail-review-frequency-and-measurement-limits).

## Current Float32 correctness and minimum-SDK follow-up

Udon Float32 writing now uses the input IEEE bits, replacing arithmetic
reconstruction with 7 added/46 removed Runtime lines. In 3,584 deliberately
selected patterns, the original writer has 450 VM failures (436 finite) and
482 successful finite writes with wrong bits. Corrected Native/current SDK/
minimum SDK 3.9.0 runs each have zero writer errors, wire-bit differences or
reader differences. Native writing, the reader, zero policies and packet format
stay unchanged; NaN host quieting is recorded rather than hidden.

Actual minimum-SDK testing also uncovers an existing Timeline API incompatibility.
A separate one-condition correction uses enabled and activeInHierarchy. The
initial compile failure remains distinct from the successful follow-up. Native
20-case Timeline regression and both SDK Timeline VM runs pass, including
disabled/inactive-parent capture/receive rejection and reactivation.

Six fresh-process AB/BA/AB binding runs pass 2,880 Encode calls, 1,944 protected
packet/header pairs, 99,360 floats and 972 byte-identical corresponding payload
pairs. Encoder bytecode shrinks by 792 B, but **stable whole-Encoder speed
improvement is not established**: two changing 128-field means improve about
0.69%/4.21%, one unchanged control slows 1.87%, and four upper-tail reversals
remain. For example, 128 fields/one target/changing has p99=max (N=96)
10.5286 to 15.5909 ms. The writer's causation is not established; this is a
correctness fix, not a performance claim. No extra timing attempt selects away
unfavorable observations.

The combined final product passes Native GPU loopback (11 frames), current
SDK client compilation (16 programs), minimum Core/Luma4 compilation
(13 programs), generated bindings and Timeline VM checks on both SDKs.
See [complete evidence and reproduction](../OptimizationFeasibility/README.md#float32-writer-correction-and-minimum-sdk-2026-11-01).
Raw results: `F:/Unity/TSMP/Validation-Results/float32-correctness-20261101`.
All work stays on codex/tsmp-optimization; no main merge, push, release or live
VRChat/FPS claim. The prior optimization-trial decisions below remain historical.

## Earlier guarded optimization implementation round

The follow-up on codex/tsmp-optimization measures the proposed changes but
ships **no new sender/receiver Runtime optimization**. Core Encoder/Decoder
helpers are restored byte for byte after failed adoption guards. The recent
Color256 calibration-row change is reverted by `2fe5026`; the older palette,
block-index, CRC, snapshot, callback-group and diagnostic fixes remain.

- Color256's repeated 5KB tail measurements improve, but its 60-second case
  applies only 1,798/1,800 measured updates. This does not prove the row copy
  caused the losses. The complete-delivery gate fails and the small change is
  conservatively withdrawn. All accepted bytes remain correct.
- Encoder target-validation reuse passes correctness but primary mean gains
  are only 1.154% / 0.892%, below the frozen 2% threshold, with a control-tail
  failure. The original source is retained.
- Decoder eligibility reuse improves primary means about 19–21%, but the
  first experiment and one separately frozen larger-control confirmation each
  fail a different upper-tail guard. Both complete datasets remain. The final
  observed p99 reversal is 26.0607 to 29.1918 ms (96 samples, hence its maximum).
  Its product causation is not established; no third timing attempt is made.
- Float32 BitConverter APIs compile and execute in the current client VM.
  Prototype helper savings per 128 ordinary values are 0.140905 ms write and
  0.076444 ms read, not whole-Encoder reductions. The selected 114-edge corpus
  reveals ten existing writer VM errors and 23 finite wire-bit differences.
  Product Binary.cs remains unchanged; minimum-SDK compatibility and explicit
  special-value behavior need separate work before a product change.

Retained changes are validation improvements: protected Decode timing in the
existing matrix, three receive-policy callback/cache controls, and a Float32
API/edge/helper probe. The restored product passes 50 Native / 51 VM receive
policy cases and fresh 16-program client compilation with Bindings=True.
Candidate array, structural preflight and Native GPU loopback regressions also
pass; correctness PASS is kept distinct from performance/adoption FAIL.

Evidence: `F:/Unity/TSMP/Validation-Results/optimization-implementation-20261101`.
See [Color256 decisions](../Color256Performance/RESULTS.md),
[sender/receiver experiments](../BindingCost/RESULTS.md),
[receive controls](../ReceivePolicies/README.md) and
[Float32 feasibility](../OptimizationFeasibility/README.md).
All work remains on the optimization branches; main refs, remote refs, package
versions, assets and GUIDs are preserved. No push, release or live-client claim.

## Product finishing follow-up

After the architecture/overdevelopment review, the next work fixes installation
and observability contracts and joins existing validation entry points. It adds
no transport/recovery feature or new validation framework. The measurements
below remain historical evidence for their recorded source revisions.

- Udon automatic clearing now resets only the private frame builder after
  successful output. Public payload/message counters remain available to the
  Inspector/debug canvas; BeginFrame, explicit ClearFrame, empty attempts and
  failures retain their reset behavior. The Runtime change is one file,
  **9 added/4 removed lines**. CRC, packet format, sending defaults and finite
  RPC repeats are unchanged. Native encoding is unchanged.
- Color256, RGB16 and RGB20 now declare the required Luma4 header codec in UPM
  and VPM. Their installation guides explain the complete package set and
  explicit project sources needed for local/Git UPM installs. Metadata checks
  enforce the required dependency rather than accepting its omission.
- English/Korean/Japanese codec selection distinguishes the Luma4 starting
  point, conditional Color256/RGB16 paths and the RGB20 H.264/NV12 limitation.
  Current development-branch recovery results are distinguished from published
  packages. The Encoder API now documents integer stages and the actual void
  Udon BeginFrame signature, alongside counter lifetimes.
- The existing suite exposes opt-in FrameStructure/PayloadBudget steps by
  delegating to their existing runners. Its default workload is unchanged.
  The README indexes current evidence and preserves the historical reports.

Automatic verification uses Unity 2022.3.22f1 / Windows / D3D11:

| Check | Result |
| --- | --- |
| Original Encoder with isolated diagnostic fixture | Expected FAIL: successful output loses its protected payload length |
| Modified Encoder/client VM and compiled debug canvas | **240 assertions PASS**; three protected outputs, exactly two finite RPC copies, CRC/content checks, byte accounting, reset/empty/failure/opt-out controls |
| Native payload budget / prior output parity | **336 assertions PASS**, including 64 byte-identical frozen-builder inputs |
| Native / compiled Udon structural preflight | **1,256 / 3,857 assertions PASS** |
| Native real GPU texture loopback | PASS: Transform, humanoid pose, Timeline, int/Unicode state and finite RPC dedup/recovery |
| Fresh product client compilation / automatic bindings | **16 programs, Bindings=True PASS** |
| Package contract fixtures | PASS: one valid fixture plus **15 intentionally rejected cases**, actual installed VPM SemanticVersioning assembly; no fresh installation |
| Entry-point parsing/routing/default preservation | **20 stub checks PASS**, plus actual Native suite and SDK targeted runs |
| Website | English, Korean and Japanese production builds PASS |
| Ownership/configuration audit | **600** frozen files, **46** configurations preserved, **708** Runtime/Editor source comparisons, **320** existing GUIDs preserved |

Encoder client bytecode changes from 138,324 to **138,368 B** (+44 B). This is a
diagnostic correction, with no new performance improvement claimed. Canvas
accounting is checked once per output; its existing estimate when it misses
multiple outputs is not promoted to exact byte measurement. Native/SDK source
copies match Runtime/Editor code; optional package metadata in those existing
projects intentionally remains the prior installation, so these checks do not
claim a fresh VCC/VPM download or resolver installation.

The initial new fixture reused GetRealProgram's shared heap and failed its
default assertion after an earlier test changed that heap. That harness failure
is retained. Retrieving an independent serialized program per fixture fixes the
isolation, then the unchanged original Encoder reproduces the actual defect.
The initial metadata controller's encoding error is also retained; explicitly
using UTF-8 fixes that controller without changing the package manifests.

Evidence: `F:/Unity/TSMP/Validation-Results/product-finish-20261101`, including
`root-final-audit.json`, before/after logs, metadata fixtures and routing checks.
Implementation commits: Core `6dc3750`, `4118998`, `628f584`; Color256 `3eaa706`,
RGB16 `6ad1c58`, RGB20 `2794219`. All remain on codex/tsmp-reliability without
main merge, push or release. The separate old local Core main history remains
untouched. Actual VRChat, Quest, populated worlds and prolonged OBS/VRCDN
validation remain outside these automated checks.

## Earlier measured workload improvement

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
