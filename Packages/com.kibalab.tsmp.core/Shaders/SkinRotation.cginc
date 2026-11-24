#ifndef TSMP_SKIN_ROTATION_INCLUDED
#define TSMP_SKIN_ROTATION_INCLUDED
float4 TSMPSkinInverse(float4 q) { return float4(-q.xyz, q.w); }
float4 TSMPSkinMul(float4 a, float4 b)
{
    return float4(a.w*b.xyz + b.w*a.xyz + cross(a.xyz,b.xyz), a.w*b.w - dot(a.xyz,b.xyz));
}
float3 TSMPSkinRotate(float4 q, float3 p)
{
    return p + 2.0 * cross(q.xyz, cross(q.xyz, p) + q.w*p);
}
#endif
