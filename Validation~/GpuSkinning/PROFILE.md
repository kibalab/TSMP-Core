# Humanoid GPU skinning: receiver profile

**Decision:** For a fixed, display-only performance rig limited by Udon CPU time, use the GPU-only receiver. For one Shapell body the median Udon receive cost fell from 0.846 to 0.475 ms per accepted pose, including submission and completion. This is a transfer of work, not a universal speedup: the measured model and skinning GPU passes increased from 43.5 to 69.1 microseconds. Use the hybrid path when some vertices actually need native bones; it was not the cheapest path on either processor.

## Workload and controls

- Core `62415836` on `feat/gpu-receive-outputs`, packaged Luma4 1.0.0, Unity 2022.3.22f1, Windows x64, D3D11, i9-13900K, RTX 4090. The standalone Development Player used Mono and Unity GPU Skinning. Udon measurements used the SDK's VM in Editor Play Mode with ClientSim (Worlds 3.10.4-beta.2), **not** the uploaded VRChat client.
- One Shapell body: 21,342 vertices, 180 skin slots, five material slots. The fixture recorded eight changing poses with the actual sender and codec, and played the images into a stable input RenderTexture. Every image was Luma4 640x360, block size 8, sample size 1; one 327-byte v4 pose (51 mapped bones) in a 352-byte network payload. Keeping the wire data identical isolates receiver cost; this does not measure sender filtering or bandwidth savings.
- Native reference: discrete pose application to Transforms and Unity's own GPU skinning, **not** CPU vertex deformation. GPU-only: shader channel, two palettes and baked mesh, no Udon pose binding. Hybrid: a native mesh below the left lower arm, a baked remainder, and five CPU anchor bones. No physics solver was running. All three used the same manual 800x600 view, one light, no shadows/MSAA, and no Animator playback.

## Udon CPU per accepted pose

Times in milliseconds. Each case ran twice, in opposite orders: 16 warm-ups and 80 measured completions per block. The timer bracketed the actual decoder's Udon VM entry points for submission and GPU readback completion. Total is the sum for the same input, **excluding the wait**; nested pose dispatch is already in total. One outstanding input allowed attribution. This is CPU work, not end-to-end latency.

| Receiver | Total p50 | Total p95 | Nested pose p50 |
| --- | ---: | ---: | ---: |
| Decode only, no channel or pose target | 0.399 | 0.459 | 0 |
| Full native, default rig checks | 0.846 | 0.883 | 0.398 |
| Full native, fixed-rig cache | 0.813 | 0.865 | 0.363 |
| GPU-only | **0.475** | **0.506** | 0 |
| Hybrid, five anchors, fixed-rig cache | 0.701 | 0.743 | 0.170 |

GPU-only reduced median receiver work by 43.9% vs the default or 41.6% vs the cached native receiver. Hybrid saved 17.2% vs the default. GPU-only still does decoder submission, readback completion and shader-channel publication. Calling this a readback-free decoder or adding the CPU and GPU times together would misrepresent the pipeline. [Full event statistics](Evidence/vm.csv).

## GPU and memory

RenderDoc 1.44: one warmed frame per rendered case, one discarded warm replay and ten timed replays. GPU event medians in **microseconds**. Model draws include vertex, raster and fragment work; this is not an isolated vertex shader benchmark. Selected total is computed per replay before taking percentiles and excludes the common decoder and the rest of the frame.

| Path | Model draws | Model GPU | Unity skin dispatch | Channel | Pose palette | Matrices | Selected total p50 / p95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Full native | 5 | 37.34 | 6.14 | - | - | - | 43.49 / 45.06 |
| GPU-only | 5 | 43.49 | - | 5.12 | 12.75 | 7.17 | 69.06 / 81.82 |
| Hybrid | 10 | 61.86 | 5.12 | 5.12 | 13.31 | 7.17 | 91.55 / 102.40 |

Two independent 240-frame Player trials per mode, after 90 warm-up frames, confirmed whole-frame GPU p50s of **0.333 / 0.340 ms native**, **0.483 / 0.566 ms GPU-only**, and **0.521 / 0.496 ms hybrid**. The two GPU-only p95s were 1.79 / 1.68 ms. Native main-thread p50s overlapped between the native and GPU-only paths. The Player was capped at 60 Hz: these are work timings, not FPS gains. Every measured case observed 240/240 frame-ID changes per trial, using local synthetic playback, not video transport. A hidden direct launch produced zero GPU clock readings and was excluded; accepted trials used the RenderDoc launcher with no frame capture during the timing window. [Player results including the decode, channel-only and palette-only controls](Evidence/player.csv) and [GPU pass statistics](Evidence/gpu-passes.csv).

Unity reported 2,288 B for the full pose palette, 13,928 B for skin matrices and 80,992 B for the baked rig texture (94.9 KiB combined); the baked mesh was another 3.90 MiB. These are Unity runtime object sizes, not driver VRAM or net savings, and exclude the shared channel atlas. The original mesh can remain referenced. The mixed bake had 1,681 native + 19,674 baked vertices (13 additional boundary vertices). Whole-frame managed allocations (Player p50) were 492 B native, 288 B GPU-only, 492 B hybrid; not Udon allocation measurements.

## Correctness and next measurements

The separate GPU probe compared world-space vertices against Unity's CPU-readable `BakeMesh` reference. Across 256 sampled body vertices in each root mode the worst errors were 1.298e-6 units (local) and 1.784e-6 (world). Mixed-boundary checks, normals/tangents, send exclusions, no-input and malformed-pose fallback also passed. The VM fixture checked expected frame IDs, applied field counts, shader channel publication where enabled, and final upper-arm rotation for native targets. After timing, the Player independently checked live palettes and model coverage (44,936 pixels native/GPU, 44,937 hybrid).

1. For several renderers driven by the **same** pose, test sharing the pose palette by channel and field identity; keep bind-pose-dependent matrices separate. The baker creates realtime palettes per bake, the pose shader scans bone records per output texel, and the palette-only control showed that hiding the surface alone does not avoid this work.
2. For mixed rigs, start with draw calls: the split retained all five material slots on both meshes and two of the native draw calls had zero indices in this capture. Test compacting empty submeshes with matching material remapping before changing skinning math.
3. Measure multiple avatars, real local physics, stereo/shadows, a midrange GPU, and a running VRChat client before setting defaults or claiming event-scale frame budgets. The two short Player trials and repeated replays of one frame per mode are not hardware-wide estimates. No Quest, Radeon, live-network or cold-start performance claim was tested here.

## Reproduction

Use two disposable projects with the tested Core and Luma4 sources. Use a real graphics device, not `-nographics`. Set `TSMP_SKIN_OUT` to an empty absolute results directory. No profiler component belongs in a shipped scene.

1. No SDK: copy `SkinProfile.cs` to `Assets` and `SkinProfileBuild.cs` to `Assets/Editor`. Run Unity with `-batchmode -quit -force-d3d11 -projectPath <project> -executeMethod SkinProfileBuild.Build -logFile <out>/SkinProfileBuild.Build.log`. This generates scenes, input assets and `<out>/Player/Skin.exe`.
2. Set `TSMP_SKIN_CAPTURE=0`, `TSMP_SKIN_RUN=presentA` and `TSMP_SKIN_CASE` for each of `decode,publish,palettes,gpu,cpu,hybrid`. Run each to completion using `renderdoccmd capture -w -c <out>/<case>-capture <out>/Player/Skin.exe -force-d3d11 -screen-width 800 -screen-height 600 -screen-fullscreen 0 -logFile <out>/<case>-presentA.log`. Repeat in reverse order as `presentB`. For the three rendered modes, repeat once with `TSMP_SKIN_CAPTURE=1` and `TSMP_SKIN_RUN=rd`; these runs are excluded from whole-frame statistics.
3. SDK project: copy the eight generated `Assets/SkinProfileData/Frame*.asset` textures in recording order to `Assets/SkinProfileInputs/0.asset` through `7.asset`. Install `SkinVmProfile.cs` in `Assets/Editor`, enable ClientSim and compile UdonSharp. Run `-batchmode -force-d3d11 -projectPath <sdk-project> -executeMethod SkinVmProfile.Run -logFile <out>/SkinVmProfile.Run.log` **without** `-quit`. Require ten PASS blocks and `<out>/vm.csv`.
4. With the same output environment, run `qrenderdoc --python <absolute-path-to>/ReplaySkinCapture.py`, then `python SummarizeSkinProfile.py <out> <summary-directory>`. Keep the per-frame CSVs, runtime-object summaries, Player logs, VM samples and three RDCs alongside the build. To repeat the independent geometry comparison, see [the correctness fixture](README.md).
