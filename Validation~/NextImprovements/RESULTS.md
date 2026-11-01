# Measured follow-up improvements, 2026-11-01

The substantial product improvement is **Color256 Udon encoding**: a fixed
palette and incremental block cursor remove repeated per-symbol color and
coordinate calculations. The change adds 51 lines in one codec source file,
retains the original full-pixel fallback, and preserves encoded pixels. Payload
CRC, packet format, recovery algorithms and defaults are unchanged.

## Improvement measured through the actual product path

Seven fresh SDK Editor processes run compiled client Encoder, Decoder, codec and
receiver programs with real VRC GPU callbacks. Three pairs alternate original
and modified source; each measures 1KB and 5KB fields for twelve seconds. A
preselected sixty-second modified run confirms 5KB sustained delivery. The plan,
both sources and their hashes were frozen before the A/B outputs.

| Field | Original mean EncodeNow | Modified mean | Reduction |
| --- | ---: | ---: | ---: |
| 1,024 B | 11.2778 ms | 2.6047 ms | **76.90%** |
| 5,120 B | 43.6219 ms | 9.1275 ms | **79.08%** |

These are pooled elapsed VM call measurements, including nested product work
once. The 1KB means use 1,080 calls per version; the 5KB means use 743 original
and 1,080 modified calls. The faster sender produces more updates, so CPU per
second alone is not a comparison of equal useful output. Individual 5KB pair
reductions are 78.44–80.17%; the slower original pair remains in the results.

Across the three 5KB pairs:

- Published update rate rises **19.58–21.25 to 30 Hz**. Twelve-second sender
  deadline misses fall **104–125 to zero**. Every actual published ID arrives.
- Frame-level TSMP VM time p95 falls **50.48–56.44 to 10.18–10.48 ms**.
- Source-age p95 falls **146.18–164.86 to 44.82–45.44 ms**.

The sixty-second modified run receives **1,800/1,800** measured updates at
30 Hz, with zero sender deadline misses. EncodeNow averages 9.194 ms; its p95
is 9.964 ms. Frame VM time p95/p99/max is **10.493/11.396/15.714 ms**, and
source-age p95 is 45.347 ms. The SDK Editor achieves 89.58 render Hz against a
90 Hz target; this is not a real VRChat FPS result.

The seven runs retain 43,791 top-level events, 18,473 frame rows and all
**6,884/6,884** published/applied IDs including warmup and observation tails.
Every received field is checked in the C# receiver observer. Independent Python
audits timing arithmetic, IDs, ages, sources and bytecode/configuration receipts;
it does not re-read GPU pixels. Other 391 product source files and 23 project
configuration files stay unchanged across each source variant/run.

See [Color256 pixel parity](../Color256Encoder/RESULTS.md) and the
[Color256 performance report](../Color256Performance/RESULTS.md) for
reproduction and full samples.

## Correctness and cost of the small writer change

Before editing, all 392 measured source files were archived byte-for-byte.
Original and modified SDK C# output match an independent integer palette and
coordinate oracle in 25 full-buffer vectors. Nineteen valid vectors also run
the actual client codec VM after fresh compilation in Play Mode. All output
bytes match the original, including untouched header/static/tail sentinels.
Managed invalid-count/short-buffer cases retain results, exception types and
partial writes. These tests pass 88 original and 208 modified assertions.

The cache is lazy and per codec instance. Its 256 Color32 values have a
**1,024-byte array body**, excluding object overhead; no allocation-counter
claim follows. Codec bytecode grows **15,696 to 17,924 B**, +2,228 B. Palette
initialization cost is not isolated, and startup is not claimed faster.
The dedicated Native encoder branch and all decoder shaders are unchanged.

## Other approved work completed

1. **Allocation evidence corrected.** An actual 8,192-byte allocation also
   returns zero from this Unity Mono counter. The original structural-preflight
   raw zero therefore does not verify zero allocation. The probe now requires
   a positive control and reports verified allocation as -1 when unavailable.
   Earlier correctness and timing results remain separate evidence.
2. **RGB16 preparation scoped correctly.** Two fresh processes with existing
   caches separate the default fixed 4/4/4 material from twelve broad draws.
   The default ordinary draw plus synchronous flush/comparison first takes
   8.296 ms. The historical 96-second broad delay is neither reproduced nor
   resolved; this is not a 96s-to-8ms product improvement. No cache was deleted.
3. **Sustained bottleneck located.** The frozen 27-case, 468-second baseline
   separates sender misses, actual published-ID delivery and per-frame VM work.
   All 7,610 measured publications arrive, while the 5KB Color256 sender itself
   fails to maintain 30 Hz. Six missing warmup/tail observations in the wider
   baseline trace remain documented rather than counted as lossless delivery.
4. **Payload deferral diagnosed.** The Inspector identifies the last deferred
   field and blocked head RPC, their minimum complete-packet sizes and whether
   they fit alone. Aggregate oversized counts distinguish unsendable fields
   from ordinary current-frame contention. Values reset each encode attempt
   and reuse existing serialization results; no extra serialization or queue
   scan was added. Native 12,419 assertions, actual client VM 131 assertions,
   and the existing sixteen-program compilation/bindings check pass.
5. **Default refresh checked with actual time.** Seven Native scenarios run
   actual EncodeNow with production realtime clocks for 8.503 seconds. All
   1,785 observations, 115 protected packet/header pairs, 19,721 assertions and
   255 shared 30 Hz opportunities pass, with no deadline misses. Unchanged
   state recovers at eligible default-refresh/minimum-interval opportunities.
   A 100 ms loss actually covers all four RPC copies; periodic state refresh
   does not recreate that lost event. An intact stream-restart control executes
   the same event ID once in each stream. Decoder completion is injected with
   original protected bytes; this is a local loss-policy test, not a network
   latency or whole-client reconnect test.
6. **Polling defaults retained.** The pre-optimization Color256 polling matrix
   shows very small benefit while encoding already limits rendering below
   30 Hz. This result must not be generalized to the faster modified sender.
   There is no RPC workload or real video-arrival signal proving a universal
   cap safe. No Runtime polling feature, automatic retry reduction or frame
   disposal policy was added.

## Work units and evidence

All work stays on `codex/tsmp-reliability`. Core and Color256 changes are committed
by work unit. No main merge, push, release, live VRChat login, external streaming
service, runtime FEC, Sparse recovery, fragmentation or automatic budget policy
is added. `targetPayloadBytes=0`, two payload retries and existing CRC checks
remain in place.

| Commit | Repository / work |
| --- | --- |
| `1a9beb5` | Core: allocation positive control and correction of unsupported claims |
| `c814c27` | Core: isolated RGB16 material/preparation measurement |
| `4f55b14` | Core: precise deferral diagnostics and deterministic refresh policy tests |
| `7c7f181` | Core: sustained VM/frame/source measurement and bottleneck report |
| `8c597c1` | Core: actual-clock EncodeNow loss/hold/refresh validation |
| `7f2fe8f` | Core: frozen original and client VM Color256 pixel parity |
| `f89e3e2` | Color256: cached palette and incremental block writer |
| `318b46a` | Core: paired performance report and reproducible source-isolated runner |

Evidence root:
`F:/Unity/TSMP/Validation-Results/next-improvements-20261101`.
Source archives, failed preliminary fixtures, frozen plans, complete logs and
raw samples are retained. `source-audit-final.json` checks five repository main
refs, branches, committed package sources, dedicated-project copies and existing
GUIDs. `root-independent-audit.json`, `sustained-independent-audit.json`,
`budget-independent-audit.json`, and Color256 correctness/performance audits
bind the arithmetic and provenance to raw evidence.

The final source audit passes **708 dedicated-project comparisons and 320
preserved existing GUIDs** across five repositories. Root's independent recount
also passes the allocation calibration, 108 preparation draws, deterministic
and actual-clock packet/state traces, original/modified pixel-buffer parity,
the 27-case baseline and all seven performance runs.

These measurements cover one changing RawBytes field in a lightweight SDK
Editor scene on i9-13900K/RTX4090. They do not establish populated-world/live
VRChat FPS, Quest performance, many-scalar binding cost or OBS/VRCDN transport
loss. The optimization preserves raster colors; compressed-video robustness and
the unreproduced historical RGB16 preparation delay are not claimed improved.
