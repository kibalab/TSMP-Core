# 80-actor receiver follow-up

Same hardware, three paths and input as the [crowd comparison](CROWD.md). The baseline is `a6779c8`. Only the sparse receiver and split-mesh baker changes were retained. No protocol, shader-channel API or interpolation settings changed.

## Udon receiver

Udon VM in ClientSim; 80 independent 51-bone fields per image, five native anchors in hybrid. Two blocks of 80 accepted samples per path, before and after, with opposite path orders within each run. Times are milliseconds of actual decoder submission + completion, not elapsed time waiting for a readback. Pose events are nested in that total.

| Path | Before p50 / p95 | After p50 / p95 | Nested pose p50, before / after |
| --- | ---: | ---: | ---: |
| Full native, fixed rig | 43.62 / 44.25 | 43.65 / 44.83 | 30.70 / 30.58 |
| GPU-only | 2.13 / 2.23 | 2.14 / 2.28 | 0 / 0 |
| Hybrid | **29.27 / 29.90** | **24.12 / 24.52** | **15.42 / 10.31** |

Hybrid total CPU work fell by 17.6%; it still exceeds a 60 Hz budget at this size. A sparse rig records selected offsets in packet order during validation and visits those offsets only during application. All records, even unwanted ones, must pass bounds/duplicate checks before touching a Transform. Root position is checked for non-finite IEEE-754 encodings before conversion, and the root tail is located by the wire count, not the selected count.

## GPU results

RenderDoc 1.44, D3D11. One warmed frame per case; one untimed and ten timed counter replays per capture. Sum event durations within each replay before taking the median. Replaying hundreds of instrumented draws is **not** equivalent to production wall-clock GPU frame time.

- **Hybrid bake:** model draws 800 to 640, zero-index model draws 160 to 0, same 42,973 covered pixels and all 80 arm checks passed. Replayed model events were 4.832 ms before and 4.972 ms after: fewer draws, **no measured GPU time win**. The benefit applies only to meshes re-baked with the updated baker.
- **GPU-only, 80 actors:** the reference capture had 400 model draws, 80 pose and 80 matrix updates. In the shader comparison the original pose pass total was 1.202 ms. A prototype moved validation and a packed per-ID lookup into the CRT vertex stage and passed it to the fragments without interpolation. Geometry tests passed but the pose pass cost **1.474 ms**. For one actor the same comparison was **12.29 vs 16.38 microseconds**. Rejected: the work moved, but the targeted pass got slower at both populations.
- **Whole-frame cross-check (no capture triggered):** 80-actor GPU-only p50s in two Player runs were 3.09 / 2.41 ms original vs 2.17 / 2.39 ms trial. p95s were 5.89 / 3.64 vs 7.97 / 3.39 ms. These short runs do not establish a consistent frame-tail improvement.

See [per-stage event counts and timings](Evidence/crowd-capture.csv). Final verification also passed the plain-Unity geometry checks, UdonSharp compilation of 27 scripts and a local Windows VRChat SDK world build with the mixed mesh. These are not live VRChat, stereo, or AMD GPU measurements.

## Repeat

1. Use the [crowd setup](CROWD.md) and `SkinVmProfile.Run` with `TSMP_CROWD_N=80` against the original and changed package. Keep both raw VM CSVs. For a geometry/material regression test run `SkinValidation.Run`; it also exercises missing/duplicate/out-of-range pose records, recovery, and atomic rejection of malformed sparse input.
2. Rebuild single-actor templates with `SkinProfileBuild.Build` **before** `CrowdProfileBuild.Build` (`TSMP_CROWD_WIDE=1`) so the comparison uses a fresh hybrid bake, not references to the previous one.
3. Run the RenderDoc launcher as in the [one-rig profile](PROFILE.md), using `TSMP_SKIN_CAPTURE=1` to capture a warmed frame of `Crowd.exe` with `TSMP_SKIN_CASE=gpu-80` or `hybrid-80`. Replay with `ReplaySkinCapture.py` in QRenderDoc, then pass the resulting `.rdc.csv` files to `SummarizeCrowdCapture.py`. Set `TSMP_SKIN_CAPTURE=0` for independent frame-timing runs.
