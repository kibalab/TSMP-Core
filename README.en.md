[한국어](README.ko.md) | **English** | [日本語](README.md)

# TSMP Core

TSMP, the Trans Sync Media Protocol, is an open-source runtime for sending network state, RPC calls, avatar pose data, Animator state, Timeline state, and similar runtime data through texture streams in VRChat worlds.

The Core package contains the scene runtime and setup workflow. Pixel encoding is provided by codec packages. For the default setup, install the Luma4 codec package together with Core.

## Installation

Add the VPM repository in VRChat Creator Companion.

```text
https://vpm.kiba.red/
```

Then install `TSMP Core` and `TSMP Codec Luma4`.

## Quick Start

1. Add `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab` to your scene.
2. Add the `TSMPNetwork*` components you need to the objects you want to synchronize.
3. Open `TSMPSetup`, click `Refresh Codecs`, and select a codec.
4. Click `Apply Setup` to refresh the Encoder, Decoder, codec handlers, and binding table.
5. Broadcast the Encoder output RenderTexture and feed the same TSMP image into the Decoder input RenderTexture.

## Features

- TSMP Encoder and Decoder
- TSMPSetup scene configuration tool
- `[TransSync]` field synchronization
- TSMP RPC through `SendTransRPC(methodName, target)`
- Transform, Rigidbody, Humanoid Pose, VRChat Avatar Pose, Animator, Timeline, and BlendShape sync components
- Automatic codec package discovery and codec selection UI
- Shared runtime APIs, shader includes, and catalog assets for custom codec packages

## Documentation

User guides and developer documentation are available here:

https://kibalab.github.io/TSMP-Core/

## Release Status

TSMP is currently in beta. Package versions and Git tags use the `v0.0.x-beta.x` format.

## License

MIT License. Copyright (c) 2026 KIBA_Labs.
