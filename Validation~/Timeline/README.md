# Timeline validation

Run these checks from the repository root against dedicated validation projects. The projects must reference this working copy of the Core package and a real Luma4 package.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
$native = 'F:/Unity/TSMP/Validation-NoSDK'
$vrc = 'F:/Unity/TSMP/Validation-VRC'
$results = 'F:/Unity/TSMP/Validation-Results/timeline'
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project $native -Results $results -Step TimelineRegression
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project $vrc -Results $results -Step Udon
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project $vrc -Results $results -Step TimelineVm
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project $native -Results $results -Step Play
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project $native -Results $results -Step Build
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project $native -Results $results -Step Player
```

`TimelineRegression` runs 20 native Editor cases covering initial seeks, paused graphs, repeated stops, malformed packets and warnings, missing sources, replaced Directors/assets, disabled receivers, Seek, Continuous correction, playback-clock prediction and loop boundaries. It also evaluates an actual Animation Track from captured Timeline bytes. Director availability covers a disabled component and an inactive parent with an active child, capture clearing, receive rejection and recovery after reactivation.

`Udon` compiles all programs for the Udon client. `TimelineVm` executes the compiled Timeline program in the SDK's real Udon VM during Play Mode, with SDK heap references resolved to a real UdonBehaviour and PlayableDirector. It checks controls, receive state changes, malformed data, Continuous updates and v1 capture. It does not substitute C# proxy calls for Udon execution.

The 2026-11-01 minimum-SDK follow-up found that Worlds 3.9.0 does not expose
PlayableDirector.isActiveAndEnabled. The original compilation failure is retained.
Checking enabled and gameObject.activeInHierarchy preserves the availability
condition using exposed getters. Native 20-case regression, current SDK
3.10.4-beta.2 and minimum SDK 3.9.0 Timeline VM checks all pass, including disabled
Director/inactive-parent rejection and reactivation. Fresh client compilation
passes 16 programs in the existing all-codec project and 13 in the isolated
Core/Luma4 minimum-SDK project, both with generated bindings. This is embedded
package/SDK evidence, not a VCC resolver or live VRChat test.
Evidence: `F:/Unity/TSMP/Validation-Results/float32-correctness-20261101`.

`Play` and `Player` use the shared GPU loopback harness. Timeline time and play/pause state pass through the native Encoder, the Luma4 texture, GPU readback, Decoder and component bindings. A child Transform animated by an Animation Track must reach each of six received positions. The child binding avoids depending on Animator root-motion settings. The existing Transform, humanoid, Unicode and RPC checks remain enabled.

`Build` produces a Windows x64 Development Player with Mono and managed stripping disabled, plus a BuildReport summary. `Player` runs that executable with graphics enabled and records the GPU, graphics API and individual results. Unity logs and result files are written to the results directory.

## Limits

- The v1 packet remains exactly 10 bytes. Timeline assets, bindings, wrap mode and update mode are not transmitted; use matching content and configuration.
- Udon does not expose PlayableDirector state, graph or completion events. Explicit Play/Pause/Resume/Stop/Seek control is supported, but automatic completion and arbitrary external Director controls still cannot be observed reliably. This change does not take ownership of the Director's playback clock.
- An empty received sample cancels pending correction without stopping independent local playback.
- VM checks are not a test in an uploaded VRChat world. IL2CPP and stripping are not covered by the Mono Player test.
- A pre-existing Luma4 `SampleBlockLuma` shader warning may appear during Player builds; it is separate from Timeline compilation and runtime assertions.
