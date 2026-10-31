Shader "Hidden/TSMP/Validation/Prepare Luma4 Recovery Calibration"
{
    Properties
    {
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

            #include "Luma4Recovery.cginc"

            float4 frag(v2f i) : SV_Target
            {
                int index = (int)floor(i.pos.x);
                float2 fit = float2(1.0, 0.0);
                if (_RecoveryMode > 0.5 && _RecoveryMode < 1.5)
                    fit = RecoveryFitParameters();
                return RecoveryCalibrationLuma(index, fit).xxxx;
            }
            ENDCG
        }
    }

    Fallback Off
}

