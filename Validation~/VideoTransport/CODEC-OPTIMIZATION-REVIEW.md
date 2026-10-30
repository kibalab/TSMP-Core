# Performance improvement review, 2026-10-30

The transport report supports useful efficiency improvements, but does not itself measure current codec CPU/GPU time. The strongest new implementation candidate is Native color-codec raster-buffer reuse. The strongest saved-data efficiency choice is using an existing codec and native resolution that fit the actual payload. Codec robustness, useful delivered data, allocation churn and frame-processing time must be evaluated separately.

This is a read-only implementation review and saved-evidence reanalysis. No new encoder, Unity, OBS, GPU timing or live-client experiment was run. Runtime, shaders and codec repositories were not changed. Documentation remains on `codex/tsmp-reliability`, without a merge or push.

## Priorities

| Priority | Candidate | Evidence and expected scope | Decision |
| --- | --- | --- | --- |
| 1 | Reuse Native Color256/RGB16/RGB20 raster arrays | Concrete per-frame full-image allocation remains; existing Core buffered API is sufficient | Small implementation, preserve pixels and wire format; measure Native allocation/time before and after |
| 1 | Choose the required codec capacity and native source resolution | Existing Luma4/Color256 finite profiles preserve the same common payloads at different packet bitrates | Validate a supported profile before spending bandwidth or pixels |
| 2 | Profile current color-codec CPU, GPU and polling costs | Latest report establishes accuracy, not these timings | Obtain a current production baseline before shader or scheduler changes |
| 2 | Eliminate duplicate local-refinement candidates | Current clamp-based neighborhood loops can evaluate identical boundary candidates | Byte-equivalent shader candidate; accept only if GPU timing improves |
| 2 | Screen existing sample/radius/channel-bit settings | Existing options can trade density and search cost for robustness | Experiment first; neither less nor more sampling guarantees better recovery |
| 3 | Reduce redundant receiver polling | Snapshot/predicted payload work can precede trusted duplicate detection | Optional experiment using existing manual DecodeNow; preserve IDs, age and RPC semantics |
| Defer | Redesign RGB20 or add a new codec/FEC protocol | RGB20 failed the ordinary H.264 path, while existing alternatives fit the tested load | Establish an unmet capacity/transport requirement first |

## 1. Native color raster allocation is a remaining gap

Core `Runtime/Encoder/TSMPEncoder.cs:310` already calls `codec.TryWriteFrameBuffered(..., ref _rasterPixels, ...)`. However, `Runtime/Codecs/Common/Scripts/TSMPCodec.cs:384` defaults to delegating to the old `TryWriteFrame`. The installed Color256, RGB16 and RGB20 implementations override only the old method and call `FrameRaster.CreateClearedPixels(width, height)` for every frame:

- Color256 `Runtime/Scripts/TSMPCodecColor256.cs:41`
- RGB16 `Runtime/Scripts/TSMPCodecRGB16.cs:55`
- RGB20 `Runtime/Scripts/TSMPCodecRGB20.cs:41`

Luma4 already has its buffered/GPU paths. Udon color writers already reuse their base/symbol arrays; this newly identified full-image allocation is a **Native/Editor sender issue**, not evidence of the same allocation in VRChat Udon.

Array-body arithmetic, excluding object headers and other allocations:

| Native raster | Bytes per new Color32 array | Array data generated at 30 fps |
| --- | ---: | ---: |
| 1280x720 | 3,686,400 B / 3.516 MiB | 105.469 MiB/s |
| 2560x1440 | 14,745,600 B / 14.063 MiB | 421.875 MiB/s |

Implement the existing optional buffered override in each color codec, using the Encoder-owned `ref Color32[]`, reallocating only on size changes. Retain the existing validation, complete clearing, calibration, payload/end-marker writing, texture upload and public fallback. Do not use a process-wide mutable array or introduce dirty-region tracking for the initial change.

This avoids the identified repeated image-array allocation. Full-image clearing, SetPixels32/Apply, GPU upload and rendering still remain. These figures are code-derived allocation volume, not measured GC time, retained leakage or a predicted FPS gain.

Acceptance must exercise **the new production call path**: Native `TSMPEncoder.EncodeNow`, or an explicit buffered-API test. The current `CodecTransportFixtureValidation` directly calls `TryWriteFrame`, so its unchanged PASS would not demonstrate that a new buffered override ran. Compare complete pixels and protected payloads against the original for growth/shrink (including 5,375 to 32 B), resolution/codec switches, independent concurrent Encoders and lifecycle cleanup. Measure warmed Player allocation and CPU mean/p95 before and after, with resources prepared outside timing.

## 2. Existing profiles can reduce bandwidth and pixel storage

The following reanalysis uses the same 32/2,495/5,375 B workloads, structured/random patterns and 600 source frames per finite profile. Packet bitrate excludes network overhead. It is not a minimum-required-rate or live-service certification.

| Saved comparison, all 600/600 exact | Mean video-packet bitrate | Observed difference |
| --- | ---: | --- |
| Luma4 native 720p NVENC nominal 3.5M | 3.435262 Mbps | About 45.66% below the tested Color256 720p 6M profile |
| Color256 native 720p NVENC nominal 6M | 6.322362 Mbps | About 35.56% below the tested Color256 1440p 10M Full profile |
| Color256 native 1440p NVENC nominal 10M Full | 9.810492 Mbps | Comparison profile |

The largest common payload, 5,375 B, fits Luma4's calculated 720p capacity of 6,720 B. Consequently the tested application load does not, by itself, require a denser color codec or 1440p. Keep existing Luma4 as the first candidate; use Color256 when the actual required payload exceeds the chosen Luma4 profile or another measured requirement justifies it.

Native 720p has one quarter the pixels of 1440p. Two matching RGBA8 receiver snapshot textures contain 7.031 MiB of texels at 720p versus 28.125 MiB at 1440p. This is snapshot texel storage only; it does not establish a 75% reduction in total VRAM, CPU/GPU time or latency.

Generate the sender raster and header at 720p and preserve native-size OBS composition/output. **Do not simply downscale a 1440p TSMP texture in OBS**: that changes block/sample/header geometry and is outside these passing measurements. Also retain burst measurements: the nominal Color256 6M profile reached a 9.274 Mbps one-second packet window.

## 3. Optimize useful data, not just frame counts

Color256 / 720p x264 3.5M illustrates why a frame-success percentage is incomplete:

| Payload size in the interleaved test | Exact frames |
| --- | ---: |
| 32 B | 194/200 |
| 2,495 B | 163/200 |
| 5,375 B | 5/200 |

Although 362/600 frames were exact, those frames preserve only **439,768 / 1,580,400 payload bytes, or 27.83%**. RGB16 under the same encoder profile preserves 22.78% of payload bytes. This does not certify a separate small-payload profile: sizes were interleaved, and compression depends on the sequence.

For the tested successful Color256 profiles, the workload represents 632,160 intact payload bits per media second at both resolutions. That is a finite file-retention metric, not measured real-time applied throughput. Existing OnChange, minimum intervals, refresh scheduling, avatar root thresholds and quantization can be evaluated to reduce redundant application data before changing the protocol. Do not remove periodic recovery, RPC repeats or pose precision without measuring their behavior.

Future comparisons should report CPU encode/CRC/callback mean and p95, GPU draw time, readback latency, frame GC bytes, retained textures, unique intact source IDs, duplicate observations, source age, payload bytes preserved and mean/peak packet bitrate. A fast rejection path must never rank a codec with zero delivered payloads as performant.

The nine bad RGB16 OBS payload observations are not nine distinct permanently missing source updates. Of header-observed IDs, Full random never recovered ID 225; Partial random never recovered IDs 183/225; structured Full eventually recovered all its observed IDs. ID 300 was separately unobserved in some files. This is source-byte evidence, not RPC or event-delivery certification.

## 4. Shader improvements need targeted timing

Color256 Robust Refine evaluates a 3x3x3 neighborhood and clamps each channel to its legal range (`TSMPDecodeColor256RobustRefineBytes.shader:67`). RGB16 radius two evaluates 5x5x5 candidates, likewise clamped (`TSMPDecodeRgb16RefineBytes.shader:107`). At palette boundaries these loops can evaluate the same symbol repeatedly.

Enumerating only each axis's valid interval can remove identical candidates while retaining the original first-visit order and tie behavior. This is a plausible pixel/byte-preserving optimization, not an accuracy fix. Dynamic-loop bounds and branching may offset the saved evaluations on a GPU, so accept it only after full-byte parity and timed production-size tests. Counts such as 27 or 125 candidates are not milliseconds or a speedup percentage.

Adaptive calibration LUTs already exist for Luma4, RGB20 and Color256 Robust Refine. RGB16 Refine was deliberately excluded after an earlier LUT candidate became slower. See `../GpuCalibration/RESULTS.md`; its four-byte microbenchmark is not current 5,375-byte OBS timing. Adding another LUT or cross-frame calibration cache is not a fresh confirmed improvement.

Screen existing settings rather than introducing a new robust codec: sample sizes two/four, RGB16 refinement radius one/two and a lower-density RGB16 channel layout such as 3/3/2 are reasonable **unvalidated candidates**. Lower density reduces capacity and needs more symbols for the same bytes, potentially increasing GPU work; reducing refinement can reduce recovery. The current fixtures carry sample4 and codec options in their protected headers, and the receiver follows those fields. Changing only a decoder Inspector/prefab value may not change the tested mode. A valid A/B must stamp new headers/options/CRCs and regenerate/re-encode the fixtures, or explicitly label a forced diagnostic decoder as outside normal operation.

RGB20 places 128 red and green levels inside 24..232, about 1.64 byte levels apart on average. That is consistent with sensitivity to small conversion errors, but individual errors have not been decomposed into matrix/subsampling/rounding causes. Since all tested H.264 paths failed, including QP0, faster RGB20 decoding is a low priority for an ordinary OBS path. Restrict its supported transport or establish a concrete redesign requirement; existing RGB16/Color256 already cover lower-density alternatives.

## 5. Receiver cadence is an experiment, not a default 30-Hz cap

`TSMPDecoder.Update` currently invokes DecodeNow per render frame. A full source snapshot is captured before a trusted duplicate ID is available; predicted decoding can also perform payload GPU work before the duplicate decision. A high render rate with slower video can therefore repeat work on held source frames.

Existing `applyEveryFrame=false` plus manual `DecodeNow` permits a controlled polling experiment without a new Core API. Test video/render phase offsets, jitter, delayed frames and retries. Do not set a universal 30-Hz default or promise a threefold saving from a 90/30 ratio: missing a short-lived frame can reduce useful updates or lose an event. Compare unique intact IDs, source age, backlog and exactly-once RPC behavior alongside cost.

Keep CRC before parsing/application, trusted StreamId/FrameIndex deduplication, independent snapshots and two-slot ownership. Sharing pending snapshots, accepting corrupt bytes, or skipping frames because their payload lengths match would change correctness rather than improve the current implementation safely.

## Work units and decision gates

1. Preserve a current production baseline for Luma4/Color256 (and RGB16 where useful), at the same actual payloads and target rates. Current transport-harness wall time includes file I/O and cannot substitute for it.
2. Implement the three Native color buffered overrides as a separate work unit. Require pixel/byte equivalence and a measured allocation reduction; report Native and Udon scope separately.
3. Validate native 720p source profiles against actual required payload sizes and burst limits. Actual OBS 720p and service ingestion/playback still need their own tests.
4. Profile large-payload GPU refinement; test candidate deduplication or existing settings only if that work is material. Reject a shader change that saves instructions but increases GPU time or corrupts additional frames.
5. Experiment with polling only if repeated-source processing is a measured cost. Preserve delivery, recovery and event semantics as acceptance criteria.

CRC slicing-by-eight, bulk copies, slot-owned raw readback, prediction/combined readback, matching snapshot formats, binding caches and the GPU Luma4 writer are already implemented. The latest 4,121-byte Udon CRC report records about 2.837 ms Encoder and 2.787 ms callback means after optimization in its Luma4 fixture. Earlier pre-payload-CRC resource-profile numbers are historical and must not be presented as the current baseline or as color-codec timing.

Evidence: the linked [transport report](CODEC-TRANSPORT-RESULTS.md), [previous Luma4 matrix](OBS-TRANSPORT-RESULTS.md), [CRC results](../CrcPerformance/RESULTS.md), [implemented resource optimizations](../OptimizationFeasibility/IMPLEMENTED.md), current source review, and saved-data reanalysis at `F:/Unity/TSMP/Validation-Results/codec-transport-20261030/performance-review/saved-data-analysis.json`. Live VRChat, VRCDN and new performance measurements remain outside this review.
