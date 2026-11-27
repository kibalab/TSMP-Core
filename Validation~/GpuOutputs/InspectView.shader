Shader "Hidden/TSMP Validation/Inspect GPU View"
{
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
            #include "Packages/com.kibalab.tsmp.core/Shaders/TSMP.cginc"
            uint _Mode, _Channel, _Width;
            float4 frag(v2f_img i) : SV_Target
            {
                uint pixel = (uint)i.pos.y * _Width + (uint)i.pos.x;
                if (_Mode == 3u)
                {
                    int number;
                    if (!TSMPTryGetInt(_Channel, 13u, 0xfacedeadu, number)) return 0;
                    uint bits = asuint(number);
                    return float4(bits & 255u, (bits >> 8) & 255u, (bits >> 16) & 255u, bits >> 24) / 255.0;
                }
                TSMPByteView v;
                bool present;
                if (_Mode == 0u) present = TSMPTryGetFrame(_Channel, v);
                else present = TSMPTryGetField(_Channel, 13u, 0xfedcba98u, v);
                if (!present) return 0;
                if (_Mode != 2u)
                {
                    uint o = pixel * 4u;
                    return float4(TSMPReadByte(v, o), TSMPReadByte(v, o + 1u),
                        TSMPReadByte(v, o + 2u), TSMPReadByte(v, o + 3u)) / 255.0;
                }
                uint w = pixel == 0u ? TSMPStreamId(_Channel) : pixel == 1u ? TSMPFrameIndex(_Channel) :
                    pixel == 2u ? v.length : pixel == 3u ? v.valueType :
                    pixel == 4u ? (uint)(TSMPReadFloat32LE(v, 0u) * 1000.0) :
                    pixel == 5u ? TSMPReadUInt16LE(v, 3u) : TSMPReadUInt32LE(v, v.length - 1u);
                return float4(w & 255u, (w >> 8) & 255u, (w >> 16) & 255u, w >> 24) / 255.0;
            }
            ENDCG
        }
    }
}
