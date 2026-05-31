---
title: Troubleshooting
---

# Troubleshooting

Work from the sender outward: encoder, texture transport, decoder, then receiver objects.

Do not start by changing every component. TSMP gives you enough diagnostics to identify the failing stage.

## Quick diagnosis order

1. TX frame index increases.
2. TX payload bytes are greater than zero.
3. The encoded image reaches the receiver input texture.
4. RX header is valid.
5. RX frame is valid.
6. RX message count is greater than zero.
7. Receiver objects are enabled and receive interpolation is not `None`.

Stop at the first failed step and debug there.

## Output is a flat gray texture

Likely causes:

- Encoder output is not assigned.
- Luma4 handler is not assigned.
- No enabled TSMP network component is sending data.
- Setup was not applied after adding components.
- Editor-time encode is disabled.

Check that encoder frame index increases and payload bytes are greater than zero.

## Output changes, but receiver does not move

Check:

1. Decoder input is the captured texture.
2. Decoder `header=yes` and `valid=yes`.
3. Decoder message count is greater than zero.
4. Receiver objects have matching TSMP components.
5. Receive interpolation is not `None`.
6. `Apply Setup` was run after the latest scene changes.

If RX message count is zero, debug the encoder and transport. If RX message count is non-zero, debug receiver bindings and active state.

## CRC mismatch

The received pixels do not match the written header. Check:

- Scaling or filtering.
- OBS color conversion.
- Post processing over the TSMP area.
- Wrong input texture.
- Cropped header area.
- Sender and receiver using incompatible Luma4 settings.

CRC mismatch means the frame is discarded before payload processing. Receiver objects cannot update from that frame.

## Payload buffer is full

The frame cannot fit all selected data.

Fix in this order:

1. Reduce synchronized objects.
2. Reduce selected humanoid bones or blend shapes.
3. Avoid full avatar pose sync unless it is required.
4. Increase output capacity.
5. Run `Apply Setup`.

If this appears only when another player joins, check avatar pose sync and per-player payload growth.

## RPC calls are missed

Short events can be lost if the video path drops the frames carrying them.

Check:

- Receiver loss percentage.
- `rpc` count on TX and RX.
- Duplicate RPC counters.
- Whether the receiving object has the matching network ID.
- Whether the method name passed to `SendTransRPC` exactly matches the public method.

For loopback tests, use `TSMPNetworkGameObjectToggle` first.

## Avatar movement is choppy

Check:

- Receiver loss percentage.
- Encoder frame rate.
- Payload capacity.
- Whether full pose data is being truncated.
- Receive interpolation mode.
- Whether the receiver avatar rig has the expected humanoid targets.

If root motion moves but limbs lag or freeze, the pose payload is usually too large, selected bones are incomplete, or the receiver is skipping frames.

## Luma4 does not appear

Verify:

1. The Luma4 package is imported.
2. The Luma4 codec prefab exists.
3. `TSMPSetup` has refreshed the codec catalog.
4. The selected codec list is not empty.

After fixing package import, click `Refresh Codecs` and `Apply Setup`.

## UdonSharp compile fails

Compile all UdonSharp programs after large script or assembly changes. If Unity reports stale Udon fields, let Unity finish C# compilation, then compile UdonSharp again.

If errors mention unsupported Udon APIs, check whether the script uses a Unity API that is available in C# but not exposed to Udon.

## A value updates in editor but not in VRChat

Check:

- The script belongs to a UdonSharp assembly when required.
- UdonSharp compile completed after the latest code change.
- The field type is supported by TSMP and UdonSharp.
- The receiver component is active in the uploaded world.
- The runtime path does not depend on editor-only APIs.
