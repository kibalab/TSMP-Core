Shader "Hidden/TSMP Validation/Typed Fields"
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

            bool Check(uint test)
            {
                uint id = 27u, cursor = 0u, cp = 0u;
                int n; bool b; float f; float2 v2; float3 v3; float4 v4; float4x4 m;
                TSMPArrayView a; TSMPByteView s;
                switch (test)
                {
                    case 0: return TSMPTryGetBool(id, 1u, b) && !b;
                    case 1: return TSMPTryGetInt(id, 2u, n) && asuint(n) == 0x80000001u;
                    case 2: return TSMPTryGetFloat(id, 3u, f) && f == 0;
                    case 3: return TSMPTryGetVector2(id, 4u, v2) && all(v2 == float2(-1, 0.5));
                    case 4: return TSMPTryGetVector3(id, 5u, v3) && all(v3 == float3(8, -2, 3));
                    case 5: return TSMPTryGetQuaternion(id, 6u, v4) && all(v4 == float4(1, -2, 3, 4));
                    case 6:
                        if (!TSMPTryGetUtf8(id, 7u, s) || s.length != 8u) return false;
                        if (!TSMPTryReadCodePoint(s, cursor, cp) || cp != 65u) return false;
                        if (!TSMPTryReadCodePoint(s, cursor, cp) || cp != 0xd55cu) return false;
                        return TSMPTryReadCodePoint(s, cursor, cp) && cp == 0x1f441u && cursor == s.length;
                    case 7: return TSMPTryGetBytes(id, 8u, s) && TSMPTryReadByteAt(s, 1u, cp) && cp == 128u;
                    case 8: return TSMPTryGetBoolArray(id, 9u, a) && a.count == 2u && TSMPTryReadBoolAt(a, 1u, b) && b;
                    case 9: return TSMPTryGetIntArray(id, 10u, a) && TSMPTryReadIntAt(a, 1u, n) && n == -7;
                    case 10: return TSMPTryGetFloatArray(id, 11u, a) && TSMPTryReadFloatAt(a, 1u, f) && f == -2;
                    case 11: return TSMPTryGetVector2Array(id, 12u, a) && TSMPTryReadVector2At(a, 1u, v2) && all(v2 == float2(0,1));
                    case 12: return TSMPTryGetVector3Array(id, 13u, a) && TSMPTryReadVector3At(a, 1u, v3) && all(v3 == float3(0,0,-1));
                    case 13: return TSMPTryGetQuaternionArray(id, 14u, a) && TSMPTryReadQuaternionAt(a, 0u, v4) && all(v4 == float4(0,0,0,1));
                    case 14:
                        if (!TSMPTryGetUtf8Array(id, 15u, a) || a.count != 3u) return false;
                        if (!TSMPTryReadNextUtf8(a, cursor, s) || s.length != 0u) return false;
                        if (!TSMPTryReadNextUtf8(a, cursor, s) || s.length != 2u || TSMPReadByte(s, 1u) != 0xa9u) return false;
                        if (!TSMPTryReadUtf8At(a, 2u, s)) return false;
                        cursor = 0u;
                        return TSMPTryReadCodePoint(s, cursor, cp) && cp == 0x1f441u;
                    case 15: return TSMPTryGetMatrix4x4(id, 16u, m) && m[1][3] == 7 && m[3][1] == 13;
                    case 16: return TSMPTryGetFloat4(id, 11u, v4) && all(v4 == float4(1,-2,3,4));
                    case 17: return TSMPTryGetColor32(id, 8u, v4) && all(round(v4 * 255.0) == float4(0,128,255,64));
                    case 18: return TSMPTryGetIntArray(id, 17u, a) && a.count == 0u && !TSMPTryReadIntAt(a, 0u, n) && n == 0;
                    case 19: return TSMPTryGetUtf8(id, 18u, s) && s.length == 0u && !TSMPTryReadCodePoint(s, cursor, cp);
                    case 20: return TSMPTryGetBytes(id, 19u, s) && s.length == 0u && !TSMPTryReadByteAt(s, 0u, cp);
                    case 21: return TSMPTryGetUtf8Array(id, 20u, a) && a.count == 0u && !TSMPTryReadUtf8At(a, 0u, s);
                    case 22: return !TSMPTryGetFloat(3u, id, 3u, f) && f == 0;
                    case 23: return !TSMPTryGetFloat(id, 2u, f) && f == 0;
                    case 24: return !TSMPTryGetFloat4(id, 16u, v4) && all(v4 == 0);
                    case 25: return TSMPTryGetFloatArray(id, 11u, a) && !TSMPTryReadFloatAt(a, 0xffffffffu, f);
                    case 26: return !TSMPTryGetUtf8(id, 99999u, s) && s.length == 0u;
                    case 27: return TSMPTryGetVector3Array(id, 13u, a) && !TSMPTryReadVector2At(a, 0u, v2);
                    case 28: return !TSMPTryGetUtf8Array(id, 22u, a) && a.count == 0u;
                    default:
                        if (!TSMPTryGetUtf8(id, test == 29u ? 21u : test == 30u ? 23u : 24u, s)) return false;
                        return !TSMPTryReadCodePoint(s, cursor, cp) && cursor == 0u && cp == 0u;
                }
            }

            float4 frag(v2f_img i) : SV_Target { return Check((uint)i.pos.x) ? 1 : 0; }
            ENDCG
        }
    }
}
