Shader "Hidden/TSMP/Humanoid Skin Palette"
{
    Properties
    {
        _Rig ("Baked rig", 2D) = "black" {}
        _Pose ("Pose palette", 2D) = "black" {}
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
            #include "SkinRotation.cginc"
            Texture2D<float4> _Rig, _Pose;

            float4 frag(v2f_customrendertexture i) : SV_Target
            {
                uint x = (uint)i.vertex.x;
                float4 root = _Pose.Load(int3(55,0,0));
                if (root.w == 0.0 || x == 0u) return root;
                if (x == 1u) return _Pose.Load(int3(56,0,0));

                uint skin = 1u + (x - 2u) / 3u, axis = (x - 2u) % 3u;
                float3 hipsScale = _Rig.Load(int3(0,0,0)).xyz;
                float4 row = float4(float3(axis == 0u, axis == 1u, axis == 2u) * hipsScale, 0);
                float4 parent = _Pose.Load(int3(0,0,0));
                if (dot(parent,parent) < 0.5) parent = _Rig.Load(int3(1,0,0));
                uint depth = (uint)_Rig.Load(int3(3,skin,0)).x;
                [loop] for (uint j = 0u; j < depth; j++)
                {
                    uint node = (uint)_Rig.Load(int3(4u+j/4u,skin,0))[j & 3u];
                    float4 joint = _Rig.Load(int3(0,node,0));
                    float4 local = _Rig.Load(int3(1,node,0));
                    float4 received = 0;
                    if (joint.w >= 0) received = _Pose.Load(int3((int)joint.w,0,0));
                    if (dot(received,received) > 0.5)
                    {
                        local = TSMPSkinMul(TSMPSkinInverse(parent), received);
                        parent = received;
                    }
                    else parent = TSMPSkinMul(parent,local);
                    row.w += dot(row.xyz,joint.xyz);
                    row.xyz = TSMPSkinRotate(TSMPSkinInverse(local), row.xyz) * _Rig.Load(int3(2,node,0)).xyz;
                }
                float4 result = row.x*_Rig.Load(int3(0,skin,0)) + row.y*_Rig.Load(int3(1,skin,0))
                              + row.z*_Rig.Load(int3(2,skin,0));
                result.w += row.w;
                return result;
            }
            ENDCG
        }
    }
}
