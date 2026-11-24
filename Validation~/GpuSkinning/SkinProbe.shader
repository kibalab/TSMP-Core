Shader "Hidden/TSMP Validation/Skin Probe"
{
    Properties { _TSMPSkinPalette ("Skin", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Packages/com.kibalab.tsmp.core/Shaders/TSMPSkinning.cginc"
            float _PointCount, _ProbeMode;
            struct Out { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float size : PSIZE; };
            Out vert(appdata_full v, uint index : SV_VertexID)
            {
                Out o;
                float3 p = v.vertex.xyz;
                TSMPSkinVertex(p,v.normal,v.tangent,v.texcoord2,v.texcoord3);
                o.world = mul(unity_ObjectToWorld,float4(p,1)).xyz;
                if (_ProbeMode == 1) o.world = UnityObjectToWorldNormal(v.normal);
                if (_ProbeMode == 2) o.world = UnityObjectToWorldDir(v.tangent.xyz);
                o.pos = float4((index+0.5)/_PointCount*2.0-1.0,0,0.5,1);
                o.size = 1;
                return o;
            }
            float4 frag(Out i) : SV_Target { return float4(i.world,1); }
            ENDCG
        }
    }
}
