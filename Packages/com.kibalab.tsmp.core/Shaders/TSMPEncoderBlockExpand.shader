Shader "Hidden/TSMP/Encoder Block Expand"
{
    Properties
    {
        _MainTex ("Symbol Texture", 2D) = "black" {}
        _SourceBlockWidth ("Source Block Width", Float) = 1
        _SourceBlockHeight ("Source Block Height", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _SourceBlockWidth;
            float _SourceBlockHeight;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 blocks = max(float2(_SourceBlockWidth, _SourceBlockHeight), float2(1.0, 1.0));
                float2 block = floor(saturate(i.uv) * blocks);
                block = min(block, blocks - 1.0);
                float2 uv = (block + 0.5) / blocks;
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }

    Fallback Off
}
