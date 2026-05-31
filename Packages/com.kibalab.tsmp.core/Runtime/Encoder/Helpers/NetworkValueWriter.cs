using UnityEngine;

namespace K13A.TSMP
{
    public static class NetworkValueWriter
    {
        public static int WriteBool(byte[] buffer, int offset, bool value)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.BoolBytes))
                return -1;

            buffer[offset] = value ? (byte)1 : (byte)0;
            return offset + NetworkFrameProtocol.BoolBytes;
        }

        public static int WriteInt32(byte[] buffer, int offset, int value)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.Int32Bytes))
                return -1;

            Binary.WriteInt32LE(buffer, offset, value);
            return offset + NetworkFrameProtocol.Int32Bytes;
        }

        public static int WriteFloat32(byte[] buffer, int offset, float value)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.Float32Bytes))
                return -1;

            return Binary.WriteFloat32LE(buffer, offset, value);
        }

        public static int WriteVector2(byte[] buffer, int offset, Vector2 value)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.Vector2Bytes))
                return -1;

            return Binary.WriteVector2Float32LE(buffer, offset, value);
        }

        public static int WriteVector3(byte[] buffer, int offset, Vector3 value)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.Vector3Bytes))
                return -1;

            return Binary.WriteVector3Float32LE(buffer, offset, value);
        }

        public static int WriteQuaternion(byte[] buffer, int offset, Quaternion value)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.QuaternionBytes))
                return -1;

            return Binary.WriteQuaternionFloat32LE(buffer, offset, value);
        }

        public static int WriteUtf8String(byte[] buffer, int offset, string value)
        {
            if (value == null)
                value = string.Empty;

            int byteCount = Utf8.GetByteCount(value, 2147483647);
            if (!CanWrite(buffer, offset, byteCount))
                return -1;

            return Utf8.WriteString(buffer, offset, value, byteCount);
        }

        public static int WriteRawBytes(byte[] buffer, int offset, byte[] value)
        {
            int length = value != null ? value.Length : 0;
            if (!CanWrite(buffer, offset, length))
                return -1;

            for (int i = 0; i < length; i++)
                buffer[offset + i] = value[i];

            return offset + length;
        }

        public static int WriteBoolArray(byte[] buffer, int offset, bool[] value)
        {
            int count = value != null ? value.Length : 0;
            if (!CanWrite(buffer, offset, count))
                return -1;

            for (int i = 0; i < count; i++)
                buffer[offset + i] = value[i] ? (byte)1 : (byte)0;

            return offset + count;
        }

        public static int WriteInt32Array(byte[] buffer, int offset, int[] value)
        {
            int count = value != null ? value.Length : 0;
            if (!CanWriteElements(buffer, offset, count, NetworkFrameProtocol.Int32Bytes))
                return -1;

            int cursor = offset;
            for (int i = 0; i < count; i++)
                cursor = WriteInt32(buffer, cursor, value[i]);

            return cursor;
        }

        public static int WriteFloat32Array(byte[] buffer, int offset, float[] value)
        {
            int count = value != null ? value.Length : 0;
            if (!CanWriteElements(buffer, offset, count, NetworkFrameProtocol.Float32Bytes))
                return -1;

            int cursor = offset;
            for (int i = 0; i < count; i++)
                cursor = WriteFloat32(buffer, cursor, value[i]);

            return cursor;
        }

        public static int WriteVector2Array(byte[] buffer, int offset, Vector2[] value)
        {
            int count = value != null ? value.Length : 0;
            if (!CanWriteElements(buffer, offset, count, NetworkFrameProtocol.Vector2Bytes))
                return -1;

            int cursor = offset;
            for (int i = 0; i < count; i++)
                cursor = WriteVector2(buffer, cursor, value[i]);

            return cursor;
        }

        public static int WriteVector3Array(byte[] buffer, int offset, Vector3[] value)
        {
            int count = value != null ? value.Length : 0;
            if (!CanWriteElements(buffer, offset, count, NetworkFrameProtocol.Vector3Bytes))
                return -1;

            int cursor = offset;
            for (int i = 0; i < count; i++)
                cursor = WriteVector3(buffer, cursor, value[i]);

            return cursor;
        }

        public static int WriteQuaternionArray(byte[] buffer, int offset, Quaternion[] value)
        {
            int count = value != null ? value.Length : 0;
            if (!CanWriteElements(buffer, offset, count, NetworkFrameProtocol.QuaternionBytes))
                return -1;

            int cursor = offset;
            for (int i = 0; i < count; i++)
                cursor = WriteQuaternion(buffer, cursor, value[i]);

            return cursor;
        }

        public static int WriteStringArray(byte[] buffer, int offset, string[] value)
        {
            int count = value != null ? value.Length : 0;
            if (count > NetworkFrameProtocol.UInt16MaxValue)
                return -1;

            int cursor = WriteUInt16(buffer, offset, count);
            if (cursor < 0)
                return -1;

            for (int i = 0; i < count; i++)
            {
                int lengthOffset = cursor;
                cursor = WriteUInt16(buffer, cursor, 0);
                if (cursor < 0)
                    return -1;

                int stringStart = cursor;
                cursor = WriteUtf8String(buffer, cursor, value[i]);
                if (cursor < 0)
                    return -1;

                int length = cursor - stringStart;
                if (length > NetworkFrameProtocol.UInt16MaxValue)
                    return -1;

                Binary.WriteUInt16LE(buffer, lengthOffset, (ushort)length);
            }

            return cursor;
        }

        public static int WriteObject(byte[] buffer, int offset, int valueType, object value)
        {
            if (valueType == NetworkFrameProtocol.ValueTypeBool)
            {
                bool typedValue = false;
                if (value != null)
                    typedValue = (bool)value;
                return WriteBool(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
            {
                int typedValue = 0;
                if (value != null)
                    typedValue = (int)value;
                return WriteInt32(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
            {
                float typedValue = 0f;
                if (value != null)
                    typedValue = (float)value;
                return WriteFloat32(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
            {
                Vector2 typedValue = Vector2.zero;
                if (value != null)
                    typedValue = (Vector2)value;
                return WriteVector2(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
            {
                Vector3 typedValue = Vector3.zero;
                if (value != null)
                    typedValue = (Vector3)value;
                return WriteVector3(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
            {
                Quaternion typedValue = Quaternion.identity;
                if (value != null)
                    typedValue = (Quaternion)value;
                return WriteQuaternion(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeUTF8String)
            {
                string typedValue = null;
                if (value != null)
                    typedValue = (string)value;
                return WriteUtf8String(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
            {
                byte[] typedValue = null;
                if (value != null)
                    typedValue = (byte[])value;
                return WriteRawBytes(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
            {
                bool[] typedValue = null;
                if (value != null)
                    typedValue = (bool[])value;
                return WriteBoolArray(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
            {
                int[] typedValue = null;
                if (value != null)
                    typedValue = (int[])value;
                return WriteInt32Array(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
            {
                float[] typedValue = null;
                if (value != null)
                    typedValue = (float[])value;
                return WriteFloat32Array(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
            {
                Vector2[] typedValue = null;
                if (value != null)
                    typedValue = (Vector2[])value;
                return WriteVector2Array(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
            {
                Vector3[] typedValue = null;
                if (value != null)
                    typedValue = (Vector3[])value;
                return WriteVector3Array(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
            {
                Quaternion[] typedValue = null;
                if (value != null)
                    typedValue = (Quaternion[])value;
                return WriteQuaternionArray(buffer, offset, typedValue);
            }

            if (valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray)
            {
                string[] typedValue = null;
                if (value != null)
                    typedValue = (string[])value;
                return WriteStringArray(buffer, offset, typedValue);
            }

            return -1;
        }

        private static int WriteUInt16(byte[] buffer, int offset, int value)
        {
            if (value < 0)
                return -1;
            if (value > NetworkFrameProtocol.UInt16MaxValue)
                return -1;
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.UInt16Bytes))
                return -1;

            Binary.WriteUInt16LE(buffer, offset, (ushort)value);
            return offset + NetworkFrameProtocol.UInt16Bytes;
        }

        private static bool CanWriteElements(byte[] buffer, int offset, int count, int stride)
        {
            if (count < 0)
                return false;
            if (stride < 0)
                return false;
            if (stride != 0)
            {
                if (count > 2147483647 / stride)
                    return false;
            }

            return CanWrite(buffer, offset, count * stride);
        }

        private static bool CanWrite(byte[] buffer, int offset, int byteCount)
        {
            if (buffer == null)
                return false;
            if (offset < 0)
                return false;
            if (byteCount < 0)
                return false;

            if (offset > buffer.Length)
                return false;

            return byteCount <= buffer.Length - offset;
        }
    }
}
