Shader "Hidden/TSMP/Publish Shader Channel"
{
    Properties
    {
        [HideInInspector] _MainTex ("Decoded bytes", 2D) = "black" {}
        [HideInInspector] _Previous ("Previous channel", 2D) = "black" {}
        [HideInInspector] _Directory ("Field directory", 2D) = "black" {}
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

            Texture2D<float4> _MainTex, _Previous, _Directory;
            float4 _Layout;
            float _End;
            #define _Fields ((uint)_Layout.x)
            #define _RawBytes ((uint)_Layout.y)
            #define _Prefix ((uint)_Layout.z)
            #define _InputWidth ((uint)_Layout.w)

            uint4 Bytes(float4 rgba) { return (uint4)round(saturate(rgba) * 255.0); }
            float4 Descriptor(uint w) { return _Directory.Load(int3(w & 255u, w >> 8, 0)); }
            uint Word(uint w)
            {
                uint4 b = Bytes(Descriptor(w));
                return b.x | (b.y << 8) | (b.z << 16) | (b.w << 24);
            }
            float4 Input(uint w) { return _MainTex.Load(int3(w % _InputWidth, w / _InputWidth, 0)); }

            float4 frag(v2f_img i) : SV_Target
            {
                uint w = (uint)i.pos.y * 256u + (uint)i.pos.x;
                uint dir = 5u + _Fields * 5u;
                if (w < dir) return Descriptor(w);
                if (w * 4u >= _End) return 0;
                if (w - dir < (_RawBytes + 3u) / 4u) return Input(w - dir + _Prefix / 4u);

                uint low = 0u, high = _Fields;
                [loop] while (low < high)
                {
                    uint mid = (low + high) >> 1;
                    if (Word(7u + mid * 5u) <= w * 4u) low = mid + 1u;
                    else high = mid;
                }
                uint entry = low * 5u;
                uint delta = w * 4u - Word(entry + 2u);
                uint src = Word(entry + 4u);
                if ((src & 0x80000000u) != 0u)
                {
                    uint old = ((src & 0x7fffffffu) + delta) >> 2;
                    return _Previous.Load(int3(old & 255u, old >> 8, 0));
                }
                uint address = _Prefix + src + delta;
                uint lane = address & 3u;
                uint4 first = Bytes(Input(address >> 2));
                if (lane == 0u) return first / 255.0;
                uint4 second = 0u;
                if (Word(entry + 3u) - delta > 4u - lane) second = Bytes(Input((address >> 2) + 1u));
                uint4 value = lane == 1u ? uint4(first.yzw, second.x) :
                    lane == 2u ? uint4(first.zw, second.xy) : uint4(first.w, second.xyz);
                return value / 255.0;
            }
            ENDCG
        }
    }
    Fallback Off
}
