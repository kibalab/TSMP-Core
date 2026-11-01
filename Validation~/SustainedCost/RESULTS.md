# Sustained Udon processing baseline, 2026-11-01

The main observed bottleneck is **Color256 Udon encoding of a5,120-byte field**.
It takes about43ms per EncodeNow in this SDK Editor VM and cannot sustain the
requested30Hz source. Luma4, with its existing GPU encoder configured, sustains
30Hz at the same size. This harness does not record its effective GPU flag for
each frame; the separate BudgetDelivery experiment does record that flag.
Retry and polling defaults remain unchanged. This is the frozen baseline before
any subsequent Color256 optimization, which needs separate evidence.

## Provenance and correctness

Actual freshly compiled client Encoder/Decoder/codec/receiver programs and real
VRC GPU callbacks; Unity2022.3.22f1 D3D11/Gamma, RTX4090/i9-13900K. Twenty-seven
cases and468 measured seconds were selected before output. Assertions88,113 pass.
Every accepted application is checked after its receiver VM returns, including
multiple applications drained by one Decoder event. Clean bytes are independently
recomputed from the received ID; damaged accepted headers/payloads match originals.

The analyzer passes67,979 top-level VM events and27,059 frame rows, with392 product
source comparisons and23 unchanged configuration files. Independent
`sustained-independent-audit.json` recounts ticks/gross/net arithmetic, nonoverlapping
intervals, traces, exact age arithmetic, byte-check counts, program hashes and
configuration receipts. All **7,610 measured-window publications arrive**. The
whole warmup/tail trace has9,056 publications and9,050 applications; six missing
observations (four warmup and two post-window tail publications, all in polling
phase/jitter or held-source cases) are outside the measured source cohort. The entire trace
is consequently not described as lossless.

## Continuously changing clean fields

Publications occur at most once per Unity frame. Missed sender deadlines are
separate from published IDs missing at the receiver. CPU/frame sums top-level
Encoder and Decoder Interpret work by Time.frameCount, including nested VM work
once. Measured nested host byte checks are retained separately and subtracted
from gross Interpret time. Other host observations still affect scheduling.
Percentiles use nearest-rank ceil(n*q)-1.

| Codec / field / render target | Actual render Hz | Actual source Hz | EncodeNow mean ms | CPU/frame p95 / p99 / max ms | Received |
| --- | ---: | ---: | ---: | ---: | ---: |
| Luma4 / 1KB / 60 | 59.92 | 30.00 | 1.093 | 2.874 / 3.108 / 22.531 | 360/360 |
| Luma4 / 1KB / 90 | 90.00 | 30.00 | 1.102 | 1.903 / 2.097 / 3.380 | 360/360 |
| Luma4 / 5KB / 60 | 60.00 | 30.00 | 3.319 | 7.597 / 7.854 / 8.492 | 360/360 |
| Luma4 / 5KB / 90 | 90.00 | 30.00 | 3.340 | 4.226 / 4.792 / 9.141 | 360/360 |
| Color256 / 1KB / 60 | 59.92 | 30.00 | 11.202 | 13.594 / 14.042 / 16.392 | 360/360 |
| Color256 / 1KB / 90 | 84.75 | 30.00 | 11.321 | 13.081 / 13.414 / 14.023 | 360/360 |
| Color256 / 5KB / 60 | 20.83 | 20.83 | 43.169 | 50.709 / 53.139 / 53.508 | 250/250 |
| Color256 / 5KB / 90 | 20.75 | 20.75 | 43.332 | 50.933 / 52.501 / 55.019 | 249/249 |

The preselected60-second Color2565KB confirmation reaches20.65 actual render Hz
and20.62 published Hz: **563 sender deadline misses**, with1,237/1,237 published
IDs received. EncodeNow averages43.608ms; CPU/frame p95/p99/max are
51.321/53.175/66.000ms. Approximately899ms of Encoder VM work and79ms of Decoder
work per second consume nearly the entire measured main-thread interval.
Successful delivery cannot compensate for updates the sender never produces.

The Luma4 1KB/60 case records one GC collection in each reported generation and
a22.531ms maximum despite a3.108ms p99. The60-second Color256 confirmations also
record one collection per generation. Harness allocations remain in the measured
environment; these observations do not isolate a product allocation cause.

## Damaged repeated-image processing

These unchanged second-alternate fixtures come from the earlier RefineCost
selection. Duplicate skipping is deliberately off to measure recovery saturation;
this is not changing-video delivery, unique applied Hz or RPC success. Budgets0/1
reject the selected frames, while budget2 recovers exact payloads.

| Codec / budget | Actual render Hz | CPU/frame p95 / p99 / max ms | CPU ms/s | Busy decode skips |
| --- | ---: | ---: | ---: | ---: |
| Color256 / 0 | 90.00 | 3.947 / 4.097 / 6.091 | 324.41 | 30 |
| Color256 / 1 | 90.00 | 3.901 / 4.072 / 9.650 | 303.81 | 562 |
| Color256 / 2 | 90.00 | 7.256 / 7.432 / 8.079 | 314.45 | 724 |
| RGB16 / 0 | 90.08 | 4.000 / 4.195 / 7.571 | 322.32 | 41 |
| RGB16 / 1 | 90.00 | 3.951 / 4.192 / 5.339 | 310.60 | 559 |
| RGB16 / 2 | 90.00 | 7.316 / 7.653 / 8.359 | 317.16 | 726 |

The60-second Color256 budget2 stress holds90Hz, with CPU/frame p95/p99/max
**7.327/7.585/8.594ms** and317.05ms CPU/s. Multiple callbacks can share a render
frame. Busy skips are bounded admission behavior, not GPU utilization or corrupt
application. Lowering the retry limit rejects these otherwise recoverable fixtures
and is not adopted. The measured-window accepted processing counts are356/354
for Color256/RGB16 budget2 screening and1,774 for the60-second Color256 stress;
they are fixed-frame repetitions, not unique updates.

## Polling experiment: retain the default

Three paired every-render versus30Hz polling comparisons reduce CPU/s by only
**0.26–0.43%** in the Color2565KB workload. Both already render below30Hz because
encoding dominates. The60-second polling confirmation remains near21Hz. Phase
and jitter comparisons show no material improvement. Four warmup publications
also go unobserved in the polling phase/jitter and held-source traces; zero
measured-cohort losses do not establish safe startup or RPC delivery.

During the1.5-second held-source schedule, polling reduces duplicate skips134→46,
but CPU/s changes865.68→858.66 (0.81%) while source counts differ222 versus219.
Both source-age p95 values remain about1.356s, reflecting the deliberate hold.
This freezes the local source; it does not simulate continued hidden sender
updates behind a held CDN image.

No Runtime polling feature or default changes. There is no RPC workload or actual
video frame-arrival signal, so these results cannot establish RPC safety or a
universal polling rate. A fixed cap is also unsafe as an encoding-cost fix for an
indivisible5KB field: earlier BudgetDelivery evidence shows that a smaller cap
prevents the field from being sent at all.

## Next measured fix and limits

A small Color256 experiment can cache256 palette entries and use a guarded
direct block-texture write with the existing full-resolution fallback. It first
needs all256 colors, boundary layouts, calibration/end markers, full-resolution
output and stale-tail equivalence in the SDK C# proxy and compiled codec VM.
Fresh-process A/B encoding and sustained delivery evidence must establish any
gain. No GPU encoder architecture, protocol, retries, fragmentation or FEC changes
are justified by this baseline.

This uses one RawBytes field and a lightweight receiver. It is not many scalar
bindings, a populated world, live VRChat FPS, Quest, compressed video or CDN/OBS
service behavior. Achieved Editor render Hz is not real-client FPS. Compilation
and shader preparation are outside steady timing. Logical request association is
conservative; ambiguous occupied-slot replacements have unknown identities.
Source age and delivery use received IDs independently.

Evidence: `F:/Unity/TSMP/Validation-Results/next-improvements-20261101/sustained/vrc-final`.
Immutable plan-v2 SHA256:
`ff9a222a292c066cb95db34935485ad8bf47b43893e1f14282a0f8765d2b433a`.
Independent audit is at the evidence root's parent: `sustained-independent-audit.json`.
Initial harness compile/import/pump failures are preserved as `vrc-pilot` through
`vrc-pilot4`; `vrc-pilot5` strictly passes. No failed run or outlier was removed.
