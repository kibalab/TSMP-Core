# Unified Unity and VRChat Setup Workflow

## Scope

This is a design review with a small Unity Editor experiment, not an implementation of the proposed workflow. Existing SDK-free support changes remain in the worktree. No package runtime, inspector, prefab, or SDK source was changed during this review.

The intended user workflow is the same in both environments:

1. Install Core and Luma4.
2. Place the same TSMPController prefab in a scene.
3. Configure input/output and synchronization components through the usual inspectors.
4. Enter Play Mode or build through the environment's normal build tools.

There must be no native-controller conversion command, backend selector, manual scripting define, or required Apply Setup click.

## Current Menu Is Not an Appropriate Final Workflow

`Editor/Setup/UnitySampleCreator.cs` currently creates a separate native copy, unpacks nested prefabs, removes missing scripts, clones textures, disables codec discovery, and overrides the decoder input with the encoder output. These are substantive configuration changes, not just a different way of instantiating the same controller.

Calling this converter automatically would hide the extra click without removing its divergent behavior. It also makes package updates and user prefab overrides harder to preserve. Its successful loopback tests do not validate an unchanged, common prefab workflow.

## SDK Constraint Confirmed in Unity

Environment: Unity 2022.3.22f1, VRChat SDK base/worlds 3.10.4-beta.2 and its bundled UdonSharp. Test project: `F:/Unity/TSMP/Validation-VRC`.

The UdonSharp implementation of `RunBehaviourSetup` explicitly refuses to initialize an existing source-prefab component whose backing UdonBehaviour is absent. The method is internal; invoking it through reflection would neither remove this restriction nor be a stable integration API.

`CreateBehaviourForProxy` is public, but in this SDK it retrieves the existing backing behaviour and copies values. It does not create a missing backing behaviour despite its name.

The experiment used a prefab containing a TSMPEncoder without a backing UdonBehaviour. After saving and reopening its scene, the SDK logged:

```text
Cannot setup behaviour on prefab instance, original prefab asset needs setup
```

The encoder still had no backing behaviour. Therefore simply removing serialized Udon components from all prefabs is not enough.

For comparison, the experiment instantiated a component-free prefab, then used the public `AddUdonSharpComponent` API to add Encoder/Decoder children and a Transform Sync component on the prefab root. This succeeded without modifying or unpacking the source prefab.

```text
NeutralBeforeSaveBacking=False
NeutralAfterSceneOpenBacking=False
NeutralPrefabLink=Connected
NeutralSourceUnchanged=True
AddedRootComponentBacking=True
GeneratedEncoderBacking=True
GeneratedDecoderBacking=True
GeneratedAfterReloadAllBacked=True
GeneratedProxyCount=3
GeneratedBackingCount=3
GeneratedPrefabLink=Connected
GeneratedSourceUnchanged=True
```

The executable probe is `F:/Unity/TSMP/Validation-VRC/Assets/Validation/Editor/WorkflowProbe.cs`. Its generated test assets are under `Assets/WorkflowProbe`, separate from package assets and the previous validation scenes.

Reproduction, using only the dedicated validation project:

```powershell
$env:TSMP_VALIDATION_RESULT = 'F:/Unity/TSMP/Validation-Results/21-workflow-probe-result.txt'
$p = Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe' -ArgumentList '-batchmode -quit -projectPath F:/Unity/TSMP/Validation-VRC -executeMethod WorkflowProbe.Run -logFile F:/Unity/TSMP/Validation-Results/21-workflow-probe.log' -WindowStyle Hidden -PassThru
$p.WaitForExit()
```

Process exit code was 0. Full log: `F:/Unity/TSMP/Validation-Results/21-workflow-probe.log`.

## Recommended Direction

### Common Authoring Prefab, Automatically Prepared Instances

Ship a common controller prefab with SDK-neutral setup configuration and required visual/resource references. Do not serialize backing UdonBehaviour components or optional third-party streaming components into the baseline controller. Merely leaving unprepared UdonSharp components in that source prefab is not sufficient, as the experiment demonstrated.

Extend Setup's existing preparation path to ensure the required Encoder/Decoder and codec instances automatically. In the SDK Editor, create new required components with the public UdonSharp API; without the SDK, use ordinary Unity component creation. Keep the existing conditional TSMPBehaviour inheritance and Encoder/Decoder implementations. This is not a second implementation of the protocol or a parallel family of network components.

Generated components remain real, editable components in the scene. Existing assigned Encoder/Decoder references must be reused, not replaced. Preserve user fields and references when saving, duplicating, undoing, or rebuilding configuration. Generate only what is absent, according to the existing configuration flags; do not silently turn a sender-only setup into a sender/receiver pair.

The baseline source prefab can retain its GUID. Existing SDK-configured scenes and customized prefabs should remain on their current path when already valid. Removing SDK-generated data from arbitrary user prefabs during SDK uninstallation is a separate migration problem, not something this experiment proves safe.

### Prepare Before Bindings and Before Build

The ordering must be: resolve/create components, resolve codecs, configure resources, generate bindings, then copy proxy values into Udon. Building binding tables before backing behaviours exist would reproduce the null-target bugs already seen in TSMP.

Editor lifecycle notifications should queue an idempotent preparation pass after import/reload, scene instantiation, and relevant setting changes. Avoid structural scene mutations directly during asset import or validation callbacks. Do not scan and reconstruct every controller on each editor frame.

Run the same preparation automatically before Play Mode and before the relevant build serialization steps. The VRChat build must contain already prepared Udon behaviours: runtime AddComponent or an editor-only Setup callback cannot provide them in a deployed world. Correct ordering relative to SDK proxy stripping remains an implementation and validation requirement.

### Codecs and Resources Are Part of the Same Work

The current Luma4 prefab also contains a backing UdonBehaviour. Fixing only TSMPController would leave an incomplete SDK-free workflow. Neutral codec templates and their environment-aware instance creation must be addressed together, without hard-coding codec implementations into Core.

The creation path must preserve codec-specific serialized settings, materials, hierarchy, and object references. Supporting a one-component Luma4 template must not silently break more elaborate third-party codec prefabs. Whether an existing prepared codec prefab can be instantiated unchanged or needs preparation should be an internal concern, not a second user workflow. The small experiment did not test this codec-template path.

Do not disable `autoDiscoverCodecs`, force a loopback input, or replace a configured Source just because the SDK is absent. The same source-selection rules must apply everywhere. Default loopback behavior, if desired, must be a common sample setting rather than a native-only override.

Do not rewrite read-only package-cache files or indiscriminately remove missing scripts from user scenes. Mutable generated resources must have explicit ownership and must not overwrite package resources or unrelated user assets.

## Alternatives Considered

| Approach | Assessment |
| --- | --- |
| Automatically invoke the current native converter | Smaller change, but retains divergent settings, copied assets and lost prefab links. Not recommended. |
| Remove backing behaviours and rely on automatic SDK repair | Reproduced failure for existing source-prefab components. Insufficient. |
| Modify SDK internals or invoke internal setup via reflection | Fragile SDK-version coupling; also does not remove the existing prefab restriction. Not recommended. |
| Ship different contents for VPM and UPM | Can preserve a familiar sample name, but the distribution channel does not reliably indicate SDK presence. Not the general solution. |
| Common authoring prefab with automatic instance preparation | Recommended direction. Public API creation and scene persistence were confirmed; complete setup/codec/build integration is still required. |

## Next Implementation and Acceptance Tests

- Replace the dedicated native sample creation workflow with the shared automatic preparation path, then remove its menu and native-only documentation instructions.
- Keep normal Setup inspectors and existing user-assigned references. Add no backend-choice UI.
- Update the common Controller, Luma4 template handling, and sample scene together.
- Test repeated preparation, scene save/reload, Undo/Redo, prefab duplication and variants, and multiple controllers without duplication or lost settings.
- Test a read-only package installation, not only writable local file dependencies.
- Drag the same prefab into fresh SDK-free and SDK projects without executing a conversion menu or manually applying Setup.
- Re-run native Editor loopback, Windows Player build/execution, full UdonSharp compile, and SDK local world build with the new workflow. Include a real codec and preserve the existing Transform, humanoid, variable and RPC checks.

The previous native Player and SDK world-build passes in `RESULTS.md` concern the earlier implementation. The new experiment confirms only component creation, backing association, source-prefab immutability and scene persistence. It does not establish end-to-end operation of the proposed redesign.
