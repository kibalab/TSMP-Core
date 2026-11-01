# Indexed BeforeEncode target cache, 2026-11-01

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
