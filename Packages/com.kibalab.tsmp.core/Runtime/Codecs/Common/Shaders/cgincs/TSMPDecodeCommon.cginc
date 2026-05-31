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

float3 SampleBlockRgb(float blockX, float blockY)
{
    float sampleSize = _SampleSize > 0.5 ? floor(_SampleSize) : (_BlockSize >= 8.0 ? 4.0 : 3.0);
    sampleSize = clamp(sampleSize, 1.0, min(_BlockSize, 8.0));
    int sampleLimit = (int)sampleSize;
    float startOffset = floor((_BlockSize - sampleSize) * 0.5);
    float2 startPixel = float2(blockX, blockY) * _BlockSize + startOffset.xx;

    if (sampleLimit <= 1)
        return SampleRgbAtTopLeftPixel(startPixel);

    float3 sum = 0.0;

    [loop]
    for (int y = 0; y < 8; y++)
    {
        [loop]
        for (int x = 0; x < 8; x++)
        {
            if (x < sampleLimit && y < sampleLimit)
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

float3 SampleBlockByIndex(float blockIndex)
{
    float blockX = fmod(blockIndex, _ActiveWidthBlocks);
    float blockY = floor(blockIndex / _ActiveWidthBlocks);
    return SampleBlockRgb(blockX, blockY);
}

float SampleLumaBlockByIndex(float blockIndex)
{
    float blockX = fmod(blockIndex, _ActiveWidthBlocks);
    float blockY = floor(blockIndex / _ActiveWidthBlocks);
    return SampleBlockLuma(blockX, blockY);
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
