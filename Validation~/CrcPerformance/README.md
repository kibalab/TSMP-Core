# CRC32 Udon microbenchmark

Use a dedicated Worlds SDK project containing the Core package being measured. The runner copies only this test probe/host into `Assets/CrcPerformance`; it does not change Core or require graphics.

```powershell
& './Validation~/CrcPerformance/Run-Validation.ps1' -UnityEditor $unity -Project $sdkProject -Results $emptyResults -Warmup 20 -Samples 60
```

The host creates a test Udon program asset, compiles for the client, enters Play Mode, repeats client compilation, then loads a single actual Editor Udon VM. `RunBaseline` contains the frozen original 256-entry table byte loop. `RunProduction` calls the installed `Crc32Runtime.Compute`; `RunControl` records dispatch-only work. Baseline and production have separate public lookup-table fields. Baseline always uses `EnsureTable(null)` (256 entries); production uses public `EnsureFastTable` through Editor reflection when it exists, otherwise `EnsureTable(null)`. Entry counts, array storage and Native factory setup time are recorded; reflected factory overhead is included and VM table construction is not timed. All events run in the same compiled probe, VM, Unity process and input buffers. Input and output heap access is outside timing. One Compute is executed per event, so individual calls retain the VM's normal watchdog limit.

Before timing, standard-vector (`123456789`), offset/dirty-tail, null/empty/invalid-range/table and 64 seeded random cases compare an independent bitwise Editor reference, Native production output and both Udon outputs. The standard vector must equal `0xCBF43926`. The benchmark sizes are 32, 1024, 4121 and 6750 valid bytes at nonzero offsets. The larger input is a general CRC workload, not a TSMP frame-capacity guarantee.

Each size gets 20 warm calls per event by default and 60 measured baseline/production pairs with alternating order. Control follows each pair. `summary.csv` reports mean, nearest-rank p50/p95, minimum and maximum event wall time. `samples.csv` preserves every sample and `correctness.csv` preserves reference comparisons. Control is not subtracted. Timings include common `SendCustomEvent`/VM dispatch and exclude heap input/result access, compilation, table creation and warmup.

`result.txt` includes Unity/CPU/OS, source revision, client bytecode SHA256 and installed production source SHA256. `Crc32Runtime-used.cs` preserves the measured source. Use a fresh results directory for each run. Baseline and production differences are microbenchmark observations for the recorded environment; they are not sender/receiver frame time, encoder/decoder totals or real VRChat client measurements. A lower mean alone does not establish p95 improvement. CRC rejection coverage remains in PayloadIntegrity, Snapshot and ContinuousFrames regressions.
