# Native color raster performance validation

See [RESULTS.md](RESULTS.md) for the implemented buffer reuse, measured Native CPU/allocation changes, accepted RGB16 shader change and integration results.

Use a dedicated SDK-free Unity 2022.3 project with Core, Luma4, Color256, RGB16 and RGB20. The runner builds and executes a Windows x64 Development Mono Player, D3D11, Gamma, stripping disabled. It saves a scene and changes settings in that isolated project. Never run it against an open working project.

```powershell
./Validation~/ColorRaster/Run-Validation.ps1 -Project <isolated-project> -Results <fresh-baseline> -Phase baseline
./Validation~/ColorRaster/Run-Validation.ps1 -Project <isolated-project> -Results <fresh-optimized> -Phase optimized -Reference <baseline>
```

The baseline must contain the original color packages without buffered overrides. The optimized project must contain the new package sources; the script does not switch repositories or install them. Both phases retain source hashes and require fresh output directories. Default measurements are 18 conditions, 20 warm calls and 180 measured frames each at a 30-Hz target. Only one benchmark process should run at a time.

`ColorRasterProbe` provides one always-sent RawBytes field. Profile labels are total NetworkFrame payload bytes (32 / 2,495 / 5,375); the field values are 25 bytes shorter. Timings wrap the real `TSMPEncoder.EncodeNow`; resource preparation, source filling and validation assertions lie outside that scope. Actual rendering/upload submission within EncodeNow remains included. This is not GPU completion, streaming delay or VRChat CPU/FPS.

The harness compares fixed-header raster pixels with independently saved baseline hashes/PNGs/header/payload bins, checks buffered reuse and independent ownership, then exercises actual production encoding and strict GPU payload checks. Cases include shrinking/growing values, empty/tail raster payloads, 720p/1440p/720p resizing, codec changes and disable/re-enable.

Allocation counters are calibrated before use. Unsupported scoped counters are `-1`. `GC Allocated In Frame` covers the whole Player frame, including harness overhead. It is not Encoder-only allocation or retained memory. Profiles, per-sample observations, reference-loader diagnostics and source manifests remain in the results directory.

For decoder shader candidates use the separate [RefinePerformance](../RefinePerformance/README.md) experiment. It measures paired GPU timestamps and A/B recovered bytes; it does not alter package shaders automatically.
