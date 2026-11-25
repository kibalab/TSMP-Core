#ifndef TSMP_SHADER_VALUES_INCLUDED
#define TSMP_SHADER_VALUES_INCLUDED

static const uint TSMP_TYPE_BOOL = 1u;
static const uint TSMP_TYPE_INT32 = 2u;
static const uint TSMP_TYPE_FLOAT32 = 3u;
static const uint TSMP_TYPE_VECTOR2 = 4u;
static const uint TSMP_TYPE_VECTOR3 = 5u;
static const uint TSMP_TYPE_QUATERNION = 6u;
static const uint TSMP_TYPE_UTF8 = 7u;
static const uint TSMP_TYPE_BYTES = 8u;
static const uint TSMP_TYPE_BOOL_ARRAY = 9u;
static const uint TSMP_TYPE_INT32_ARRAY = 10u;
static const uint TSMP_TYPE_FLOAT32_ARRAY = 11u;
static const uint TSMP_TYPE_VECTOR2_ARRAY = 12u;
static const uint TSMP_TYPE_VECTOR3_ARRAY = 13u;
static const uint TSMP_TYPE_QUATERNION_ARRAY = 14u;
static const uint TSMP_TYPE_UTF8_ARRAY = 15u;

struct TSMPArrayView
{
    TSMPByteView bytes;
    uint count;
};

float2 TSMPValueFloat2(TSMPByteView v, uint offset)
{
    return float2(TSMPReadFloat32LE(v, offset), TSMPReadFloat32LE(v, offset + 4u));
}

float3 TSMPValueFloat3(TSMPByteView v, uint offset)
{
    return float3(TSMPValueFloat2(v, offset), TSMPReadFloat32LE(v, offset + 8u));
}

float4 TSMPValueFloat4(TSMPByteView v, uint offset)
{
    return float4(TSMPValueFloat2(v, offset), TSMPValueFloat2(v, offset + 8u));
}

bool TSMPTypedField(uint channel, uint id, uint hash, uint type, out TSMPByteView v)
{
    if (TSMPTryGetField(channel, id, hash, v) && v.valueType == type) return true;
    v = (TSMPByteView)0;
    return false;
}

bool TSMPFixedArray(uint channel, uint id, uint hash, uint type, uint stride, out TSMPArrayView a)
{
    a = (TSMPArrayView)0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, type, v) || v.length % stride != 0u) return false;
    a.bytes = v;
    a.count = v.length / stride;
    return true;
}

bool TSMPTryGetBool(uint channel, uint id, uint hash, out bool value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_BOOL, v) || v.length != 1u) return false;
    value = TSMPReadByte(v, 0u) != 0u;
    return true;
}

bool TSMPTryGetInt(uint channel, uint id, uint hash, out int value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_INT32, v) || v.length != 4u) return false;
    value = asint(TSMPReadUInt32LE(v, 0u));
    return true;
}

bool TSMPTryGetFloat(uint channel, uint id, uint hash, out float value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_FLOAT32, v) || v.length != 4u) return false;
    value = TSMPReadFloat32LE(v, 0u);
    return true;
}

bool TSMPTryGetVector2(uint channel, uint id, uint hash, out float2 value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_VECTOR2, v) || v.length != 8u) return false;
    value = TSMPValueFloat2(v, 0u);
    return true;
}

bool TSMPTryGetVector3(uint channel, uint id, uint hash, out float3 value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_VECTOR3, v) || v.length != 12u) return false;
    value = TSMPValueFloat3(v, 0u);
    return true;
}

bool TSMPTryGetQuaternion(uint channel, uint id, uint hash, out float4 value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_QUATERNION, v) || v.length != 16u) return false;
    value = TSMPValueFloat4(v, 0u);
    return true;
}

bool TSMPTryGetBytes(uint channel, uint id, uint hash, out TSMPByteView v) { return TSMPTypedField(channel, id, hash, TSMP_TYPE_BYTES, v); }
bool TSMPTryGetUtf8(uint channel, uint id, uint hash, out TSMPByteView v) { return TSMPTypedField(channel, id, hash, TSMP_TYPE_UTF8, v); }

bool TSMPTryReadByteAt(TSMPByteView v, uint index, out uint value)
{
    value = 0u;
    if ((v.valueType != TSMP_TYPE_BYTES && v.valueType != TSMP_TYPE_UTF8) || index >= v.length) return false;
    value = TSMPReadByte(v, index);
    return true;
}

bool TSMPTryGetBoolArray(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    return TSMPFixedArray(channel, id, hash, TSMP_TYPE_BOOL_ARRAY, 1u, a);
}

bool TSMPTryReadBoolAt(TSMPArrayView a, uint index, out bool value)
{
    value = 0;
    if (a.bytes.valueType != TSMP_TYPE_BOOL_ARRAY || index >= a.count) return false;
    value = TSMPReadByte(a.bytes, index) != 0u;
    return true;
}

bool TSMPTryGetIntArray(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    return TSMPFixedArray(channel, id, hash, TSMP_TYPE_INT32_ARRAY, 4u, a);
}

bool TSMPTryReadIntAt(TSMPArrayView a, uint index, out int value)
{
    value = 0;
    if (a.bytes.valueType != TSMP_TYPE_INT32_ARRAY || index >= a.count) return false;
    value = asint(TSMPReadUInt32LE(a.bytes, index * 4u));
    return true;
}

bool TSMPTryGetFloatArray(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    return TSMPFixedArray(channel, id, hash, TSMP_TYPE_FLOAT32_ARRAY, 4u, a);
}

bool TSMPTryReadFloatAt(TSMPArrayView a, uint index, out float value)
{
    value = 0;
    if (a.bytes.valueType != TSMP_TYPE_FLOAT32_ARRAY || index >= a.count) return false;
    value = TSMPReadFloat32LE(a.bytes, index * 4u);
    return true;
}

bool TSMPTryGetVector2Array(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    return TSMPFixedArray(channel, id, hash, TSMP_TYPE_VECTOR2_ARRAY, 8u, a);
}

bool TSMPTryReadVector2At(TSMPArrayView a, uint index, out float2 value)
{
    value = 0;
    if (a.bytes.valueType != TSMP_TYPE_VECTOR2_ARRAY || index >= a.count) return false;
    value = TSMPValueFloat2(a.bytes, index * 8u);
    return true;
}

bool TSMPTryGetVector3Array(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    return TSMPFixedArray(channel, id, hash, TSMP_TYPE_VECTOR3_ARRAY, 12u, a);
}

bool TSMPTryReadVector3At(TSMPArrayView a, uint index, out float3 value)
{
    value = 0;
    if (a.bytes.valueType != TSMP_TYPE_VECTOR3_ARRAY || index >= a.count) return false;
    value = TSMPValueFloat3(a.bytes, index * 12u);
    return true;
}

bool TSMPTryGetQuaternionArray(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    return TSMPFixedArray(channel, id, hash, TSMP_TYPE_QUATERNION_ARRAY, 16u, a);
}

bool TSMPTryReadQuaternionAt(TSMPArrayView a, uint index, out float4 value)
{
    value = 0;
    if (a.bytes.valueType != TSMP_TYPE_QUATERNION_ARRAY || index >= a.count) return false;
    value = TSMPValueFloat4(a.bytes, index * 16u);
    return true;
}

bool TSMPTryReadNextUtf8(TSMPArrayView a, inout uint cursor, out TSMPByteView text)
{
    text = (TSMPByteView)0;
    if (a.bytes.valueType != TSMP_TYPE_UTF8_ARRAY || cursor > a.bytes.length || a.bytes.length - cursor < 2u) return false;
    uint length = TSMPReadUInt16LE(a.bytes, cursor);
    uint start = cursor + 2u;
    if (length > a.bytes.length - start) return false;
    text = a.bytes;
    text.valueType = TSMP_TYPE_UTF8;
    text.offset += start;
    text.length = length;
    cursor = start + length;
    return true;
}

bool TSMPTryGetUtf8Array(uint channel, uint id, uint hash, out TSMPArrayView a)
{
    a = (TSMPArrayView)0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_UTF8_ARRAY, v) || v.length < 2u) return false;
    TSMPArrayView candidate;
    candidate.count = TSMPReadUInt16LE(v, 0u);
    candidate.bytes = v;
    candidate.bytes.offset += 2u;
    candidate.bytes.length -= 2u;
    if (candidate.count > candidate.bytes.length / 2u) return false;
    uint cursor = 0u;
    [loop] for (uint i = 0u; i < candidate.count; ++i)
    {
        TSMPByteView item;
        if (!TSMPTryReadNextUtf8(candidate, cursor, item)) return false;
    }
    if (cursor != candidate.bytes.length) return false;
    a = candidate;
    return true;
}

bool TSMPTryReadUtf8At(TSMPArrayView a, uint index, out TSMPByteView text)
{
    text = (TSMPByteView)0;
    if (a.bytes.valueType != TSMP_TYPE_UTF8_ARRAY || index >= a.count) return false;
    uint cursor = 0u;
    [loop] for (uint i = 0u; i <= index; ++i)
        if (!TSMPTryReadNextUtf8(a, cursor, text)) return false;
    return true;
}

bool TSMPTryReadCodePoint(TSMPByteView text, inout uint cursor, out uint codePoint)
{
    codePoint = 0u;
    if (text.valueType != TSMP_TYPE_UTF8 || cursor >= text.length) return false;
    uint lead = TSMPReadByte(text, cursor);
    uint width = lead < 0x80u ? 1u : lead < 0xc2u ? 0u : lead < 0xe0u ? 2u : lead < 0xf0u ? 3u : lead < 0xf5u ? 4u : 0u;
    if (width == 0u || width > text.length - cursor) return false;
    uint cp = width == 1u ? lead : lead & (0x7fu >> width);
    [loop] for (uint i = 1u; i < width; ++i)
    {
        uint b = TSMPReadByte(text, cursor + i);
        if ((b & 0xc0u) != 0x80u) return false;
        cp = (cp << 6u) | (b & 0x3fu);
    }
    if ((width == 3u && cp < 0x800u) || (width == 4u && cp < 0x10000u) ||
        (cp >= 0xd800u && cp <= 0xdfffu) || cp > 0x10ffffu) return false;
    codePoint = cp;
    cursor += width;
    return true;
}

bool TSMPTryGetFloat4(uint channel, uint id, uint hash, out float4 value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_FLOAT32_ARRAY, v) || v.length != 16u) return false;
    value = TSMPValueFloat4(v, 0u);
    return true;
}

bool TSMPTryGetColor32(uint channel, uint id, uint hash, out float4 value)
{
    value = 0;
    TSMPByteView v;
    if (!TSMPTryGetBytes(channel, id, hash, v) || v.length != 4u) return false;
    uint rgba = TSMPReadUInt32LE(v, 0u);
    value = float4(rgba & 255u, (rgba >> 8u) & 255u, (rgba >> 16u) & 255u, rgba >> 24u) / 255.0;
    return true;
}

bool TSMPTryGetMatrix4x4(uint channel, uint id, uint hash, out float4x4 value)
{
    value = (float4x4)0;
    TSMPByteView v;
    if (!TSMPTypedField(channel, id, hash, TSMP_TYPE_FLOAT32_ARRAY, v) || v.length != 64u) return false;
    value = float4x4(TSMPValueFloat4(v, 0u), TSMPValueFloat4(v, 16u), TSMPValueFloat4(v, 32u), TSMPValueFloat4(v, 48u));
    return true;
}

bool TSMPTryGetBool(uint id, uint hash, out bool v) { return TSMPTryGetBool(0u, id, hash, v); }
bool TSMPTryGetInt(uint id, uint hash, out int v) { return TSMPTryGetInt(0u, id, hash, v); }
bool TSMPTryGetFloat(uint id, uint hash, out float v) { return TSMPTryGetFloat(0u, id, hash, v); }
bool TSMPTryGetVector2(uint id, uint hash, out float2 v) { return TSMPTryGetVector2(0u, id, hash, v); }
bool TSMPTryGetVector3(uint id, uint hash, out float3 v) { return TSMPTryGetVector3(0u, id, hash, v); }
bool TSMPTryGetQuaternion(uint id, uint hash, out float4 v) { return TSMPTryGetQuaternion(0u, id, hash, v); }
bool TSMPTryGetBytes(uint id, uint hash, out TSMPByteView v) { return TSMPTryGetBytes(0u, id, hash, v); }
bool TSMPTryGetUtf8(uint id, uint hash, out TSMPByteView v) { return TSMPTryGetUtf8(0u, id, hash, v); }
bool TSMPTryGetBoolArray(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetBoolArray(0u, id, hash, v); }
bool TSMPTryGetIntArray(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetIntArray(0u, id, hash, v); }
bool TSMPTryGetFloatArray(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetFloatArray(0u, id, hash, v); }
bool TSMPTryGetVector2Array(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetVector2Array(0u, id, hash, v); }
bool TSMPTryGetVector3Array(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetVector3Array(0u, id, hash, v); }
bool TSMPTryGetQuaternionArray(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetQuaternionArray(0u, id, hash, v); }
bool TSMPTryGetUtf8Array(uint id, uint hash, out TSMPArrayView v) { return TSMPTryGetUtf8Array(0u, id, hash, v); }
bool TSMPTryGetFloat4(uint id, uint hash, out float4 v) { return TSMPTryGetFloat4(0u, id, hash, v); }
bool TSMPTryGetColor32(uint id, uint hash, out float4 v) { return TSMPTryGetColor32(0u, id, hash, v); }
bool TSMPTryGetMatrix4x4(uint id, uint hash, out float4x4 v) { return TSMPTryGetMatrix4x4(0u, id, hash, v); }

#endif
