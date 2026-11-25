#ifndef TSMP_DECODE_COMMON_INCLUDED
#define TSMP_DECODE_COMMON_INCLUDED

#include "UnityCG.cginc"

Texture2D _MainTex;
SamplerState sampler_MainTex;

float _BlockSize;
float _SampleSize;
float _StartBlock;
float _ByteCount;
float _ActiveWidthBlocks;
float _SourceWidth;
float _SourceHeight;
float _OutputWidth;
float _OutputHeight;
float _FlipY;

#define TSMP_COMBINED_BYTE_OUTPUT 1
Texture2D<float4> _TSMPHeaderTex;
float _TSMPHeaderPixels;

bool TSMPReadOutputPrefix(float2 uv, out int baseByte, out float4 prefix)
{
    int2 pixel = clamp((int2)floor(uv * float2(_OutputWidth, _OutputHeight)), int2(0, 0), (int2)float2(_OutputWidth - 1, _OutputHeight - 1));
    int index = pixel.y * (int)_OutputWidth + pixel.x;
    int headerPixels = max(0, (int)_TSMPHeaderPixels);
    baseByte = (index - headerPixels) * 4;
    prefix = 0;
    if (index >= headerPixels)
        return false;
    prefix = _TSMPHeaderTex.Load(int3(index, 0, 0));
    return true;
}

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

v2f vert(appdata v)
{
    v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    return o;
}

float3 TSMP_RgbToYCoCg(float3 c)
{
    float y = dot(c, float3(0.25, 0.5, 0.25));
    float co = c.r - c.b;
    float cg = c.g - (c.r + c.b) * 0.5;
    return float3(y, co, cg);
}

float3 TSMP_YCoCgToRgb(float3 c)
{
    float t = c.x - c.z * 0.5;
    return float3(t + c.y * 0.5, c.x + c.z * 0.5, t - c.y * 0.5);
}

float3 SampleRgbAtTopLeftPixel(float2 pixel)
{
    float rawY = (pixel.y + 0.5) / _SourceHeight;
    float uvY = lerp(rawY, 1.0 - rawY, _FlipY);
    float2 uv = float2((pixel.x + 0.5) / _SourceWidth, uvY);
    return _MainTex.SampleLevel(sampler_MainTex, uv, 0.0).rgb;
}

float SampleLumaAtTopLeftPixel(float2 pixel)
{
    float3 rgb = SampleRgbAtTopLeftPixel(pixel);
    return dot(rgb, float3(0.33333334, 0.33333334, 0.33333334));
}

#if defined(TSMP_ENABLE_RGB_BOX4)
float _UseBilinear4;
float4 _MainTex_TexelSize;
SamplerState tsmp_linear_clamp_sampler;

float3 TSMPBoxSample(float2 pixelCenter)
{
    float2 uv = pixelCenter / float2(_SourceWidth, _SourceHeight);
    uv.y = lerp(uv.y, 1.0 - uv.y, _FlipY);
    return _MainTex.SampleLevel(tsmp_linear_clamp_sampler, uv, 0).rgb;
}
#endif

float3 SampleBlockRgb(float blockX, float blockY)
{
    float sampleSize = _SampleSize > 0.5 ? floor(_SampleSize) : (_BlockSize >= 8.0 ? 4.0 : 3.0);
    sampleSize = clamp(sampleSize, 1.0, min(_BlockSize, 8.0));
    int sampleLimit = (int)sampleSize;
    float startOffset = floor((_BlockSize - sampleSize) * 0.5);
    float2 startPixel = float2(blockX, blockY) * _BlockSize + startOffset.xx;

    if (sampleLimit <= 1)
        return SampleRgbAtTopLeftPixel(startPixel);

#if defined(TSMP_ENABLE_RGB_BOX4)
    if (_UseBilinear4 > 0.5 && sampleLimit == 4 &&
        all(_MainTex_TexelSize.zw == float2(_SourceWidth, _SourceHeight)))
    {
        float2 p = startPixel + 1.0;
        return 0.25 * (TSMPBoxSample(p) + TSMPBoxSample(p + float2(2, 0))
                     + TSMPBoxSample(p + float2(0, 2)) + TSMPBoxSample(p + 2));
    }
#endif
    float3 sum = 0.0;

    [loop]
    for (int y = 0; y < sampleLimit; y++)
    {
        [loop]
        for (int x = 0; x < sampleLimit; x++)
        {
            sum += SampleRgbAtTopLeftPixel(startPixel + float2(x, y));
        }
    }

    return sum / (sampleLimit * sampleLimit);
}

float SampleBlockLuma(float blockX, float blockY)
{
    float sampleSize = _SampleSize > 0.5 ? floor(_SampleSize) : (_BlockSize >= 8.0 ? 4.0 : 3.0);
    sampleSize = clamp(sampleSize, 1.0, min(_BlockSize, 8.0));
    int sampleLimit = (int)sampleSize;
    float startOffset = floor((_BlockSize - sampleSize) * 0.5);
    float2 startPixel = float2(blockX, blockY) * _BlockSize + startOffset.xx;

    if (sampleLimit <= 1)
        return SampleLumaAtTopLeftPixel(startPixel);

    float sum = 0.0;

    [loop]
    for (int y = 0; y < 8; y++)
    {
        [loop]
        for (int x = 0; x < 8; x++)
        {
            if (x < sampleLimit && y < sampleLimit)
                sum += SampleLumaAtTopLeftPixel(startPixel + float2(x, y));
        }
    }

    return sum / (sampleLimit * sampleLimit);
}

#define TSMP_HAS_BLOCK_COORDINATES 1
uint2 TSMPBlockCoordinates(uint index)
{
    uint width = max(1u, (uint)_ActiveWidthBlocks);
    uint row = index / width;
    return uint2(index - row * width, row);
}

float3 SampleBlockByIndex(uint blockIndex)
{
    uint2 block = TSMPBlockCoordinates(blockIndex);
    return SampleBlockRgb(block.x, block.y);
}

float3 SampleBlockByIndex(float blockIndex)
{
    return SampleBlockByIndex((uint)blockIndex);
}

float SampleLumaBlockByIndex(float blockIndex)
{
    uint2 block = TSMPBlockCoordinates((uint)blockIndex);
    return SampleBlockLuma(block.x, block.y);
}

float PayloadBlockIndex(int symbolIndex)
{
    return _StartBlock + symbolIndex;
}

int FloorDivNonNegative(int value, float divisor)
{
    return (int)floor((float)value / divisor);
}

#define RgbToYCoCg TSMP_RgbToYCoCg
#define YCoCgToRgb TSMP_YCoCgToRgb

#endif
