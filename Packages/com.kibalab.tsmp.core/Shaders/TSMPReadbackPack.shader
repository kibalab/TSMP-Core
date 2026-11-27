Shader "Hidden/TSMP/Readback Pack"
{
    Properties
    {
        _MainTex ("Payload Bytes", 2D) = "black" {}
        _HeaderTex ("Header Bytes", 2D) = "black" {}
        _OutputWidth ("Output Width", Float) = 1
        _OutputHeight ("Output Height", Float) = 1
        _PayloadWidth ("Payload Width", Float) = 1
        _HeaderPixels ("Header Pixels", Float) = 14
        _PayloadPixels ("Payload Pixels", Float) = 1
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
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float4> _MainTex;
            Texture2D<float4> _HeaderTex;
            float _OutputWidth;
            float _OutputHeight;
            float _PayloadWidth;
            float _HeaderPixels;
            float _PayloadPixels;

            float4 frag(v2f_img i) : SV_Target
            {
                int2 pixel = min((int2)(i.uv * float2(_OutputWidth, _OutputHeight)), (int2)float2(_OutputWidth - 1, _OutputHeight - 1));
                int index = pixel.y * (int)_OutputWidth + pixel.x;
                if (index < (int)_HeaderPixels)
                    return _HeaderTex.Load(int3(index, 0, 0));
                index -= (int)_HeaderPixels;
                if (index >= (int)_PayloadPixels)
                    return 0;
                uint width = max(1u, (uint)_PayloadWidth);
                uint address = (uint)index;
                return _MainTex.Load(int3(address % width, address / width, 0));
            }
            ENDCG
        }
    }
    Fallback Off
}
