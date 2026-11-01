# Receive Policy Regressions

## Additional callback eligibility controls, 2026-11-01

Three retained cases check the public dispatcher rejecting None/inactive
targets, and one receive callback changing mode or deactivating its target
before a second binding is processed. The second binding's previously received
RawBytes array must retain its original reference/content. These exercise the
existing production policy rather than a replacement receiver.

They pass both for an experimental duplicate-check optimization and again after
that optimization is rejected and the original helpers restored: **50 Native /
51 actual client VM cases PASS**. No production receive behavior changes are
shipped. The candidate's mean benefit and failed upper-tail guards are recorded
in [BindingCost](../BindingCost/RESULTS.md#receive-target-experiment-not-adopted).
Evidence: `F:/Unity/TSMP/Validation-Results/optimization-implementation-20261101`,
with candidate results in decoder-active and restored-code final-native-policy /
final-vm-policy plus final-client-compile. No live VRChat validation is claimed.

These fixtures exercise the real Core decoder dispatch/cache helpers and encoder. They do not replace the receive implementation. Use dedicated projects referencing this checkout and the real Luma4 package.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
$results = 'F:/Unity/TSMP/Validation-Results/receive-policies'
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-NoSDK' -Results $results -Step ReceivePolicies
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-VRC' -Results $results -Step ReceivePoliciesVm
```

The SDK-free step runs C# in the Editor. The SDK step compiles all Udon programs for the client, enters Play Mode, recompiles client bytecode after SDK editor compilation, and interprets fresh program heaps with real UdonBehaviour receivers. It checks both VM return codes and error logs. This is not an uploaded VRChat test or IL2CPP validation.

## Issue #6

The existing eligibility check now rejects `ReceiveInterpolationMode.None` before decoding into cached arrays and before dispatch. No TransSync field, previous array contents, last-variable hash or receive callback changes. Outgoing TransSync and RPC behavior are unchanged. Components without the TSMP receive mode remain supported. SDK edit-mode dispatch reads the proxy; client Udon checks the heap field type before reading an optional mode. Native SDK execution uses the non-logging `TryGetProgramVariable` API, which is not exposed to Udon.

2026-09-13: Unity 2022.3.22f1; SDK-free C# and Worlds 3.10.4-beta.2 / bundled UdonSharp; real Luma4 0.0.3; RTX 4090 / D3D11.

- Before: scalar and eight array cases reproduced unwanted application. The initial outgoing fixture also lacked a native codec assignment; that fixture was corrected to instantiate the real Luma4 prefab.
- After: 11 behavior cases passed in C# and in the client Udon VM; all 27 validation-project Udon programs compiled successfully.
- Cases cover scalar/string preservation, all eight supported array families, reused-array identity/content, same-size/resized/empty incoming arrays, None/Discrete/Continuous transitions, callback/hash state, ordinary non-network components, and actual outgoing frame encoding while None.
- Initial SDK testing found error logging on an optional missing field; the final run asserts no error logs. An intermediate attempt to use `TryGetProgramVariable` in Udon failed compilation, so only native SDK execution uses that API.

Evidence root: `F:/Unity/TSMP/Validation-Results/issue6-20260913`.
- Reproduction: `before/20260913-131007-ReceivePolicies.log`.
- Final C#: `final/20260913-131506-ReceivePolicies.log`.
- Final Udon: `final-udon/20260913-131415-ReceivePoliciesVm.log`.
- Matching `-result.txt` files contain case-by-case results.

No packet format, receive interpolation algorithm, RPC retry budget, package version or release tag changes are involved.

## Issue #8

Transform sync now checks `syncRigidbody` before storing or applying received velocity/angular velocity. Its per-frame update discards pending velocity flags when the option is disabled, even if the receive mode has changed away from Continuous. Transform targets remain available. Re-enabling physics sync cannot replay the discarded velocity targets; a new packet is required. No mass, gravity or kinematic fields were added to the packet.

The same runner now executes 22 behavior cases in native C# and client Udon bytecode. Eleven additional cases cover Discrete/Continuous, enabled/disabled Rigidbody sync, local/world coordinates under a transformed parent, disabling/re-enabling pending physics, missing Rigidbody, outgoing physics capture and local-only physics settings. All passed in both environments; all 27 Udon programs compiled.

Evidence root: `F:/Unity/TSMP/Validation-Results/issue8-20260913`.
- Before: `before-final/20260913-131915-ReceivePolicies.log` reproduces four opt-out velocity failures and two retained-target failures. An earlier fixture was corrected to position its Transform before creating the Rigidbody, avoiding edit-mode physics/Transform synchronization delays.
- Native: `after/20260913-131851-ReceivePolicies.log`.
- Udon: `udon/20260913-131948-ReceivePoliciesVm.log`.

## Issue #9

Continuous BlendShape targets are invalidated when their selection, renderer, mesh, receive mode or active state changes. Remaining selected shapes keep interpolating. Re-enabling a shape does not replay its discarded target; the next received packet can set it again. Mesh bounds are checked in both native C# and Udon.

Ten additional cases passed in SDK-free C# and client Udon VM (32 total). They cover in-place and null selection changes, renderer replacement, same-size/smaller/empty meshes, None/Discrete and component/object disable cycles. Repeated Discrete packets still restore externally modified weights. Full Udon compilation passed.

Evidence: `F:/Unity/TSMP/Validation-Results/issue9`, native `after/20260913-133749-ReceivePolicies.log`, Udon `udon/20260913-133813-ReceivePoliciesVm.log`. `NetworkApiValidation.Run` can inventory the installed SDK's mesh and AnimatorStateInfo externs; set `TSMP_VALIDATION_RESULT` to its output path before running it with Unity `-executeMethod`.

## Issue #10

Animator reception now treats `layerIndices` as a whitelist before changing layer weight, state or time, matching the parameter selection policy. Null/empty selections receive no layers. The check reads the current list, including in-place edits.

Six real-Animator cases cover selected/excluded/empty/null/changed/invalid layers. Before the fix, excluded/empty/null/changed cases reproduced unwanted weight changes. After the fix all 38 receive-policy cases passed in native C# and client Udon VM, with full client compilation. Evidence: `F:/Unity/TSMP/Validation-Results/issue10`, native `after/20260913-134424-ReceivePolicies.log`, Udon `udon/20260913-134446-ReceivePoliciesVm.log`.

## Issue #11

Animator time correction compares the shortest phase distance for looping states and the full normalized-time distance for non-looping states. State changes still use Play/CrossFade independently of the time threshold. Eight real-Animator cases cover boundary crossing, larger loop drift, whole loop cycles, non-loop cycles and completed playback, sub-threshold drift and both transition APIs.

Before: the loop boundary and two non-loop cases failed in C#. Client bytecode additionally exposed checked UInt32-to-Int32 conversion in `Binary.ReadInt32LE` for negative state hashes. Signed byte assembly now preserves the bit pattern without that conversion. The existing `Upper.Once` fixture deliberately retains a negative state hash.

After: all 46 cases passed in C# and Udon VM, with full client compilation. Evidence: `F:/Unity/TSMP/Validation-Results/issue11`, native `after/20260913-134640-ReceivePolicies.log`, Udon `udon-final/20260913-134841-ReceivePoliciesVm.log`. The first Udon run records the discovered integer overflow, not a successful result.

## Issue #3

`TransSyncAttribute.SentEvent` opts a field into a public parameterless completion callback. Snapshot generation and editor binding generation preserve the event name. The native send state and Udon encoder invoke it only for fields included in a successfully written frame, not for unchanged/deferred fields or failed outputs. Existing fields incur no extra event calls. `EncodeNow` rejects reentry while capturing, writing or notifying; native callback exceptions are logged without aborting the remaining commit bookkeeping.

The avatar synchronizer stages the player membership, root inclusion and sequence of its current capture. `CommitAvatarPose` advances only included player entries and included roots, then consumes that staged capture once. Membership invalidation cancels pending capture state. The wire format, default Full mode, interpolation, sampling budgets and 30-frame intervals are unchanged. Local output success is not a delivery acknowledgement.

2026-09-13 results:

- 47 native C# cases passed, including codec failure, missing output, capacity deferral while another field succeeds, latest-value retry, unchanged suppression, callback reentry, snapshot generation and editor binding generation.
- 48 client Udon VM cases passed, including those runtime cases and actual avatar/encoder programs connected through the field/event bridge. All validation-project Udon programs compiled for the client.
- Avatar input uses real SDK `VRCPlayerApi` objects registered with `AddToList`, with deterministic tracking/bone delegates restored after the test. This is an executable client-bytecode test, not live VRChat motion tracking.
- Default Full capture, unsent stationary roots, changed roots after failure and periodic keyframes pass. A seeded 17-player committed baseline exercises the real 16-player root sample budget: failed output consumes no keepalive records; successful retry commits all included entries but only 16 sampled roots.
- Initial fixture attempts incorrectly assumed Luma4 would invoke a custom codec writer and that hidden avatar intervals could be overridden. The final fixture uses a test symbol mode to inject write failures and retains the actual 30-frame intervals. Intermediate failing logs are retained separately.

Evidence: `F:/Unity/TSMP/Validation-Results/issue3`, native `native-final/20260913-141145-ReceivePolicies.log`, Udon `udon-complete/20260913-141016-ReceivePoliciesVm.log`. The updated API is documented in English, Korean and Japanese.

Existing scenes receive the new hidden binding array through the normal script-reload/setup/build preparation workflow. No SDK-free-specific menu or setup path is added. Custom code assigning binding arrays directly must also provide `bindingSentEvents` for fields using this callback; omitted entries mean no callback. Unity/Udon generated program references are excluded from source commits.

## Final Verification of #3, #9, #10, #11 and #12

Windows x64 Development Mono Player (stripping disabled) was rebuilt after all runtime edits: zero build errors and one existing Luma4 `SampleBlockLuma` warning. The resulting executable passed the nine-frame Transform/Humanoid/Timeline/Unicode/RPC GPU loopback plus all eight block expansion pixel/CRC/payload cases. Evidence: `F:/Unity/TSMP/Validation-Results/issues3-9-10-11-12/final`, `20260913-141206-Build.log` and `20260913-141230-Player.log`. Executable: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`.

Live VRChat, OBS/RTMP transport, other graphics APIs and IL2CPP were not tested. No versions, tags or release workflows were changed.

## Final GPU Loopback

After all three fixes (#5, #6, #8), the existing full loopback fixture passed in SDK-free Play Mode and an actual Windows x64 Development Mono Player (stripping disabled). It encodes using real Luma4, uses D3D11 GPU readback, and applies Transform, humanoid, Timeline, integer/Unicode fields and RPC results. The fixture also rejects blank input without changing variables. This is regression coverage for the full transport; the 22 policy-specific cases above run separately in Editor C# and client Udon VM.

Evidence root: `F:/Unity/TSMP/Validation-Results/issues5-6-8-20260913/loopback`.
- Play: `20260913-132047-Play.log`.
- Build: `20260913-132115-Build.log`; succeeded, zero errors, one Luma4 shader warning about potentially uninitialized `SampleBlockLuma`.
- Player: `20260913-132216-Player.log`.
- Executable: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`.
- BuildReport summary: adjacent `TSMPValidation.build-report.txt`.

The separate FFmpeg Editor/Player suite and measurements are recorded in [Streaming validation](../Streaming/README.md#issue-5-output-pacing). Live VRChat/OBS/RTMP transport and IL2CPP were not tested. Validation-generated Udon program references are not committed.
