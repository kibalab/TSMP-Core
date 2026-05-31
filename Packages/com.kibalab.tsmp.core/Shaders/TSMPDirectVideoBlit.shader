Shader "Hidden/TSMP/Direct Video Blit"
{
    Properties
    {
        _MainTex ("Input Texture", 2D) = "black" {}
        _FlipY ("Flip Y", Float) = 0
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
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityCustomRenderTexture.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _FlipY;

            half4 frag(v2f_customrendertexture i) : SV_Target
            {
                float2 uv = i.globalTexcoord.xy;
                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;

                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            ENDCG
        }
    }

    Fallback Off
}
