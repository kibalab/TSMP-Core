---
title: TSMPDecodeByteOutput.cginc
---

# `TSMPDecodeByteOutput.cginc`

`TSMPDecodeByteOutput.cginc`는 codec decode pass용 shader include입니다. Byte index로 한 byte를 복구하는 `DecodeByte(index)` 함수를, decoded bytes를 RGBA output texture에 쓰는 fragment shader로 연결합니다.

Custom codec이 byte index 기준으로 한 byte를 복구할 수 있고, Core decoder readback path가 기대하는 표준 texture layout으로 bytes를 넘기고 싶을 때 사용하세요.

Include path:

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

## Required inputs

이 include는 shader 안에 다음 symbol이 존재한다고 가정합니다.

| Symbol | Type | 용도 |
| --- | --- | --- |
| `v2f` | struct | Fragment input type. `float2 uv`를 가져야 합니다. |
| `_OutputWidth` | numeric uniform | Byte output texture width in pixels. |
| `_OutputHeight` | numeric uniform | Byte output texture height in pixels. |
| `_ByteCount` | numeric uniform | Decoder가 요청한 valid decoded bytes 수. |
| `DecodeByte(int byteIndex)` | function | Byte index에 대한 decoded byte 하나를 반환합니다. |

`_OutputWidth`, `_OutputHeight`, `_ByteCount`는 보통 decoder 또는 codec material setup이 할당합니다.

## `DecodeByte(int byteIndex)` contract

기본적으로 include는 다음 함수를 호출합니다.

```hlsl
int DecodeByte(int byteIndex)
```

함수는 `0`부터 `255` 사이의 integer byte value를 반환해야 합니다.

Rules:

- `byteIndex`는 zero-based payload byte index입니다.
- 마지막 RGBA pixel 안에서는 `_ByteCount`를 넘는 index로 호출될 수 있습니다.
- Out-of-range read는 `0`을 반환하는 것이 안전합니다.
- `0..255` 바깥 값은 codec에서 clamp하거나 만들지 않아야 합니다.
- 정확한 byte value가 필요하므로 filtered sampling에 의존하지 않는 것이 좋습니다.

## Custom byte function name

다른 함수 이름을 쓰고 싶다면 include 전에 `TSMP_DECODE_BYTE_FUNC`를 정의하세요.

```hlsl
int MyCodecDecodeByte(int byteIndex)
{
    return DecodeMySymbol(byteIndex);
}

#define TSMP_DECODE_BYTE_FUNC MyCodecDecodeByte
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

대체 함수는 같은 형태여야 합니다. `int` index 하나를 받고 byte-like `int`를 반환합니다.

## Output layout

각 output pixel은 네 byte를 저장합니다.

| Channel | Byte index |
| --- | --- |
| `R` | `baseByte + 0` |
| `G` | `baseByte + 1` |
| `B` | `baseByte + 2` |
| `A` | `baseByte + 3` |

Base index는 다음처럼 계산됩니다.

```hlsl
baseByte = ((int)pixel.y * (int)_OutputWidth + (int)pixel.x) * 4;
```

Fragment color는 다음 형태로 반환됩니다.

```hlsl
float4(b0 / 255.0, b1 / 255.0, b2 / 255.0, b3 / 255.0)
```

`baseByte >= _ByteCount`이면 fragment는 zero color를 반환합니다.

## `TSMPDecodeByteOutputFragment(v2f i)`

```hlsl
float4 TSMPDecodeByteOutputFragment(v2f i)
```

Include가 제공하는 main helper function입니다.

1. `i.uv`를 output pixel coordinate로 변환합니다.
2. Pixel coordinate를 output texture bounds 안으로 clamp합니다.
3. 해당 pixel의 첫 byte index를 계산합니다.
4. 최대 네 byte에 대해 `TSMP_DECODE_BYTE_FUNC`를 호출합니다.
5. Bytes를 RGBA channels에 pack합니다.

Shader가 이미 자체 `frag` entry point를 갖고 있을 때 이 함수를 직접 호출하세요.

## Default `frag(v2f i)`

Suppress하지 않으면 include는 다음 함수를 정의합니다.

```hlsl
float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
```

Pass가 byte output texture만 쓰는 단순 decode material이라면 이 기본 `frag`를 그대로 사용할 수 있습니다.

## `TSMP_SUPPRESS_DEFAULT_FRAG`

직접 fragment function을 제공하려면 include 전에 `TSMP_SUPPRESS_DEFAULT_FRAG`를 정의하세요.

```hlsl
#define TSMP_SUPPRESS_DEFAULT_FRAG
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"

float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
```

Extra debug output, conditional logic, 다른 entry point name이 필요할 때 사용합니다.

## Minimal shader pass example

```hlsl
struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float _OutputWidth;
float _OutputHeight;
float _ByteCount;

int DecodeByte(int byteIndex)
{
    return byteIndex & 255;
}

#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

이 예시는 output texture에 예측 가능한 byte pattern을 씁니다. 실제 codec은 TSMP source frame에서 bytes를 decode해야 합니다.

## Common mistakes

| Symptom | Likely cause |
| --- | --- |
| Output is all zero | `_ByteCount`가 zero이거나 `DecodeByte`가 항상 zero를 반환. |
| Last bytes contain garbage | `DecodeByte`가 payload length 이후 index를 guard하지 않음. |
| Every fourth byte is wrong | RGBA channel order가 output layout과 다름. |
| Works in editor but fails in runtime | Shader property names 또는 material setup이 runtime path와 다름. |
| Stale bytes after a smaller frame | Output texture가 clear되지 않았고 decoder가 `_ByteCount`보다 많이 읽음. |
