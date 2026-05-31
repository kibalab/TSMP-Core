using UnityEngine;

namespace K13A.TSMP
{
    public static class NetworkValueReader
    {
        private static bool IsValueLengthReadable(int valueType, int length)
        {
            if (!NetworkValueCodec.IsScalarValueType(valueType))
                return false;

            return NetworkValueCodec.IsValueLengthValid(valueType, length);
        }

        public static object ReadScalarObject(byte[] bytes, int valueType, int offset, int length)
        {
            if (!IsValueLengthReadable(valueType, length))
                return null;

            if (valueType == NetworkFrameProtocol.ValueTypeBool)
                return ReadBool(bytes, offset, length);
            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
                return ReadInt32(bytes, offset);
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
                return ReadFloat32(bytes, offset);
            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
                return ReadVector2(bytes, offset);
            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
                return ReadVector3(bytes, offset);
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
                return ReadQuaternion(bytes, offset);
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8String)
                return Utf8.ReadString(bytes, offset, length);

            return null;
        }

        private static bool ReadBool(byte[] bytes, int offset, int length)
        {
            if (bytes == null || length <= 0)
                return false;
            if (offset < 0 || offset >= bytes.Length)
                return false;

            return bytes[offset] != 0;
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            return Binary.ReadInt32LE(bytes, offset);
        }

        private static float ReadFloat32(byte[] bytes, int offset)
        {
            return Binary.ReadFloat32LE(bytes, offset);
        }

        private static Vector2 ReadVector2(byte[] bytes, int offset)
        {
            return Binary.ReadVector2Float32LE(bytes, offset);
        }

        private static Vector3 ReadVector3(byte[] bytes, int offset)
        {
            return Binary.ReadVector3Float32LE(bytes, offset);
        }

        private static Quaternion ReadQuaternion(byte[] bytes, int offset)
        {
            return Binary.ReadQuaternionFloat32LE(bytes, offset);
        }

        public static byte[] CopyRawBytes(byte[] source, int offset, int length, byte[] value)
        {
            if (value == null)
                value = new byte[length];
            else if (value.Length != length)
                value = new byte[length];

            for (int i = 0; i < length; i++)
                value[i] = source[offset + i];

            return value;
        }

        public static bool[] CopyBoolArray(byte[] source, int offset, int length, bool[] value)
        {
            if (value == null)
                value = new bool[length];
            else if (value.Length != length)
                value = new bool[length];

            for (int i = 0; i < length; i++)
                value[i] = source[offset + i] != 0;

            return value;
        }

        public static int[] CopyInt32Array(byte[] source, int offset, int length, int[] value)
        {
            int count = length / NetworkFrameProtocol.Int32Bytes;
            if (value == null)
                value = new int[count];
            else if (value.Length != count)
                value = new int[count];

            for (int i = 0; i < count; i++)
                value[i] = (int)Binary.ReadUInt32LE(source, offset + i * NetworkFrameProtocol.Int32Bytes);

            return value;
        }

        public static float[] CopyFloat32Array(byte[] source, int offset, int length, float[] value)
        {
            int count = length / NetworkFrameProtocol.Float32Bytes;
            if (value == null)
                value = new float[count];
            else if (value.Length != count)
                value = new float[count];

            for (int i = 0; i < count; i++)
                value[i] = ReadFloat32(source, offset + i * NetworkFrameProtocol.Float32Bytes);

            return value;
        }

        public static Vector2[] CopyVector2Array(byte[] source, int offset, int length, Vector2[] value)
        {
            int count = length / NetworkFrameProtocol.Vector2Bytes;
            if (value == null)
                value = new Vector2[count];
            else if (value.Length != count)
                value = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                int elementOffset = offset + i * NetworkFrameProtocol.Vector2Bytes;
                value[i] = ReadVector2(source, elementOffset);
            }

            return value;
        }

        public static Vector3[] CopyVector3Array(byte[] source, int offset, int length, Vector3[] value)
        {
            int count = length / NetworkFrameProtocol.Vector3Bytes;
            if (value == null)
                value = new Vector3[count];
            else if (value.Length != count)
                value = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                int elementOffset = offset + i * NetworkFrameProtocol.Vector3Bytes;
                value[i] = ReadVector3(source, elementOffset);
            }

            return value;
        }

        public static Quaternion[] CopyQuaternionArray(byte[] source, int offset, int length, Quaternion[] value)
        {
            int count = length / NetworkFrameProtocol.QuaternionBytes;
            if (value == null)
                value = new Quaternion[count];
            else if (value.Length != count)
                value = new Quaternion[count];

            for (int i = 0; i < count; i++)
            {
                int elementOffset = offset + i * NetworkFrameProtocol.QuaternionBytes;
                value[i] = ReadQuaternion(source, elementOffset);
            }

            return value;
        }

        public static string[] CopyStringArray(byte[] source, int offset, int length, string[] value)
        {
            int count;
            if (!TryGetStringArrayCount(source, offset, length, out count))
                return EnsureStringArray(value, 0);

            int cursor = offset;
            int end = offset + length;
            cursor += NetworkFrameProtocol.UInt16Bytes;

            value = EnsureStringArray(value, count);

            for (int i = 0; i < count; i++)
            {
                int stringLength = Binary.ReadUInt16LE(source, cursor);
                cursor += NetworkFrameProtocol.UInt16Bytes;

                value[i] = Utf8.ReadString(source, cursor, stringLength);
                cursor += stringLength;
            }

            return value;
        }

        private static bool TryGetStringArrayCount(byte[] source, int offset, int length, out int count)
        {
            count = 0;
            if (!CanRead(source, offset, length))
                return false;
            if (length < NetworkFrameProtocol.UInt16Bytes)
                return false;

            int cursor = offset;
            int end = offset + length;
            int encodedCount = Binary.ReadUInt16LE(source, cursor);
            cursor += NetworkFrameProtocol.UInt16Bytes;

            int maximumCount = (length - NetworkFrameProtocol.UInt16Bytes) / NetworkFrameProtocol.UInt16Bytes;
            if (encodedCount > maximumCount)
                return false;

            for (int i = 0; i < encodedCount; i++)
            {
                if (cursor + NetworkFrameProtocol.UInt16Bytes > end)
                    return false;

                int stringLength = Binary.ReadUInt16LE(source, cursor);
                cursor += NetworkFrameProtocol.UInt16Bytes;

                if (stringLength > end - cursor)
                    return false;

                cursor += stringLength;
            }

            count = encodedCount;
            return true;
        }

        private static string[] EnsureStringArray(string[] value, int count)
        {
            if (value == null)
                return new string[count];
            if (value.Length != count)
                return new string[count];

            return value;
        }

        private static bool CanRead(byte[] source, int offset, int length)
        {
            if (source == null)
                return false;
            if (offset < 0)
                return false;
            if (length < 0)
                return false;
            if (offset > source.Length)
                return false;

            return length <= source.Length - offset;
        }
    }

    public static class DecoderValueCache
    {
        public static byte[] CopyRawBytes(byte[] source, int offset, int length, byte[][] cache, int bindingIndex)
        {
            byte[] value = null;
            if (cache != null && bindingIndex >= 0 && bindingIndex < cache.Length)
                value = cache[bindingIndex];

            value = NetworkValueReader.CopyRawBytes(source, offset, length, value);
            if (cache != null && bindingIndex >= 0 && bindingIndex < cache.Length)
                cache[bindingIndex] = value;

            return value;
        }

        public static bool[] CopyBoolArray(byte[] source, int offset, int length, bool[] cache)
        {
            return NetworkValueReader.CopyBoolArray(source, offset, length, cache);
        }

        public static int[] CopyInt32Array(byte[] source, int offset, int length, int[] cache)
        {
            return NetworkValueReader.CopyInt32Array(source, offset, length, cache);
        }

        public static float[] CopyFloat32Array(byte[] source, int offset, int length, float[] cache)
        {
            return NetworkValueReader.CopyFloat32Array(source, offset, length, cache);
        }

        public static Vector2[] CopyVector2Array(byte[] source, int offset, int length, Vector2[] cache)
        {
            return NetworkValueReader.CopyVector2Array(source, offset, length, cache);
        }

        public static Vector3[] CopyVector3Array(byte[] source, int offset, int length, Vector3[] cache)
        {
            return NetworkValueReader.CopyVector3Array(source, offset, length, cache);
        }

        public static Quaternion[] CopyQuaternionArray(byte[] source, int offset, int length, Quaternion[] cache)
        {
            return NetworkValueReader.CopyQuaternionArray(source, offset, length, cache);
        }

        public static string[] CopyStringArray(byte[] source, int offset, int length, string[] cache)
        {
            return NetworkValueReader.CopyStringArray(source, offset, length, cache);
        }
    }
}
