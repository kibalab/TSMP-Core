---
title: TSMPDecoder
---

# TSMPDecoder

Use `TSMPDecoder` on the receiver side. It reads a TSMP input texture and applies decoded messages to matching scene objects.

Most decode problems are caused by the input texture not containing an unmodified TSMP image. Confirm the texture path before changing receiver components.

## What you need

- An input texture that contains the encoded TSMP frame.
- A compatible Luma4 handler.
- A payload byte texture.
- Matching TSMP network behaviours and bindings.
- `TSMPSetup` applied after references are assigned.

## How to use it

1. Assign the input texture.
2. Use the same Luma4 path as the sender.
3. Assign or auto-size the payload byte texture through `TSMPSetup`.
4. Make sure receiver objects have matching TSMP network components.
5. Click `Apply Setup`.
6. Watch logs or `TSMPDebugCanvas` for frame status.

## What valid decode looks like

In `TSMPDebugCanvas`, a working decoder should show:

- `valid=yes`
- `header=yes`
- A frame index that changes over time.
- A payload size greater than zero when data is being sent.
- Message counts that match the kind of data being sent.
- Low loss during steady playback.

If the frame is valid but objects do not move, check network IDs, receive interpolation, and whether receiver objects are active.

## Decode order

The decoder works in stages:

1. Read the header area from the input texture.
2. Validate magic, version, header size, and CRC.
3. Choose a codec handler from the codec ID.
4. Decode payload bytes into the payload byte texture.
5. Read payload bytes.
6. Dispatch variable state messages and RPC messages.

When a stage fails, later stages are skipped. For example, a CRC failure means payload decoding does not start.

## Receive interpolation

Each `TSMPNetworkBehaviour` has a receive mode:

| Mode | Result |
| --- | --- |
| None | Ignore received values. |
| Discrete | Apply each received value directly. |
| Continuous | Smooth toward received targets where the component supports it. |

Use `None` when a component should send data but not apply incoming data on that object.

## CRC failures

If the decoder logs a CRC mismatch, the frame was damaged before decode. Check scaling, filtering, compression, and Luma4 setting mismatch first.

Typical causes:

- OBS resized the TSMP image.
- A material or camera effect altered pixels.
- The receiver input is the wrong texture.
- The header area is cropped.
- Sender and receiver use incompatible texture layout settings.

## Common fixes

| Symptom | Try this |
| --- | --- |
| `header=no` | Confirm the full TSMP image reaches the decoder input. |
| `valid=no` with CRC warnings | Remove scaling/filtering/color processing from the transport. |
| `msg=0` | Confirm encoder payload is non-zero and setup bindings exist. |
| RPC count stays zero | Test with `TSMPNetworkGameObjectToggle` and check frame loss. |
| Values are received but not applied | Check receive interpolation and active state. |
