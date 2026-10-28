---
title: TSMPDecoder
---

# TSMPDecoder

## Combined byte output

`useCombinedByteOutput` defaults to `true`. In the predicted path, compatible codec shaders write the 56-byte decoded header and payload directly into the private RGBA8 readback target, removing the separate packing pass and intermediate payload write. The immutable source snapshot, header CRC validation, exact-length prediction check and same-snapshot fallback are unchanged. This does not change the wire datagram or guarantee a delivery rate.

Shaders opt in by implementing both `_TSMPHeaderTex` and `_TSMPHeaderPixels`. Existing codecs without these properties keep the separate packing path. The switch is in Diagnostics > Advanced Decode. `combinedByteOutputCount` counts submissions, including predictions later rejected or retried; it is not a received-frame count. `ResetDecodeDiagnostics()` resets it.

Use `TSMPDecoder` on the receiver side. It reads a TSMP input texture and applies decoded messages to matching scene objects.

Most decode problems are caused by the input texture not containing an unmodified TSMP image. Confirm the texture path before changing receiver components.

## What you need

- An input texture that contains the encoded TSMP frame.
- A compatible Luma4 handler.
- A payload byte texture.
- Matching TSMP network behaviours and bindings.
- `TSMPSetup` automatic preparation completed after references are assigned.

## How to use it

1. Assign the input texture.
2. Use the same Luma4 path as the sender.
3. Assign or auto-size the payload byte texture through `TSMPSetup`.
4. Make sure receiver objects have matching TSMP network components.
5. Allow automatic Setup preparation to finish; use `Apply Setup` only for an optional manual refresh.
6. Watch logs or `TSMPDebugCanvas` for frame status.

## Frame window

In the decoder's **Decode** section, keep **Filter Frames** enabled for live reception. **Window Size** defaults to **256** frames and is adjustable on the receiver; the sender needs no matching setting.

The decoder normally ignores repeated or older frame numbers. It also recognizes frame zero as a possible restart when the last applied number is at least one window: with 256, `256 -> 0` is allowed but `255 -> 0` is not. Natural UInt32 counter wrap is handled separately. No textures are buffered for this window and the datagram is unchanged.

This does not identify sessions: an old zero can look like a restart, and missing frame zero can prevent restart detection. Disable the filter for intentional seeking through recorded frames, understanding that old values may then be applied. See [exact ordering rules and limitations](../scripting-api/decoder.md#frame-window).

## What valid decode looks like

In `TSMPDebugCanvas`, a working decoder should show:

- `valid=yes`
- `header=yes`
- A frame index that changes over time.
- A payload size greater than zero when data is being sent.
- Message counts that match the kind of data being sent.
- Low loss during steady playback.

If the frame is valid but objects do not move, check network IDs, receive interpolation, and whether receiver objects are active. `valid=yes` means the frame passed the selected header/structural checks; it does not certify byte-for-byte equality with the sender. CRC32 covers header bytes 0..51, not the payload. Structurally valid changes to values or RPC arguments can therefore go undetected. Test the actual transport against known sent bytes or application markers.

## Decode order

The first frame and the fallback path work in stages:

1. Read the header area from the input texture.
2. Validate magic, version, header size, and CRC.
3. Choose a codec handler from the codec ID.
4. Decode payload bytes into the payload byte texture.
5. Read payload bytes.
6. Dispatch variable state messages and RPC messages.

After a valid frame, **Use Predicted Readback** is enabled by default. The decoder can recover the current header and payload together using the previous configuration, reducing two sequential GPU readback waits to one. It still checks the current header before applying any values or RPCs. Changed settings or payload length cause the same captured image to be processed through the fallback path.

No extra material assignment is required. You can disable the optimization under **Diagnostics > Advanced Decode** for comparison. Manual layout and safety modes retain sequential decoding. CRC failures discard the frame even when speculative GPU decoding has already run. This improves processing opportunities, not guaranteed delivery or a fixed receive frame rate.

## Overlapping readbacks

**Overlap Readbacks** is enabled by default under **Diagnostics > Advanced Decode**. The decoder can retain two images, starting the next capture while the first is waiting on the GPU. Results are applied in capture order. If both slots are occupied, it skips a new capture instead of extending a queue. Disable this option to compare with single-slot operation; no sender change is needed.

This can reduce missing frames when a readback takes longer than one update, but uses more memory and can increase application latency and GPU/CPU load. Snapshot texel storage depends on the input format: ARGB32 uses 4 bytes/pixel, ARGBHalf 8, and ARGBFloat 16; other inputs or unsupported formats can use the Float32 fallback. In the Udon path, matching applies to supported RenderTexture formats; other texture types use Float32. At 1280x720 this is about 3.52 / 7.03 / 14.06 MiB per snapshot respectively. Two allocated slots double those amounts, excluding byte textures, CPU buffers and driver overhead. It cannot recover frames that never reached the input texture. **Pending Frames** shows occupied slots and **Skipped Busy Captures** counts attempts rejected at capacity, not unique lost frames.

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
