# Decoder input snapshot regression tests

Regression coverage for [TSMP-Core#22](https://github.com/kibalab/TSMP-Core/issues/22). The tests replace the pixels of one input RenderTexture immediately after `DecodeNow()`. The decoder must read both the header and payload from the captured frame, not combine the old header with the replacement pixels.

This directory is excluded from Unity package imports. The scripts are test infrastructure, not additional runtime components or setup steps for package users.

## Requirements

- Windows with Unity 2022.3.22f1 and Windows x64 build support.
- An active Unity license and a Direct3D 11 graphics device supporting Float32 render targets and asynchronous GPU readback.
- Separate, disposable Unity projects for SDK-free and SDK-enabled validation. The runner saves a test scene, enters Play Mode, changes Player settings for builds and exits the editor. Do not run it in an open working project.
- Actual Core, Luma4, RGB16, RGB20 and Color256 packages. Tests load the packages' prefab materials and shaders; they do not provide codec substitutes.
- For Udon tests, VRChat Worlds SDK with bundled UdonSharp. The recorded run used 3.10.4-beta.2.

In the SDK-free project, reference the checked-out package directories through `file:` dependencies in `Packages/manifest.json`, alongside the packages' Unity dependencies. For example:

```json
"com.kibalab.tsmp.core": "file:F:/Unity/TSMP/TSMP-Core-main/Packages/com.kibalab.tsmp.core",
"com.kibalab.tsmp.codec.luma4": "file:F:/Unity/TSMP/TSMPCodec-Luma4/Packages/com.kibalab.tsmp.codec.luma4",
"com.kibalab.tsmp.codec.rgb16": "file:F:/Unity/TSMP/TSMPCodec-RGB16/Packages/com.kibalab.tsmp.codec.rgb16",
"com.kibalab.tsmp.codec.rgb20": "file:F:/Unity/TSMP/TSMPCodec-RGB20/Packages/com.kibalab.tsmp.codec.rgb20",
"com.kibalab.tsmp.codec.color256": "file:F:/Unity/TSMP/TSMPCodec-Color256/Packages/com.kibalab.tsmp.codec.color256"
```

The SDK validation may use embedded copies of these packages to isolate generated Udon program assets from the working repositories. Compare the source, shader and assembly-definition hashes with the working packages before accepting that result. Do not fix only the validation copies.

The harness clones prefab materials before assigning decode properties and releases them afterward, so running the test does not persist frame-specific properties into package material assets.

## Run

Run from the Core repository root, substituting the isolated project and output paths:

```powershell
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/NoSDK' -ResultsDirectory 'F:/Results/Snapshot/Gamma' -Mode Play
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/NoSDK' -ResultsDirectory 'F:/Results/Snapshot/Linear' -Mode PlayLinear
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/NoSDK' -ResultsDirectory 'F:/Results/Snapshot/Player' -Mode Player
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/VRC' -ResultsDirectory 'F:/Results/Snapshot/Udon' -Mode Udon
```

`Play` uses the project's current color space; use a Gamma project for the first command. `PlayLinear` changes it to Linear. `Player` builds and runs a Windows x64 Development Player using Mono, with managed stripping disabled and the current project color space. Each command returns a nonzero exit on failure and writes a compact result and editor log. The build also writes `Player/Snapshot.build-report.txt`; execution writes `Player.log`.

Both Editor and Player run with `-batchmode -force-d3d11`, without `-nographics`. This still uses the GPU. A hidden non-batch Player stalled asynchronous callbacks on the recorded Windows host; the same executable passed in graphics-enabled batch mode. This runner does not certify minimized-window behavior.

For the pre-fix reproduction, use a separate project referencing an unmodified Core revision before this fix. The first Luma4 case should fail with `header/payload generation mismatch`, before tests requiring the new snapshot helper run. Do not change the main checkout to obtain the baseline.

## Coverage

- Native C#: 216 changing-input cases covering all four codecs and all nine material variants, sample size 1/4, both vertical orientations, ARGB32/Float inputs, different payload bytes, changed lengths and a codec change between readback stages.
- The result must contain frame A's valid header and exact raw payload even though the input now contains frame B. Native dispatch separately verifies that A's value `42`, not B's `99`, is applied to a Component.
- Source destruction after capture, component/GameObject disable and re-enable during either stage, ignored late callbacks, cleanup and fixed-size allocation reuse.
- CRC rejection, existing safety/duplicate policies, resize to 1080p/4K, and Float32 capture precision for negative/HDR values.
- Payload CRC32 across four codecs in readback-only and parse/apply modes; rejection before field/RPC dispatch, intact retry with the same frame/event identity, predicted/fallback/combined paths, both bad-slot orders with different lengths, strict missing-checksum rejection and permissive legacy compatibility. Header-only or skipped frames must not report payload verification.
- Udon: full client-program compilation followed by actual compiled decoder/codec execution in the SDK editor VM, using `VRCGraphics` and real asynchronous VRC GPU readback callbacks. Sixteen changing-input cases and eight component/GameObject cancellation cases include remaining disabled until the callback and immediate re-enable.
- Payload capacity reuse is checked against the next write slot's existing buffer, matching the Native test. Alternating slots need not share one array. The Udon result must reach the capacity/invalid-length, eight cancellation and thirty frame-window cases after the changing-input cases; a partial run is not a suite pass.
- Client compilation is repeated after Play Mode entry, before VM programs are retrieved. Jagged slot arrays are read using UdonSharp's `object[]` heap representation. Cancellation retains the pending snapshot until its callback finishes; early re-enable may capture a replacement in the other slot, which must have its own snapshot and exact payload. Disabled capture attempts must leave the pending count unchanged.
- Before VM graphics assertions, the harness draws the cloned codec materials through native Graphics calls and flushes a pixel readback. Fresh-project first use exceeded the Editor VM's fixed 10-second watchdog at the VRCGraphics.Blit boundary; the preparation duration is reported separately. This warmup does not increase the VM limit or certify cold-start performance. The original unprepared failures must remain in the evidence.

The Udon runner uses SDK private VM fields to load compiled programs and register their entry points. SDK upgrades may require test-harness changes. A passing editor VM test is not a claim that a VRChat client or world build was executed.

## Cost measurements

The native runner records GPU timestamps for batches of 32 copies, with ten warmup frames followed by fifteen measured frames. A small readback barrier is used only for this copy benchmark, not to replace the decoder's asynchronous callbacks in correctness tests. Reported time is the median batch time divided by 32, not end-to-end decoding latency. The input is a synthetic TSMP frame, so cache/compression effects differ from arbitrary video.

Production snapshots match supported source formats: ARGB32 uses 4 bytes/texel, ARGBHalf uses 8 and ARGBFloat uses 16. Multiply `width * height * bytesPerTexel` by the number of allocated slot snapshots (up to two). Unsupported or unrecognized source formats use Float32; Native also falls back to Float32 if a matching render target format is unavailable, then rejects the capture if Float32 is unavailable. The standalone `Capture` helper used by some measurements explicitly requests Float32. These are snapshot texel-storage estimates, excluding driver overhead, readback targets and other GPU/process memory. One full-image copy is made per accepted decode attempt without adding a CPU readback stage.

See [RESULTS.md](RESULTS.md) for the recorded environment, failures encountered during validation, final results and untested platforms.
