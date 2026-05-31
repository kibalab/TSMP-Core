---
title: Codec Selection
---

# Codec selection

A codec controls how TSMP bytes are represented as pixels.

The protocol defines the header and payload meaning. The codec defines the visual representation used to carry those bytes through the texture path.

## Luma4

Luma4 is the default codec included with TSMP. It is the baseline path used by the sample prefab and the recommended first choice for setup and troubleshooting.

Use Luma4 when:

- You are setting up TSMP for the first time.
- You need a stable reference path before changing capture settings.
- You are debugging whether a problem is in the scene setup or the transport path.
- You want the sample prefab and default setup to match your scene exactly.

## Luma4 in setup

`TSMPSetup` shows the Luma4 handler in the Codec tab. If it does not appear:

1. Confirm the Luma4 package is installed.
2. Click `Refresh Codecs`.
3. Confirm the codec prefab and catalog assets are present.
4. Click `Apply Setup`.

Sender and receiver must use compatible Luma4 settings. A codec mismatch can still produce visible pixels, but the decoder may read the wrong blocks or wrong byte values.

## Capture quality matters

The codec can only decode what survives the transport path. Avoid:

- Bilinear or trilinear scaling on the encoded area.
- Color correction or tone mapping.
- Video compression over the TSMP region.
- OBS filters that alter pixel values.
- Cropping that removes header or payload blocks.
- Camera effects that draw over the TSMP output.

If the decoder reports CRC mismatch, debug the transport before changing network components.

## Capacity and quality tradeoff

Higher texture resolution gives more payload capacity, but it also costs more rendering, capture, and decode work.

Start with the sample defaults. Increase resolution only when the debug canvas shows payload usage near capacity and you have already reduced unnecessary synchronized data.
