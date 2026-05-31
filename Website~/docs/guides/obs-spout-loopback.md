---
title: OBS and Spout Loopback
---

# OBS and Spout loopback

Loopback is useful for testing the real capture path on one machine.

The expected result is delay, not instant response. For example, a sender-side avatar should move first, then the decoded avatar should follow after the OBS or capture delay.

## Sender path

1. Assign the encoder output render texture.
2. Display the TSMP output through the screen-space or camera output path.
3. Send that visible output to Spout.
4. Install an OBS Spout2 plugin if OBS does not already show a Spout source type.
5. Add the Spout source in OBS.
6. Select the TSMP sender from the Spout source settings.
7. Make sure OBS is not scaling or filtering the TSMP region.

OBS does not include a Spout source as a default built-in source. A common option is the OBS Spout2 plugin, which adds Spout input/output support to OBS.

The OBS preview should show the full TSMP frame. If the preview crops the top header area, decoding will fail.

## Receiver path

1. Feed the OBS output back into a texture source.
2. Assign that texture to `TSMPDecoder`.
3. Keep Luma4 settings compatible with the sender.
4. Run `Apply Setup`.
5. Watch `TSMPDebugCanvas`.

Do not assign the original encoder render texture to the decoder when you are testing OBS. That bypasses the path you are trying to validate.

## Recommended first test

Use `TSMPNetworkGameObjectToggle`.

With `RPCTarget.All`, the sender should toggle immediately. After the OBS delay, the decoded RPC should toggle again. This proves that RPC messages are surviving the texture path.

If local toggle works but delayed toggle does not:

- TX `rpc` should increase.
- RX `rpc` should increase after the delay.
- RX frame loss should stay low.
- The receiver object must have the same network ID.

## Expected behaviour

When loopback is working:

- Encoder frame index increases.
- Decoder frame index follows after delay.
- Receiver-side transforms or avatars follow with the delay introduced by OBS.
- CRC warnings do not appear.
- Loss stays low.
- Payload and message counts are non-zero when data is changing.

## If the receiver does not move

Check:

1. OBS is showing the TSMP image.
2. OBS has a Spout source available through an installed plugin.
3. The receiver input texture is the OBS output, not the encoder texture.
4. The full TSMP frame is visible and not cropped.
5. Decoder `header=yes` and `valid=yes`.
6. Decoder `msg` is greater than zero.
7. Receiver components are enabled and receive interpolation is not `None`.
8. `Apply Setup` was run after adding the receiver component.

## If motion is delayed or choppy

Some delay is expected. Choppy motion usually means:

- OBS or the receiver is dropping frames.
- Encoder frame rate is higher than the transport can carry.
- Payload is near capacity.
- The receiver component is using `Discrete` mode where `Continuous` would look better.

Use the debug canvas before changing visual settings.
