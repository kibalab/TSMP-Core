---
title: Quickstart
---

# Quickstart

This quickstart creates a basic TSMP sender and receiver setup.

The goal is not to tune performance yet. The first goal is to prove that a TSMP frame can be encoded, transported, decoded, and applied to a visible object.

## 1. Add the controller prefab

Drag this prefab into the scene:

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

The prefab is the recommended starting point. It contains the normal controller objects, codec references, encoder, decoder, and setup entry point.

Keep the prefab together while learning TSMP. Splitting sender and receiver objects is easier after a loopback test works.

## 2. Assign textures

Assign the textures used by your transport path:

| Texture | Purpose |
| --- | --- |
| Encoder output render texture | The texture that `TSMPEncoder` writes. This is what you display, capture, or stream. |
| Decoder input texture | The texture that `TSMPDecoder` reads. In a real stream, this should be the captured TSMP image, not the original encoder texture. |
| Payload byte texture | A decoder work texture used while converting encoded pixels back into bytes. `TSMPSetup` can size this for you. |

For local testing, the output may be routed through Spout, OBS, a camera capture setup, or another loopback path before it reaches the decoder input.

The sender output and receiver input do not have to be the same texture. In a real transport, they should normally be different textures connected by the capture path.

## 3. Pick data to synchronize

Add TSMP network components to the objects you want to send:

| Component | Use it for |
| --- | --- |
| `TSMPNetworkTransformSync` | Object position, rotation, scale, and optional Rigidbody motion. |
| `TSMPNetworkHumanoidPoseSync` | A humanoid Animator rig and selected humanoid bones. |
| `TSMPNetworkBlendShapesSync` | Selected facial or mesh blend shapes. |
| `TSMPNetworkAnimatorSync` | Selected Animator parameters and layer state. |
| `TSMPNetworkTimelineSync` | Timeline or `PlayableDirector` playback state. |
| `TSMPNetworkVrchatAvatarPoseSync` | Mirroring live VRChat player poses into avatar instances. |
| `TSMPNetworkGameObjectToggle` | A simple Interact plus TSMP RPC test. |

Only enabled components on active GameObjects participate. Disable a component or GameObject to stop it from sending and applying TSMP data.

## 4. Apply setup

Select the object with `TSMPSetup` and click `Apply Setup`.

Run this again after you:

- Add or remove TSMP network components.
- Move synchronized objects between prefabs or scene objects.
- Change Luma4 or texture references.
- Change manual network IDs.
- Replace encoder, decoder, or codec objects.

`Apply Setup` is important because it rebuilds the binding table used by both encoder and decoder.

## 5. Test the stream

Enter Play mode or test in VRChat.

Expected signs of a working stream:

- The encoder output texture changes when synchronized objects move.
- The encoder frame index increases.
- Encoder payload bytes are greater than zero when data is selected.
- The decoder reports a valid header and valid frame.
- Receiver-side objects update after the capture path delay.
- `TSMPDebugCanvas` shows frame index, payload, message, and loss information.

For a capture loopback, see [OBS and Spout loopback](../guides/obs-spout-loopback.md).

## Recommended first test

Use `TSMPNetworkGameObjectToggle` first.

With `RPCTarget.All`, Interact should toggle the object immediately on the sender. After the stream delay, the decoded RPC should toggle it again. This proves that short event messages survive the path.

After that works, add `TSMPNetworkTransformSync`. Then add higher-bandwidth components such as humanoid pose or VRChat avatar pose.

## If nothing moves

Check these in order:

1. `TSMPEncoder` frame index increases.
2. Encoder payload bytes are greater than zero.
3. The encoded texture is visible in the transport path.
4. `TSMPDecoder` reports `header=yes` and `valid=yes`.
5. Decoder message count is greater than zero.
6. Receiver objects have matching TSMP network components.
7. Receive interpolation is not `None`.
8. `TSMPSetup` was applied after the last scene change.
