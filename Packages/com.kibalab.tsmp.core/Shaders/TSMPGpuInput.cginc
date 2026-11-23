#ifndef TSMP_GPU_INPUT_INCLUDED
#define TSMP_GPU_INPUT_INCLUDED

Texture2D<float4> _MainTex;
float4 _TSMPByteView;
float4 _TSMPFrameInfo;
float4 _TSMPOutputSize;

uint TSMPByteLength() { return (uint)_TSMPByteView.y; }
uint TSMPValueType() { return (uint)_TSMPByteView.w; }
uint TSMPStreamId() { return (uint)_TSMPFrameInfo.x | ((uint)_TSMPFrameInfo.y << 16); }
uint TSMPFrameIndex() { return (uint)_TSMPFrameInfo.z | ((uint)_TSMPFrameInfo.w << 16); }

bool TSMPHasBytes(uint offset, uint count)
{
    uint length = TSMPByteLength();
    return offset <= length && count <= length - offset;
}

uint TSMPReadByte(uint offset)
{
    if (offset >= TSMPByteLength())
        return 0u;
    uint address = (uint)_TSMPByteView.x + offset;
    uint pixel = address >> 2;
    uint width = (uint)_TSMPByteView.z;
    float4 rgba = _MainTex.Load(int3(pixel % width, pixel / width, 0));
    return (uint)round(saturate(rgba[address & 3u]) * 255.0);
}

uint TSMPReadUInt16LE(uint offset)
{
    if (!TSMPHasBytes(offset, 2u))
        return 0u;
    return TSMPReadByte(offset) | (TSMPReadByte(offset + 1u) << 8);
}

uint TSMPReadUInt32LE(uint offset)
{
    if (!TSMPHasBytes(offset, 4u))
        return 0u;
    return TSMPReadUInt16LE(offset) | (TSMPReadUInt16LE(offset + 2u) << 16);
}

float TSMPReadFloat32LE(uint offset) { return asfloat(TSMPReadUInt32LE(offset)); }

#endif
