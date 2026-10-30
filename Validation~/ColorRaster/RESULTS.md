# Color codec performance improvements, 2026-10-30

Native Color256, RGB16 and RGB20 now reuse Encoder-owned raster arrays. The measured 5,375-byte Native workloads reduced average EncodeNow time by **5.14–13.11%**, and measured whole-Player frame allocation fell from approximately 3.687 MB / 14.746 MB at 720p / 1440p to **421–479 B** across the tested conditions. RGB16 fixed and variable local-refinement shaders also avoid duplicate boundary candidates; their default radius-two GPU pass times improved **2.24–8.52%** in the tested cases. A Color256 shader candidate became slower and was rejected.

The implementation preserves complete source pixels and decoder bytes. CRC checks, packet format, palettes, codec IDs, sample defaults, snapshot ownership and scheduling semantics were retained. It does not correct the compression failures in the preceding [transport report](../VideoTransport/CODEC-TRANSPORT-RESULTS.md).

All changes are local commits on `codex/tsmp-reliability` in the Core and three codec repositories. Main refs were preserved; no merge, push, package-version bump or release was performed. Core runtime/package files were not changed.

## Implemented work units

| Repository | Commit | Change |
| --- | --- | --- |
| Color256 | `6bde357` | Native TryWriteFrameBuffered using the existing Encoder-owned array |
| RGB16 | `6402728` | Same Native buffer reuse |
| RGB20 | `f176b15` | Same Native buffer reuse |
| RGB16 | `916fbc4` | Fixed/variable Refine: enumerate legal neighbor offsets once instead of repeating clamped boundary symbols |

The old public TryWriteFrame API delegates through a local null buffer and remains operational. The buffered path validates arguments before mutation, clears all pixels, then retains the same base/header/calibration/payload/end-marker writing and texture upload. Arrays are owned by each Encoder, reused at the same dimensions, resized when needed and released by existing lifecycle cleanup. Udon color writers already reused their symbol/base buffers; this Native allocation fix is not an Udon allocation reduction.

The shader change retains the unique candidate set, first-visit order, strict-less-than tie rule and distance function. Only RGB16's two Refine shaders were adopted. The measured Color256 variant is preserved under validation-only frozen sources and is absent from its runtime package.

## Native Player measurements

Unity 2022.3.22f1, Windows x64 Development Mono Player, managed stripping disabled, D3D11/Gamma, i9-13900K, RTX 4090. The baseline and optimized phases ran sequentially without another benchmark process. Each has three codecs x two native dimensions x three total payload lengths: **18 conditions / 3,240 measured samples**, with 20 warm calls then 180 samples per condition at a 30-Hz target.

The real Native TSMPEncoder.EncodeNow builds a protected NetworkFrame, renders the selected color raster and publishes its output. One always-sent RawBytes value is 25 bytes shorter than the stated payload. Stopwatches wrap EncodeNow, including its CPU/upload/driver submission work; source filling and assertions are outside. GPU completion, receiver CPU, capture, OBS, network and world rendering are excluded.

Representative largest common payload, **5,375 B**:

| Codec | Native dimensions | Mean before -> after | Mean reduction | p95 before -> after |
| --- | --- | ---: | ---: | ---: |
| Color256 | 1280x720 | 3.120 -> 2.853 ms | 8.53% | 3.777 -> 3.129 ms |
| Color256 | 2560x1440 | 9.347 -> 8.122 ms | 13.11% | 11.500 -> 8.925 ms |
| RGB16 | 1280x720 | 2.627 -> 2.333 ms | 11.20% | 3.143 -> 2.649 ms |
| RGB16 | 2560x1440 | 8.561 -> 8.120 ms | 5.14% | 10.131 -> 9.068 ms |
| RGB20 | 1280x720 | 2.353 -> 2.112 ms | 10.23% | 2.894 -> 2.327 ms |
| RGB20 | 2560x1440 | 8.309 -> 7.644 ms | 8.01% | 10.231 -> 8.161 ms |

Across all 18 conditions, mean reduction ranges from **-1.63% to 17.16%**. RGB20 / 720p / 2,495 B is the one worse mean: 1.937 -> 1.969 ms, a 0.0315 ms / 1.63% increase; its p95 still improves. All eighteen p95 values improve **8.11–26.24%**. These are one sequential before/after comparison with finite samples, not a statistical-significance claim or a guarantee that every workload gets faster. Both phases remain near their imposed 30 calls/s ceiling, so they do not measure maximum FPS.

## Allocation and retained storage

The scoped GC.GetAllocatedBytesForCurrentThread counter returned zero for an 8,192-byte allocation control and is therefore **unavailable (-1)** in both phases. A separately validated whole-Player-frame allocation counter observed the 16,384-byte control correctly and was used for frame allocation figures.

| Native raster | Before: measured Player frame allocation | After, across tested conditions | Reused array body retained per enabled Encoder |
| --- | ---: | ---: | ---: |
| 720p | Approximately 3.687 MB/frame | 421–479 B/frame overall | 3.516 MiB |
| 1440p | Approximately 14.746 MB/frame | 421–479 B/frame overall | 14.063 MiB |

Remaining frame allocations include validation/reflection/framework overhead and cannot be assigned to Encoder alone. The result is not zero allocation. GC.CollectionCount(0) deltas were 180 before versus zero after in each 180-sample window; this is a Player-process counter. Reusing the array removes allocation churn while retaining its body until resize or lifecycle cleanup. Clearing, texture CPU storage, uploads and output textures remain, so total process memory or GPU traffic was not reduced by those same percentages.

## GPU refinement experiment and selection

Original/candidate shadow shaders used distinct names and cloned codec/material ownership. Color256 includes its existing current-source LUT preparation in every timed draw; RGB16 Refine retains its existing no-LUT policy. Byte-output target geometry is 2048x1 RGBA8, matching the transport experiment. All timing cases use sample4, native 720p/1440p structured/random inputs and 32/2,495/5,375 payload bytes.

For each case, ten warm frames precede **30 valid paired GPU timestamp samples**. Each marker contains 32 decode draws (and preparation where applicable), with AB/BA order alternating. An explicit readback synchronizes submissions outside the timed markers. Source/upload/snapshot/header/CRC/CPU parsing/world rendering are excluded. GPU timestamps, rather than submission wall time, determine the results.

| Shader path | Radius | Mean GPU time reduction across cases | Decision |
| --- | ---: | ---: | --- |
| Color256 Robust Refine | 1 | **-0.79 to -0.41%** | Rejected; all 12 mean and p95 results are worse |
| RGB16 fixed 4/4/4 Refine | 1 | 1.38–3.19% | Adopted with the same shader change |
| RGB16 fixed 4/4/4 Refine | **2, default** | **2.24–5.12%** | Adopted |
| RGB16 fixed 4/4/4 Refine | 3 | 10.62–16.23% | Adopted with the same shader change |
| RGB16 variable 5/6/4 Refine | 1 | 1.69–5.55% | Adopted with the same shader change |
| RGB16 variable 5/6/4 Refine | 2 | 2.99–8.52% | Adopted |
| RGB16 variable 5/6/4 Refine | 3 | 6.97–17.39% | Adopted with the same shader change |

RGB16's 72 timing cases improve both mean and p95. At radius two, fixed-path savings are approximately **0.99–2.12 microseconds per draw**, variable-path savings 1.93–7.45 microseconds. The larger radius-three percentages must not be presented as default-setting gains. These are isolated GPU pass results on one desktop GPU, not whole-decoder or VRChat FPS gains; Native and GPU percentages are not additive.

The experiment has **1,572 A/B full-output byte comparisons with zero differences**, including exact/noisy sources, flips, samples two/four, radii one/two/three, variable bit layouts, padding and known lossy video. Of these, 810 have both outputs differing from the original payload due to diagnostic overrides/noise/known corruption. The claim is preserved decode behavior, not 1,572 successful original transmissions. Headers were not rewritten for diagnostic overrides; those cases do not certify new transport settings.

## Correctness and production integration

- Baseline and optimized Native Players each passed 72 deterministic raster controls, 90 actual production pixel/check rows and 27 strict production GPU payload/CRC checks. These are separate coverage counts, not 90 GPU readbacks. Controls cover shrinking 5,375 -> 32 B, empty/tail raster payloads, RGB16 alternate 3/3/2, 720p/1440p/720p resizing, independent buffers and Encoder instances, codec switches and restart cleanup. The optimized result matches independently saved original pixels/header/payload bins.
- After copying the adopted runtime shaders, the actual package Snapshot suite passed in **Gamma and Linear**, each with 216 changing-source cases across four codecs/all variants, sample1/4, both orientations and ARGB32/Float sources. Payload CRC/state/RPC rejection and intact retry, both slot orders, capacity/shrink, lifecycle cancellation, frame windows and high-precision capture also passed.
- Independent audits recomputed all Native samples, all 84 GPU timing cases / 2,520 post-warm pairs, source hashes, frame counters and A/B parity. They verify the adopted runtime shader bodies match the tested candidates while preserving original shader names/includes. Color256's rejected candidate was not installed.
- Final source parity was checked after the runs: 189 selected Core/codec files in the Native project match the source repositories. Snapshot PASS texts do not embed those hashes; the post-run audit binds them to the recorded adoption/copy hashes and execution order.

No actual VRChat client, new compiled-Udon benchmark, OBS/CDN run, Quest/mobile GPU, Vulkan/Metal or IL2CPP run was performed. Auxiliary snapshot-copy timing was unavailable at two sizes; it is not the GPU refinement measurement. The compression failures reported previously remain failures: these changes improve processing costs while preserving recovered bytes and CRC rejection behavior.

## Validation history and evidence

Evidence root: `F:/Unity/TSMP/Validation-Results/codec-performance-20261030`.

The initial optimized Player stopped before profiling because the harness compared RGBA32 raw bytes with a PNG reloaded as ARGB32. All 72 baseline PNGs independently reconstruct their original raw hashes. The repaired harness requires both the original baseline raw-pixel hash and normalized logical RGBA pixels, records actual/loaded format and hashes, and confirms all 72 comparisons. This matches Unity's documented [LoadImage format behavior](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ImageConversion.LoadImage.html). The failed `optimized` directory is preserved; `optimized-final` is the valid comparison. No codec runtime change was made between those two attempts. Independent auditing verifies Profile()/Fill() were unchanged by the correctness-only harness repair.

Gamma Snapshot passed on its first run. A surrounding ad-hoc PowerShell guard incorrectly treated an unset LASTEXITCODE after the successful script as failure and prevented Linear dispatch; Linear was then run separately. No Gamma test was rerun to alter its result.

- `baseline`, `optimized-final`: Player builds/reports/logs, source manifests, original controls, CPU samples, counter calibration, diagnostics and coverage. `optimized` retains the first harness failure.
- `performance-audit.json`, `native-comparison.json`, `baseline-png-reference-audit.json`: independently recalculated CPU/allocation/reference evidence.
- `refine-experiment`, `refine-radii`, `refine-performance-audit.json`: frozen-source A/B, GPU marker observations, all mean/p95 calculations and adoption decisions.
- `snapshot-gamma`, `snapshot-linear`: actual runtime-package integration results.
- `environment/session.json`, `optimized-copy.json`, `rgb16-shader-adoption.json`, `shader-copy.json`, `buffer-commits.json`, `shader-commit.json`: baselines, exact changed sources, branch/commit identities and adoption provenance.

Use the [Native harness instructions](README.md) and [refinement experiment instructions](../RefinePerformance/README.md) to reproduce in dedicated projects. The earlier [feasibility review](../VideoTransport/CODEC-OPTIMIZATION-REVIEW.md) distinguishes these implemented changes from further source-resolution/profile/cadence candidates requiring their own validation.
