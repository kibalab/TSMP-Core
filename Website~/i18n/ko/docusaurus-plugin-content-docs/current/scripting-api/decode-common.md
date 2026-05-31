---
title: TSMPDecodeCommon.cginc
---

# `TSMPDecodeCommon.cginc`

`TSMPDecodeCommon.cginc`는 TSMP codec decode pass용 공용 shader include입니다. Source texture uniforms, vertex input/output structs, default vertex function, block sampling helpers, YCoCg conversion helpers를 정의합니다.

TSMP source frame을 pixel 또는 block 기준으로 sample하는 codec-specific decode logic 앞에 include하세요.

Include path:

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"
```

## Texture inputs

| Symbol | Type | 용도 |
| --- | --- | --- |
| `_MainTex` | `Texture2D` | Source TSMP frame texture. |
| `sampler_MainTex` | `SamplerState` | `_MainTex` sampler. |

정확한 byte recovery가 필요하므로 decode shader는 point-like sampling을 사용해야 합니다. Helpers는 mip selection을 피하기 위해 `SampleLevel(..., 0.0)`을 호출합니다.

## Common uniforms

| Uniform | 용도 |
| --- | --- |
| `_BlockSize` | Codec block 하나의 pixel size. |
| `_SampleSize` | Block 내부에서 sample할 pixel 수. 설정되지 않으면 helper가 `_BlockSize`에서 default를 고릅니다. |
| `_StartBlock` | First payload block index. |
| `_ByteCount` | Decoder가 요청한 valid payload bytes 수. |
| `_ActiveWidthBlocks` | Row당 writable/readable block 수. |
| `_SourceWidth` | Source frame width in pixels. |
| `_SourceHeight` | Source frame height in pixels. |
| `_OutputWidth` | Byte output texture width in pixels. |
| `_OutputHeight` | Byte output texture height in pixels. |
| `_FlipY` | Non-zero이면 source sampling을 vertically flip합니다. |

이 값들은 decoder/material setup path가 할당합니다. Uniform이 제공된다면 custom codec은 texture dimensions에서 추측하지 않는 것이 좋습니다.

## Vertex structs

Include는 다음 struct를 정의합니다.

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

Decode pass가 position과 UV만 필요할 때 사용하세요. Custom interpolator가 필요하면 이 default shape에 의존하지 말고 별도 pass에서 직접 struct를 정의하는 편이 좋습니다.

## `vert(appdata v)`

```hlsl
v2f vert(appdata v)
```

Default vertex function입니다. `UnityObjectToClipPos(v.vertex)`로 clip-space position을 만들고 `v.uv`를 전달합니다.

## Pixel sampling helpers

### `SampleRgbAtTopLeftPixel(float2 pixel)`

주어진 source pixel의 center를 sample하고 RGB를 반환합니다.

Y coordinate는 `_FlipY`로 계산됩니다.

```hlsl
float rawY = (pixel.y + 0.5) / _SourceHeight;
float uvY = lerp(rawY, 1.0 - rawY, _FlipY);
```

### `SampleLumaAtTopLeftPixel(float2 pixel)`

Pixel RGB를 sample하고 average luma를 반환합니다.

```hlsl
dot(rgb, float3(0.33333334, 0.33333334, 0.33333334))
```

Perceptual luma가 아니라 단순 channel average입니다.

## Block sampling helpers

### `SampleBlockRgb(float blockX, float blockY)`

Block 중앙의 square region을 sample하고 average RGB를 반환합니다.

Sample size rule:

- `_SampleSize > 0.5`이면 `floor(_SampleSize)`를 사용합니다.
- 아니면 `_BlockSize >= 8`일 때 `4`, 더 작은 block에서는 `3`을 사용합니다.
- Sample size는 `1..min(_BlockSize, 8)`로 clamp됩니다.

Loop는 `8x8`로 제한되어 codec sampling cost가 예측 가능합니다.

### `SampleBlockLuma(float blockX, float blockY)`

`SampleBlockRgb`와 같은 방식이지만 average luma를 반환합니다.

### `SampleBlockByIndex(float blockIndex)`

`_ActiveWidthBlocks`로 linear block index를 `(blockX, blockY)`로 변환한 뒤 `SampleBlockRgb`를 호출합니다.

### `SampleLumaBlockByIndex(float blockIndex)`

Linear block index를 `(blockX, blockY)`로 변환한 뒤 `SampleBlockLuma`를 호출합니다.

## Payload block helper

```hlsl
float PayloadBlockIndex(int symbolIndex)
```

다음을 반환합니다.

```hlsl
_StartBlock + symbolIndex
```

Header/payload boundary를 hard-code하지 않고 codec symbol index를 payload block index로 매핑할 때 사용하세요.

## Integer helper

```hlsl
int FloorDivNonNegative(int value, float divisor)
```

Non-negative value에 대해 `floor(value / divisor)`를 반환합니다. Byte index를 symbol index로 매핑할 때 사용할 수 있습니다.

## Color conversion helpers

```hlsl
float3 TSMP_RgbToYCoCg(float3 c)
float3 TSMP_YCoCgToRgb(float3 c)
```

Compatibility alias도 정의됩니다.

```hlsl
#define RgbToYCoCg TSMP_RgbToYCoCg
#define YCoCgToRgb TSMP_YCoCgToRgb
```

새 shader code에서는 macro/name conflict를 줄이기 위해 가능하면 `TSMP_` 이름을 사용하세요.

## Common use with `TSMPDecodeByteOutput.cginc`

일반 decode shader는 `TSMPDecodeCommon.cginc`로 sampling helper를 받고, `DecodeByte(int byteIndex)`를 구현한 뒤, `TSMPDecodeByteOutput.cginc`를 include해서 byte output texture를 씁니다.

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

실제 codec은 보통 한 block에서 여러 bit 또는 symbol을 decode합니다. 이 예시는 include flow를 보여주기 위한 것입니다.
