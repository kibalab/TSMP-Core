Shader "Hidden/TSMP/Validation/Prepare RGB16 Affine"
{
    Properties
    {
        _MainTex ("TSMP Source", 2D) = "black" {}
        _BlockSize ("Block Size", Float) = 8
        _SampleSize ("Sample Size", Float) = 4
        _ActiveWidthBlocks ("Active Width Blocks", Float) = 80
        _SourceWidth ("Source Width", Float) = 640
        _SourceHeight ("Source Height", Float) = 360
        _FlipY ("Flip Y", Float) = 1
        _Rgb16CalibrationStartBlock ("Calibration Start Block", Float) = 640
        _RBits ("R Bits", Float) = 4
        _GBits ("G Bits", Float) = 4
        _BBits ("B Bits", Float) = 4
    }
    SubShader
    {
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

            float4 FitAxis(int offset, int count)
            {
                float3 numerator = 0;
                float denominator = 0;
                [loop]
                for (int index = 0; index < count; index++)
                {
                    int mask = count - 1;
                    float x = ((24 + (index * 208 + mask / 2) / mask) - 128) / 255.0;
                    numerator += x * SampleBlockByIndex(_Rgb16CalibrationStartBlock + offset + index);
                    denominator += x * x;
                }
                float3 slope = numerator / max(denominator, 0.000001);
                // NaN comparisons are false. Reject invalid or implausibly large fits.
                bool valid = denominator > 0.000001 && all(abs(slope) < 8.0);
                return float4(slope, valid ? 1.0 : 0.0);
            }

            float4 frag(v2f i) : SV_Target
            {
                int index = (int)floor(i.pos.x);
                int rCount = 1 << clamp((int)_RBits, 1, 8);
                int gCount = 1 << clamp((int)_GBits, 1, 8);
                int bCount = 1 << clamp((int)_BBits, 1, 4);
                if (index == 0) return FitAxis(0, rCount);
                if (index == 1) return FitAxis(rCount, gCount);
                if (index == 2) return FitAxis(rCount + gCount, bCount);
                float3 sum = 0;
                int total = rCount + gCount + bCount;
                [loop]
                for (int sampleIndex = 0; sampleIndex < total; sampleIndex++)
                    sum += SampleBlockByIndex(_Rgb16CalibrationStartBlock + sampleIndex);
                float3 center = sum / total;
                return float4(center, all(abs(center) < 8.0) ? 1.0 : 0.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
