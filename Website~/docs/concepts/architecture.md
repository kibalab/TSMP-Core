---
title: How TSMP Works
---

# How TSMP works

TSMP turns scene data into pixels, sends those pixels through a texture or video path, and turns them back into scene changes.

The same idea works in the Unity editor, in Play mode, and in an uploaded VRChat world. The risky part is the transport path: if the pixels change, the data changes.

## Sender

On the sender side, `TSMPEncoder` looks for enabled TSMP network components. Each component copies its current state into one or more `[TransSync]` fields before the encoder reads them.

The encoder then:

1. Builds a network payload from variable state messages and queued RPC messages.
2. Writes a 56-byte TSMP header with frame index, codec ID, payload size, and CRC.
3. Passes the header and payload to the selected codec.
4. Writes the resulting pixels into the output render texture.

You normally do not edit this process directly. Add or remove sync components, then run `Apply Setup`.

## Transport

The encoded texture can be displayed, captured, streamed, or looped back.

Common transports:

- A VRChat screen-space TSMP output seen by a camera.
- Spout to OBS.
- OBS output back into a receiver texture.
- A local render texture path for testing.

The transport must preserve the TSMP region. Avoid color correction, compression, scaling, filtering, anti-aliasing, or post-processing on the encoded area.

## Receiver

On the receiver side, `TSMPDecoder` reads the input texture. It first validates the frame header. If the header is valid and the CRC matches, it decodes the payload bytes and dispatches messages.

The receiver uses:

- Network ID to find the correct `TSMPNetworkBehaviour`.
- Variable hash to find the correct `[TransSync]` field.
- RPC hash and method name to dispatch TSMP RPC events.

Sender and receiver scenes must therefore be configured consistently. `TSMPSetup` builds the runtime binding tables that make those IDs line up.

## Failure boundaries

TSMP is designed to fail closed:

- Invalid header: the frame is discarded.
- CRC mismatch: the frame is discarded and a warning is logged.
- Malformed payload: payload processing stops and diagnostics are updated.
- Missing binding: that value or RPC cannot be applied.

This is why the debug canvas is useful. It shows which stage failed instead of forcing you to infer everything from object motion.
