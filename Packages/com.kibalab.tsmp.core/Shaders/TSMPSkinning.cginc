#ifndef TSMP_GPU_SKINNING_INCLUDED
#define TSMP_GPU_SKINNING_INCLUDED
#include "SkinRotation.cginc"
#if defined(SHADER_TARGET_SURFACE_ANALYSIS)
sampler2D _TSMPSkinPalette;
float4 _TSMPSkinPalette_TexelSize;
float4 TSMPSkinTexel(int x) { return tex2Dlod(_TSMPSkinPalette, float4((x+0.5)*_TSMPSkinPalette_TexelSize.x,0.5,0,0)); }
#else
Texture2D<float4> _TSMPSkinPalette;
float4 TSMPSkinTexel(int x) { return _TSMPSkinPalette.Load(int3(x,0,0)); }
#endif

#if defined(TSMP_BLEND_SHAPES)
#if defined(SHADER_TARGET_SURFACE_ANALYSIS)
sampler2D _TSMPShapeDeltas, _TSMPShapeWeights;
float4 _TSMPShapeDeltas_TexelSize, _TSMPShapeWeights_TexelSize;
float4 TSMPShapeDelta(uint i) { return tex2Dlod(_TSMPShapeDeltas, float4((float2(i & 255u, i >> 8) + 0.5) * _TSMPShapeDeltas_TexelSize.xy, 0, 0)); }
float TSMPShapeWeight(uint i) { return tex2Dlod(_TSMPShapeWeights, float4((i+0.5)*_TSMPShapeWeights_TexelSize.x, 0.5, 0, 0)).x; }
#else
Texture2D<float4> _TSMPShapeDeltas, _TSMPShapeWeights;
float4 TSMPShapeDelta(uint i) { return _TSMPShapeDeltas.Load(int3(i & 255u, i >> 8, 0)); }
float TSMPShapeWeight(uint i) { return _TSMPShapeWeights.Load(int3(i,0,0)).x; }
#endif
#endif

void TSMPApplyBlendShapes(inout float3 position, inout float3 normal, inout float4 tangent, float2 range)
{
#if defined(TSMP_BLEND_SHAPES)
    [loop] for (uint i = 0u; i < (uint)range.y; ++i)
    {
        uint cursor = (uint)range.x + 3u * i;
        float4 dp = TSMPShapeDelta(cursor);
        float factor = TSMPShapeWeight((uint)dp.w);
        if (factor == 0) continue;
        position += dp.xyz * factor;
        normal += TSMPShapeDelta(cursor+1u).xyz * factor;
        tangent.xyz += TSMPShapeDelta(cursor+2u).xyz * factor;
    }
#endif
}

void TSMPSkinVertex(inout float3 position, inout float3 normal, inout float4 tangent, float4 indices, float4 weights)
{
    float4 root = TSMPSkinTexel(0);
    if (root.w != 6.0 && root.w != 14.0) return;
    float4 x = 0, y = 0, z = 0;
    [unroll] for (uint i = 0; i < 4u; i++)
    {
        if (weights[i] <= 0.0) continue;
        int col = 2 + 3 * (int)indices[i];
        x += weights[i] * TSMPSkinTexel(col);
        y += weights[i] * TSMPSkinTexel(col+1);
        z += weights[i] * TSMPSkinTexel(col+2);
    }
    float4 q = TSMPSkinTexel(1);
    float4 v = float4(position, 1);
    float3 p = TSMPSkinRotate(q, float3(dot(x,v), dot(y,v), dot(z,v)));
    float3x3 basis = float3x3(x.xyz, y.xyz, z.xyz);
    float3x3 cofactors = float3x3(cross(y.xyz,z.xyz), cross(z.xyz,x.xyz), cross(x.xyz,y.xyz));
    float parity = dot(x.xyz, cofactors[0]) < 0.0 ? -1.0 : 1.0;
    float3 n = TSMPSkinRotate(q, parity * mul(cofactors,normal));
    float3 t = TSMPSkinRotate(q, mul(basis,tangent.xyz));
    float scale = length(unity_ObjectToWorld._m00_m10_m20);
    if (root.w == 6.0)
        position = p + root.xyz / max(scale, 1e-8);
    else
    {
        position = mul(unity_WorldToObject, float4(root.xyz + p * scale, 1)).xyz;
        n = mul((float3x3)unity_WorldToObject,n);
        t = mul((float3x3)unity_WorldToObject,t);
    }
    if (dot(n,n) > 1e-12) normal = normalize(n);
    t -= normal * dot(normal,t);
    if (dot(t,t) > 1e-12) tangent.xyz = normalize(t);
    tangent.w *= parity;
}
#endif
