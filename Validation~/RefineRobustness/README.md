# Refine robustness experiment

Run only in a dedicated SDK-free Unity 2022.3 project, while other owned GPU measurements are stopped. The runner copies the harness and `Candidates/` into `Assets/RefineRobustness`; it does not edit package source files.

```powershell
./Validation~/RefineRobustness/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<Native project>' -Plan '<plan.json>' -Results '<new empty directory>'
```

The JSON plan accepts the existing transport `profiles` and `frameMappings` schema. Add optional `expectedHeaderDirectory` (defaults to the `header` sibling of `expectedDirectory`), and per-mapping `headerFile`. References must be independent pre-raster `.bin` files. `maxFramesPerProfile` and `frameStride` are explicit diagnostic subsets; zero and one respectively preserve all mapped inputs. Unknown-reference mappings skip raw probes and still undergo production decoding when enabled.

`variants` defines raw shadow-shader probes. Every variant has a unique `name`, `mode` (`Header` or `Payload`), optional `codecId` filter, optional `shaderAssetPath`, optional `materialField`, `sampleSize` (negative follows reference header), `radius` (negative follows header), and optional `properties: [{name,value}]`. A blank shader/material uses the production material selected from each independent header. Specify `materialField: "refine444ByteDecodeMaterial"` for a fixed RGB16 shadow shader so its original no-LUT preparation policy remains intact. Variable RGB16 uses `variableRefineByteDecodeMaterial`; Color256 Robust Refine uses `robustRefineByteDecodeMaterial`.

`lutWidth` manually invokes the existing calibration preparation for an explicit diagnostic LUT experiment; `disableLut` explicitly disables that policy. Custom preparation supports `calibrationShaderAssetPath`, `calibrationWidth`, `calibrationHeight` (default one), `calibrationTextureProperty` and optional `calibrationKeyword`. It renders an ARGBFloat target and binds it to the shadow decoder. Preparation receives the decoder's geometry, sampling, channel bits and other material properties. GPU timing includes preparation in every draw, including this custom preparation.

Example RGB16 affine probe:

```json
{
  "name": "RGB16Affine",
  "mode": "Payload",
  "codecId": 5,
  "shaderAssetPath": "Assets/RefineRobustness/Candidates/Rgb16AffineRefine.shader",
  "materialField": "refine444ByteDecodeMaterial",
  "sampleSize": 4,
  "radius": 2,
  "properties": [{"name":"_RecoveryMode","value":1}],
  "calibrationShaderAssetPath": "Assets/RefineRobustness/Candidates/Rgb16AffinePrepare.shader",
  "calibrationWidth": 4,
  "calibrationHeight": 1,
  "calibrationTextureProperty": "_Rgb16ForwardLut"
}
```

Use the actual reference header's codec ID in a plan; the example does not define a protocol value.

`raw-frames.csv` contains complete byte/bit mismatch counts even when a CRC fails. Header probes decode the common Luma4 header at row two. Payload probes use the independent reference header's geometry/options/byte count even if the received header is corrupt. Thus raw payload success is **diagnostic algorithm recovery, not production end-to-end frame acceptance**. The header contains the independent expected payload CRC. Header/payload CRC acceptance with different original bytes is `CRC_ACCEPTED_WRONG` and fails the harness. Rejected lossy frames remain valid measurements.

Set `runProduction: true` to separately run the actual TSMPDecoder with actual installed package materials. It requires payload CRC, disables duplicate skipping and overlap, and uses parse-only safety mode. This mode does not exercise predicted/combined short paths; the separate Core recovery safety suite uses state/RPC application mode for those paths. `production-frames.csv` labels actual header/payload verification, exact original bytes, current failed-payload mismatches and readback wall time. No state/RPC is applied. `decoderProperties` assigns explicit public bool/int/float settings. Optional `productionVariants: [{name, decoderProperties:[{name,value}]}]` runs multiple settings on the same source snapshot in each case. For a recovery-enabled Core, compare `maxCrcRecoveryAttempts=0` and `2`; diagnostics record per-stage attempts and deltas in cumulative recovered counters. An unavailable counter remains -1, including its delta, rather than appearing as zero attempts. A baseline obtained from current Core with retries disabled still needs comparison against the frozen previous production results.

Set `timing: true` for GPU timestamp pairs, and `timingFramesPerProfile` to bound timed inputs (default three). The first variant of each mode is the baseline for remaining variants. Each pair alternates AB/BA, discards ten warm frames and collects thirty valid timestamp pairs. Each custom marker contains 32 draws and includes per-draw LUT preparation. `timing-pairs.csv` retains raw nanoseconds and marker counts; `timings.csv` reports mean/median/p95 microseconds per draw. Unavailable GPU timestamps are labelled; they are not replaced with CPU or readback times. These timings do not establish total decoder, Udon, service, or VRChat FPS.

`summary.csv` reports raw exact frames, CRC-valid frames, total original bytes and bytes in complete exact outputs. Summaries from forced payload probes must not be presented as delivered goodput. `source-hashes.csv` includes every installed candidate shader/include/preparation source and candidate JSON provenance, alongside inputs/references/runtime source. The retained plan and candidate sources bind observations to each run. GPU timing rows also name their baseline variant explicitly. `PASS` means execution completed without input errors or wrong accepted bytes, not that every lossy input was recovered.

## Bounded-recovery integration safety

Run `Run-Validation.ps1 -CoreRecovery` (without `-Plan`) after installing the recovery-enabled runtime. This copies `CoreRecoveryValidation.cs` and the existing Snapshot fixture helper. It exercises actual protected codec rasters through the real decoder: both CRC stages, budgets zero/one/two, captured request settings, prediction/combined output, both bad-slot orders, independent snapshots with source mutation, state/RPC rejection and intact retransmission, and component/GameObject cancellation during a retry. `Assertions` records checks performed. These safety fixtures intentionally contain literal incorrect bytes rather than lossy observations; rejecting them is the expected result.

Optional `-SavedCases '<json>'` additionally requires actual previously rejected compressed inputs to recover exactly with the production runtime. Its schema is `{ "cases": [{"name":"...", "input":"<PNG>", "header":"<original header bin>", "payload":"<original payload bin>", "stage":"Header|Payload"}] }`. Each case must reject with retries disabled, accept with retries enabled, match every original byte/frame ID and increment the appropriate successful-recovery counter. Use independently measured recovered cases, not a new guessed success target.
