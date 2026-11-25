Shader "Hidden/TSMP/Humanoid Pose Palette"
{
    Properties { _Field ("Channel, network ID, hash low/high", Vector) = (0,0,0,0) }
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
            float4 _Field;

            float4 UnpackRotation(TSMPByteView v, uint addr)
            {
                uint bits = TSMPReadUInt32LE(v, addr);
                uint largest = bits & 3u;
                uint3 codes = uint3((bits >> 2) & 4095u, (bits >> 14) & 4095u,
                    ((bits >> 26) & 63u) | ((TSMPReadByte(v, addr + 4u) & 63u) << 6));
                float3 q = codes * (1.41421356237 / 4095.0) - 0.70710678118;
                float w = sqrt(saturate(1.0 - dot(q, q)));
                if (largest == 0u) return float4(w, q);
                if (largest == 1u) return float4(q.x, w, q.yz);
                if (largest == 2u) return float4(q.xy, w, q.z);
                return float4(q, w);
            }

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                uint id = (uint)i.vertex.x;
                TSMPByteView v;
                uint hash = (uint)_Field.z | ((uint)_Field.w << 16);
                if (!TSMPTryGetField((uint)_Field.x, (uint)_Field.y, hash, v) || v.valueType != 8u) return 0;
                uint header = TSMPReadUInt32LE(v, 0u);
                uint count = header >> 16, flags = (header >> 8) & 255u;
                uint tail = 4u + count * 6u;
                if ((header & 255u) != 4u || (flags != 6u && flags != 14u) ||
                    count > 55u || v.length != tail + 17u) return 0;

                uint seenLo = 0u, seenHi = 0u;
                bool valid = true;
                uint record = 0u;
                [loop] for (uint n = 0u; n < count; n++)
                {
                    uint addr = 4u + n * 6u;
                    uint bone = TSMPReadByte(v, addr);
                    uint mask = 1u << (bone & 31u);
                    if (bone < 32u)
                    {
                        valid = valid && (seenLo & mask) == 0u;
                        seenLo |= mask;
                    }
                    else
                    {
                        valid = valid && bone < 55u && (seenHi & mask) == 0u;
                        seenHi |= mask;
                    }
                    if (bone == id) record = addr + 1u;
                }
                if (!valid) return 0;
                if (id == 55u)
                {
                    float3 p = float3(TSMPReadFloat32LE(v, tail), TSMPReadFloat32LE(v, tail + 4u),
                        TSMPReadFloat32LE(v, tail + 8u));
                    if (!all(isfinite(p))) return 0;
                    return float4(p, (float)flags);
                }
                if (id == 56u) return UnpackRotation(v, tail + 12u);
                return record != 0u ? UnpackRotation(v, record) : 0;
            }
            ENDCG
        }
    }
}
