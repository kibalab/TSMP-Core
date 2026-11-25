Shader "TSMP/Humanoid GPU Skinning"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _TSMPSkinPalette ("Skin palette", 2D) = "black" {}
        [HideInInspector] _TSMPShapeDeltas ("Shape deltas", 2D) = "black" {}
        [HideInInspector] _TSMPShapeWeights ("Shape weights", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }
        CGPROGRAM
        #pragma target 3.5
        #pragma shader_feature_local _ TSMP_BLEND_SHAPES
        #pragma surface surf Standard vertex:vert addshadow
        #include "TSMPSkinning.cginc"
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; };
        void vert(inout appdata_full v)
        {
            float3 p = v.vertex.xyz;
            TSMPApplyBlendShapes(p, v.normal, v.tangent, v.texcoord1.zw);
            TSMPSkinVertex(p, v.normal, v.tangent, v.texcoord2, v.texcoord3);
            v.vertex.xyz = p;
        }
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            o.Albedo = tex2D(_MainTex, i.uv_MainTex).rgb * _Color.rgb;
            o.Smoothness = 0;
        }
        ENDCG
    }
    Fallback Off
}
