# Unity Support Validation

These scripts are test harnesses, not runtime package dependencies. Run them in dedicated projects only. The runner copies the harness into `Assets/Validation`; it does not copy or patch the TSMP implementation. Use local UPM dependencies pointing directly at the Core and Luma4 worktrees under test. For package-cache validation, use `npm pack` on those package directories, then reference both tarballs from a fresh project's manifest. Do not edit the installed cache.

## Projects

Create a Unity 2022.3.22f1 project without VRChat. Install the modified Core and Luma4 packages with Package Manager's Add package from disk. No custom Udon scripting defines should remain. Unity's standard 3D project modules are sufficient. The real Luma4 shaders and prefab are required.

For VRChat, create a separate Worlds project with the SDK, then reference the same worktree packages instead of published TSMP releases. Never copy SDK DLLs into the SDK-free project. Do not copy the ordinary Unity runtime test scripts into the VRChat project.

## Run

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
$runner = '.\Validation~\Run-Validation.ps1'
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Import
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Workflow
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Play
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Build
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Player
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Inspect
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step InitializeSdk
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step Udon
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step Workflow
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step World
```

Run steps sequentially after import settles. `InitializeSdk` uses the SDK's `EnvConfig.SetActiveSDKDefines` to configure the isolated Worlds project; restart Unity before `Udon`. `World` uses `IVRCSdkWorldBuilderApi.Build`, including SDK validation and callbacks, and never calls an upload API. A first SDK build can change Player settings and request another Unity compile. Wait for that import and retry rather than bypassing validation.

`Build` creates a Development Windows x64 Mono Player with managed stripping disabled. `Player` deliberately does not use `-batchmode` or `-nographics`: an earlier Windows batch-mode Player did not complete GPU readback in the test timeout despite reporting a graphics adapter. The harness enables Run In Background. `Inspect` opens an EditorWindow and executes the six custom inspectors' IMGUI paths, then exits; it is not a screenshot-based layout review.

## Assertions

- The same package Controller prefab in both environments, without a conversion menu or an explicit Apply Setup call in the workflow test.
- Deferred component, codec and binding preparation; independent generated resources; preservation of prefab links and settings through duplication, Undo/Redo, and scene reload.
- Source-prefab hash unchanged, no missing scene scripts, Udon backings present in the SDK environment.
- A codec hierarchy with multiple components preserves both cross-Udon and native-to-Udon serialized references when prepared.
- Real Luma4 encoding texture, header verification and asynchronous GPU readback.
- Six distinct Transform positions/rotations.
- AnimationClip sampled onto valid humanoid Animator avatars, arm rotation and hips position reception.
- Reflection-based int and Unicode string TransSync fields.
- Remote RPC, sender exclusion and duplicate retransmission suppression.
- Blank texture rejection without applying new variable values. The expected header-mismatch error is explicitly allowed only during this negative test.
- Unity native custom inspectors: Setup, Encoder, Decoder, Transform, Humanoid and base NetworkBehaviour.
- All installed Udon programs compiled for the client, with nonempty bytecode checked for TSMP programs; shared Controller and backing Udon binding generation.
- SDK local Windows world bundle, separate from the standalone Player.

To put both endpoints in one test scene, the runtime harness maps sender network IDs to the receiver components after the ordinary Setup binding-generation check. Production usually places endpoints in separate scenes/projects; no decoder dispatch shortcut or direct assignment to received fields is used here.

IL2CPP, non-Windows platforms, other graphics APIs and an uploaded VRChat client session are not implied by these tests. See `WORKFLOW-RESULTS.md` for the shared-prefab run and log paths. `RESULTS.md` records the earlier SDK-isolation work; its manual controller-conversion workflow has been superseded and removed.
