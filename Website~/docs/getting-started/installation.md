---
title: Installation
---

# Installation

Choose the installation method for the application you are building. TSMP uses the same component sources in ordinary Unity and VRChat; you do not need a separate standalone branch.

## Requirements

- Unity 2022.3 LTS. The standalone validation uses Unity 2022.3.22f1 on Windows.
- Core and an actual codec package. Start with Luma4.
- A graphics device supporting the codec shaders and asynchronous GPU readback.
- VRChat Worlds SDK 3.9.0 or newer, including UdonSharp, **only for VRChat worlds**.

Without the SDK, the components run as MonoBehaviours. Animator-based humanoid capture, Transform synchronization, TransSync fields and RPC can run in Unity Editor and a Windows Player. Capturing VRChat players requires the SDK; receiving their pose packets and playing them on configured avatar rigs does not require player APIs.

## VRChat: VPM

Add the following repository to VRChat Creator Companion or another VPM-compatible manager:

```text
https://vpm.kiba.red/
```

Install **TSMP Core** and **TSMP Codec Luma4**, or the TSMP bundle containing both. VPM metadata retains the Worlds SDK dependency for this installation route.

Wait for package import and the SDK's script compilation to finish, then use the shared controller below. TSMP prepares its Udon components and bindings automatically.

## Ordinary Unity: UPM

Use Core and Luma4 revisions containing standalone Unity support. Older packages may still declare a Worlds SDK dependency in their UPM metadata; merely deleting SDK scripting symbols will not fix those versions.

The validated installation method is **Add package from disk** in Unity Package Manager:

1. Obtain Core and Luma4 package sources.
2. Select Core's `Packages/com.kibalab.tsmp.core/package.json`.
3. Select Luma4's `Packages/com.kibalab.tsmp.codec.luma4/package.json`.
4. Wait for package resolution and script compilation.

Alternatively, point your project's `Packages/manifest.json` dependencies to those local package folders. Adjust the paths to your machine:

```json
{
  "dependencies": {
    "com.kibalab.tsmp.core": "file:../../TSMP-Core/Packages/com.kibalab.tsmp.core",
    "com.kibalab.tsmp.codec.luma4": "file:../../TSMPCodec-Luma4/Packages/com.kibalab.tsmp.codec.luma4"
  }
}
```

Merge these entries into your existing dependencies; do not replace the whole manifest. UPM installs Unity module dependencies without installing VRCSDK. The VPM SDK requirements are separate from UPM dependencies.

Do not define `UDONSHARP` or `COMPILER_UDONSHARP` manually in a project without the SDK. If removing an SDK from an existing project, also remove its stale custom scripting symbols.

## Add the controller

In both ordinary Unity and VRChat, drag `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab` into your scene. No conversion command or manual Apply Setup step is needed. Setup creates the required components and discovers installed codecs automatically.

The shared sample initially connects Encoder output directly to Decoder input for a local loopback. Assign Decoder Source Texture when you are ready to receive an external stream. SDK presence never changes your chosen input or codec.

Working textures and materials are prepared automatically under `Assets/TSMPGenerated`. Keep these assets with your scene in version control. They keep controllers independent without modifying installed package resources.

The package's Udon program assets are used only with the SDK. The shared Controller is SDK-neutral; other demonstration scenes may still use optional avatars, streaming plugins, or VRChat components and require those dependencies.

## Existing scenes

Existing Controller instances keep their original prefab references and overrides: the previous prefab is retained at `Samples/Legacy/TSMPControllerLegacy.prefab` with its original GUID. New instances use the shared `Samples/TSMPController.prefab`. Existing SDK scenes do not need a conversion command. When moving a legacy scene into an SDK-free project, use the shared Controller and review the scene's other optional dependencies; the legacy prefab is not a universal SDK-free sample.

## Player settings

Windows x64 with the Mono backend and managed stripping disabled is the validated standalone configuration. Keep **Run In Background** enabled when the sender or receiver must keep updating while another application has focus.

IL2CPP and managed stripping need separate validation for your application: reflection is used to discover TransSync fields and invoke RPC methods. Preserve fields and methods used only through reflection when enabling stripping; a successful Mono build does not prove an IL2CPP build works.

Continue with [Quickstart](quickstart.md).
