Shader "Hidden/TSMP/Validation/Decode Luma4 Recovery Bytes"
{
    Properties
    {
        [HideInInspector] _TSMPHeaderTex ("Decoded Header", 2D) = "black" {}
        [HideInInspector] _TSMPHeaderPixels ("Header Pixels", Float) = 0
        [HideInInspector] _CalibrationLut ("Calibration LUT", 2D) = "black" {}
        _RecoveryMode ("Recovery Mode", Float) = 0
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
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Overlay"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ TSMP_CALIBRATION_LUT
            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"

            #include "Luma4Recovery.cginc"

            int ClassifySymbol(float luma)
            {
                float2 fit = float2(1.0, 0.0);
#if !defined(TSMP_CALIBRATION_LUT)
                if (_RecoveryMode > 0.5 && _RecoveryMode < 1.5)
                    fit = RecoveryFitParameters();
#endif
                int bestSymbol = 0;
                float bestDistance = 999.0;

                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    float c = RecoveryCalibrationLuma(i, fit);
                    float d = abs(luma - c);

                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestSymbol = i;
                    }
                }

                return bestSymbol;
            }

            int DecodeSymbolAtBlockIndex(float blockIndex)
            {
                float luma = SampleLumaBlockByIndex(blockIndex);
                return ClassifySymbol(luma);
            }

            int DecodeByte(int byteIndex)
            {
                if (byteIndex < 0 || byteIndex >= (int)_ByteCount)
                    return 0;

                float blockIndex = _StartBlock + byteIndex * 2;
                int low = DecodeSymbolAtBlockIndex(blockIndex);
                int high = DecodeSymbolAtBlockIndex(blockIndex + 1.0);
                return (high << 4) | low;
            }

            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
            ENDCG
        }
    }

    Fallback Off
}
