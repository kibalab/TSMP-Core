# Color256 Udon encoder improvement, 2026-11-01

## Current decision after repeat verification

The small calibration-row change `151128b` is **reverted by `2fe5026`** on the
optimization branch after a second frozen seven-process experiment failed its
complete-delivery gate. The older palette/block-index optimization remains.
Current Color256 source is again
`63cf1f4d725774bc923e9cc2f302b89d633640d823ed5fa9bb91329e42e64bfd`.

The repeat does **not** reproduce the prior 5KB tail regression. Pooled p99
original/candidate is Encode 5.3393/5.2102 ms, frame VM 5.7997/5.5815 ms and
source age 47.3221/44.8833 ms. Mean reductions remain 6.803% at 1KB and 2.719%
at 5KB. However, the candidate's 60-second case receives **1,798/1,800** measured
IDs; missing IDs are **1978 and 2372**, with no sender deadline misses. Overall
measured delivery is **6,118/6,120**. Warm/tail loss is zero; all **7,302** accepted
applications have complete-field checks. No corrupted application is accepted.

These results do not establish that calibration copying caused the two losses.
The isolated change is conservatively withdrawn because the acceptance gate
failed. A passing per-run measurement validates accepted bytes and recording;
it does not establish complete delivery. The official aggregate analyzer's
FAIL on missing IDs is retained, not relaxed or relabeled PASS.

Repeat evidence:
`F:/Unity/TSMP/Validation-Results/optimization-implementation-20261101/`
`color-tail-recheck`. `guards-before-timing.json` pins mean, three pooled p99
limits (candidate <=105% original), and complete-delivery requirements before
output. `independent-tail-audit.json` independently verifies source/program/
configuration isolation and records measurement-valid PASS / adoption FAIL.
Every process and outlier remains available. The initial experiment below is
historical evidence for the now-reverted candidate.

## Calibration-row follow-up on the optimization branch

After integrating reliability into local main, the first optimization on
codex/tsmp-optimization copies complete calibration rows from the existing
256-color palette with Array.Copy. Short/partial destination rows retain
per-element bounds handling. Payload writing, palette initialization, the
full-pixel fallback, Native encoding, shaders and protocol are unchanged. This
is one Runtime file, **13 added/10 removed lines**, with no new arrays or GPU
passes. The codec client program grows **17,924 to 18,144 B** (+220 B).

The existing correctness runner passes **88 original / 208 candidate assertions**
with **25 managed and 19 actual client-VM vectors per version**. Complete arrays
match the independent coordinate/palette oracle and the original output,
including calibration rows, short/negative regions, dirty tails and invalid
input behavior. The same existing seven-process performance runner then
executes three source pairs in AB/BA/AB order and a candidate 60-second check,
with no new permanent harness or selection of inputs after timing.

| Current matched workload | Original mean | Candidate mean | Mean reduction | Original / candidate p95 |
| --- | ---: | ---: | ---: | ---: |
| Color256 1KB | 1.454794 ms | 1.343892 ms | **7.623%** | 1.5378 / 1.4327 ms |
| Color256 5KB | 4.982289 ms | 4.896678 ms | **1.718%** | 5.1634 / 5.1046 ms |

Each mean/p95 pools **1,080 measured EncodeNow calls per version**. These are
whole Encoder client VM elapsed times with nested work included once; they
are not FPS, GPU execution time or whole-frame elapsed time. The 1KB mean
improves **8.568%, 8.292%, 5.998%** across the three pairs. The 5KB pairs improve
2.924% and 3.332%, then reverse **1.149% slower** in the last pair.

There is no universal frame/latency improvement: first-pair 5KB frame-VM p95
rises **5.5627 to 6.8991 ms**, source-age p95 **44.7774 to 53.6714 ms**; the
other two age p95 values remain about 44.8 ms. Last-pair Encode p95 rises
5.0680 to 5.3867 ms. Pooled 5KB maximum rises **5.5586 to 12.1510 ms**. These
reversals and all outliers are retained. The small 5KB average is not presented
as a dependable gain for every run, tail time, hardware or real-client workload.

Independent raw pooling also finds 5KB Encode p99 **5.2717 to 5.5420 ms**,
frame-VM p99 **5.7047 to 7.0487 ms**, and source-age p99 **44.9026 to 54.0197 ms**.
Each candidate paired process contains one or two Encode calls above 6 ms,
versus none in the originals. Their cause is not established; they are not
dismissed as noise. The 60-second candidate does not sustain those tails
(Encode p99/max 5.2134/5.7564 ms, frame-VM p99 5.6 ms).

All **6,120/6,120 measured fields** arrive, with no sender deadline misses.
Warmup/tail traces also retain **7,303/7,303 applications**, each checked across
its entire field by the executed receiver harness. The 60-second candidate
receives **1,800/1,800** at the 30Hz source target; Encode mean/p95 is
4.8896/5.0799 ms, frame-VM p95 5.44 ms, source-age p95 44.7921 ms.

Adoption criteria were frozen before timing: consistent 1KB improvement of at
least 2% pooled, nonregressing 5KB pooled mean with no pair over 2% slower,
complete measured delivery, original pixel/invalid-input parity, and no new
buffers/GPU passes. All pass. The change is retained for its small-payload CPU
saving with the 5KB tail limitations above, rather than increasing recovery or
transport complexity. It is not added to earlier percentage reductions.

Evidence: `F:/Unity/TSMP/Validation-Results/optimization-focus-20261101/`
`color-calibration`, including the frozen adoption rules, source variants,
correctness captures and `ab/ab-analysis.json`. Source SHA256 changes from
`63cf1f4d725774bc923e9cc2f302b89d633640d823ed5fa9bb91329e42e64bfd` to
`5243abfb8edb99f8b0ae21c9841d4597006baf992cfa2e954f290289711d9dc6`.
Only the Color256 source/program varies; 391 other product sources, four other
loaded programs and 23 configurations remain unchanged per run. Absolute timings
still differ from older measurements with the same product code; only these
matched pairs support the new percentages. No live VRChat, Quest or OBS/VRCDN
validation, allocation measurement, push, release or optimization-to-main merge
is claimed.

`independent-raw-audit.json` independently reconciles **51,004 VM events and
21,871 frame rows**, receiver IDs, complete-field-check records, source/program
receipts and the frozen adoption criteria. It does not recheck dumped field
bytes: those bytes are checked by the executed receiver harness. Product change
commit: Color256 `151128b`.

## Earlier palette and block-index optimization

Caching the256 fixed colors and writing block-texture pixels directly reduces
mean EncodeNow time by **76.90% at1KB and79.08% at5KB** across three fresh-process
pairs. The5KB source sustains its requested30Hz after previously achieving about
20–21Hz. This result is specific to Udon encoding; the Native buffered writer and
decoder algorithms are unchanged.

## Small implementation and correctness boundary

The existing Udon writer recalculated three color levels and block coordinates
for every symbol. The new block-texture branch reuses256 Color32 entries and
advances pixel coordinates with the existing orientation and bounds. The
full-resolution fallback remains intact. Palette data occupies1,024 bytes plus
array/heap overhead; no measured zero-allocation claim is made. Codec bytecode
increases15,696→17,924 bytes (+2,228). No public API, shader, palette value,
protocol, retry setting or polling policy changes.

Before timing, SDK managed C# and actual client codec VM outputs are checked
against an independent coordinate/color oracle:25 managed and19 VM vectors per
variant, including all256 colors, narrow/partial rows, payload bounds, full-size
fallbacks, dirty tails and invalid-input parity. Full pixel arrays include
untouched static/header/tail sentinel regions. Managed/VM checks pass88 baseline
and208 candidate assertions. The independent correctness audit reparses these
outputs and confirms the only product source change is the codec file.
Large full-resolution fallbacks and fatal-invalid vectors use SDK managed C#;
the actual VM covers the selected19 vectors, including a small full-resolution
fallback. This vector experiment is not a GPU byte oracle or a Native SDK-free
performance test.

The performance path then runs actual protected Encoder→raster→GPU Decoder→bound
field application. Every applied field is checked byte by byte from its received
ID. Header/payload CRC gates remain enabled in production code. This dynamic
workload does not compare every header byte with an independent golden header;
timestamps and outer frame indices differ between runs.

## Frozen fresh-process A/B

Seven fresh Unity2022.3.22f1 D3D11/Gamma processes, RTX4090/i9-13900K, total204
measured seconds. Three alternating pairs each run1KB and5KB fields for12seconds
with30Hz source/90Hz render targets; the seventh confirms candidate5KB for60s.
The first measured field ID is1024, aligning common field bytes. Warmup counts,
outer frame indices and timestamps differ. Source hashes and plans were pinned
before output. No failed A/B run or outlier was removed.

| Pair | 1KB mean EncodeNow old→new ms | Reduction | 5KB mean old→new ms | Reduction |
| --- | ---: | ---: | ---: | ---: |
| 0, baseline first | 11.456→2.679 | 76.61% | 42.633→9.118 | 78.61% |
| 1, candidate first | 11.262→2.584 | 77.06% | 42.182→9.095 | 78.44% |
| 2, baseline first | 11.115→2.551 | 77.05% | 46.249→9.169 | 80.17% |
| Raw-call pooled | **11.277831→2.604664** | **76.9046%** | **43.621880→9.127534** | **79.0758%** |

Pooled1KB samples are1,080 per variant;5KB samples are743 baseline and1,080
candidate because the slow baseline cannot publish at30Hz. Pooling is by actual
EncodeNow call, not averaging percentages or pretending equal throughput.

At5KB, actual source rates rise19.58–21.25→30.00Hz; source deadline misses
104–125→0 in each12-second pair. Every measured published ID arrives. Actual
Editor render rates rise19.58–21.25→89.50–89.92Hz. These are minimal Editor-loop
rates, not live VRChat FPS. CPU/frame p95 falls50.48–56.44→10.176–10.484ms and
source-age p95 falls146.18–164.86→44.82–45.44ms.

Total VM CPU/s falls about978→436ms despite publishing and decoding more updates
and rendering more frames. That total measures differing achieved workloads;
the per-call figures above isolate the encoder comparison. The slow46.249ms
third baseline is retained explicitly.

## Preselected60-second confirmation

| Measure | Candidate5KB |
| --- | ---: |
| Measured source IDs received | **1,800/1,800** |
| Actual published rate / sender deadline misses | **30Hz / 0** |
| Actual Editor render rate | 89.583Hz |
| EncodeNow mean / p95 / p99 / max | 9.194 / 9.964 / 10.645 / 12.119ms |
| Total CPU/frame p95 / p99 / max | **10.493 / 11.396 / 15.714ms** |
| Total VM CPU/s | 438.709ms |
| Source age p95 / max | 45.347 / 55.989ms |
| Local application latency p95 / max | 26.028 / 29.859ms |
| Busy decode skips / payload retries | 0 / 0 |
| Observed GC collections | 0 in all recorded generations |

GC observations do not prove zero allocations: palette creation occurs before
steady measurement, and the per-thread allocation counter was independently
found unavailable on this Unity Mono host. Source age is local time since
immediately before encoding, sampled each render frame; it is not service/network
latency. Receiver callback timestamps have float-clock precision.

## Receipts and limits

All seven processes and raw analyzers pass. **5,783/5,783 measured publications**
arrive; complete warmup and tail traces also deliver **6,884/6,884**, with6,884
full-field byte checks. Each run preserves392 product source receipts and23
configuration files. Between variants,391 other sources remain unchanged, and
the only loaded program bytecode change is Color256. The dedicated project's
candidate source is restored after the final process. The authoritative codec
source is changed separately by the root task.

Independent `independent-performance-audit.json` reparses all seven raw runs,
their source/bytecode/configuration hashes, actual ID traces, full-byte-check
counts, exact age arithmetic, gross/net CPU and timing intervals. The raw paired
pool is reproducible with `Analyze-Results.py`.

The earlier27-case polling comparison is a **pre-palette baseline** dominated by
the old Color256 encoder. It does not establish the benefit of polling after this
fix. Polling defaults remain unchanged because RPC and actual frame-arrival
guards are absent; no new polling experiment or feature is introduced here.

These results use one field in a minimal local loopback. No populated world,
actual VRChat client, Quest, compressed OBS/CDN stream or service performance is
claimed. Native buffered encoding, other codecs and Refine robustness are not
given a new measured speedup by this change. First-use palette allocation and
cold shader startup are outside these steady-state timing gains.

Evidence root:
`F:/Unity/TSMP/Validation-Results/next-improvements-20261101/color256-optimization`.
Manifest SHA256:
`6b1b425a940998617f36173d66f1ad25b6f18d9654f22fff69ef93c155d8960b`.
Baseline source SHA256:
`c96446cbba1c24f67161a667abe65f06102e44979f8148ef16a2b002aef6725d`.
Candidate source SHA256:
`63cf1f4d725774bc923e9cc2f302b89d633640d823ed5fa9bb91329e42e64bfd`.
