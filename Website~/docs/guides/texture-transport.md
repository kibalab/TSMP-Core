---
title: Texture Transport
---

# Texture transport

TSMP only works if the encoded pixels survive the path from encoder to decoder.

Treat the TSMP area like a data bus. A change that looks visually harmless can still corrupt a byte.

## Basic rule

Treat the TSMP area like data, not video. It should be copied exactly.

Avoid:

- Scaling the TSMP image.
- Bilinear or trilinear filtering.
- Anti-aliasing.
- Color grading.
- Tone mapping.
- Bloom or post processing over the TSMP area.
- Video compression that changes pixel values.
- Cropping the header or payload region.

## VRChat screen-space output

For VRChat camera capture, the TSMP output should be displayed with the TSMP camera or screen-space material.

The usual setup is:

1. Apply the TSMP output material to a cube or similar mesh.
2. Put the VRChat camera inside that mesh.
3. Make the TSMP image fill the capture view.
4. Keep the full header and payload region visible.

If the camera sees only part of the mesh, the decoder may report missing header, CRC mismatch, or zero messages.

## Resolution

Higher resolution gives more payload capacity, but it also costs more rendering and readback work.

Start with the sample prefab defaults. Increase resolution only when:

- `Payload buffer is full` appears.
- Debug canvas shows payload usage near capacity.
- You already reduced unnecessary sync data.

Do not increase resolution to hide a transport problem. CRC errors should be fixed by preserving pixels, not by using more pixels.

## Filtering settings

For textures that carry TSMP data:

- Use point filtering where possible.
- Avoid mipmaps.
- Disable anti-aliasing on TSMP render textures.
- Keep color space and sampling consistent between sender and receiver.
- Do not resize the image in OBS unless you know the TSMP region stays pixel-exact.

## OBS and capture settings

When using OBS:

- Install a Spout2 plugin if you need Spout input or output in OBS.
- Capture at the TSMP output resolution when possible.
- Avoid scaling filters.
- Avoid color correction filters.
- Keep the TSMP region fully visible.
- Verify the receiver input texture is the OBS output, not the original encoder render texture.

## FFmpeg publishing from Unity

`TSMPFfmpegRtmpPublisher` can send a Unity texture to an RTMP server using an externally installed FFmpeg executable. This is a desktop Unity component, not an Udon component that runs inside VRChat.

Assign `Source Texture`, set `FFmpeg Path` and the RTMP destination, then call `StartPublishing()` or enable `Auto Start`. Enable `Publish In Edit Mode` only when you also want editor-time capture. `StopPublishing()` stops the session; disabling or destroying the component also stops it.

Keep `Use Source Dimensions` enabled to use the texture's actual size. With it disabled, `Width` and `Height` must exactly match the source. A mismatch is rejected with a warning; the publisher never resizes TSMP pixels. YUV420p requires even width and height.

Dimensions and frame rate are fixed when a session starts. Stop and restart after changing those settings or the source resolution. Removing or resizing the source stops publishing before incompatible bytes reach FFmpeg. A previous session's pending GPU request is discarded after restart.

`Flip Vertical` reverses complete rows without changing their size. `Repeat Last Frame When Idle` repeats the most recent frame when no new capture is ready. Process failures appear in `Last Error` and warning logs; enable `Log Ffmpeg Output` for additional process output. H.264 compression and color conversion can still corrupt TSMP data, so verify the decoded result through your actual transport.

## First transport test

Before synchronizing a complex avatar:

1. Add `TSMPNetworkGameObjectToggle`.
2. Verify the local toggle.
3. Verify the delayed decoded toggle through the texture path.
4. Add transform sync.
5. Add higher-bandwidth sync components last.

If the toggle does not arrive, fix transport and decoder status before adding more data.

## Acceptance measurements

For the transport you intend to use, compare known transmitted sequences and complete payload bytes with the received result. Header CRC and `valid=yes` alone do not prove payload integrity. Keep a local Spout roundtrip, a compressed video roundtrip and an actual VRChat session as separate checks.

Record target Hz, actual published Hz, applied Hz, sender deadline misses, receiver misses, data mismatches, source age and p95 latency alongside CPU/GPU load. A receiver accepting every published frame does not prove the sender maintained its target rate. Choose latency and update-rate budgets before the trial, then test restart, reconnection, player join/leave and a longer run at the intended population.

RPC uses finite repeats and a recent 32-event deduplication cache; it has no delivery ACK or persistent exactly-once guarantee. Include sender restarts with reused IDs and old video reappearing in application tests. A successful local loopback does not establish these behaviors for an unmeasured external route.

The repository's `Validation~/VideoTransport` probe compares complete payloads after finite offline FFmpeg video roundtrips. It reports rejected or changed frames as failures. This is separate from live OBS/Spout, network and VRChat checks; passing one compression setting does not certify other settings or arbitrary content.
