#ifndef TSMP_SHADER_RECEIVE_INCLUDED
#define TSMP_SHADER_RECEIVE_INCLUDED

Texture2D<float4> _UdonTSMPChannel0, _UdonTSMPChannel1, _UdonTSMPChannel2, _UdonTSMPChannel3;

struct TSMPByteView
{
    uint channel;
    uint offset;
    uint length;
    uint valueType;
};

uint TSMPAtlasWord(uint channel, uint word)
{
    int3 p = int3(word & 255u, word >> 8, 0);
    float4 rgba = 0;
    if (channel == 0u) rgba = _UdonTSMPChannel0.Load(p);
    else if (channel == 1u) rgba = _UdonTSMPChannel1.Load(p);
    else if (channel == 2u) rgba = _UdonTSMPChannel2.Load(p);
    else if (channel == 3u) rgba = _UdonTSMPChannel3.Load(p);
    uint4 b = (uint4)round(saturate(rgba) * 255.0);
    return b.x | (b.y << 8) | (b.z << 16) | (b.w << 24);
}

uint TSMPStreamId(uint channel) { return TSMPAtlasWord(channel, 1u); }
uint TSMPFrameIndex(uint channel) { return TSMPAtlasWord(channel, 2u); }

bool TSMPTryGetFrame(uint channel, out TSMPByteView view)
{
    view = (TSMPByteView)0;
    if (TSMPAtlasWord(channel, 0u) != 0x504d5354u) return false;
    view.channel = channel;
    view.offset = (5u + 5u * TSMPAtlasWord(channel, 4u)) * 4u;
    view.length = TSMPAtlasWord(channel, 3u);
    return true;
}

bool TSMPTryGetField(uint channel, uint networkId, uint hash, out TSMPByteView view)
{
    view = (TSMPByteView)0;
    if (TSMPAtlasWord(channel, 0u) != 0x504d5354u) return false;
    uint low = 0u, high = TSMPAtlasWord(channel, 4u);
    [loop] while (low < high)
    {
        uint mid = (low + high) >> 1;
        uint entry = 5u + 5u * mid;
        uint key = TSMPAtlasWord(channel, entry);
        uint id = key & 65535u;
        uint h = TSMPAtlasWord(channel, entry + 1u);
        if (id == networkId && h == hash)
        {
            view.channel = channel;
            view.valueType = key >> 16;
            view.offset = TSMPAtlasWord(channel, entry + 2u);
            view.length = TSMPAtlasWord(channel, entry + 3u);
            return true;
        }
        if (id < networkId || (id == networkId && h < hash)) low = mid + 1u;
        else high = mid;
    }
    return false;
}

bool TSMPTryGetFrame(out TSMPByteView view) { return TSMPTryGetFrame(0u, view); }
bool TSMPTryGetField(uint id, uint hash, out TSMPByteView view) { return TSMPTryGetField(0u, id, hash, view); }

uint TSMPReadBytesLE(TSMPByteView view, uint offset, uint count)
{
    if (count == 0u || count > 4u || offset > view.length || count > view.length - offset) return 0u;
    uint address = view.offset + offset;
    uint lane = address & 3u;
    uint word = address >> 2;
    uint bits = TSMPAtlasWord(view.channel, word) >> (8u * lane);
    if (lane + count > 4u) bits |= TSMPAtlasWord(view.channel, word + 1u) << (8u * (4u - lane));
    return count == 4u ? bits : bits & ((1u << (8u * count)) - 1u);
}
uint TSMPReadByte(TSMPByteView v, uint offset) { return TSMPReadBytesLE(v, offset, 1u); }
uint TSMPReadUInt16LE(TSMPByteView v, uint offset) { return TSMPReadBytesLE(v, offset, 2u); }
uint TSMPReadUInt32LE(TSMPByteView v, uint offset) { return TSMPReadBytesLE(v, offset, 4u); }
float TSMPReadFloat32LE(TSMPByteView v, uint offset) { return asfloat(TSMPReadUInt32LE(v, offset)); }

#include "TSMPShaderValues.cginc"

#endif
