---
title: Scene Checklist
---

# Scene checklist

Use this checklist before testing in VRChat, recording an OBS loopback, or sharing a sample scene with someone else.

## Controller

- `TSMPController.prefab` is in the scene, or the scene has equivalent `TSMPSetup`, `TSMPEncoder`, `TSMPDecoder`, and Luma4 codec objects.
- `TSMPSetup` has the intended encoder and decoder assigned.
- Luma4 is selected in the Codec tab.
- `Apply Setup` has been clicked after the latest changes.
- Codec runtime instances are under the expected controller object and are not duplicated.

## Textures

- Encoder output is a valid `RenderTexture`.
- Decoder input points to the captured TSMP texture, not an unrelated preview texture.
- Payload byte texture exists and is large enough for the decoded payload.
- Sender and receiver use the same visible TSMP area.
- The encoded texture is not scaled, blurred, color-corrected, post-processed, or compressed before decode.
- Filter mode is point where possible, mipmaps are off, and anti-aliasing is not applied to data textures.

## Network components

- Objects that should send data have a TSMP sync component.
- The GameObject and component are enabled.
- Network IDs are unique.
- Receiver objects have matching TSMP components and bindings.
- Receive interpolation is not set to `None` unless you intentionally want to ignore incoming values.
- For `TSMPNetworkBlendShapesSync`, only the blend shapes you actually need are selected.
- For humanoid sync, the selected bones include the animated body parts you expect to see.

## Capacity

- Encoder payload bytes are below usable payload bytes.
- Full avatar pose sync is used only when necessary.
- Blend shape sync includes only selected keys.
- Rigidbody sync is enabled for physics-driven transform objects.
- Adding a second player or another synced object does not push payload usage to capacity.

## Runtime check

- Encoder frame index increases.
- Decoder frame index follows after transport delay.
- Decoder header and frame are valid.
- Loss stays low in `TSMPDebugCanvas`.
- TX message counts match the features you enabled.
- RX message counts are not stuck at zero.
- Last error is empty or clearly understood.

## Before blaming the decoder

Most TSMP issues come from setup or transport, not the final apply step. Check the stages separately:

1. Does the sender write non-zero payload?
2. Does the receiver see a valid header?
3. Does the receiver see messages?
4. Do network IDs and receive modes allow those messages to apply?
