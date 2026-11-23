Shader "TSMP/GPU Output/Copy Bytes"
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
            #include "TSMPGpuInput.cginc"

            float4 frag(v2f_img i) : SV_Target
            {
                uint2 size = (uint2)_TSMPOutputSize.xy;
                uint2 p = min((uint2)(i.uv * size), size - 1u);
                uint offset = (p.y * size.x + p.x) * 4u;
                return float4(TSMPReadByte(offset), TSMPReadByte(offset + 1u),
                    TSMPReadByte(offset + 2u), TSMPReadByte(offset + 3u)) / 255.0;
            }
            ENDCG
        }
    }
}
