using UnityEngine;

namespace K13A.TSMP
{
    public static class Binary
    {
        private const float QuaternionComponentLimit = 0.7071067811865476f;
        private const float QuaternionPackScale = 4095f / (QuaternionComponentLimit * 2f);
        private const float QuaternionUnpackScale = (QuaternionComponentLimit * 2f) / 4095f;

        public static void WriteUInt16LE(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        public static void WriteUInt32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static void WriteInt32LE(byte[] buffer, int offset, int value)
        {
            WriteUInt32LE(buffer, offset, (uint)value);
        }

        public static int WriteFloat32LE(byte[] buffer, int offset, float value)
        {
#if !COMPILER_UDONSHARP
            WriteUInt32LE(buffer, offset, unchecked((uint)System.BitConverter.SingleToInt32Bits(value)));
            return offset + NetworkFrameProtocol.Float32Bytes;
#else
            uint bits;
            if (value == 0f)
            {
                bits = 0u;
            }
            else
            {
                uint sign = value < 0f ? 0x80000000u : 0u;
                float abs = Mathf.Abs(value);
                int exponent = Mathf.FloorToInt(Mathf.Log(abs) * 1.4426950408f);

                if (exponent < -126)
                {
                    float scaled = abs / Mathf.Pow(2f, -126f) * 8388608f;
                    uint mantissa = (uint)Mathf.RoundToInt(scaled);
                    bits = sign | (mantissa & 0x7FFFFFu);
                }
                else
                {
                    int biased = exponent + 127;
                    if (biased >= 255)
                    {
                        bits = sign | 0x7F800000u;
                    }
                    else
                    {
                        float normalized = abs / Mathf.Pow(2f, exponent) - 1f;
                        uint mantissa = (uint)Mathf.RoundToInt(normalized * 8388608f);
                        if (mantissa >= 0x800000u)
                        {
                            mantissa = 0u;
                            biased++;
                            if (biased >= 255)
                                bits = sign | 0x7F800000u;
                            else
                                bits = sign | (((uint)biased & 0xFFu) << 23);
                        }
                        else
                        {
                            bits = sign | (((uint)biased & 0xFFu) << 23) | (mantissa & 0x7FFFFFu);
                        }
                    }
                }
            }

            WriteUInt32LE(buffer, offset, bits);
            return offset + NetworkFrameProtocol.Float32Bytes;
#endif
        }

        public static int WriteVector2Float32LE(byte[] buffer, int offset, Vector2 value)
        {
            offset = WriteFloat32LE(buffer, offset, value.x);
            return WriteFloat32LE(buffer, offset, value.y);
        }

        public static int WriteVector3Float32LE(byte[] buffer, int offset, Vector3 value)
        {
            offset = WriteFloat32LE(buffer, offset, value.x);
            offset = WriteFloat32LE(buffer, offset, value.y);
            return WriteFloat32LE(buffer, offset, value.z);
        }

        public static int WriteQuaternionFloat32LE(byte[] buffer, int offset, Quaternion value)
        {
            offset = WriteFloat32LE(buffer, offset, value.x);
            offset = WriteFloat32LE(buffer, offset, value.y);
            offset = WriteFloat32LE(buffer, offset, value.z);
            return WriteFloat32LE(buffer, offset, value.w);
        }

        public static void WritePackedQuaternion12LE(byte[] buffer, int offset, Quaternion rotation)
        {
            if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
                rotation = Quaternion.identity;

            float rx = rotation.x;
            float ry = rotation.y;
            float rz = rotation.z;
            float rw = rotation.w;

            float ax = rx < 0f ? -rx : rx;
            float ay = ry < 0f ? -ry : ry;
            float az = rz < 0f ? -rz : rz;
            float aw = rw < 0f ? -rw : rw;

            int largest = 0;
            float largestValue = ax;
            if (ay > largestValue)
            {
                largest = 1;
                largestValue = ay;
            }

            if (az > largestValue)
            {
                largest = 2;
                largestValue = az;
            }

            if (aw > largestValue)
                largest = 3;

            float largestComponent = rw;
            if (largest == 0)
                largestComponent = rx;
            else if (largest == 1)
                largestComponent = ry;
            else if (largest == 2)
                largestComponent = rz;

            if (largestComponent < 0f)
            {
                rx = -rx;
                ry = -ry;
                rz = -rz;
                rw = -rw;
            }

            float f0;
            float f1;
            float f2;
            if (largest == 0)
            {
                f0 = ry;
                f1 = rz;
                f2 = rw;
            }
            else if (largest == 1)
            {
                f0 = rx;
                f1 = rz;
                f2 = rw;
            }
            else if (largest == 2)
            {
                f0 = rx;
                f1 = ry;
                f2 = rw;
            }
            else
            {
                f0 = rx;
                f1 = ry;
                f2 = rz;
            }

            int c0 = (int)((f0 + QuaternionComponentLimit) * QuaternionPackScale + 0.5f);
            int c1 = (int)((f1 + QuaternionComponentLimit) * QuaternionPackScale + 0.5f);
            int c2 = (int)((f2 + QuaternionComponentLimit) * QuaternionPackScale + 0.5f);

            if (c0 < 0)
                c0 = 0;
            else if (c0 > 4095)
                c0 = 4095;

            if (c1 < 0)
                c1 = 0;
            else if (c1 > 4095)
                c1 = 4095;

            if (c2 < 0)
                c2 = 0;
            else if (c2 > 4095)
                c2 = 4095;

            buffer[offset] = (byte)((largest & 0x03) | ((c0 & 0x3F) << 2));
            buffer[offset + 1] = (byte)(((c0 >> 6) & 0x3F) | ((c1 & 0x03) << 6));
            buffer[offset + 2] = (byte)((c1 >> 2) & 0xFF);
            buffer[offset + 3] = (byte)(((c1 >> 10) & 0x03) | ((c2 & 0x3F) << 2));
            buffer[offset + 4] = (byte)((c2 >> 6) & 0x3F);
        }

        public static ushort ReadUInt16LE(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        public static uint ReadUInt32LE(byte[] buffer, int offset)
        {
            return (uint)buffer[offset]
                | ((uint)buffer[offset + 1] << 8)
                | ((uint)buffer[offset + 2] << 16)
                | ((uint)buffer[offset + 3] << 24);
        }

        public static int ReadInt32LE(byte[] buffer, int offset)
        {
            return (int)ReadUInt32LE(buffer, offset);
        }

        public static float ReadFloat32LE(byte[] buffer, int offset)
        {
            uint bits = ReadUInt32LE(buffer, offset);
            if (bits == 0u || bits == 0x80000000u)
                return 0f;

            float sign = (bits & 0x80000000u) == 0u ? 1f : -1f;
            int exponent = (int)((bits >> 23) & 0xFFu);
            uint mantissa = bits & 0x7FFFFFu;

            if (exponent == 255)
                return sign * float.MaxValue;

            if (exponent == 0)
                return sign * (mantissa / 8388608f) * Mathf.Pow(2f, -126f);

            return sign * (1f + mantissa / 8388608f) * Mathf.Pow(2f, exponent - 127);
        }

        public static Vector2 ReadVector2Float32LE(byte[] buffer, int offset)
        {
            return new Vector2(
                ReadFloat32LE(buffer, offset),
                ReadFloat32LE(buffer, offset + NetworkFrameProtocol.Float32Bytes));
        }

        public static Vector3 ReadVector3Float32LE(byte[] buffer, int offset)
        {
            return new Vector3(
                ReadFloat32LE(buffer, offset),
                ReadFloat32LE(buffer, offset + NetworkFrameProtocol.Float32Bytes),
                ReadFloat32LE(buffer, offset + NetworkFrameProtocol.Vector2Bytes));
        }

        public static Quaternion ReadQuaternionFloat32LE(byte[] buffer, int offset)
        {
            return new Quaternion(
                ReadFloat32LE(buffer, offset),
                ReadFloat32LE(buffer, offset + NetworkFrameProtocol.Float32Bytes),
                ReadFloat32LE(buffer, offset + NetworkFrameProtocol.Vector2Bytes),
                ReadFloat32LE(buffer, offset + NetworkFrameProtocol.Vector3Bytes));
        }

        public static Quaternion ReadPackedQuaternion12LE(byte[] buffer, int offset)
        {
            int largest = buffer[offset] & 0x03;
            int c0 = ((buffer[offset] >> 2) & 0x3F) | ((buffer[offset + 1] & 0x3F) << 6);
            int c1 = ((buffer[offset + 1] >> 6) & 0x03) | (buffer[offset + 2] << 2) | ((buffer[offset + 3] & 0x03) << 10);
            int c2 = ((buffer[offset + 3] >> 2) & 0x3F) | ((buffer[offset + 4] & 0x3F) << 6);

            float v0 = c0 * QuaternionUnpackScale - QuaternionComponentLimit;
            float v1 = c1 * QuaternionUnpackScale - QuaternionComponentLimit;
            float v2 = c2 * QuaternionUnpackScale - QuaternionComponentLimit;
            float missing = Mathf.Sqrt(Mathf.Max(0f, 1f - v0 * v0 - v1 * v1 - v2 * v2));

            if (largest == 0)
                return new Quaternion(missing, v0, v1, v2);
            if (largest == 1)
                return new Quaternion(v0, missing, v1, v2);
            if (largest == 2)
                return new Quaternion(v0, v1, missing, v2);

            return new Quaternion(v0, v1, v2, missing);
        }

    }

    public static class ArrayUtil
    {
        public static int[] CreateFilledIntArray(int length, int fill)
        {
            int[] result = new int[length];
            FillIntArray(result, fill);

            return result;
        }

        public static void FillIntArray(int[] values, int fill)
        {
            if (values == null)
                return;

            for (int i = 0; i < values.Length; i++)
                values[i] = fill;
        }

        public static int[] ResizeIntArray(int[] source, int length, int fill)
        {
            int[] result = CreateFilledIntArray(length, fill);
            if (source == null)
                return result;

            int count = source.Length < length ? source.Length : length;
            for (int i = 0; i < count; i++)
                result[i] = source[i];

            return result;
        }

        public static float[] ResizeFloatArray(float[] source, int length)
        {
            float[] result = new float[length];
            if (source == null)
                return result;

            int count = source.Length < length ? source.Length : length;
            for (int i = 0; i < count; i++)
                result[i] = source[i];

            return result;
        }
    }

    public static class Utf8
    {
        public static int GetByteCount(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes <= 0)
                return 0;

            int bytes = 0;
            for (int i = 0; i < value.Length; i++)
            {
                int code = value[i];
                int next;
                if (code < 0x80)
                {
                    next = 1;
                }
                else if (code < 0x800)
                {
                    next = 2;
                }
                else if (code >= 0xD800 && code <= 0xDBFF && i + 1 < value.Length)
                {
                    int lo = value[i + 1];
                    next = lo >= 0xDC00 && lo <= 0xDFFF ? 4 : NetworkFrameProtocol.Utf8ReplacementBytes;
                }
                else
                {
                    next = NetworkFrameProtocol.Utf8ReplacementBytes;
                }

                if (bytes + next > maxBytes)
                    break;

                bytes += next;
                if (next == 4)
                    i++;
            }

            return bytes;
        }

        public static int WriteString(byte[] buffer, int offset, string value, int byteCount)
        {
            if (string.IsNullOrEmpty(value) || byteCount <= 0)
                return offset + (byteCount > 0 ? byteCount : 0);

            int cursor = offset;
            int end = offset + byteCount;
            for (int i = 0; i < value.Length && cursor < end; i++)
            {
                int code = value[i];
                if (code < 0x80)
                {
                    if (cursor >= end)
                        break;
                    buffer[cursor++] = (byte)code;
                }
                else if (code < 0x800)
                {
                    if (cursor + 1 >= end)
                        break;
                    buffer[cursor++] = (byte)(0xC0 | (code >> 6));
                    buffer[cursor++] = (byte)(0x80 | (code & 0x3F));
                }
                else if (code >= 0xD800 && code <= 0xDBFF && i + 1 < value.Length)
                {
                    int lo = value[i + 1];
                    if (lo >= 0xDC00 && lo <= 0xDFFF)
                    {
                        if (cursor + 3 >= end)
                            break;
                        int codePoint = 0x10000 + ((code - 0xD800) << 10) + (lo - 0xDC00);
                        buffer[cursor++] = (byte)(0xF0 | (codePoint >> 18));
                        buffer[cursor++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                        buffer[cursor++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                        buffer[cursor++] = (byte)(0x80 | (codePoint & 0x3F));
                        i++;
                    }
                    else
                    {
                        cursor = WriteReplacement(buffer, cursor, end);
                    }
                }
                else
                {
                    cursor = WriteReplacement(buffer, cursor, end);
                }
            }

            return end;
        }

        public static string ReadString(byte[] buffer, int offset, int length)
        {
            string value = string.Empty;
            int cursor = offset;
            int end = offset + length;

            while (cursor < end)
            {
                int b0 = buffer[cursor++];
                if (b0 < 0x80)
                {
                    value += (char)b0;
                }
                else if (b0 >= 0xC2 && b0 <= 0xDF && cursor < end)
                {
                    int b1 = buffer[cursor++];
                    if (IsContinuation(b1))
                        value += (char)(((b0 & 0x1F) << 6) | (b1 & 0x3F));
                    else
                        value += "?";
                }
                else if (b0 >= 0xE0 && b0 <= 0xEF && cursor + 1 < end)
                {
                    int b1 = buffer[cursor++];
                    int b2 = buffer[cursor++];
                    bool valid = IsContinuation(b1);
                    if (valid)
                        valid = IsContinuation(b2);
                    if (valid && b0 == 0xE0 && b1 < 0xA0)
                        valid = false;
                    if (valid && b0 == 0xED && b1 >= 0xA0)
                        valid = false;

                    if (valid)
                        value += (char)(((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F));
                    else
                        value += "?";
                }
                else if (b0 >= 0xF0 && b0 <= 0xF4 && cursor + 2 < end)
                {
                    int b1 = buffer[cursor++];
                    int b2 = buffer[cursor++];
                    int b3 = buffer[cursor++];
                    bool valid = IsContinuation(b1);
                    if (valid)
                        valid = IsContinuation(b2);
                    if (valid)
                        valid = IsContinuation(b3);
                    if (valid && b0 == 0xF0 && b1 < 0x90)
                        valid = false;
                    if (valid && b0 == 0xF4 && b1 > 0x8F)
                        valid = false;

                    if (valid)
                    {
                        int codePoint = ((b0 & 0x07) << 18) | ((b1 & 0x3F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F);
                        codePoint -= 0x10000;
                        value += (char)(0xD800 + (codePoint >> 10));
                        value += (char)(0xDC00 + (codePoint & 0x3FF));
                    }
                    else
                    {
                        value += "?";
                    }
                }
                else
                {
                    value += "?";
                }
            }

            return value;
        }

        private static int WriteReplacement(byte[] buffer, int offset, int end)
        {
            if (offset + 2 >= end)
                return end;

            buffer[offset++] = 0xEF;
            buffer[offset++] = 0xBF;
            buffer[offset++] = 0xBD;
            return offset;
        }

        private static bool IsContinuation(int value)
        {
            return value >= 0x80 && value <= 0xBF;
        }
    }
}
