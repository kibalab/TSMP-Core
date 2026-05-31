using System;
using UnityEngine;

namespace K13A.TSMP
{
    public static class NetworkValueCodec
    {
        public static bool IsSupportedValueType(int valueType)
        {
            if (valueType == NetworkFrameProtocol.ValueTypeBool)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8String)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
                return true;

            return valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray;
        }

        public static bool IsScalarValueType(int valueType)
        {
            if (valueType == NetworkFrameProtocol.ValueTypeBool)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
                return true;

            return valueType == NetworkFrameProtocol.ValueTypeUTF8String;
        }

        public static bool IsValueLengthValid(int valueType, int length)
        {
            if (length < 0)
                return false;

            if (valueType == NetworkFrameProtocol.ValueTypeBool)
                return length == NetworkFrameProtocol.BoolBytes;
            if (valueType == NetworkFrameProtocol.ValueTypeInt32)
                return length == NetworkFrameProtocol.Int32Bytes;
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32)
                return length == NetworkFrameProtocol.Float32Bytes;
            if (valueType == NetworkFrameProtocol.ValueTypeVector2)
                return length == NetworkFrameProtocol.Vector2Bytes;
            if (valueType == NetworkFrameProtocol.ValueTypeVector3)
                return length == NetworkFrameProtocol.Vector3Bytes;
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternion)
                return length == NetworkFrameProtocol.QuaternionBytes;
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8String)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
                return true;
            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
                return length % NetworkFrameProtocol.Int32Bytes == 0;
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
                return length % NetworkFrameProtocol.Float32Bytes == 0;
            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
                return length % NetworkFrameProtocol.Vector2Bytes == 0;
            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
                return length % NetworkFrameProtocol.Vector3Bytes == 0;
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
                return length % NetworkFrameProtocol.QuaternionBytes == 0;
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray)
                return length >= NetworkFrameProtocol.UInt16Bytes;

            return false;
        }

        public static bool TryGetValueType(Type type, out NetworkValueType valueType)
        {
            if (type == typeof(bool))
            {
                valueType = NetworkValueType.Bool;
                return true;
            }
            if (type == typeof(int))
            {
                valueType = NetworkValueType.Int32;
                return true;
            }
            if (type == typeof(float))
            {
                valueType = NetworkValueType.Float32;
                return true;
            }
            if (type == typeof(Vector2))
            {
                valueType = NetworkValueType.Vector2;
                return true;
            }
            if (type == typeof(Vector3))
            {
                valueType = NetworkValueType.Vector3;
                return true;
            }
            if (type == typeof(Quaternion))
            {
                valueType = NetworkValueType.Quaternion;
                return true;
            }
            if (type == typeof(string))
            {
                valueType = NetworkValueType.UTF8String;
                return true;
            }
            if (type == typeof(byte[]))
            {
                valueType = NetworkValueType.RawBytes;
                return true;
            }
            if (type == typeof(bool[]))
            {
                valueType = NetworkValueType.BoolArray;
                return true;
            }
            if (type == typeof(int[]))
            {
                valueType = NetworkValueType.Int32Array;
                return true;
            }
            if (type == typeof(float[]))
            {
                valueType = NetworkValueType.Float32Array;
                return true;
            }
            if (type == typeof(Vector2[]))
            {
                valueType = NetworkValueType.Vector2Array;
                return true;
            }
            if (type == typeof(Vector3[]))
            {
                valueType = NetworkValueType.Vector3Array;
                return true;
            }
            if (type == typeof(Quaternion[]))
            {
                valueType = NetworkValueType.QuaternionArray;
                return true;
            }
            if (type == typeof(string[]))
            {
                valueType = NetworkValueType.UTF8StringArray;
                return true;
            }

            valueType = default;
            return false;
        }

    }
}
