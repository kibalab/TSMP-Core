# Binding cost and tail follow-up, 2026-11-01

## Private variable snapshot experiment: not adopted

A three-file prototype lets only the two private complete-entry callers retain
snapshot capacity. The complete seven-byte entry header includes the valid body
length, so full-prefix comparison still detects shortening and same-prefix
length changes. Public generic ShouldSend/Snapshot remain strict and unchanged;
there are no new fields, settings, length metadata or second buffers. Native,
current SDK and minimum SDK 3.9.0 pass the candidate contracts, including real
dirty-capacity-tail checks, failed/deferred-send preservation, retry/reset and
exact independently owned public arrays. The proposed retained-memory cost is
the largest successfully sent entry per binding until reset/rebuild.

Before the variable pilot or formal product timings, freeze ABBA order, both
plans and all adoption guards. Four source-variant runs each use separate fresh
variable and scalar processes, for eight processes total; telemetry is off and
the validated lower-interference harness is identical. Variable cases have
2,048 measured calls per variant; four focused scalar cases have 1,024, and
the other eight have 64. Smaller-case nearest-rank p99 equals the maximum.
No initial failure, sample or outlier is discarded or used for a repeat-to-pass.

Both alternating variable cases eliminate **2,048 -> 0** measured private
snapshot replacements after warmup. Producer byte-array creation and both receiver
replacement counts remain identical across source variants. In the pooled
case, producer byte-array creation is zero, but each exact-length receiver still
replaces its array 2,048 times. Fixed-length snapshots never reallocate in
either variant. Capacity observations top out at 1,048 bytes (seven-byte header
plus 1,041-byte body); this is not a global allocation or GC reduction claim.

| Variable workload | Original/candidate Encode mean ms | p99 ms | Maximum ms |
| --- | ---: | ---: | ---: |
| Alternating, fresh producer | 0.6389 / 0.6327 | 0.8041 / 0.7960 | 1.7634 / 1.6079 |
| Alternating, pooled producer | 0.6358 / 0.6294 | 0.8177 / 0.8043 | **1.4947 / 1.8614** |
| Fixed length, changing | **0.6285 / 0.6643** | 0.7810 / 0.8121 | **1.3922 / 2.3227** |
| Fixed length, unchanged | 0.4234 / 0.4137 | 0.5449 / 0.5233 | 1.3143 / 1.0861 |

The pooled case's combined Encode+Decode maximum rises 2.4958 -> 3.1925 ms
(27.91%); the fixed-changing mean rises 5.69%. Some other results improve:
fresh-producer combined maximum falls 3.2399 -> 2.1819 ms, and fixed-changing
combined maximum falls 8.1184 -> 2.9766 ms. All results count; selecting only
the allocation or favorable combined maximum would misstate the experiment.

The pooled producer's worst Encode and combined values occur on the same one
of 2,048 candidate observations; the other candidate process peaks at 1.4596 ms.
By contrast, fixed-changing mean rises in both matched pairs (about 8.06% and
3.37%), and median rises 0.61245 -> 0.6550 ms. Its observations above the
original p95 rise 102 -> 229 of 2,048. That mean failure cannot be explained
solely by its one 2.3227 ms maximum. These are process-correlated synthetic
observations, not real-world probabilities or proof of product causation.

Scalar controls also fail upper-tail guards: 32 fields/one target/changing
Encode maximum is 2.1266 -> 8.3516 ms, 128 fields/one target 11.5324 ->
13.7603 ms, and 128 distinct changing targets 16.8927 -> 26.7521 ms. The
last case's p99 is almost unchanged at 12.7758/12.7781 ms. These observations
do not establish that the source change caused the spikes. They do establish
that this trial cannot support adopting it to reduce maximum latency.

The frozen limits require every case's Encode mean <=102%, Encode p99/maximum
and combined p99/maximum <=105%, plus correctness, warm allocation and bytecode
guards. **Fifteen metric guards fail**, including duplicate p99/maximum checks
on small cases. Correctness and allocation checks pass, and Encoder bytecode
remains 137,576 bytes. The three Runtime files are restored byte for byte in
the repository, Native project, current SDK project and minimum SDK project.
No additional timing attempt is made. This round retains validation improvements
and regression coverage, with **no new product Runtime optimization**.

Frozen policy is `plans/product-and-telemetry-policy.json` (SHA256
`8210ef983b89f25d19a1ee76e15173cc653ceed4bf998322b74f1ee6fb18be00`).
`product-ab` retains the manifest, all eight raw outputs, independent audits,
complete per-case distributions, adoption calculation and selected-source copy
receipts. These data are separate from the old/new measurement-method and GC
instrumentation comparisons below.

Independent product audits pass all 16,640 variable calls, 12,484 protected
pairs and 12,854,336 field bytes, with 6,242 byte-identical paired payloads.
Scalar audits pass 9,600 calls, 7,056 protected pairs, 619,200 floats and 3,528
paired payloads. Independent exact-rational gate arithmetic agrees with all
fifteen failures. Audit integrity PASS remains separate from adoption FAIL.
The final restored product passes fresh Native/current/minimum-SDK scheduling,
Native GPU loopback (11 frames), current-SDK client compilation (16 programs)
and minimum-SDK compilation (13 programs), both with generated bindings.

The practical next priority is representative packed-pose/field and binding
change measurements, then repeated Decoder batch cost and actual per-frame
completion counts. Existing MinSendInterval skips before serialization; due
OnChange fields still serialize. Packed fields already exist in shipped pose
components. A payload-byte limit is not a CPU budget, and lower call rate does
not bound one call. New queues, CPU budgets or reduced recovery are not justified
by this experiment. Real VRChat, production refresh/rate, receiver GPU waits,
OBS/CDN loss and binding-change frequency remain unmeasured here.

## Lower-interference measurement implementation

The current harness removes repeated identity/name/hash searches, reuses callback
and Decoder injection arrays, and buffers bounded packet/header snapshots and
timing structs until each case ends. All packet/CRC/value/callback checks remain.
An intentional packet corruption fails on the first measured call and preserves
nine partial observations and raw bytes. This improves validation; it is **not a
shipped Runtime optimization or proof that product maximum latency fell**.

Six fresh SDK Editor processes compare old/new harnesses in AB/BA/AB order on
identical product sources, programs and configurations. Four focused conditions
have 1,024 measured calls/process, and eight other cases keep 32. The table pools
3,072 focused calls per harness, uses nearest-rank percentiles and retains every
outlier. These are actual compiled Udon custom-event timings with GPU Luma4
submission; Decode receives injected completed packet bytes and excludes receiver
capture/GPU/transport work.

| Fields / targets / changes | Old/new Encode p99 ms | Old/new Encode maximum ms |
| --- | ---: | ---: |
| 32 / 1 / all | 1.9625 / 1.9038 | 6.2915 / 3.9861 |
| 128 / 1 / all | 10.6724 / 9.7874 | 17.2543 / 10.7576 |
| 128 / 128 / all | 13.6058 / 12.7594 | 23.0927 / 15.6050 |
| 128 / 128 / none | 9.4948 / 9.5198 | 15.3919 / 10.1774 |

The 128-field/one-target median gap between Encode end and Decode start falls
from 2.8427 to 0.0470 ms. Observer work demonstrably mattered. However, that
condition's Decode p99 is 28.1226/25.8435 ms while maximum is 40.0211/**45.7173**
ms, with 3/4 of 3,072 observations above 33.33 ms. Distinct-target Decode has
p99 29.9968/28.2659 ms, maximum 41.9948/38.5757 ms and four exceedances in each
version. Both full 128-field Decode cases exceed 16.67 ms in **3,072/3,072**
observations per harness. This exposes repeated batch work as well as occasional
spikes; it does not establish real VRChat FPS or occurrences per second.

Independent raw audit passes 26,688 Encode calls, 19,800 protected packet/header
pairs, 1,813,536 Float32 values and 9,900 byte-identical paired payloads. Headers
match except timestamps and their dependent header CRC. All 392 product source
receipts, five loaded programs and 23 configurations are identical. Warm/cold
observations and process clusters remain separate; these figures do not improve
production startup, rebuilds or binding-churn behavior.

### Calibrated GC diagnostics

Telemetry is opt-in and observed outside VM timers. The known-allocation control
shows the scoped Mono allocation counter is unusable here; it is recorded as
unavailable (`-1`). Available frame allocation/used-memory/collection-marker
recorders and global collection counts have positive controls. Generation counts
advance together on this Boehm runtime and must not be summed.

The initial c184 telemetry experiment is retained with its limitation: calling
ProfilerRecorder.Reset stops collection. Frame counters were stopped during the
timed workload, so those values are unavailable; direct global collection counts
remain valid. A separate v2 source resets **and restarts** recorders, verifies
7 -> 0 -> 7 running flags with live allocation/forced-GC/frame controls, and
performs a newly frozen off/on/on/off experiment. No v1/v2 samples are pooled.
See [Unity's Reset documentation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.Reset.html).

The v2 independent audit passes 9,600 Encode calls, 7,056 protected pairs,
619,200 floats and 16,656 live recorder observations. All sources, five programs
and configurations remain identical. Four focused conditions have 1,024 measured
calls per mode. Encoder off/on means are 1.7225/1.7177, 9.2509/9.3527,
11.9921/11.8785 and 9.0084/8.9856 ms. The second condition's p99 increases
9.8178 -> 11.0811 ms; diagnostics are not a free or normalized timing baseline.
Snapshot collection itself has median 1.2 microseconds and p99 3.8-3.9
microseconds, excluding broader recorder/host overhead.

All v2 measured on-mode Encode/Decode observation windows have zero direct
collection-count delta, but a latest-available collection marker reports
3.127 ms once. The initial v1 epoch has one 37.3745 ms Decode window with a
collection-count increment, while its 37.5148 ms maximum has none. These are
observations, not proof of cause: zero completed collections does not mean no GC
work, profiler samples lack exact frame attribution, and collection work cannot
explain every tail. Producer, host reflection, GPU submission and receiver work
still exist. No zero-allocation claim is made.

### Variable-length ownership and allocation fixture

A separate fixture sends synthetic RawBytes of 1,024 bytes or alternating
1,024/1,041 bytes through the actual compiled Encoder, GPU publication, protected
packet and injected completed Decoder. The 17-byte delta matches a root pose
record width, but this is not an actual avatar/API/OBS/CDN trace. Two receive
bindings share a field identity and must own independent exact-length arrays;
mutating one cannot modify the other or the sender snapshot.

The corrected four-case pilot passes 832,524 assertions over 320 calls and 241
protected pairs (248,144 independently checked field bytes). Alternating lengths
replace the private sender snapshot on all 80 calls, even with a pooled producer
that allocates no new source array. Both receivers also replace their exact-size
arrays. Fixed-length and unchanged controls distinguish identity changes from
content changes. Source generation is timed separately. The initial jagged-heap
cast failure and partial bytes remain; the corrected fixture uses the actual
Udon object-array representation rather than relabeling the failed run.

Evidence root:
`F:/Unity/TSMP/Validation-Results/tail-latency-implementation-20261101`.
Formal harness evidence is under `harness-paired`, calibrated telemetry v2 under
`telemetry-restart-followup`, and the corrected variable pilot under
`pilot-variable-fixed`. Independent audit scripts/results and pinned executed
sources are retained. The earlier missing-helper import failure, preliminary
legacy run and c184 stopped-recorder epoch are preserved and excluded from
unrelated comparisons. The original VM snapshot preflight also preserves an
obsolete-private-cache assertion failure; its separately labeled follow-up
checks observable once-per-source callback behavior and passes.

## Current tail review: frequency and measurement limits

This historical read-only review uses the retained Float32 experiment at Core d88a3c8.
No product/fixture changes or new Unity timing runs are made. Earlier rejected
Encoder/Decoder candidates and Color256 calibration copying remain withdrawn;
their historical maxima are not current-product measurements.

Each of six frozen AB/BA/AB processes retains 480 Encode calls. After warmup,
each variant has 1,152 Encode and 768 nonzero Decode observations. Each condition
has 96 measured Encode calls/variant: nearest-rank p99 is its maximum, not a
stable estimate of population p99. All original samples remain included.

| Scope | Original | Retained Float32 writer |
| --- | ---: | ---: |
| All 12 measured Encode conditions, p99 | 15.4009 ms | 12.6028 ms |
| All 12 measured Encode conditions, maximum | 17.5793 ms | 15.5909 ms |
| Measured Decode, maximum | 36.0833 ms | 29.8741 ms |
| Encode maximum including warm calls | 51.8879 ms | 53.3732 ms |

Whole-matrix figures describe an equally sampled synthetic mixture, not real
workload prevalence or a universal speedup. A per-condition maximum increase
does not mean all maxima worsen. Decoder bytecode is identical across variants;
its timing variation also prevents assigning product causation from maxima alone.

| Condition | Original/current p95 | Original/current maximum | Observed frequency/context |
| --- | ---: | ---: | --- |
| 32 fields, one target, changing | 1.9730 / 1.9823 ms | 1.9943 / 6.4337 ms | One of 96 current calls exceeds 1.5x original median; one of three current processes |
| 128 fields, one target, changing | 10.0108 / 10.9111 ms | 10.5286 / 15.5909 ms | One large spike above 1.5x original median, but nine of 96 exceed the old maximum, including a separate milder slowdown |
| 128 fields, 128 targets, unchanged | 10.2345 / 11.0063 ms | 10.6092 / 12.0459 ms | Third current process has 19/32 calls above original pooled maximum; not an isolated spike |
| 32 fields, one target, quarter changing | 1.8748 / 1.7460 ms | 1.9319 / 2.1949 ms | Maximum once at first measured sample 8; p95 improves |

The 6.4337 ms call is surrounded by 1.9768/1.9700 ms calls; the 15.5909 ms call
by 9.1216/9.1982 ms. Identical input/sample positions in the other five processes
are ordinary. Inputs are nonzero finite integers, not rare NaNs/boundaries. The
corrected writer uses one bitcast and four masked writes without an additional
input-dependent retry/search/allocation. Current 128-target unchanged process
medians are 8.8917, 8.9997 and 10.7655 ms: its slower period must not be dismissed.
These fractions cannot establish occurrences per real-world second.

Repeated batch cost also matters: applying all 128 independent fields has
current Decode medians 24.881 ms (one target) and 27.275 ms (128 targets), with
96/96 calls above 16.67 ms in each. These SDK Editor measurements are not actual
VRChat FPS, but expose a substantial batch cost. The shipped Transform, Timeline,
Humanoid and VRChat avatar components already pack data into byte arrays;
128 independent TransSync fields do not mean 128 bones/components.

Cold paths are separate. Every fresh process has its first Encode near 50-53 ms
in both variants; new 128-target instances/bindings have first calls around
25-30 ms. The fixture marks Start already done and invokes OnEnable manually;
production Start already calls EnsureResources. Neither the startup sequence
nor these values establish actual client startup cost. Rebind/resize/reenable
need separate tests; a warm filter cannot establish a hard maximum.

### Measurement limits

Encode timing surrounds the real custom event, including callbacks and CPU
graphics submission; it does not wait for GPU completion/video delivery. Decode
uses injected completed bytes, excluding receiver capture/GPU waiting. Outside
the timers, but in the same Unity frame:

- VerifyPacket searches all N names/hashes per single-target field via
  Enumerable.Range(...).Single(...). A full 128-field output evaluates 16,384
  predicates, repeatedly formatting strings and hashing names.
- Header/payload clones, two file writes, CSV strings and callback arrays are
  created. Deliver creates 14 new injection/slot arrays per emitted packet.
- This can affect later calls despite direct execution being outside timing.
  Per-sample GC activity/allocation, phase times, CPU-vs-wall time and GPU waits
  are not recorded. Neither GC nor scheduling is an established spike cause.

Unity describes temporary allocations and repeated strings as GC pressure;
this supports a plausible confound, not attribution of these samples.
See [Unity 2022.3 GC guidance](https://docs.unity3d.com/2022.3/Documentation/Manual/performance-garbage-collection-best-practices.html).
Observed 128-field Encode intervals are about 37.85/41.79 ms for one/many targets
because Decode/host work shares the frame. A 90 Hz target is not 90 Hz incidence.
The unchanged case emits no fields and returns before CRC/upload/Blit, so its
slowdown cannot be blamed solely on those stages. Static warm scalar cases do
not demonstrate a cache-rebuild or variable-length-buffer-growth cause either.

### Proposed maximum-latency work

1. Lower measurement overhead first, keeping all checks and old evidence.
   Precompute independent identity indexes, reuse injection/scratch arrays and
   buffer bounded snapshots/timing structs. Write files/CSV after a measured
   case. Record narrow phases, per-frame completion counts and available GC/
   allocation metrics. Verify counters with positive controls: the earlier
   scoped counter failed to detect a known 8,192-byte allocation. Use supported
   [ProfilerRecorder metrics](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html)
   only after availability/validity checks, with instrumentation overhead controls.
2. Repeat problematic changing/unchanged controls over thousands of calls and
   several fresh processes. Report p95/p99, explicit budget-exceedance counts,
   worst values and clusters per process. Keep cold/rebind/resize separate.
   Do not remove spikes, relax old frozen gates or retry until a favored result.
3. Test realistic variable-length pose payloads. Root delta/keepalive/player
   inclusion changes avatarPoseBytes length; pose output, private sender snapshot
   and receiver RawBytes storage allocate on length changes. This can repeat in
   normal use. If correlated with tails, private sender capacity plus valid-length
   reuse is a candidate, with retained-memory/state costs and exact change
   detection. Public RawBytes must stay exact-length and independently owned.
4. Reduce demonstrated batch work with existing controls: MinSendInterval on
   lower-priority fields skips before serialization; coherent custom data can
   use existing arrays/packed values. Shipped poses already do so. OnChange still
   reads/serializes due fields. targetPayloadBytes is checked afterward and is
   not a CPU budget; lower frameRate reduces calls/second, not the worst call.
5. After relevant profiling, consider same-size private array reuse during real
   send-state rebuilds, or removing repeated per-element checks after a complete
   fixed-stride array range check. These are bounded candidates, not proven
   spike remedies. New automatic CPU budgets, queues and modes are lower priority.

Decoder can drain two ready captures in one frame; measure completion counts
before capping them. A cap can increase source age/full-slot capture skips.
Keep the two slots, CRC and recovery guarantees. Refine wall p95 near 43 ms spans
multiple GPU waits/callbacks (maximum measured single callback 5.154 ms), unlike
one EncodeNow call. Clean input does not retry; lower retry limits sacrifice
recorded recoveries. No Float32 correctness rollback or reinstatement of rejected
duplicate-check candidates is justified from this review alone.

Derived evidence and read-only scripts:
`F:/Unity/TSMP/Validation-Results/tail-latency-review-20261101/{frequency,harness}`.

## Receive-target experiment: not adopted

A separate two-helper candidate retained the eligibility check before decoding
each value, then used an internal apply path without repeating that same check.
The public Apply guards stayed intact. It introduced no buffers or state and
reduced Decoder client bytecode **138,380 to 138,260 B** (-120 B). Native 50 / VM
51 policy cases, including three new public-guard/callback/cache regressions,
passed, as did array ownership, structural preflight, Native GPU loopback and
16 product client programs with automatic bindings.

Both complete performance experiments are retained. The first six-process
AB/BA/AB matrix measured 128-field mean reductions of **21.465%** for one target
and **20.552%** for distinct targets. However, a 32-field single-target control's
pooled p99 rose **3.2350 to 4.2440 ms**, failing the frozen 105% limit. With 96
measured calls/variant, that nearest-rank p99 is its observed maximum. The
initial experiment is not reclassified as an adoption PASS.

One separately frozen confirmation increased only that control to 512 measured
calls/process, retaining the other eleven cases and all six AB/BA/AB processes.
The enlarged control pools **1,536 calls/variant** and improves p99
**3.2361 to 2.7477 ms**. But a different 128-field single-target condition,
still pooling 96 calls, now fails its p99 gate:

| Confirmation workload | Original mean | Candidate mean | Reduction | Original / candidate p99 |
| --- | ---: | ---: | ---: | ---: |
| 32 fields / one target | 2.953939 ms | 2.523082 ms | 14.586% | 3.2361 / 2.7477 ms |
| 128 fields / one target | 25.018689 ms | 19.883050 ms | 20.527% | **26.0607 / 29.1918 ms** |
| 128 fields / distinct targets | 27.266496 ms | 22.131336 ms | 18.833% | 28.8048 / 26.6048 ms |

The second p99 reversal exceeds 105%. Its cause is not established as a product
regression, but the confirmation's predeclared decision requires restoring the
original helpers and no third timing attempt. **The candidate is not adopted.**
No timing sample is removed; the positive means and failed upper-tail guards
are both reported. Product Decoder sources are restored byte for byte.

The confirmation independently verifies **5,760 EncodeNow calls, 4,416 measured
Decode calls, 4,824 protected packet/header pairs, 191,520 Float32 values and
2,412 identical corresponding payload pairs**. The 768 measured no-output rows
have zero Decode ticks and are excluded from Decode statistics. Only the two
Decoder helpers/program vary; 390 other sources, four other loaded programs and
23 configurations remain unchanged. Timed Decoder Update includes extraction,
CRC, structural preflight, field dispatch and receiver callbacks; it does not
include receiver GPU requests or establish live-client/frame/FPS improvement.

Evidence under
`F:/Unity/TSMP/Validation-Results/optimization-implementation-20261101`:
`decoder-active` retains the first failed-tail experiment and all candidate
regressions; `decoder-tail-confirmation` retains the enlarged-control plan,
every run, performance-analysis.json and independent-confirmation-audit.json.
The original code then passes the retained new policy tests (50 Native / 51 VM)
and fresh 16-program client compilation again. Frozen candidate sources remain
available for later evaluation without shipping the change.

## Later target-validation experiment: not adopted

A separate optimization-branch experiment reused the existing resolved-target
cache validation result to avoid comparing the same target again in send-state
validation. It changed one Encoder file by 6 added/5 removed lines, with no new
arrays or policy changes. Rotation arithmetic was deliberately left unchanged.

The 373-assertion semantic preflight and all six fresh-process matrices pass.
Each formal run passes **126,583 assertions**. Independent raw audits verify
**2,880 EncodeNow calls, 1,944 protected packet/header pairs, 99,360 Float32
values and 972 byte-identical corresponding payload pairs**. Callback-time
replacement, direction/sendMode changes and once/order behavior are preserved.
Only Encoder source/program changes; 391 other sources, four other loaded
programs and 23 configuration files remain identical.

| 128 fields / distinct targets | Original mean | Candidate mean | Reduction |
| --- | ---: | ---: | ---: |
| 128 / changing | 12.177042 ms | 12.036496 ms | 1.154% |
| 128 / unchanged | 9.078913 ms | 8.997911 ms | 0.892% |

Both primary reductions miss the **2%** acceptance threshold frozen before
timing. The 128-field single-target changing control also has a pooled p99
increase **10.1829 to 13.0678 ms**, exceeding the frozen 5% tail allowance.
There are 96 measured calls per variant/case, so this p99 is the observed maximum.
The candidate program shrinks **138,368 to 138,324 B** (-44 B), but the source
is **not adopted**. The original Encoder is restored byte for byte, retaining
the earlier indexed callback-group cache and automatic-clear diagnostic fix.

Evidence: `F:/Unity/TSMP/Validation-Results/optimization-implementation-20261101/`
`encoder-cache`, including frozen sources/plan/guards, all six complete runs,
`ab-analysis.json` and `independent-cache-audit.json`. Measurement-valid PASS
is distinct from adoption FAIL. No failed case or timing outlier was removed.

The existing driver now additionally records protected Decoder completion
ticks in `samples.csv`. Only `decoder._update` is timed, including extraction,
CRC, structural preflight, application and receiver callbacks. Heap/input setup,
file output and value verification are outside that interval. Rows without an
emitted packet have zero ticks and are excluded from Decoder statistics. This
addition is separate from Encoder timing and does not measure receiver GPU work.

## Earlier indexed target-group implementation

The Udon Encoder previously searched the targets already visited on every due
field. With many distinct targets, those comparisons repeat quadratically on
every encode attempt, including attempts where no field value changes. The
private Encoder path now builds target-group indices when its existing send
state is rebuilt, then uses indexed visited flags and the existing sendMode
cache during each attempt. The public helper remains unchanged.

Across three matched full-matrix source pairs, mean actual EncodeNow time falls
**27.39% for 32 changing targets**, **53.05% for 128 changing targets**, and
**59.49% for 128 unchanged targets**. The largest gains apply to many targets;
single-target controls do not establish a universal improvement.

## Formal source-isolated measurements

Six fresh Unity 2022.3.22f1 SDK Editor processes run the original/candidate in
**AB/BA/AB** order. Each process runs all twelve frozen cases: 32/128 Float32
fields, one/distinct targets, and changing/unchanged/quarter-changing values.
Eight warm calls precede 32 measured calls per case. Each table value pools
**96 measured calls per version**, with equal sample counts. Percentiles use
nearest-rank `ceil(n*q)-1`, recomputed from raw samples rather than averaging
the per-run percentiles.

| Fields / targets / changes | Original mean ms | Candidate mean ms | Reduction | Original p95 ms | Candidate p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 32 / 1 / all | 1.858 | 1.828 | 1.59% | 1.950 | 1.946 |
| 32 / 1 / none | 1.430 | 1.367 | 4.39% | 1.503 | 1.436 |
| 32 / 1 / quarter | 1.752 | 1.703 | 2.80% | 1.837 | 1.826 |
| 32 / 32 / all | **3.399** | **2.468** | **27.39%** | **3.578** | **2.625** |
| 32 / 32 / none | 2.483 | 1.591 | 35.94% | 2.637 | 1.768 |
| 32 / 32 / quarter | 2.964 | 2.107 | 28.90% | 3.098 | 2.343 |
| 128 / 1 / all | 9.632 | 9.420 | 2.20% | 9.974 | 9.634 |
| 128 / 1 / none | 8.583 | 8.397 | 2.16% | 8.754 | 8.661 |
| 128 / 1 / quarter | 9.150 | 8.884 | 2.91% | 9.449 | 9.115 |
| 128 / 128 / all | **26.461** | **12.423** | **53.05%** | **27.664** | **13.007** |
| 128 / 128 / none | **23.319** | **9.445** | **59.49%** | **23.864** | **9.714** |
| 128 / 128 / quarter | 24.314 | 10.602 | 56.39% | 24.674 | 11.145 |

The changing 128-target mean reductions are **53.49%, 51.87%, 53.80%** in
the three pairs; the 32-target reductions are **27.27%, 27.36%, 27.55%**.
All many-target cases improve in every pair. No failed formal run, sample or
outlier was removed, and no control-based timing normalization was applied.

One single-target 32-field changing pair reverses slightly: mean
**1.8473 to 1.8641 ms** (+0.0168 ms, 0.91%), and p95 **1.9776 to 2.0949 ms**.
The pooled p99/max also rises **2.0059 to 2.1181 ms** despite a slightly lower
pooled mean/p95. Other small-control percentile reversals remain in raw results.
These cases are described as near-unchanged controls, not proof of a single-target
speedup or guaranteed tail-time improvement.

## Correctness and receipts

All six runs pass **126,583 assertions each**. Independent raw audits pass:

- **2,880** full EncodeNow calls, including every warm observation; **2,304**
  measured calls. Each case has ordered sample IDs 0–39, measured IDs 8–39 and
  forty distinct observed Unity frames.
- **1,944** captured protected packet/header pairs, **99,360** independently
  checked Float32 values and original header/payload CRCs. All **972** corresponding
  paired payloads are byte-identical. Outer header timestamps differ between
  processes and are not called byte-identical.
- Actual GPU Luma4 Encoder output is recorded and asserted on every publication.
  Original protected packet/header bytes then enter the actual compiled Decoder
  through an injected completed slot; all resulting target fields are checked.
  This validates application and CRC gating, not receiver GPU performance.
- Actual per-call callback-counter deltas are recorded; each writable/due target
  receives BeforeEncode once. Nine original semantic rounds per run check
  rotation, in-place same-length target replacement, direction changes and
  sendMode selected inside BeforeEncode.
- Nineteen additional semantic rows per run cover mixed-priority rotation,
  interval-ineligible/ReceiveOnly first fields with later due fields, callbacks
  changing other targets' sendMode before/after first visits, callback-time public
  binding target replacement (current copied snapshot, replacement next frame),
  and callback-time changes to an upcoming field's direction. Their actual order,
  field effects and counters match independent expected cases. All **114** edge
  rows and **54** original rounds pass across the six processes.
- Each run retains **392** product source receipts and **23** unchanged project
  configuration receipts. The only source variant is TSMPEncoder.cs; the other
  **391** files are identical across all six runs. All five loaded program hashes
  are stable within each variant; only the Encoder bytecode differs across them.

The source experiment is a single Runtime file, **29 added/9 removed lines**,
inside the Udon branch. Protocol, CRC, field serialization, callback timing/order,
send scheduling, priority/rotation policy, retry defaults and the Native branch
remain unchanged. The codec and Decoder programs remain unchanged.

Client Encoder bytecode falls **139,520 to 138,324 bytes**, a 1,196-byte decrease.
Two bounded cache bodies contain one Int32 group index and one Boolean visited
flag per binding: **640 bytes at 128 bindings**, excluding array/object overhead.
They replace the old target-reference array; the sendMode array already existed.
This is a structural size statement, not a measured allocation or zero-allocation
claim. No per-frame allocation claim follows from the unavailable Mono counter.

## First use, rebuilds and measurement limits

Group construction still performs bounded quadratic comparisons when binding
send state is rebuilt. The first 128-distinct-target changing call is
**25.675–27.141 ms original versus 25.956–26.846 ms candidate**. That first call
has not received the steady-state improvement. Frequent binding identity changes
can keep paying this cost. Every first/warm observation and semantic rebuild
timing remains in the evidence.

The very first 32-field/one-target call, including new Encoder resources and
graphics first use in each process, takes **49.221–50.397 ms original versus
50.668–54.845 ms candidate**. No startup improvement is claimed. Semantic timing
also includes its extra recorder callbacks and must not be equated with ordinary
matrix timing or isolated rebuild cost.

The earlier discovery run was materially slower even on original source:
128-target changing mean **46.848 ms**, versus **26.106–26.983 ms** in the formal
original runs. Its old helper scan took 27.281 ms versus 14.510–14.933 ms across
the formal runs. The cause of this host/run variation is not established; only
the matched formal pairs support the reported product improvement. The public
helper remains a separate, unchanged control, with per-run timings retained.
Its direct callback control excludes sendMode queries/bookkeeping, so their
difference is not a pure search-only attribution.

This matrix sets **refresh=0** to isolate changed/unchanged serialization,
autoEncode=false and unlimited payload. It measures full actual client VM
EncodeNow elapsed with nested product work included once. Source-value setup,
packet checks, injected Decoder completion and rendering are outside that timer.
It does not measure the production default 1-second refresh workload, fixed
30Hz source pacing, total TSMP frame p95, populated-world/live VRChat FPS, Quest,
OBS/CDN transport, string/pose/array mixtures or many concurrent Encoder/Decoder
pairs. Many scalar fields still cost work even after target searching is removed.

## Evidence and reproduction

Evidence root:
`F:/Unity/TSMP/Validation-Results/workload-followup-20261101/binding-cost`.

- `ab-manifest.json`: all six orders, exact source/probe/plan/harness receipts,
  frozen before formal outputs; SHA256
  `6c44e5f9f1d9ff8547ba24bc067e4604eba9a30c605a1103e26db80131303ce2`.
- `plan-v2.json`: twelve complete workloads and expanded probe hash; SHA256
  `aa982555becaf01ab37ea70017d4af15a21bf92ed5fdbbe1e572d78fba6de941`.
- Original Encoder source SHA256:
  `5f916d80fd72fe9c1f983657a109bb86272d1951f3ae93a073eaf2341f7165e8`.
- Candidate Encoder source SHA256:
  `8292b7b659f6ef8c2daee6dd27c6ee44af4319086717bc0706cb29297828af2c`.
- `ab-analysis.json`, `pooled-raw-metrics.json`, and peer
  `independent-ab-audit.json` retain all per-pair/pooled timing, payload parity,
  source/configuration/program controls, helper controls and first/rebuild calls.
- `ab/pair*-*/` retains raw samples, protected packets/headers, client program
  hash receipts, copied executed harnesses and complete logs. The candidate source
  is restored in the dedicated VRC project and synchronized into Native afterward.

Preliminary failures remain outside the formal comparison: `baseline-initial`
failed before samples because harness cloning did not honor Unity Material null
semantics; `baseline-corrected` failed before samples because the shared binder
set Encoder-only scheduling fields on a Decoder. Null-safe cloning and explicit
Encoder/Decoder binding scopes fixed those harness faults. The corrected
`baseline-bindings-corrected` discovery run and the original-source semantic-only
preflight (373 assertions) remain separate evidence. The formal probe was expanded
after discovery; no discovery sample is pooled into the formal pairs.
