---
sidebar_position: 1
title: Introduction
---

# TSMP

TSMP, Texture Stream Messaging Protocol, sends structured runtime data through texture frames.

In a VRChat world, that means a sender can encode scene state into a visible texture, a capture or streaming path can carry that texture, and a receiver can decode the texture back into object movement, avatar pose, blend shapes, animator state, timeline state, or TSMP RPC events.

## Where to start

Start with Getting Started if you want to place TSMP in a scene, use the built-in sync components, or debug a texture transport path.

Use the Developer and Scripting API sections only when you are writing custom TSMP components, tools, tests, or codec packages.

## Mental model

TSMP is easiest to understand as a four-part pipeline:

```text
Scene data -> TSMPEncoder -> texture/video path -> TSMPDecoder -> scene data
```

- Scene data comes from enabled `TSMPNetwork*` components.
- `TSMPEncoder` writes a TSMP frame into an output render texture.
- The texture path may be a VRChat camera view, Spout, OBS, or another capture route.
- `TSMPDecoder` reads the received texture and applies messages to matching TSMP network components.

The texture is the result of encoded data. Scaling, filtering, color correction, compression, or cropping can damage the data.

## Start with the prefab

The recommended first setup is:

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

Drag this prefab into your scene, assign the textures and references it asks for, then run `Apply Setup` on `TSMPSetup`.

Start with a loopback test before building a complex scene. A loopback proves that the encoder, transport, decoder, bindings, and receiver objects agree with each other.

## What TSMP adds to a scene

| Component | What you use it for |
| --- | --- |
| `TSMPSetup` | Configures references, codec selection, render textures, bindings, network IDs, and runtime tables. |
| `TSMPEncoder` | Creates the outgoing TSMP frame in an output render texture. |
| `TSMPDecoder` | Reads an incoming TSMP texture and dispatches decoded messages. |
| `TSMPNetwork*` components | Select which scene data should be sent or received. |
| `TSMPDebugCanvas` | Shows frame, bandwidth, loss, payload, header, and error diagnostics in-game. |

TSMP includes Luma4 as the default codec. The user-facing setup path uses Luma4 by default.

## What TSMP is good at

- Mirroring object transforms through a texture stream.
- Synchronizing data between instances.
- Replaying avatar pose data with transport delay.
- Sending selected humanoid pose, blend shapes, animator, or timeline state.
- Triggering RPC-like actions such as toggling a GameObject.
- Inspecting transport quality inside a VRChat world.

## What TSMP does not hide

TSMP still depends on the quality of the texture path. If the received pixels are changed, the decoder may reject frames or apply fewer messages. Always debug in this order:

1. Sender produces a frame.
2. The texture path carries the full image without alteration.
3. Decoder accepts the header and payload.
4. Receiver objects have matching TSMP components and network IDs.

## Next steps

1. [Install TSMP](./getting-started/installation.md) from the VPM repository.
2. Follow the [quickstart](./getting-started/quickstart.md) with `TSMPController.prefab`.
3. Use the [scene checklist](./getting-started/scene-checklist.md) before testing in VRChat.
4. Keep [troubleshooting](./troubleshooting/index.md) open while setting up your first transport path.
