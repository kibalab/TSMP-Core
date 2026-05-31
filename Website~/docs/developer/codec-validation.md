---
title: Codec Validation
---

# Codec validation

Validate a custom codec before using it in a world. A codec can compile and still fail after camera capture, scaling, readback, or package import.

## Basic checks

| Check | Expected result |
| --- | --- |
| Codec appears in Setup | Catalog and prefab discovery work. |
| Author and description appear | Package metadata can be read. |
| Encode one frame | Output texture changes and header remains readable. |
| Decode one frame | Header CRC passes and payload bytes recover. |
| Switch codec away and back | No stale pixel blocks remain. |

## Byte round-trip test

Use a deterministic payload:

- Increasing bytes `0, 1, 2, ...`.
- Alternating bytes `0x00, 0xFF`.
- Random bytes with a fixed seed.
- Short payload smaller than one row.
- Payload that ends exactly on a row or block boundary.

After decoding, compare every byte up to `PayloadSize`.

## Header and layout test

Confirm the codec respects:

- Header size and header location.
- `PayloadSize`.
- Payload start row or block.
- Block size.
- Decode sample size.
- Codec option bytes.

The decoder should never need to guess the payload size from visible pixels.

## Capture path test

Test through the real path users will use:

- In-editor loopback.
- VRChat runtime capture.
- OBS/Spout or equivalent if the world uses it.
- The intended frame size.
- The intended frame rate.

Watch `TSMPDebugCanvas` for bitrate, loss, frame index, codec ID, and decoder errors.

## UdonSharp test

When the codec has UdonSharp runtime code:

- Compile all UdonSharp programs.
- Test in Play mode.
- Test in an uploaded VRChat world when the feature depends on client runtime APIs.
- Avoid generic methods, unsupported APIs, and complex expressions that can trigger UdonSharp binder failures.

## Failure patterns

| Symptom | Likely cause |
| --- | --- |
| Header valid but payload wrong | Payload start/block layout mismatch. |
| CRC warning | Header pixels corrupted or sampled incorrectly. |
| Stale blocks after codec switch | Output texture not fully overwritten or cleared. |
| Works in editor but not runtime | UdonSharp API or VRChat runtime API difference. |
| Works at one size only | Shader assumes a fixed texture size. |
