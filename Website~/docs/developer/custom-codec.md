---
title: Custom Codec
---

# Custom codec

Create a codec when the default Luma4 texture representation does not match your transport needs.

The codec contract is simple:

```text
Core provides header bytes and payload bytes.
The codec writes those bytes into pixels.
The decoder uses the codec to recover the same bytes.
```

For a full shader-level walkthrough, see [Codec implementation guide](./codec-implementation.md).

## Core shader includes

Custom decode shaders normally use two Core cginc files:

| Include | Use it for | API |
| --- | --- | --- |
| `TSMPDecodeCommon.cginc` | Shared decode uniforms, `appdata`/`v2f`, vertex function, block sampling helpers, luma sampling, and YCoCg conversion helpers. | [Scripting API: `TSMPDecodeCommon.cginc`](../scripting-api/decode-common.md) |
| `TSMPDecodeByteOutput.cginc` | Converting a byte-indexed `DecodeByte(index)` function into the RGBA byte output texture read by the decoder. | [Scripting API: `TSMPDecodeByteOutput.cginc`](../scripting-api/decode-byte-output.md) |

Include `TSMPDecodeCommon.cginc` first when your shader needs the shared `v2f`, `_MainTex`, `_BlockSize`, `_SampleSize`, `_StartBlock`, `_ByteCount`, `_ActiveWidthBlocks`, `_SourceWidth`, `_SourceHeight`, `_OutputWidth`, `_OutputHeight`, or `_FlipY` contract. Include `TSMPDecodeByteOutput.cginc` after implementing the byte recovery function for your codec.

## Package shape

Recommended package layout:

```text
com.example.tsmp.codec.mycodec/
  package.json
  Runtime/
    Scripts/
    Shaders/
    Materials/
    Prefabs/
  Samples/
```

The package should depend on:

```json
"com.kibalab.tsmp.core": "1.0.0"
```

Use VPM dependency metadata too if the package is intended for VRChat users.

## Runtime class

Create a component that extends `TSMPCodec`.

```csharp
using K13A.TSMP;
using UnityEngine;

public sealed class TSMPCodecMyCodec : TSMPCodec
{
    private const int SymbolModeMyCodec = 128;

    public Material byteDecodeMaterial;

    public override int GetEncoderSymbolMode()
    {
        return SymbolModeMyCodec;
    }

    public override int GetEncoderPayloadStartRow(int width, int blockSize)
    {
        return 5;
    }

    public override int GetEncoderPayloadCapacityBytes(int width, int height, int blockSize)
    {
        return 0;
    }

    public override bool WriteEncoderPayload(
        Color32[] pixels,
        int width,
        int height,
        int blockSize,
        byte[] payloadBytes,
        int payloadByteCount)
    {
        return false;
    }

    public override void ApplyDecodeOptions()
    {
        selectedDecodeMaterial = byteDecodeMaterial;
    }

#if !COMPILER_UDONSHARP
    public override int SymbolMode => SymbolModeMyCodec;
#endif
}
```

This skeleton is intentionally incomplete. A real codec must write payload bytes and provide matching decode shader/material behaviour.

## Required encoder methods

| Method | Requirement |
| --- | --- |
| `GetEncoderSymbolMode()` | Return the symbol mode written to the frame header. |
| `GetEncoderPayloadStartRow(...)` | Tell Core where payload blocks begin. |
| `GetEncoderPayloadCapacityBytes(...)` | Tell Core how many payload bytes fit. |
| `WriteEncoderPayload(...)` | Write payload bytes into the provided pixel buffer. |

For UdonSharp compatibility, keep logic simple and avoid unsupported C# constructs.

Pick a stable symbol mode value for your package and avoid collisions with other installed codecs. The core encoder and decoder pass this value through the frame header; your codec package owns its meaning.

## Codec options

A codec can expose up to five option bytes in the frame header.

Override:

```csharp
public override int GetEncoderCodecOptionByteCount()
public override int GetEncoderCodecOptionByte(int index)
```

The decoder receives these bytes through `codecOptionBytes` before `ApplyDecodeOptions()` is called.

Use option bytes only for values needed to decode the frame. Do not use them for editor-only display data.

## Decoder materials

For native/editor decode support, override these inside the non-Udon path:

```csharp
public override int DecodeMaterialCount => 1;
public override Material GetDecodeMaterial(int index) => byteDecodeMaterial;
public override void ConfigureMaterials(CodecMaterialContext context)
```

For runtime decode, `ApplyDecodeOptions()` should set fields such as:

- `selectedDecodeMaterial`
- `payloadStartRow`
- `payloadBlockCount`
- any codec-specific state required by the shader

## Native editor writing

If the codec should write frames in editor/native C# paths, implement:

```csharp
public override bool TryWriteFrame(
    Texture2D texture,
    int blockSize,
    byte[] headerBytes,
    byte[] payloadBytes,
    out string error)
```

Keep this method inside `#if !COMPILER_UDONSHARP` if it uses APIs unavailable to UdonSharp.

## Registration

To appear in `TSMPSetup`:

1. Put the codec component in a prefab.
2. Set a stable `codecId`.
3. Set a readable `displayName`.
4. Include package metadata in `package.json`.
5. Add or generate a `TSMPCodecCatalog` that references the prefab.
6. Refresh codecs in `TSMPSetup`.

The encoder should not reference your codec class directly. It should discover and call it through the `TSMPCodec` API.

## Validation checklist

- Encoder payload capacity is correct.
- Header and payload regions do not overlap.
- Codec option bytes round-trip into decoder state.
- Output clears unused regions.
- Decoder rejects corrupted headers.
- The codec works in Unity Play mode and UdonSharp runtime.

## When to write a custom codec

Write a custom codec only when the default Luma4 path cannot satisfy the transport requirements. If the problem is setup, capacity, or OBS filtering, a new codec will not fix it.
