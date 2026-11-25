Shader "Hidden/TSMP/BlendShape Weights"
{
    Properties
    {
        _Field ("Channel, network ID, hash low/high", Vector) = (0,0,0,0)
        _ShapeMap ("Sender indices and frame weights", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZTest Always ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #include "UnityCustomRenderTexture.cginc"
            #include "TSMP.cginc"
            Texture2D<float4> _ShapeMap;
            float4 _Field;

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                TSMPByteView v;
                uint hash = (uint)_Field.z | ((uint)_Field.w << 16);
                if (!TSMPTryGetBytes((uint)_Field.x, (uint)_Field.y, hash, v)) return 0;
                uint header = TSMPReadUInt16LE(v, 0u), count = header >> 8;
                if ((header & 255u) != 1u || v.length != 2u + 3u * count) return 0;
                float4 key = _ShapeMap.Load(int3((int)i.vertex.x, 0, 0));
                uint mapping = (uint)key.x;
                uint id = mapping & 65535u, expected = mapping >> 16;
                uint record = 0u;
                if (expected < count) record = TSMPReadBytesLE(v, 2u + 3u * expected, 3u);
                if (expected >= count || (record & 65535u) != id)
                {
                    [loop] for (uint e = 0u; e < count; ++e)
                    {
                        record = TSMPReadBytesLE(v, 2u + 3u * e, 3u);
                        if ((record & 65535u) == id) break;
                    }
                }
                float weight = (record & 65535u) == id ? min(record >> 16, 100u) : 0;
                float factor = max(0, (weight - key.y) / (key.z - key.y));
                return key.w > 0 ? factor : saturate(factor);
            }
            ENDCG
        }
    }
}
