# Local-refinement candidate experiment

This is a measurement-only experiment. It never edits package shaders, CRC, palettes, channel bit packing or codec defaults. The frozen original/candidate shader pairs have unique names; Color256's calibration include uses its package path. `FrozenShaders/sources.json` records the original package source hashes and shadow-source hashes.

The candidates replace clamped neighborhood loops with valid per-axis offset ranges. They retain the same unique symbol candidates, first-visit order and strict-less-than tie rule. RGB16 retains the existing channel-calibration arrays. This may save boundary evaluations; GPU dynamic-loop overhead can also lose performance, so adoption is conditional.

Run only after other Unity/OBS/profile experiments have stopped:

```powershell
./Validation~/RefinePerformance/Run-Validation.ps1 `
  -UnityEditor '<Unity.exe>' -Project '<dedicated SDK-free Native project>' `
  -Evidence 'F:/Unity/TSMP/Validation-Results/codec-transport-20261030' `
  -Results '<new empty result directory>'
```

Use `-SkipTiming` for byte parity only, or `-Quick -SkipTiming` for a narrower smoke pass. The full pass compares Color256 Robust Refine, RGB16 fixed 4/4/4 Refine and variable 5/6/4 Refine across 720p/1440p, structured/random values, 32/2,495/5,375 bytes, samples two/four, orientations and RGB16 radii one/two/three. It also checks variable 1/1/1, 3/3/2 and 8/8/4 at smaller fitting payloads. Saved CBR/CRF images include known compressed failures: their full recovered shader bytes must remain identical between A/B even when both differ from original payloads.

`-Modes 'RGB16Fixed,RGB16Variable' -TimingRadii '1,3'` selects only the two RGB16 modes and adds radius-one/three timing (48 cases). PowerShell arrays such as `-Modes RGB16Fixed,RGB16Variable -TimingRadii 1,3` are also accepted. Defaults remain all three modes and RGB16 timing radius two (36 timing cases); Color256 always times radius one. Filtering modes does not narrow their existing parity checks, and timing radii do not change the parity radius matrix. Selected settings are recorded in `result.txt`, and each timing row records its radius.

Samples/radii are explicit diagnostic shader overrides. Image headers are not rewritten, and this does not certify a new normal receiver profile. Exact default controls must still recover the independent raw bins. No state/RPC application or network/service delivery is involved.

Both Color256 shadow materials are assigned to separate cloned codec instances before preparation. Both therefore use the actual current-source calibration LUT policy and an independent cloned preparation material. RGB16 Refine retains its existing no-LUT policy. Package material assets are not mutated.

GPU timing uses a fixed 2048x1 RGBA8 byte target, matching the saved transport harness, and 32 repeated draws per custom GPU marker, including Color256 calibration preparation in every draw pair. A/B order alternates, ten warm frames precede thirty valid paired GPU timestamp samples, and synchronization uses readback. `timings.csv` contains mean/median/p95 microseconds per draw plus preparation where applicable. `timing-pairs.csv` preserves raw recorder timestamps/block counts. CPU scheduling/readback wall time is not substituted for GPU time. Unavailable GPU timestamps are explicitly marked `GPU_TIMING_UNAVAILABLE` and do not support adoption. No timing estimates apply to VRChat, other devices or overall application FPS.

`comparisons.csv` records full-byte mismatch counts and baseline/candidate/reference payload CRCs. `result.txt` PASS means byte parity only. Adopt a candidate only when parity holds and supported production-size GPU measurements show a useful benefit without unacceptable regressions. `source-hashes.csv` and copied `ShaderSources` retain provenance.
