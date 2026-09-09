# TSMP Core

Core runtime package for TSMP, the Trans Sync Media Protocol.

Install this package with a codec package such as `TSMP Codec Luma4`, then add `Samples/TSMPController.prefab` to the scene and assign any external input texture in `TSMPSetup`.

Ordinary Unity and VRChat use the same `Samples/TSMPController.prefab`. After placing it in a scene, Setup automatically prepares components, codecs and bindings. No conversion menu is required. Keep automatically prepared resources in `Assets/TSMPGenerated` with your scene. UPM installation does not require VRCSDK.

Existing scene instances retain their original prefab through `Samples/Legacy/TSMPControllerLegacy.prefab`, which preserves the old GUID and component IDs. They are not silently converted. New scene instances use the shared Controller; the legacy sample still needs its original optional dependencies.

Windows x64 Mono (managed stripping disabled) is validated with Unity 2022.3.22f1. VRChat player capture requires the SDK; Animator-based humanoid capture and pose reception do not. IL2CPP and stripping require separate validation of reflection-based fields and RPC methods. See the installation guide for local package paths and migration details.

## Requirements

- Unity 2022.3
- VRChat Worlds SDK 3.9.0 or newer when used in VRChat worlds
- UdonSharp from the VRChat Worlds package when compiling Udon behaviours

## Documentation

https://kibalab.github.io/TSMP-Core/

## Release Status

This package uses `v0.x` release tags while the public API continues to settle.
