Shader "Hidden/TSMP Validation/Inspect GPU View"
{
    Properties
    {
        [HideInInspector] _MainTex ("TSMP Bytes", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Packages/com.kibalab.tsmp.core/Shaders/TSMPGpuInput.cginc"
            float4 frag(v2f_img i) : SV_Target
            {
                uint x = (uint)(i.uv.x * _TSMPOutputSize.x);
                uint v = x == 0u ? TSMPStreamId() : x == 1u ? TSMPFrameIndex() :
                    x == 2u ? TSMPByteLength() : x == 3u ? TSMPValueType() :
                    x == 4u ? (uint)(TSMPReadFloat32LE(0u) * 1000.0) :
                    x == 5u ? TSMPReadUInt16LE(3u) : TSMPReadUInt32LE(TSMPByteLength() - 1u);
                return float4(v & 255u, (v >> 8) & 255u, (v >> 16) & 255u, v >> 24) / 255.0;
            }
            ENDCG
        }
    }
}
