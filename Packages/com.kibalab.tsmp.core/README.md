# TSMP Core

Core runtime package for TSMP, the Trans Sync Media Protocol.

Install this package with a codec package such as `TSMP Codec Luma4`, then add `Samples/TSMPController.prefab` to the scene and assign any external input texture in `TSMPSetup`.

Ordinary Unity and VRChat use the same `Samples/TSMPController.prefab`. After placing it in a scene, Setup automatically prepares components, codecs and bindings. No conversion menu is required. Keep automatically prepared resources in `Assets/TSMPGenerated` with your scene. UPM installation does not require VRCSDK.

Existing scene instances retain their original prefab through `Samples/Legacy/TSMPControllerLegacy.prefab`, which preserves the old GUID and component IDs. They are not silently converted. New scene instances use the shared Controller; the legacy sample still needs its original optional dependencies.

Windows x64 Mono (managed stripping disabled) is validated with Unity 2022.3.22f1. VRChat player capture requires the SDK; Animator-based humanoid capture and pose reception do not. IL2CPP and stripping require separate validation of reflection-based fields and RPC methods. See the installation guide for local package paths and migration details.

Decoder CRC recovery uses alternative hypotheses supplied by compatible codec materials after the ordinary CRC check fails. `maxCrcRecoveryAttempts` defaults to two additional payload attempts; zero disables recovery. Optional `recoverHeaderCrc` enables header retries and defaults to false: the measured damaged-header recoveries did not produce additional intact payloads. Every attempt uses the same slot-owned source snapshot and captured settings. Clean frames keep the ordinary path. Failed frames can add two GPU readbacks by default, or four when header recovery is enabled, and delay later queued frames.

Recovery preserves the sender raster, packet layout and CRC algorithm. A recovered header must declare a protected payload, and payload recovery requires a valid header and payload checksum. State and RPC application still follow complete integrity validation. Luma4 supplies alternative calibration models, RGB16 Refine supplies cross-channel models, and Color256 Robust Refine supplies alternative distance weights. Unsupported codec materials keep their existing rejection behavior. Recovery is finite and cannot reconstruct arbitrary information lost to video compression or missing frames.

`recoverSparsePayload` defaults to true. If both payload recovery modes fail, it can combine at most two observed byte alternatives across at most eight differing positions, for protected payloads up to 8,192 bytes. It requires the two-attempt budget, adds no GPU readback, and rejects ambiguous CRC candidates before performing the usual full payload validation. CPU work and up to one retained 8,192-byte baseline buffer per decoder slot are added only after supported protected-payload failures. Disable this option to retain only whole-payload recovery modes. See the repository's `Validation~/SparseRecovery` for finite-file, safety and Udon VM validation and processing-cost limits.

## Requirements

- Unity 2022.3
- VRChat Worlds SDK 3.9.0 or newer when used in VRChat worlds
- UdonSharp from the VRChat Worlds package when compiling Udon behaviours

## Documentation

https://kibalab.github.io/TSMP-Core/

## Release Status

TSMP Core 1.0.0 is a stable release incorporating all changes from the 0.3.0 betas since 0.2.0. Install Luma4 1.0.0 alongside Core. See CHANGELOG.md for the cumulative changes and compatibility notes.
