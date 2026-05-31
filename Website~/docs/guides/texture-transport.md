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

## First transport test

Before synchronizing a complex avatar:

1. Add `TSMPNetworkGameObjectToggle`.
2. Verify the local toggle.
3. Verify the delayed decoded toggle through the texture path.
4. Add transform sync.
5. Add higher-bandwidth sync components last.

If the toggle does not arrive, fix transport and decoder status before adding more data.
