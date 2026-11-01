# Current Refine processing cost

This measurement runs the actual freshly compiled TSMP Decoder, codec and bound
RawBytes receiver programs through the SDK Editor Udon VM with real VRC GPU
readback callbacks. No product algorithm, recovery default or protocol changes
are made by this harness.

Use only a dedicated Worlds SDK project with installed matching Core/codec
sources. Stop other owned GPU/encoder/benchmark work before timing. The runner
copies this directory's measurement sources into `Assets/Validation/RefineCost`.

```powershell
python ./Validation~/RefineCost/Generate-Plan.py --evidence-parent '<Validation-Results>' --output '<new evidence root>/plan.json'
./Validation~/RefineCost/Run-Validation.ps1 -UnityEditor '<Unity.exe>' -Project '<dedicated SDK project>' -Plan '<plan.json>' -Results '<fresh results directory>'
python ./Validation~/RefineCost/Analyze-Results.py --results '<completed results directory>'
```

The fixed selection covers nine raster conditions with independently saved
5,375-byte payload references: clean Luma4/Color256/RGB16 controls, first/second alternate-mode wins
for Color256/RGB16, and two permanent rejections. Their protected headers, codec
options and received images are retained unchanged. Selection uses existing Native
results and is a representative diagnostic, not a new accuracy holdout.

For each condition, three independent decoder/receiver VMs use budgets zero/one/
two, warm up five invocations, then measure thirty. Budget order rotates between
invocations. Repeated fixed images use `skipDuplicateFrames=false` deliberately
to compare complete field application rather than increasingly cheap duplicate
rejections. Every success matches every independent header/payload byte, frame ID
and complete 5,350-byte RawBytes field; every failure applies no state. Clean
cases separately check ordinary duplicate-on suppression. No RPC workload is
included in these timing fixtures.

Decoder VM `Interpret` timings separate submission and cumulative asynchronous
callback CPU work. They include nested codec/receiver work and synchronous driver
calls, but exclude host heap observation, file IO, input upload, compilation,
explicit graphics warmup and the first five invocations. Callback count and each
raw event are preserved. Total VM time sums those events once, without adding
nested codec timings again. End-to-end invocation wall latency ends at the final
callback VM `Interpret` return, avoiding later harness polling delay. It excludes
the outer event dispatcher's remaining bookkeeping, includes asynchronous waiting
and must not be called GPU execution time.

Repeatedly rejected inputs invalidate prediction, while successful controls may
retain prediction after warmup. Thus the case measures actual steady behavior for
that repeated condition. It is not a forced common submission path or a sustained
video-stream model. Serial invocations do not measure two-slot saturation, unique
applied-update Hz, source age in a service, a populated world or real VRChat FPS.
CPU totals sum multiple callbacks; they do not establish whether callbacks would
coincide within one render frame under another workload. Individual callback
samples and quantiles are reported separately.

Installed Runtime/Editor sources, independent artifacts, plan and compiled client
bytecode are hashed. The independent analyzer recomputes event sums, verdicts,
attempt budgets, sample counts and summary quantiles, and checks source/artifact
stability. Preserve initial compilation/import/failed runs separately.
