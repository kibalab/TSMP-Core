Shader "Hidden/TSMP/Validation/RGB16 Affine Refine"
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
            float _RefineRadius;
            float _RecoveryMode;
            Texture2D<float4> _Rgb16ForwardLut;

            float CenteredQuantizedLevel(int index, int count)
            {
                int mask = count - 1;
                return ((24 + (index * 208 + mask / 2) / mask) - 128) / 255.0;
            }

            float SampleChannelCalibration(int level, int offset, int channel)
            {
                float3 c = SampleBlockByIndex(_Rgb16CalibrationStartBlock + offset + clamp(level, 0, 15));
                return channel == 0 ? c.r : channel == 1 ? c.g : c.b;
            }

            int ClassifyChannel444(float value, int offset, int channel)
            {
                float low = SampleChannelCalibration(0, offset, channel);
                float high = SampleChannelCalibration(15, offset, channel);
                float range = high - low;
                int estimated = abs(range) > 0.00001 ? (int)round(saturate((value - low) / range) * 15.0) : 8;
                int bestIndex = estimated;
                float bestDistance = 999.0;

                [loop]
                for (int i = 0; i < 9; i++)
                {
                    int index = clamp(estimated - 4 + i, 0, 15);
                    float candidate = SampleChannelCalibration(index, offset, channel);
                    float distance = abs(value - candidate);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = index;
                    }
                }

                return bestIndex;
            }

            int RefineRgb16Local444(float3 rgb, int r0, int g0, int b0)
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
                        rCandidates[candidate] = SampleChannelCalibration(clamp(r0 + delta, 0, 15), 0, 0);
                        gCandidates[candidate] = SampleChannelCalibration(clamp(g0 + delta, 0, 15), 16, 1);
                        bCandidates[candidate] = SampleChannelCalibration(clamp(b0 + delta, 0, 15), 32, 2);
                    }
                }

                [loop]
                for (int db = max(-radius, -b0); db <= min(radius, 15 - b0); db++)
                {

                    [loop]
                    for (int dg = max(-radius, -g0); dg <= min(radius, 15 - g0); dg++)
                    {

                        [loop]
                        for (int dr = max(-radius, -r0); dr <= min(radius, 15 - r0); dr++)
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
                                    + forwardR * CenteredQuantizedLevel(r, 16)
                                    + forwardG * CenteredQuantizedLevel(g, 16)
                                    + forwardB * CenteredQuantizedLevel(b, 16));
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

                return bestR | (bestG << 4) | (bestB << 8);
            }

            int DecodeRgb16Symbol444(int symbolIndex)
            {
                float3 rgb = SampleBlockByIndex(PayloadBlockIndex(symbolIndex));
                int r = ClassifyChannel444(rgb.r, 0, 0);
                int g = ClassifyChannel444(rgb.g, 16, 1);
                int b = ClassifyChannel444(rgb.b, 32, 2);
                return RefineRgb16Local444(rgb, r, g, b);
            }

            int SelectCachedSymbol(int symbolOffset, int s0, int s1, int s2, int s3)
            {
                if (symbolOffset == 0) return s0;
                if (symbolOffset == 1) return s1;
                if (symbolOffset == 2) return s2;
                return s3;
            }

            int DecodeByteFromRgb12Symbols(int bitOffset, int s0, int s1, int s2, int s3)
            {
                int symbolOffset = FloorDivNonNegative(bitOffset, 12.0);
                int bitShift = bitOffset - symbolOffset * 12;
                int symbol = SelectCachedSymbol(symbolOffset, s0, s1, s2, s3);

                if (bitShift + 8 <= 12)
                    return (symbol >> bitShift) & 0xFF;

                int nextSymbol = SelectCachedSymbol(symbolOffset + 1, s0, s1, s2, s3);
                int bitsFromFirst = 12 - bitShift;
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

                int baseBit = baseByte * 8;
                int symbolIndex = FloorDivNonNegative(baseBit, 12.0);
                int firstBitOffset = baseBit - symbolIndex * 12;

                int s0 = DecodeRgb16Symbol444(symbolIndex);
                int s1 = DecodeRgb16Symbol444(symbolIndex + 1);
                int s2 = DecodeRgb16Symbol444(symbolIndex + 2);
                int s3 = firstBitOffset == 8 ? DecodeRgb16Symbol444(symbolIndex + 3) : 0;

                int b0 = baseByte + 0 < (int)_ByteCount ? DecodeByteFromRgb12Symbols(firstBitOffset + 0, s0, s1, s2, s3) : 0;
                int b1 = baseByte + 1 < (int)_ByteCount ? DecodeByteFromRgb12Symbols(firstBitOffset + 8, s0, s1, s2, s3) : 0;
                int b2 = baseByte + 2 < (int)_ByteCount ? DecodeByteFromRgb12Symbols(firstBitOffset + 16, s0, s1, s2, s3) : 0;
                int b3 = baseByte + 3 < (int)_ByteCount ? DecodeByteFromRgb12Symbols(firstBitOffset + 24, s0, s1, s2, s3) : 0;

                return float4(b0 / 255.0, b1 / 255.0, b2 / 255.0, b3 / 255.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
