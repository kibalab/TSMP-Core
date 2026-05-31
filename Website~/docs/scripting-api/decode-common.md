---
title: TSMPDecodeCommon.cginc
---

# `TSMPDecodeCommon.cginc`

`TSMPDecodeCommon.cginc` is the shared shader include for TSMP codec decode passes. It declares the common source texture uniforms, vertex input/output structs, a default vertex function, block sampling helpers, and YCoCg conversion helpers.

Use it before codec-specific decode logic when your shader samples a TSMP source frame by pixel or block.

Include path:

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"
```

## Texture inputs

| Symbol | Type | Purpose |
| --- | --- | --- |
| `_MainTex` | `Texture2D` | Source TSMP frame texture. |
| `sampler_MainTex` | `SamplerState` | Sampler used for `_MainTex`. |

Decode shaders should use point-like sampling for exact byte recovery. The helpers call `SampleLevel(..., 0.0)` to avoid mip selection.

## Common uniforms

| Uniform | Purpose |
| --- | --- |
| `_BlockSize` | Pixel size of one codec block. |
| `_SampleSize` | Number of pixels sampled inside a block. If not set, helpers choose a default from `_BlockSize`. |
| `_StartBlock` | First payload block index. |
| `_ByteCount` | Number of valid payload bytes requested by the decoder. |
| `_ActiveWidthBlocks` | Number of writable/readable blocks per row. |
| `_SourceWidth` | Source frame width in pixels. |
| `_SourceHeight` | Source frame height in pixels. |
| `_OutputWidth` | Byte output texture width in pixels. |
| `_OutputHeight` | Byte output texture height in pixels. |
| `_FlipY` | Flips source sampling vertically when non-zero. |

These values are assigned by the decoder/material setup path. Custom codecs should not guess them from texture dimensions when the uniforms are available.

## Vertex structs

The include defines:

```hlsl
struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};
```

Use these when the decode pass only needs position and UV. If your shader needs custom interpolators, define your own structs in a separate pass instead of relying on this default shape.

## `vert(appdata v)`

```hlsl
v2f vert(appdata v)
```

Default vertex function. It writes clip-space position with `UnityObjectToClipPos(v.vertex)` and forwards `v.uv`.

## Pixel sampling helpers

### `SampleRgbAtTopLeftPixel(float2 pixel)`

Samples `_MainTex` at the center of the given source pixel and returns RGB.

The Y coordinate is calculated with `_FlipY`:

```hlsl
float rawY = (pixel.y + 0.5) / _SourceHeight;
float uvY = lerp(rawY, 1.0 - rawY, _FlipY);
```

### `SampleLumaAtTopLeftPixel(float2 pixel)`

Samples RGB at a pixel and returns average luma:

```hlsl
dot(rgb, float3(0.33333334, 0.33333334, 0.33333334))
```

This is a simple channel average, not perceptual luma.

## Block sampling helpers

### `SampleBlockRgb(float blockX, float blockY)`

Samples a square region centered inside the block and returns the average RGB.

Sample size rule:

- If `_SampleSize > 0.5`, uses `floor(_SampleSize)`.
- Otherwise uses `4` when `_BlockSize >= 8`, or `3` for smaller blocks.
- Clamps sample size to `1..min(_BlockSize, 8)`.

The loop is capped at `8x8`, so codec sampling stays predictable.

### `SampleBlockLuma(float blockX, float blockY)`

Same as `SampleBlockRgb`, but returns average luma.

### `SampleBlockByIndex(float blockIndex)`

Converts a linear block index to `(blockX, blockY)` using `_ActiveWidthBlocks`, then calls `SampleBlockRgb`.

### `SampleLumaBlockByIndex(float blockIndex)`

Converts a linear block index to `(blockX, blockY)`, then calls `SampleBlockLuma`.

## Payload block helper

```hlsl
float PayloadBlockIndex(int symbolIndex)
```

Returns:

```hlsl
_StartBlock + symbolIndex
```

Use this to map codec symbol indices to payload block indices without hard-coding the header/payload boundary.

## Integer helper

```hlsl
int FloorDivNonNegative(int value, float divisor)
```

Returns `floor(value / divisor)` for non-negative values. Use it when mapping byte indices to symbol indices.

## Color conversion helpers

```hlsl
float3 TSMP_RgbToYCoCg(float3 c)
float3 TSMP_YCoCgToRgb(float3 c)
```

The include also defines compatibility aliases:

```hlsl
#define RgbToYCoCg TSMP_RgbToYCoCg
#define YCoCgToRgb TSMP_YCoCgToRgb
```

Use the `TSMP_` names in new shader code when possible to avoid macro/name conflicts.

## Common use with `TSMPDecodeByteOutput.cginc`

A typical decode shader uses `TSMPDecodeCommon.cginc` for sampling helpers, implements `DecodeByte(int byteIndex)`, then includes `TSMPDecodeByteOutput.cginc` to write the byte output texture.

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"

int DecodeByte(int byteIndex)
{
    float blockIndex = PayloadBlockIndex(byteIndex);
    float luma = SampleLumaBlockByIndex(blockIndex);
    return (int)round(saturate(luma) * 255.0);
}

#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

Real codecs usually decode multiple bits or symbols per block. The example only demonstrates the include flow.
