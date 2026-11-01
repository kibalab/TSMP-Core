# Resource optimization feasibility

## Float32 writer correction and minimum SDK, 2026-11-01

The current product replaces only the Udon Float32 writer's arithmetic
reconstruction with SingleToInt32Bits and four masked byte writes. Native
writing and the reader remain unchanged. The Runtime edit is 7 added/46 removed
lines, with no new fields, buffers, options or packet types. Udon retains +0
normalization; Native retains signed zero. The reader still maps either zero
to +0 and every exponent-255 input to signed MaxValue. Signaling NaNs may
quiet before the VM; the test records requested, host and heap bits separately.
The [protocol](../../Packages/com.kibalab.tsmp.core/Documentation~/TSMP_PROTOCOL.md)
now states these existing policies explicitly.

The new product contract exercises 3,584 scalar patterns: every finite exponent,
both signs, five mantissa boundaries, deterministic random samples and selected
zeros/infinities/NaNs. It also checks unaligned offsets, sentinels, returned
cursors, Vector2/3, Quaternion and Float32Array input ownership/storage reuse.

| Product/environment | Writer VM errors | Successful writes with wrong bits | Reader differences |
| --- | ---: | ---: | ---: |
| Original Udon, current SDK | 450 | 482 | 0 |
| Corrected Native | 0 | 0 | 0 |
| Corrected Udon, SDK 3.10.4-beta.2 | 0 | 0 | 0 |
| Corrected Udon, minimum SDK 3.9.0, with Timeline compatibility fix | 0 | 0 | 0 |

The original errors include 436 finite values, two infinities and 12 NaNs;
all 482 successful wrong writes are finite. These selected inputs do not
estimate ordinary traffic error rates. Five signaling NaNs quiet at the host;
the heap makes no further changes. An independent audit recomputes all 14,336
scalar rows. Wrapper/sentinel/array assertions are fixed-fixture PASS receipts,
not independently recomputed exported byte traces.

The initial SDK 3.9.0 attempt fails before the Float32 probe because the existing
Timeline component uses unexposed PlayableDirector.isActiveAndEnabled. That
failure and the initial SDK data-locator import error remain in the evidence.
A separate one-condition Runtime correction checks enabled and
gameObject.activeInHierarchy. It passes Native 20-case Timeline regression and
actual Timeline VM checks on both SDKs, including disabled components, inactive
parents, clearing captured data, rejecting receive and recovering on activation.
See [Timeline validation](../Timeline/README.md). No original FAIL is relabeled.

Six fresh SDK processes run the existing full Encoder/protected Decoder matrix
in AB/BA/AB order: 12 workloads, 8 warm and 32 measured opportunities per case.
All 2,880 Encode calls, 1,944 protected packet/header pairs, 99,360 floats and
972 corresponding payload pairs pass. Binary is the only source exchanged;
the existing Timeline source is constant during this earlier A/B. Encoder
bytecode changes 138,368 to 137,576 B (-792 B); the other four loaded programs,
391 other sources and 23 project configurations remain identical.

| 128-field Encode workload | Original mean | Corrected mean | Change |
| --- | ---: | ---: | ---: |
| 1 target, changing | 9.5577 ms | 9.4918 ms | -0.69% |
| 128 targets, changing | 12.8010 ms | 12.2618 ms | -4.21% |
| 1 target, unchanged | 8.4565 ms | 8.2697 ms | -2.21% |
| 128 targets, unchanged | 9.3320 ms | 9.5063 ms | +1.87% |

Stable whole-Encoder speed improvement is **not established**. Per-pair results
vary and all upper-tail reversals are retained below. Each pooled case has
96 samples per variant, so nearest-rank p99 is its maximum.

| Encode upper-tail reversal | Original p99/max | Corrected p99/max |
| --- | ---: | ---: |
| 32 fields, 1 target, changing | 1.9943 ms | 6.4337 ms |
| 32 fields, 1 target, quarter changing | 1.9319 ms | 2.1949 ms |
| 128 fields, 1 target, changing | 10.5286 ms | 15.5909 ms |
| 128 fields, 128 targets, unchanged | 10.6092 ms | 12.0459 ms |

Unchanged Decoder bytecode also shows timing variation. These data do not prove
the writer caused the spikes; they do not justify a whole-frame speed claim
either. This change is retained for demonstrated correctness, not a claimed
performance win. The previous helper-only figures below remain historical.

Final combined-product checks pass: Native GPU texture loopback (11 frames),
current SDK client compilation (16 programs), minimum SDK compilation
(13 Core/Luma4 programs), generated bindings in both and both Timeline VM runs.
The minimum project uses separately verified official cached Base/Worlds 3.9.0
archives; existing projects are not downgraded. This is automated embedded-SDK
evidence, with no live VRChat, FPS, VCC resolver or allocation-counter claim.

Evidence: `F:/Unity/TSMP/Validation-Results/float32-correctness-20261101`.
`contract-original-vm` retains the original product failure;
`contract-minimum390-vm` retains the initial minimum-SDK compile failure;
`contract-minimum390-compatible-vm` is its distinct successful follow-up.
Independent binding/contract audits retain all raw timing and scalar rows.

Reproduce on dedicated projects with their Editors closed and current product
sources installed. Use a fresh result directory for each command:

```powershell
./Validation~/OptimizationFeasibility/Run-Float32Contract.ps1 -Project $native -Results '<fresh>/native'
./Validation~/OptimizationFeasibility/Run-Float32Contract.ps1 -Project $vrc -Results '<fresh>/vm' -Vm
```

For minimum-SDK testing, pass a separately prepared SDK 3.9.0 project to the
second command. `PackageCompatibility/Run-Minimum.ps1` instead tests SDK-free
package combinations and does not establish minimum SDK compatibility.

## Float32 API and helper feasibility, 2026-11-01

Historical snapshot before the product correction above; all figures and
unchanged-product statements in this section refer to its frozen earlier sources.

Worlds SDK **3.10.4-beta.2** exposes SingleToInt32Bits and Int32BitsToSingle.
Actual client compilation and serialized-program VM calls confirm both APIs.
**Product Binary.cs is unchanged**, including all Native/VRC copies. Minimum
SDK compatibility and a product-level Encoder/Decoder A/B remain unverified.

The validation-only probe tests 114 selected IEEE754 edge vectors and times
128 ordinary finite values per event, 10 warm rounds then 80 measured rounds
in alternating forward/reverse order. Input/heap preparation, symbol/PC lookup
and output checks are outside Interpret timing. All 320 raw samples are retained.

| Helper / 128 values | Current mean | Bitcast prototype mean | Absolute saving |
| --- | ---: | ---: | ---: |
| Write | 0.274364 ms | 0.133459 ms | 0.140905 ms |
| Read | 0.247193 ms | 0.170749 ms | 0.076444 ms |

These are one-process helper costs, not whole-Encoder percentages or evidence
that Float32 conversion dominates the many-field workload. No allocation counter
is used and no allocation-free claim is made. The prototype uses no GetBytes
inside the VM; its existing arrays are reused.

The edge corpus also characterizes existing product behavior: **10 current
writer VM errors** (largest positive/negative subnormals, two infinities, six
NaNs) and **23 successful finite writes with different bits**. Examples include
127.999992 becoming 128 and maximum finite values becoming infinity. These
deliberately selected boundaries do not estimate an ordinary traffic error rate.
The current reader matches the prototype's existing zero/NaN/infinity policy
for all 114 vectors. Those policies must be explicit before product adoption.

The initial probe stopped after 55 vectors because it assumed every signaling
NaN payload survived the host boundary unchanged. That FAIL, source and log are
preserved. The corrected characterization records requested/host/heap/API bits,
requires exact finite/zero/infinity bits and preserves NaN classification while
recording payload changes. Two signaling NaNs are already quieted at the host;
the heap adds no further changes. The Runtime probe and client bytecode stay
identical. The completed run passes **25,295 assertions**; an independent audit
recounts all vectors, timings and unchanged product sources.

Evidence: `F:/Unity/TSMP/Validation-Results/optimization-implementation-20261101/`
`float32-api`, with `inventory`, `probe`, `probe-characterized` and
`independent-float-audit.json` records. The inventory's files are directly under
that root; `probe` is the retained initial failure.

To reproduce in a dedicated SDK project with its Editor closed, copy the two
validation sources, use a fresh result path and let the Play Mode probe exit:

```powershell
$destination = Join-Path $project 'Assets/Validation/Float32Api'
New-Item -ItemType Directory -Path (Join-Path $destination 'Editor'),(Join-Path $destination 'Runtime') -Force | Out-Null
Copy-Item -LiteralPath './Validation~/OptimizationFeasibility/Editor/Float32ApiValidation.cs' -Destination (Join-Path $destination 'Editor') -Force
Copy-Item -LiteralPath './Validation~/OptimizationFeasibility/Runtime/Float32ApiProbe.cs' -Destination (Join-Path $destination 'Runtime') -Force
$env:TSMP_VALIDATION_RESULT = '<fresh output directory>/result.txt'
& $unity -batchmode -force-d3d11 -projectPath $project -executeMethod Float32ApiValidation.RunProbe -logFile '<fresh output directory>/editor.log'
```

For node inventory only, use Float32ApiValidation.RunInventory and a separate
fresh result path. Node exposure alone does not establish VM execution or speed.

## Earlier feasibility investigation

The following is the original investigation. The five subsequent implementations and measured results are recorded in [IMPLEMENTED.md](./IMPLEMENTED.md).

This investigation follows [the resource profile](../ContinuousFrames/RESOURCE-PROFILE.md). It changes validation code only. No runtime optimization, protocol change, package release or revised end-to-end performance claim is included.

## Environment and evidence

- Runtime baseline: Core commit `92386b4`, package version `0.3.0-beta.2`.
- Unity 2022.3.22f1; Windows 11; Core i9-13900K; RTX 4090; Direct3D11.
- Worlds SDK 3.10.4-beta.2 with its bundled UdonSharp.
- Bulk-copy/readback tests: full client-target UdonSharp compilation, then actual bytecode execution in the SDK Editor VM. This is not execution in the VRChat client.
- Snapshot tests: native Unity Editor Play Mode, separately in Gamma and Linear. Graphics was enabled; these are not `-nographics` results.
- All tests use the installed real Core package through the isolated validation projects. No production source was substituted.

Compact output is in `Evidence/`. Raw logs are under `F:/Unity/TSMP/Validation-Results/optimization-feasibility/` on the measurement machine. `udon/` is the first copy/readback run; `udon-upload/` repeats it and also exercises raw texture upload. `gamma/` and `linear/` contain the snapshot comparisons. Each directory includes `Editor.log`.

## 1. Replace same-type Udon copy loops first

`Array.Copy(Array, int, Array, int, int)` compiled and ran for both `Color32[]` and `byte[]`. Tested byte copies include nonzero offsets, untouched destination sentinels, zero count, and overlapping copies in both directions. A correctly sized destination remains independently owned; bulk copy does not mean sharing buffers.

The following are warmed helper microbenchmarks, not full encoder/decoder timings. The loop uses the production helper; the candidate uses `Array.Copy` with already validated arguments. Ten warm calls precede eighty measured calls. Times include VM interpretation of the event, but not symbol lookup or test assertions. Input validation must remain in production code.

| Workload | Current loop mean | Bulk copy mean |
| --- | ---: | ---: |
| 640x360 symbol image, 3,600 colors | 1.048 ms | 0.00121 ms |
| 720p symbol image, 14,400 colors | 4.391 ms | 0.00431 ms |
| 1080p symbol image, 32,400 colors | 9.908 ms | 0.00933 ms |
| 4K symbol image, 129,600 colors | 36.751 ms | 0.03747 ms |
| 32 bytes | 0.01441 ms | 0.000225 ms |
| 4,096 bytes | 1.473 ms | 0.000496 ms |
| 65,535 bytes | 24.916 ms | 0.00531 ms |

The earlier repeat measured 4.421/0.00433 ms for the 720p color case. CPU frequency and cache effects account for run-to-run differences; sub-microsecond measurements approach timer/dispatch granularity. The conclusion is the removal of per-element VM work, not a universal speedup ratio for TSMP.

Recommended first changes:

- `EncoderUdonTextureRuntime.CopyPixelBuffer`: retain null handling and the minimum source/destination length; bulk-copy the whole base image. This preserves clearing of the old payload and codec-switch residue without introducing a dirty-region tracker.
- `NetworkValueWriter.WriteRawBytes`: retain bounds/error handling and null-as-zero-length semantics, then copy once. A null value must not be passed to `Array.Copy`, even with a zero count.
- `NetworkValueReader.CopyRawBytes`: keep one correctly sized receiver-owned cache per binding and bulk-copy into it. Do not return a shared readback array.
- Do not mechanically replace bool/numeric/string serialization loops: they perform wire-format conversions, not same-type copies.

Acceptance: invalid ranges must still fail before mutation, empty arrays and shrinking/growing values must retain existing behavior, receiver mutation must not alter another binding or an in-flight slot, and byte-array heap types must remain correct in actual client tests.

## 2. Read GPU bytes directly

The probe successfully compiled and executed `VRCAsyncGPUReadbackRequest.TryGetData(byte[])`. The same completed request was also read as `Color32[]`; every RGBA channel matched the byte output.

| Logical payload | Readback texture storage | Checked |
| --- | ---: | --- |
| 32 bytes | 1,024 bytes | Channel order, 56-byte header offset, payload extraction |
| 4,096 bytes | 5,120 bytes | Same, including unused row padding |
| 65,535 bytes | 66,560 bytes | Same, including a partial final pixel |

This supports replacing the current `Color32[] -> byte[]` per-pixel conversion with slot-owned raw byte arrays and bulk extraction. The previous profile measured that conversion at about 1.36 ms for 4 KiB. It also measured the subsequent receiver-array loop at about 1.53 ms; the second copy should remain for ownership but become a bulk operation.

Implementation requirements:

- Size each readback buffer for the actual requested rectangle/texture, not just `PayloadSize`. Exclude header prefix and row padding when parsing or applying values.
- Keep two independent buffers and FIFO completion order. Header-only, combined-output, fallback and external-codec paths must use the correct offsets.
- Keep CRC, length, stale callback, disable/re-enable and per-binding isolation checks.
- The native path can use `GetData<byte>()` plus native-array copying; it must not retain a request-owned view beyond its lifetime.
- Test empty payloads, byte counts modulo four, resize, prediction invalidation and codecs without combined-output support.

This probe establishes API availability and byte correctness, not a measured reduction in the complete callback. Re-run the continuous-frame benchmark after integration before claiming a new frame budget.

The official [VRChat readback documentation](https://creators.vrchat.com/worlds/udon/vrc-graphics/asyncgpureadback/) describes the callback and `TryGetData` wrapper. The byte-array overload's applicability here was established by the local compile/runtime test, not inferred from its Color32 example.

## 3. Remove native encoder allocation churn

The existing native Luma4 path allocates `Color32[width * height]` in `FrameRaster.CreateClearedPixels` on every frame. At 720p/60 this image alone generates about 211 MiB/s of allocations, and at 4K/30 about 949 MiB/s. This is allocation churn, not proven retained-memory leakage.

First reuse a raster buffer owned by the encoding instance, resizing only when dimensions change. Keep the existing public codec API operational, and add any buffer-taking helper internally/optionally rather than requiring every plugin to implement a new contract. Do not use a process-wide mutable static buffer shared by encoders.

Reusing memory removes the dominant allocation but does not remove the full-resolution fill, raster work or texture upload. A second step is an optional block-symbol path using the existing expansion material: block size 8 reduces the CPU image from `width * height` to approximately `width * height / 64`. Final RenderTexture dimensions and wire format remain unchanged. Custom codecs need an explicit supported path or the current fallback; Core must not recognize their concrete classes.

Acceptance: warm steady-state image allocations disappear, output pixels/decoded bytes remain correct in both color spaces, resolution/block-size/codec switches leave no old blocks, and multiple encoders remain isolated. Measure Player frame GC counters because scoped Mono allocation counters were unsupported in the previous profile.

## 4. Reduce snapshot memory without quantization regressions

Blindly changing linear `ARGBFloat` to `ARGB32` or `ARGBHalf` is unsafe. The new probe compares shader-sampled values after capture against the current Float32-linear baseline, using all 256 byte levels and separate negative/HDR Float16/Float32 inputs. It uses point-filtered Texture2D inputs and same-size blits; it is not a noisy-video or all-codec decode test.

Selected results:

| Project/input/candidate | Result versus current capture |
| --- | --- |
| Gamma / RGBA32 / ARGB32 Linear | Exact within 1e-7 |
| Linear / sRGB RGBA32 / ARGB32 sRGB | Exact within 1e-7 |
| Linear / linear RGBA32 / ARGB32 Linear | Exact within 1e-7 |
| Linear / sRGB RGBA32 / ARGB32 Linear | 3,048 changed channels; max error 0.002136 |
| Linear / linear RGBA32 / ARGB32 sRGB | 3,048 changed channels; max error 0.007935 |
| Gamma / RGBA32 / ARGBHalf Linear | 3,036 changed channels; max error 0.000486 |
| RGBAHalf / ARGBHalf Linear | Exact within 1e-7 for the tested half-float inputs |
| RGBAFloat / ARGBHalf Linear | 3,062 changed channels; max error 0.000976 |
| HDR RGBAFloat / ARGB32 Linear | Clamping/quantization; max error 0.242751 |

Differences are sample-value differences, not demonstrated packet failures. Exact preservation in these cases is necessary evidence but does not establish universal codec correctness.

The appropriate design is conservative automatic format selection, not another required user setting:

- For a recognized 8-bit input, preserve its encoding and color-space interpretation in an 8-bit snapshot, after validating the actual source type and graphics backend.
- For known half-float input, an exact half-float path may be possible; retain Float32 for Float32 and unknown/high-precision inputs.
- Recreate storage when format or sRGB interpretation changes, even if dimensions stay the same. Preserve filtering, wrap mode, slot ownership and in-flight lifecycle.
- Validate RenderTexture and video-backed inputs, source swaps, Gamma/Linear, calibration, noisy values near decision boundaries, all shipped codecs and a high-precision custom-codec fixture. Udon access to format-detection APIs needs its own compile test.

Potential storage reduction for **eligible 8-bit inputs**, two snapshots:

| Resolution | Current Float32 | Matching 8-bit candidate |
| --- | ---: | ---: |
| 720p | 28.13 MiB | 7.03 MiB |
| 1080p | 63.28 MiB | 15.82 MiB |
| 4K | 253.13 MiB | 63.28 MiB |

This is a 75% reduction in snapshot texel storage, not total process RAM/VRAM. It also reduces nominal copy traffic, but GPU-time savings have not been benchmarked for the candidate. Two slots are retained.

The current one-slot option still alternates and retains both storage slots. Fixing that could help an explicit low-memory mode, but it is not the preferred default: the preceding delivery test showed frame loss when overlap was disabled. Do not trade away the recently verified delivery behavior merely to cut memory.

## 5. GPU Luma4 expansion is a second-stage candidate

After bulk-copy fixes, Luma4's byte-to-two-symbol loop remains: approximately 4.65 ms for 4 KiB in the earlier Udon helper profile. `Array.Copy` cannot perform this conversion.

The new probe additionally compiled and executed `Texture2D.LoadRawTextureData(byte[])` followed by `Apply(false, false)` in Udon. GPU readback exactly reproduced the uploaded channels for all three sizes above. Thus raw payload upload is technically available in the tested SDK.

A follow-up prototype could upload a padded, linear RGBA32 byte texture and let a Luma4 shader extract nibbles and produce the small symbol image, then use the existing block expansion pass. This avoids per-byte Udon raster work while retaining the output format. Core must provide only generic transport hooks and its allowed Luma4 implementation; other codecs opt in through their own shader/material/handler, with a working CPU fallback.

This is not yet an implemented or timed encoder. Texture upload and extra passes have a fixed cost, so it may lose for tiny payloads. Benchmark 0/32/256/4096-byte and near-capacity inputs, odd block widths, row transitions, Gamma/Linear and payload shrink before choosing a threshold. Do not introduce a speculative threshold or force a new shader requirement on existing plugins.

## Recommended order and acceptance gate

1. Same-type bulk copies: strongest verified benefit, smallest behavioral change.
2. Raw byte readback and bulk extraction: remove receiver VM conversion while preserving independent buffers.
3. Native raster reuse; then evaluate the block-symbol path separately.
4. Conservative snapshot format selection after a precision/source-type regression matrix.
5. GPU Luma4 conversion only after re-profiling the first steps.

Do not remove source snapshots, reduce RPC retries, lower capture frequency or skip unchanged-size payloads as a substitute for the above. Do not bypass bounds or CRC checks. The previous shader measurements were roughly 0.1-0.2 ms on an RTX 4090 and excluded uploads/readback transfer/scene work, so general decoder shader simplification is lower priority than the measured CPU and memory costs. Weaker desktop and mobile GPUs remain unmeasured.

For each implemented step, repeat 30/60-Hz continuous input with small/large/changing payloads. Record configured versus actually published versus applied frame counts, end-to-end latency, root-event mean/p95/p99 CPU time, frame GC allocation and retained texture/buffer sizes. Repeat long enough to expose resize/GC spikes. A faster helper is not completion evidence if publication rate, frame ordering, RPC semantics, pose fidelity or received-array isolation regresses. Full client-target Udon compilation must be followed by an actual VRChat client smoke test before calling the change client-verified.

## Reproduction

Run one Unity benchmark process at a time:

```powershell
./Validation~/OptimizationFeasibility/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/optimization-feasibility/udon-upload `
  -Mode Udon

./Validation~/OptimizationFeasibility/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/optimization-feasibility/gamma `
  -Mode Gamma

./Validation~/OptimizationFeasibility/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/optimization-feasibility/linear `
  -Mode Linear
```

The format runs change the isolated project's color-space setting. Compile-generated Udon assets in the real package repositories are backed up externally and excluded from these investigation commits. No new Player build was made in this investigation; native Player timings come from the preceding resource profile, not these probes.
