Shader "Hidden/TSMP/Validation/RGB16 Affine Variable Refine"
{
    Properties
    {
        [HideInInspector] _TSMPHeaderTex ("Decoded Header", 2D) = "black" {}
        [HideInInspector] _TSMPHeaderPixels ("Header Pixels", Float) = 0
        _MainTex ("TSMP Source", 2D) = "black" {}
        _BlockSize ("Block Size", Float) = 8
        _SampleSize ("Sample Size", Float) = 0
        _StartBlock ("Start Block", Float) = 0
        _ByteCount ("Byte Count", Float) = 0
        _ActiveWidthBlocks ("Active Width Blocks", Float) = 80
        _SourceWidth ("Source Width", Float) = 640
        _SourceHeight ("Source Height", Float) = 360
        _OutputWidth ("Output Width", Float) = 14
        _OutputHeight ("Output Height", Float) = 1
        _FlipY ("Flip Y", Float) = 1
        _Rgb16CalibrationStartBlock ("RGB16 Calibration Start Block", Float) = 640
        _RBits ("R Bits", Float) = 4
        _GBits ("G Bits", Float) = 4
        _BBits ("B Bits", Float) = 4
        _RefineRadius ("Refine Radius", Float) = 1
        _RecoveryMode ("Recovery Mode", Float) = 0
        _Rgb16ForwardLut ("Forward Calibration", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"

            float _Rgb16CalibrationStartBlock;
            float _RBits;
            float _GBits;
            float _BBits;
            float _RefineRadius;
            float _RecoveryMode;
            Texture2D<float4> _Rgb16ForwardLut;

            float CenteredQuantizedLevel(int index, int count)
            {
                int mask = count - 1;
                return ((24 + (index * 208 + mask / 2) / mask) - 128) / 255.0;
            }

            float ChannelValue(float3 c, int channel)
            {
                return channel == 0 ? c.r : channel == 1 ? c.g : c.b;
            }

            float SampleChannelCalibration(int level, int offset, int channel, int count)
            {
                int index = clamp(level, 0, count - 1);
                return ChannelValue(SampleBlockByIndex(_Rgb16CalibrationStartBlock + offset + index), channel);
            }

            int ClassifyChannel(float value, int count, int offset, int channel)
            {
                float low = SampleChannelCalibration(0, offset, channel, count);
                float high = SampleChannelCalibration(count - 1, offset, channel, count);
                float range = high - low;
                int estimated = abs(range) > 0.00001 ? (int)round(saturate((value - low) / range) * (count - 1)) : count / 2;
                int radius = count <= 32 ? 5 : 10;
                int bestIndex = estimated;
                float bestDistance = 999.0;

                [loop]
                for (int i = 0; i < 21; i++)
                {
                    if (i > radius * 2) break;
                    int index = clamp(estimated - radius + i, 0, count - 1);
                    float candidate = SampleChannelCalibration(index, offset, channel, count);
                    float distance = abs(value - candidate);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = index;
                    }
                }

                return bestIndex;
            }

            int RefineRgb16Local(float3 rgb, int r0, int g0, int b0, int rCount, int gCount, int bCount, int gOffset, int bOffset)
            {
                float3 p = RgbToYCoCg(rgb);
                int radius = clamp((int)_RefineRadius, 1, 3);
                int bestR = r0;
                int bestG = g0;
                int bestB = b0;
                float bestDistance = 999999.0;
                bool useForward = false;
                float3 forwardR = 0;
                float3 forwardG = 0;
                float3 forwardB = 0;
                float3 forwardBase = 0;
                if (_RecoveryMode > 0.5)
                {
                    float4 fitR = _Rgb16ForwardLut.Load(int3(0, 0, 0));
                    float4 fitG = _Rgb16ForwardLut.Load(int3(1, 0, 0));
                    float4 fitB = _Rgb16ForwardLut.Load(int3(2, 0, 0));
                    float4 fitBase = _Rgb16ForwardLut.Load(int3(3, 0, 0));
                    useForward = min(min(fitR.a, fitG.a), min(fitB.a, fitBase.a)) > 0.5;
                    forwardR = fitR.rgb;
                    forwardG = fitG.rgb;
                    forwardB = fitB.rgb;
                    forwardBase = fitBase.rgb;
                }


                float rCandidates[7];
                float gCandidates[7];
                float bCandidates[7];
                [loop]
                for (int candidate = 0; candidate < 7; candidate++)
                {
                    int delta = candidate - radius;
                    rCandidates[candidate] = 0.0;
                    gCandidates[candidate] = 0.0;
                    bCandidates[candidate] = 0.0;
                    if (candidate <= radius * 2)
                    {
                        rCandidates[candidate] = SampleChannelCalibration(clamp(r0 + delta, 0, rCount - 1), 0, 0, rCount);
                        gCandidates[candidate] = SampleChannelCalibration(clamp(g0 + delta, 0, gCount - 1), gOffset, 1, gCount);
                        bCandidates[candidate] = SampleChannelCalibration(clamp(b0 + delta, 0, bCount - 1), bOffset, 2, bCount);
                    }
                }

                [loop]
                for (int db = max(-radius, -b0); db <= min(radius, bCount - 1 - b0); db++)
                {

                    [loop]
                    for (int dg = max(-radius, -g0); dg <= min(radius, gCount - 1 - g0); dg++)
                    {

                        [loop]
                        for (int dr = max(-radius, -r0); dr <= min(radius, rCount - 1 - r0); dr++)
                        {

                            int r = r0 + dr;
                            int g = g0 + dg;
                            int b = b0 + db;
                            float3 c = RgbToYCoCg(float3(
                                rCandidates[dr + radius],
                                gCandidates[dg + radius],
                                bCandidates[db + radius]));
                            if (useForward)
                            {
                                c = RgbToYCoCg(forwardBase
                                    + forwardR * CenteredQuantizedLevel(r, rCount)
                                    + forwardG * CenteredQuantizedLevel(g, gCount)
                                    + forwardB * CenteredQuantizedLevel(b, bCount));
                            }
                            float3 d = p - c;
                            float lumaWeight = useForward && _RecoveryMode > 1.5 ? 4.0 : 2.0;
                            float distance = d.x * d.x * lumaWeight + d.y * d.y * 0.85 + d.z * d.z * 0.85;
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestR = r;
                                bestG = g;
                                bestB = b;
                            }
                        }
                    }
                }

                return bestR | (bestG << (int)_RBits) | (bestB << ((int)_RBits + (int)_GBits));
            }

            int DecodeRgb16Symbol(int symbolIndex)
            {
                int rBits = (int)_RBits;
                int gBits = (int)_GBits;
                int bBits = (int)_BBits;
                int rCount = 1 << rBits;
                int gCount = 1 << gBits;
                int bCount = 1 << bBits;
                int gOffset = rCount;
                int bOffset = rCount + gCount;

                float3 rgb = SampleBlockByIndex(PayloadBlockIndex(symbolIndex));
                int r = ClassifyChannel(rgb.r, rCount, 0, 0);
                int g = ClassifyChannel(rgb.g, gCount, gOffset, 1);
                int b = ClassifyChannel(rgb.b, bCount, bOffset, 2);
                return RefineRgb16Local(rgb, r, g, b, rCount, gCount, bCount, gOffset, bOffset);
            }

            int DecodeByte(int byteIndex)
            {
                if (byteIndex < 0 || byteIndex >= (int)_ByteCount)
                    return 0;

                int totalBits = (int)_RBits + (int)_GBits + (int)_BBits;
                int bitIndex = byteIndex * 8;
                int symbolIndex = FloorDivNonNegative(bitIndex, (float)totalBits);
                int bitShift = bitIndex - symbolIndex * totalBits;
                int symbol = DecodeRgb16Symbol(symbolIndex);

                if (bitShift + 8 <= totalBits)
                    return (symbol >> bitShift) & 0xFF;

                int nextSymbol = DecodeRgb16Symbol(symbolIndex + 1);
                int bitsFromFirst = totalBits - bitShift;
                int lowMask = (1 << bitsFromFirst) - 1;
                int highBits = 8 - bitsFromFirst;
                int highMask = (1 << highBits) - 1;
                return ((symbol >> bitShift) & lowMask) | ((nextSymbol & highMask) << bitsFromFirst);
            }

            float4 frag(v2f i) : SV_Target
            {
#if defined(TSMP_COMBINED_BYTE_OUTPUT)
                int baseByte;
                float4 prefix;
                if (TSMPReadOutputPrefix(i.uv, baseByte, prefix))
                    return prefix;
#else
                float2 pixel = floor(i.uv * float2(_OutputWidth, _OutputHeight));
                pixel = clamp(pixel, 0.0, float2(_OutputWidth - 1.0, _OutputHeight - 1.0));
                int baseByte = ((int)pixel.y * (int)_OutputWidth + (int)pixel.x) * 4;
#endif
                if (baseByte >= (int)_ByteCount)
                    return 0.0;

                int totalBits = (int)_RBits + (int)_GBits + (int)_BBits;
                if (totalBits < 8)
                    return float4(DecodeByte(baseByte), DecodeByte(baseByte + 1),
                        DecodeByte(baseByte + 2), DecodeByte(baseByte + 3)) / 255.0;

                int bitIndex = baseByte * 8;
                int symbolIndex = FloorDivNonNegative(bitIndex, (float)totalBits);
                int bitShift = bitIndex - symbolIndex * totalBits;
                int requiredBits = min(4, (int)_ByteCount - baseByte) * 8;
                uint packed = (uint)DecodeRgb16Symbol(symbolIndex) >> bitShift;
                int nextBit = totalBits - bitShift;

                if (nextBit < requiredBits)
                {
                    packed |= (uint)DecodeRgb16Symbol(symbolIndex + 1) << nextBit;
                    nextBit += totalBits;
                }
                if (nextBit < requiredBits)
                {
                    packed |= (uint)DecodeRgb16Symbol(symbolIndex + 2) << nextBit;
                    nextBit += totalBits;
                }
                if (nextBit < requiredBits)
                {
                    packed |= (uint)DecodeRgb16Symbol(symbolIndex + 3) << nextBit;
                    nextBit += totalBits;
                }
                if (nextBit < requiredBits)
                {
                    packed |= (uint)DecodeRgb16Symbol(symbolIndex + 4) << nextBit;
                    nextBit += totalBits;
                }

                if (requiredBits < 32)
                    packed &= (1u << requiredBits) - 1u;

                return float4(packed & 0xFFu, (packed >> 8) & 0xFFu,
                    (packed >> 16) & 0xFFu, (packed >> 24) & 0xFFu) / 255.0;
            }
            ENDCG
        }
    }

    Fallback Off
}
