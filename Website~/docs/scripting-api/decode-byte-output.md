---
title: TSMPDecodeByteOutput.cginc
---

# `TSMPDecodeByteOutput.cginc`

`TSMPDecodeByteOutput.cginc` is a shader include for codec decode passes. It turns a byte-addressed `DecodeByte(index)` function into a fragment shader that writes decoded bytes into an RGBA output texture.

Use it when your custom codec can recover one byte by index and you want Core's decoder readback path to receive those bytes in a standard texture layout.

Include path:

```hlsl
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

## Required inputs

The include expects these symbols to exist in the shader before it is included or before the fragment runs.

| Symbol | Type | Purpose |
| --- | --- | --- |
| `v2f` | struct | Fragment input type. It must expose `float2 uv`. |
| `_OutputWidth` | numeric uniform | Width of the byte output texture in pixels. |
| `_OutputHeight` | numeric uniform | Height of the byte output texture in pixels. |
| `_ByteCount` | numeric uniform | Number of valid decoded bytes requested by the decoder. |
| `DecodeByte(int byteIndex)` | function | Returns one decoded byte for a byte index. |

`_OutputWidth`, `_OutputHeight`, and `_ByteCount` are usually assigned by the decoder or codec material setup.

## `DecodeByte(int byteIndex)` contract

By default the include calls:

```hlsl
int DecodeByte(int byteIndex)
```

The function should return an integer byte value from `0` to `255`.

Rules:

- `byteIndex` is a zero-based payload byte index.
- The function may be called for indices beyond `_ByteCount` inside the last RGBA pixel.
- Out-of-range reads should return `0`.
- Values outside `0..255` should be clamped or avoided by the codec.
- The function should not rely on filtered sampling for exact byte values.

## Custom byte function name

Define `TSMP_DECODE_BYTE_FUNC` before including the file to use a different function name.

```hlsl
int MyCodecDecodeByte(int byteIndex)
{
    return DecodeMySymbol(byteIndex);
}

#define TSMP_DECODE_BYTE_FUNC MyCodecDecodeByte
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
```

The replacement function must have the same call shape: one `int` index in, one byte-like `int` out.

## Output layout

Each output pixel stores four bytes:

| Channel | Byte index |
| --- | --- |
| `R` | `baseByte + 0` |
| `G` | `baseByte + 1` |
| `B` | `baseByte + 2` |
| `A` | `baseByte + 3` |

The base index is calculated as:

```hlsl
baseByte = ((int)pixel.y * (int)_OutputWidth + (int)pixel.x) * 4;
```

The returned fragment color is:

```hlsl
float4(b0 / 255.0, b1 / 255.0, b2 / 255.0, b3 / 255.0)
```

If `baseByte >= _ByteCount`, the fragment returns black transparent zero.

## `TSMPDecodeByteOutputFragment(v2f i)`

```hlsl
float4 TSMPDecodeByteOutputFragment(v2f i)
```

Main helper function exported by the include. It:

1. Converts `i.uv` into an output pixel coordinate.
2. Clamps the pixel coordinate to the output texture bounds.
3. Calculates the first byte index for that pixel.
4. Calls `TSMP_DECODE_BYTE_FUNC` for up to four bytes.
5. Packs those bytes into RGBA channels.

Use this function when your shader already has its own `frag` entry point.

## Default `frag(v2f i)`

Unless suppressed, the include defines:

```hlsl
float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
```

This is convenient for a simple decode material where the pass only writes the byte output texture.

## `TSMP_SUPPRESS_DEFAULT_FRAG`

Define `TSMP_SUPPRESS_DEFAULT_FRAG` before including the file if you want to provide your own fragment function.

```hlsl
#define TSMP_SUPPRESS_DEFAULT_FRAG
#include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"

float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
```

This is useful when the pass needs extra debug output, conditional logic, or a different entry point name.

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

This example writes a predictable byte pattern into the output texture. A real codec should decode from the TSMP source frame instead.

## Common mistakes

| Symptom | Likely cause |
| --- | --- |
| Output is all zero | `_ByteCount` is zero or `DecodeByte` always returns zero. |
| Last bytes contain garbage | `DecodeByte` does not guard indices beyond payload length. |
| Every fourth byte is wrong | RGBA channel order does not match the output layout. |
| Works in editor but fails in runtime | Shader property names or material setup differ in the runtime path. |
| Stale bytes after a smaller frame | The output texture is not cleared and the decoder reads more than `_ByteCount`. |
