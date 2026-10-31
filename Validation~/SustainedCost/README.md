# Sustained processing and delivery measurement

This validation-only harness reuses the VM construction, cloned codec materials,
and graphics preparation approach from RefineCost and ContinuousFrames. It runs
the actual compiled client Encoder, Decoder, codecs and receiver with real VRC
GPU callbacks in a dedicated Worlds SDK Editor project. No product overlay is
installed. Run only while other owned Unity/GPU/encoder work is stopped.

```powershell
python ./Validation~/SustainedCost/Generate-Plan.py --refine-plan '<previous RefineCost plan.json>' --output '<fresh plan.json>'
./Validation~/SustainedCost/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Plan '<plan.json>' -Results '<fresh results>'
python ./Validation~/SustainedCost/Analyze-Results.py --results '<completed results>'
```

The frozen plan selects eight clean workloads (Luma4/Color256, 1,024/5,120-byte
fields, source30Hz and render targets60/90Hz), six damaged processing workloads
(Color256/RGB16, budgets0/1/2), preselected60-second confirmations, three paired
polling comparisons, and phase/jitter/held-source comparisons. Cases warm for
two seconds, measure12seconds unless specified, publish for one observation-tail
second, then drain outstanding requests. Publications occur at most once per
Unity frame; missed wall-clock source deadlines are separate from lost published
IDs. Actual render/source rates are reported separately from targets.

Clean fields carry a changing ID and ID-seeded xorshift32 bytes. Host generation
is outside Encoder VM timing. Every receiver callback is observed immediately
after its receiver VM returns, and all bytes are independently recomputed from
the received ID. This still checks both applications if a Decoder event drains
two ready slots. Receiver host checks run inside the outer Decoder dispatch;
their measured instrumentation ticks are retained and subtracted from gross
Decoder Interpret time. Reports retain gross, nested observation, and net time.
Nested codec/receiver VM execution remains included once in the outer timer.

VM events are retained during warmup and observation tail, then filtered by their
own start timestamps against exact case bounds. Frame CPU is the sum of unique
top-level Encoder/Decoder VM events sharing Time.frameCount. CPU/s, p95/p99/max
per frame (nearest-rank ceil(n*q)-1), individual callback time, callbacks per frame, source age, receiver
IDs, and sender deadline misses are independently recounted. A source-cohort
apply rate counts measured-window publications arriving before that window ends;
it differs from all application timestamps falling inside the window. Raw traces
retain both and the analyzer reports both rates. Per-frame CPU uses the union of
measured Update frame IDs and timestamp-eligible VM event frame IDs; boundary
events can add a frame, and both frame counts are reported. `busyTicks` is observed occupancy before `_update`; actual rejected
admissions use the existing `skippedBusyDecodeCount` delta.

Logical request IDs are assigned only when a slot changes from idle to pending.
Callback replacement of a readback request retains the logical ID. Occupied-slot
request replacement during `_update` is ambiguous and gets ID/source `-1`.
Consequently proven admissions are a lower bound; request correlation does not
drive source age or delivery accounting. Those use actual received IDs.

Damaged stress cases replay unchanged protected frames selected in the previous
RefineCost plan. Their fixed identities require duplicate skipping off to measure
repeated recovery under load. Every accepted header/payload matches its original.
They are **processing saturation**, not changing-video delivery, and have no
unique update rate, source age, or RPC-delivery claim.

The polling experiment uses existing `applyEveryFrame` and `_update` controls;
it does not change Runtime code. Its phase/jitter/held cases are deterministic
local schedules. No RPC workload or real video frame-arrival API is included,
so even favorable CPU results cannot justify changing the production default.

GC collection counts, Stopwatch frequency, CPU/OS and host observation costs are
retained. Source-age samples retain their own timestamps and sent timestamps for
exact independent arithmetic, separately from the prestep frame timestamp. VM samples use
preallocated struct storage and are formatted after cases; frame/publication
observations still allocate outside VM timers and can affect achieved scheduling.
Net VM time is not total host CPU, GPU execution, live VRChat FPS, populated-world
performance, or RTMP/HLS/CDN behavior. PASS asserts completion and byte/order
correctness, not zero missing frames. Keep initial failures and project-setting
receipts alongside final results.

For a separately approved Color256 source experiment, `Generate-AB-Plan.py`
freezes three alternating baseline/candidate pairs. Each fresh process runs the
1KB and5KB workloads for12seconds each; a seventh process confirms candidate5KB
for60seconds. Pin both source files and copy only the dedicated project's codec
source between processes. Source stability is still required within every run.

The optional case field `measuredStartId` defaults to0. A positive value performs
a monotonic ID jump inside the first actual measured publication, immediately
before allocating that publication's ID. The A/B plans use1024, making the first
measured field ID1024 in each process. Common measured IDs therefore share field
bytes, while host timestamps, outer TSMP frame indices and warmup counts differ.
Keep those distinctions when comparing performance; do not call packets bit
identical. The original27-case source copies remain frozen in the result folder.
