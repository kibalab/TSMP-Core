# Follow-up correctness and processing measurements, 2026-10-31

One product defect is fixed: a CRC-valid packet with malformed later structure
can no longer apply an earlier state/RPC prefix before rejection. Current Refine
cost and fixed transmission budgets were measured with the actual production
paths. **Retry defaults and `targetPayloadBytes=0` remain unchanged.** Sparse
recovery remains removed; no new recovery algorithm or protocol was introduced.

## Completed work units

| Commit | Work |
| --- | --- |
| `f17ba2b` | Explain transmission budgets and recovery cost in English/Korean/Japanese tuning guides; identify old pre-payload-CRC performance figures as historical. |
| `b5f3575` | Check complete message/entry/RPC structure before application, with Native and compiled Udon regressions. |
| `38a976b` | Measure actual compiled Decoder/codec/receiver programs and real VRC GPU callbacks at retry budgets zero/one/two. |
| `8124054` | Measure fixed budgets through actual Encoder output, video compression, GPU decoding and state/RPC application. |

Product sources for both performance experiments were frozen at `b5f3575`.
All work stays on `codex/tsmp-reliability`; no main merge, push, release, live
VRChat login, stream publication or external service test was performed.

## Correctness boundary and added cost

The preflight checks the complete declared NetworkFrame structure before binding
caches, field application, RPC dispatch or RPC deduplication. Parse-only mode now
checks RPC structure too. It adds no CRC calculation, payload clone or staging
array. Existing trusted-header prediction caching and readback-only semantics
are retained; arbitrary callback side effects are not transactional.

Native 1,257 assertions, existing Core recovery 910 assertions, actual Decoder
Udon VM 3,858 assertions and sixteen client program compilations/bindings pass.
Corrected retransmissions execute their RPC once after malformed predecessors
are rejected. The original warmed Native structural check recorded a raw 0 B
delta in 1,000 calls, without calibrating its allocation counter. A follow-up
8,192-byte positive control also returned zero, so this environment's counter
is unavailable: **the previous result does not verify zero allocation**. The
corrected probe reports verified allocation as -1 when calibration fails. This
does not invalidate the separate correctness or timing results. Follow-up
evidence: `F:/Unity/TSMP/Validation-Results/next-improvements-20261101/allocation/native`.

The additional structural pass costs about **0.020 ms** for one RawBytes field
in the SDK Editor VM, including event dispatch. At the same 5,375-byte payload,
65 entries cost 0.360 ms and 257 entries cost **1.423 ms** on average. Cost scales
with entry count; the one-field value is not a whole-decoder time or a claim
about hundreds of scalar fields. Decoder bytecode is 138,380 B versus 135,744 B
after Sparse rollback. See [FrameStructure](../FrameStructure/RESULTS.md).

## Current Refine cost

Actual client-target Udon programs, Unity 2022.3.22f1, D3D11/Gamma, RTX 4090.
Nine selected 5,375-byte raster conditions at three budgets produce 945
invocations, 810 measured after warmup, with 7,303 assertions passing. Every
accepted complete payload and bound 5,350-byte field matches its saved original.

| Default two-retry condition | Accumulated Decoder VM CPU mean | Wall latency p95 |
| --- | ---: | ---: |
| Clean Luma4 / Color256 / RGB16 | 3.919 / 4.083 / 4.206 ms | 24.626 / 16.799 / 24.282 ms |
| First alternate succeeds, Color256 / RGB16 | 7.412 / 7.497 ms | 26.159 / 32.836 ms |
| Second alternate succeeds | 10.596 / 10.706 ms | 41.572 / 35.113 ms |
| All alternatives fail | 10.336 / 10.527 ms | 42.809 / 42.878 ms |

CPU totals sum submission and multiple asynchronous callbacks. They are not one
10 ms callback or a render-frame/FPS measurement. Individual callback p95 for
second-mode success is 3.648 / 3.615 ms; the matrix's observed maximum is 5.154 ms.
Wall time includes waiting and ends at final callback VM Interpret return.

Clean inputs perform no retries, so lowering the configured budget has no
established clean-path benefit. Earlier Native CSVs independently recount 125
first-mode and 137 second-mode successes: cutting every request to one retry
would give up a material part of those 262 recoveries. Keep existing defaults;
choose a different budget only against an application's delivery/latency needs.

These are repeated fixed-input invocations, not sustained two-slot delivery Hz.
The first broad RGB16 graphics preparation took 96.272 seconds outside the timed
samples; later preparations took about 9–11 ms. That warmup draws four material
variants at each of three modes, while the default 4/4/4 receiver selects one
fixed material. The broad warmup is not a measured default receiver startup
time. Its original cause and cold client behavior remain unverified.
See [RefineCost](../RefineCost/RESULTS.md).

## Fixed payload budgets: packet success and useful state differ

The Native experiment uses the same four-second semantic schedule for both
Luma4 and Color256: 120 critical 64-byte updates, forty 512-byte updates, twenty
2,048-byte updates, and eight four-repeat RPC events. Actual GPU Luma4 runs on
all Luma4 source frames. Native-size 720p output is compressed with x264 veryfast
CBR 3.5 Mbps, YUV420/BT.709 Limited/B0, then decoded and applied by production code.

| Codec / cap | Exact packets /120 | Unique state bytes /69,120 | Events /8 |
| --- | ---: | ---: | ---: |
| Luma4 / 0 | 120 | 69,120 | 8 |
| Luma4 / 1024 | 120 | 28,160 | 8 |
| Luma4 / 2304 | 120 | 69,120 | 8 |
| Color256 / 0 | 119 | 68,544 | 8 |
| Color256 / 1024 | 120 | 28,160 | 8 |
| Color256 / 2304 | 120 | 69,120 | 8 |

The 1024-byte cap never sends the bulk field: packet success is 100%, but only
**40.74%** of desired unique state bytes arrive. The 2304-byte cap preserves all
state, with one initial observation lacking bulk and bulk source-age p95 rising
**166.67 → 200.00 ms**. That age uses source ordinals and includes intentional
six-frame update cadence; it is not network or GPU-completion latency. Each of
the twenty delivered bulk versions first applies one media frame (33.33 ms)
later than at target zero. A 200 ms generation-timestamp gap is not a 200 ms
queue wait.

The 2304-byte sequence saves one Color256 observation here. A single four-second
sequence, different normal host timestamps and sequential runs do not justify a
universal cap or statistical speedup claim. Native Encoder means remain about
0.19 ms for GPU Luma4 and 1.5–1.6 ms for buffered Color256; GPU completion is
outside those submission measurements. Fixed-CBR video packet rates remain
about 3.327 Mbps, so smaller network payloads are not video bitrate savings here.

A separate twelve-frame held-image control yields 108 exact observations,
twelve duplicate skips and seven of eight events. All four copies of event46
are covered by the held interval. No corrupt/duplicate event is applied. Refresh
is explicitly disabled in this offline workload; this does not test default
periodic refresh or actual service reconnection.

Generation passes 2,940 assertions and replay passes 13,323, with 1,560 GPU
observations and 1,547 accepted full-payload rechecks. All six pristine controls
pass 120/120. Keep `targetPayloadBytes=0`; use measured fixed limits only when
the full field/event workload fits. See [BudgetDelivery](../BudgetDelivery/RESULTS.md).

## Evidence and remaining limits

Evidence root: `F:/Unity/TSMP/Validation-Results/followup-performance-20261031`.
Five pre-work Git package archives, source/bytecode/input hashes, before/after
configuration receipts, raw event samples and independent audits are preserved.
Final `source-audit-final.json` and `summary.json` bind repository refs and checks.
Initial validation-only preparation failures are retained separately.

`budget-independent-review.json` separately reparses all 720 source packets,
checks video/image hashes and 1,547 accepted payloads, and models all 1,560
state/event traces. `root-independent-audit.json` independently recounts Refine
event sums/quantiles and budget packet/unique-state/event totals. These audits
preserve the single video rejection rather than converting it into a pass.

Actual VRChat FPS, populated-world load, sustained changing-video Udon throughput,
overlapping callbacks, real OBS/VRCDN/RTMP/RTSP/HLS service behavior and the RGB16
cold-preparation cause remain outside these results. Fixed polling changes,
automatic budget adjustment, fragmentation, ACK and runtime FEC were not added.
