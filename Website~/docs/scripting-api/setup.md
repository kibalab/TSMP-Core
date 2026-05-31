---
title: TSMPSetup API
---

# `TSMPSetup`

`TSMPSetup` configures a scene so encoder, decoder, codec handlers, bindings, render textures, and materials agree with each other.

Most users interact with it through the inspector. Scripts and editor tools can call its public methods to refresh the same configuration.

## Main references

| Field | Purpose |
| --- | --- |
| `encoder` | `TSMPEncoder` to configure. |
| `decoder` | `TSMPDecoder` to configure. |
| `sourceTexture` | Texture used by the sender side when needed. |
| `decoderSourceTexture` | Texture read by the decoder. |
| `preferEncoderOutputAsDecoderSource` | Uses encoder output as decoder source for loopback tests. |
| `encoderOutput` | Output render texture assigned to the encoder. |
| `payloadByteTexture` | Byte texture assigned to the decoder. |

## Frame settings

| Field | Purpose |
| --- | --- |
| `width` / `height` | Fallback frame size when no texture provides one. |
| `blockSize` | Pixel block size used by compatible codecs. |
| `sampleSize` | Header/decode sample size. |
| `flipY` | Decoder-side image flip. |

When output and source textures are assigned, TSMPSetup can derive sizes from those textures instead of requiring repeated manual fields.

## Codec discovery

| Field | Purpose |
| --- | --- |
| `autoDiscoverCodecs` | Finds installed codec catalogs and prefab entries. |
| `codecPrefabs` | Codec prefabs available to the scene. |
| `selectedCodecIndex` | Selected codec entry. |
| `codecInstanceRoot` | Parent object for codec instances. |

The Codec tab can show package metadata such as package name, author, and description when the codec package exposes a catalog.

## Render texture setup

| Field | Purpose |
| --- | --- |
| `resizeFrameRenderTextures` | Resizes frame textures to match setup dimensions. |
| `resizeByteRenderTextures` | Resizes byte textures for decoder readback. |
| `autoSizeByteTexture` | Computes the byte texture size from payload capacity. |
| `roundByteTextureWidthToPowerOfTwo` | Makes byte texture width power-of-two friendly. |
| `byteTextureSafetyPixels` | Adds extra pixels to avoid edge overflow. |

## `ApplyNow()`

```csharp
public void ApplyNow()
```

Applies all setup options immediately. It refreshes codec instances, assigns encoder/decoder references, rebuilds network bindings, updates textures, and writes codec configuration.

Run it after:

- Adding or removing `TSMPNetworkBehaviour` components.
- Changing a network ID.
- Changing codec selection.
- Changing output or source textures.
- Changing package imports.

## `EncodeEncoderNow()`

```csharp
public void EncodeEncoderNow()
```

Calls `EncodeNow()` on the configured encoder. This is mainly a debug action for editor-time frame preview.

## `RefreshInstalledCodecs()`

```csharp
public void RefreshInstalledCodecs()
```

Scans installed codec catalogs and updates the codec list. Use it after importing or removing codec packages.

## Setup prefab

For a normal scene, start from:

```text
Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab
```

Place the prefab, assign the textures for your transport path, select a codec, then run `Apply Setup`.
