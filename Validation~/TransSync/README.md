# TransSync Send Scheduling

This feature implements the existing `Priority`, `SendOnChange`, and `MinSendInterval` attribute properties in both native Unity and Udon. It does not change packet layouts, receiver interpolation, or RPC delivery guarantees.

## Behavior

- Fields are considered in descending priority across the encoder's sources. Equal-priority fields rotate after successful output. Strict priority can starve lower-priority traffic under sustained overload.
- The first sample is immediately eligible. Later samples obey an unscaled real-time minimum interval measured from successful frame output, not from attempted encoding.
- Change detection compares serialized contents, including in-place array edits. Multiple changes during an interval are coalesced into the latest value.
- Only successfully output fields update snapshots and timestamps. Failed output and capacity deferral leave them eligible for retry.
- Queued RPC data takes capacity before automatic fields. Oversized fields are deferred without truncating them; smaller eligible fields can still fit.
- Unchanged fields refresh every second by default. Set the encoder's `transSyncRefreshInterval` to zero to disable refresh. Minimum intervals also apply to refresh. This is best-effort recovery, not acknowledgement-based reliability.
- If nothing is eligible and no RPC is queued, the encoder preserves its output texture and frame index. Manual Writer calls retain their existing behavior and are not change-filtered.
- Native capture hooks still run once per encoding attempt. In Udon, capture runs once per target with an interval-eligible binding. Existing per-frame component capture remains independent of this scheduler.
- The component's `sendMode` overrides only change filtering: Default honors field attributes, On Change forces change filtering with refresh, and Always includes unchanged values. Live switches preserve snapshots and interval clocks. Udon caches the mode once per captured target per encode. RPC and manual writes remain independent.

Existing attributes now affect traffic. To retain unconditional per-encode sending for a field, specify `SendOnChange = false, MinSendInterval = 0`. Rebuild bindings after changing attribute options and recompile Udon programs before building a world. Existing Setup preparation remains the entry point; no alternative SDK-free workflow is required.

## Reproduce

Use the dedicated projects described in [the validation guide](../README.md). Both must reference the real Core worktree and the actual Luma4 package. Do not use `-nographics` for these tests. The runner copies only the test harness, not an alternative implementation.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
$runner = './Validation~/Run-Validation.ps1'
$results = 'F:/Unity/TSMP/Validation-Results/transsync'
$native = 'F:/Unity/TSMP/Validation-NoSDK'
$vrc = 'F:/Unity/TSMP/Validation-VRC'
& $runner -UnityEditor $unity -Project $native -Results $results -Step TransSync
& $runner -UnityEditor $unity -Project $vrc -Results $results -Step TransSyncVm
& $runner -UnityEditor $unity -Project $native -Results $results -Step RpcDelivery
& $runner -UnityEditor $unity -Project $native -Results $results -Step EditorEncoding
& $runner -UnityEditor $unity -Project $vrc -Results $results -Step EditorEncoding
& $runner -UnityEditor $unity -Project $native -Results $results -Step Play
& $runner -UnityEditor $unity -Project $native -Results $results -Step Build
& $runner -UnityEditor $unity -Project $native -Results $results -Step Player
& $runner -UnityEditor $unity -Project $vrc -Results $results -Step Udon
```

`TransSync` checks the native scheduler, metadata generation and legacy manual writes through the real native Encoder. `TransSyncVm` compiles the Udon client programs and executes the real Encoder bytecode, Udon field/event bridge and Luma4 output in the Editor VM. It does not substitute C# proxy calls for the sender. Timing cases inspect/set the scheduler clock state rather than depending on wall-clock sleeps.

Both suites also check component send mode overrides, live switching, refresh and minimum interval preservation. Native tests cover independent instances sharing metadata, serialized multi-edit and undo. Run `TransSync` in the SDK project as well to check editor proxy mode reads. `EditorEncoding` checks stationary Transform output across all three modes in both editor paths. Recorded results for that addition are in [Component Send Mode](./COMPONENT-SEND-MODE.md).

The variable snapshot regression executes grow/shrink/empty transitions with a
shared byte prefix, reference-equivalent unchanged values and a valid-byte
mutation. Failed output and capacity deferral must preserve the last successful
snapshot; retry sends the latest value, and reset starts fresh. Public generic
helpers must still detect an opaque same-prefix length change and return an
independent exact-size snapshot. If an experimental private snapshot retains
excess capacity, the suite mutates its unused tail and verifies it cannot cause
a false send. A final exact-length product reports zero such tail checks rather
than pretending that optional branch executed. Native/current/minimum-SDK
results for the guarded experiment are recorded in
[Binding cost and tail follow-up](../BindingCost/RESULTS.md).

## Recorded Results: 2026-09-12

Core worktree: `F:/Unity/TSMP/TSMP-Core-main`, branch `fix/runtime-bugs`, based on `6695f2d` plus this uncommitted feature. Unity 2022.3.22f1; Worlds SDK 3.10.4-beta.2 with bundled UdonSharp; Luma4 0.0.3; NVIDIA GeForce RTX 4090; Direct3D11.

Logs are under `F:/Unity/TSMP/Validation-Results/transsync-20260912/`. Each result file starts with `PASS` and contains individual assertions.

| Check | Result | Evidence |
| --- | --- | --- |
| SDK-free import | Pass | `20260912-185113-Import.log` |
| Native scheduling, 11 cases | Pass | `final/20260912-191213-TransSync-result.txt` |
| Real Udon VM scheduling, codec capacity, fairness and RPC reservation | Pass | `final/20260912-190737-TransSyncVm-result.txt` |
| Native RPC regression suite | Pass | `final/20260912-191309-RpcDelivery-result.txt` |
| Editor GPU texture loopback, nine frames | Pass | `20260912-190043-Play-result.txt` |
| Windows x64 Development Mono build, stripping disabled | Succeeded, zero errors | `final/20260912-191315-Build.log` |
| Actual Windows Player GPU texture loopback, nine frames | Pass | `final/20260912-191401-Player-result.txt` |
| UdonSharp full client compilation and shared bindings, 13 TSMP programs | Pass | `final/20260912-191405-Udon-result.txt` |
| SDK-free editor drivers, manual encoding and unchanged-state suppression | Pass | `final/20260912-191927-EditorEncoding-result.txt` |
| UdonSharp editor proxy drivers, manual encoding and unchanged-state suppression | Pass | `final/20260912-191942-EditorEncoding-result.txt` |
| English, Korean and Japanese documentation build | Pass | `npm run build` in `Website~` |

The Player executable is `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`; its adjacent `.build-report.txt` records the build report. Loopback covers Transform, a real animated Humanoid rig, Timeline, Component int/Unicode string variables and RPC delivery through Luma4 shaders and GPU readback. It is a regression smoke test, not a bandwidth or latency benchmark.

One pre-existing D3D11 shader warning remains: potentially uninitialized `SampleBlockLuma`. The log also contains Unity Licensing Client startup diagnostics; they did not prevent compilation or the successful build report. IL2CPP/stripping, Quest and an uploaded VRChat client session were not tested. No world upload, commit or release was performed for this feature.

Validation can regenerate Udon program assets with validation-project-specific serialized program GUIDs. Preserve the repository's original program references when reviewing those generated changes; retain the Encoder's updated field metadata. Test-created programs belong only to the isolated validation project.
