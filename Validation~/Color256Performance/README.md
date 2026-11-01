# Color256 Udon encoder A/B

This experiment reuses SustainedCost's actual compiled client Encoder, Decoder,
codecs, full-field receiver checks and real VRC GPU callbacks. It compares only
the frozen Color256 source variants. Use an isolated Worlds SDK project and one
owned Unity/GPU benchmark process at a time.

```powershell
python ./Validation~/SustainedCost/Generate-AB-Plan.py --baseline-source '<old codec.cs>' --candidate-source '<new codec.cs>' --output-root '<fresh A/B directory>'
./Validation~/Color256Performance/Run-Validation.ps1 -Manifest '<fresh A/B directory>/manifest.json' -Project '<dedicated SDK project>' -FinalVariant candidate
python ./Validation~/Color256Performance/Analyze-Results.py --root '<fresh A/B directory>'
```

The manifest pins both source hashes and all seven fresh-process plans before
output. Each of three alternating baseline/candidate pairs runs1KB and5KB for
12seconds each, with30Hz source/90Hz render targets. The seventh process confirms
candidate5KB for60seconds. Every case also has two seconds of warmup and a
one-second publication tail before draining.

Only the dedicated project's Color256 `.cs` file changes between processes.
The runner rejects unknown installed source, nonempty evidence and mismatched
hashes; checks that this project's Unity process is closed before replacing the
file; and restores the selected frozen final variant in `finally`. It does not
change the authoritative codec repository, shaders, package versions or settings.
Existing failed evidence must be preserved and a fresh manifest directory used.

The first measured publication carries field ID1024 in every process. Its
generation-dependent xorshift bytes, and each common subsequent field ID's bytes,
match across variants. Outer TSMP frame indices, host timestamps and warmup
counts differ, so full packets are not described as bit identical. Full applied
fields are checked independently from received IDs. Production header/payload
CRC gates remain enabled; this dynamic A/B does not retain an independent golden
header byte oracle. Static/untouched raster regions are checked separately by
the Color256Encoder correctness vectors.

Compare CPU per EncodeNow call as well as CPU/s and actual throughput. A faster
variant can publish and decode more updates and render more frames during the
same duration. The analyzer reconciles source variants, other product files,
configuration receipts, bytecode changes, published/application traces, byte-check
counts and pooled raw EncodeNow times. Independent audits reparse the raw data.

The executed task controller is preserved verbatim at
`.../color256-optimization/ab/Run-AB.ps1`. This repository wrapper has portable
path parameters and additional preservation guards; it was syntax-checked after
those seven runs, without launching another timing experiment. See
[results](RESULTS.md) for exact source hashes and measurements.
