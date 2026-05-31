#ifndef TSMP_DECODE_BYTE_OUTPUT_INCLUDED
#define TSMP_DECODE_BYTE_OUTPUT_INCLUDED

#ifndef TSMP_DECODE_BYTE_FUNC
#define TSMP_DECODE_BYTE_FUNC DecodeByte
#endif

float4 TSMPDecodeByteOutputFragment(v2f i)
{
    float2 pixel = floor(i.uv * float2(_OutputWidth, _OutputHeight));
    pixel = clamp(pixel, 0.0, float2(_OutputWidth - 1.0, _OutputHeight - 1.0));
    int baseByte = ((int)pixel.y * (int)_OutputWidth + (int)pixel.x) * 4;

    if (baseByte >= (int)_ByteCount)
        return 0.0;

    int b0 = TSMP_DECODE_BYTE_FUNC(baseByte + 0);
    int b1 = TSMP_DECODE_BYTE_FUNC(baseByte + 1);
    int b2 = TSMP_DECODE_BYTE_FUNC(baseByte + 2);
    int b3 = TSMP_DECODE_BYTE_FUNC(baseByte + 3);

    return float4(b0 / 255.0, b1 / 255.0, b2 / 255.0, b3 / 255.0);
}

#ifndef TSMP_SUPPRESS_DEFAULT_FRAG
float4 frag(v2f i) : SV_Target
{
    return TSMPDecodeByteOutputFragment(i);
}
#endif

#endif
