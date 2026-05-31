using UnityEngine;

namespace K13A.TSMP
{
    public static class DecoderValueRuntime
    {
        public static object DecodeProgramVariableValue(
            byte[] payloadBytes,
            int bindingIndex,
            int valueType,
            int valueOffset,
            int valueLength,
            byte[][] rawByteValueArrays,
            bool[] boolValueArray,
            int[] intValueArray,
            float[] floatValueArray,
            Vector2[] vector2ValueArray,
            Vector3[] vector3ValueArray,
            Quaternion[] quaternionValueArray,
            string[] stringValueArray,
            out bool[] nextBoolValueArray,
            out int[] nextIntValueArray,
            out float[] nextFloatValueArray,
            out Vector2[] nextVector2ValueArray,
            out Vector3[] nextVector3ValueArray,
            out Quaternion[] nextQuaternionValueArray,
            out string[] nextStringValueArray)
        {
            nextBoolValueArray = boolValueArray;
            nextIntValueArray = intValueArray;
            nextFloatValueArray = floatValueArray;
            nextVector2ValueArray = vector2ValueArray;
            nextVector3ValueArray = vector3ValueArray;
            nextQuaternionValueArray = quaternionValueArray;
            nextStringValueArray = stringValueArray;

            object decodedValue = NetworkValueReader.ReadScalarObject(payloadBytes, valueType, valueOffset, valueLength);
            if (decodedValue != null)
                return decodedValue;

            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
                return DecoderValueCache.CopyRawBytes(payloadBytes, valueOffset, valueLength, rawByteValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
            {
                nextBoolValueArray = DecoderValueCache.CopyBoolArray(payloadBytes, valueOffset, valueLength, boolValueArray);
                return nextBoolValueArray;
            }
            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
            {
                nextIntValueArray = DecoderValueCache.CopyInt32Array(payloadBytes, valueOffset, valueLength, intValueArray);
                return nextIntValueArray;
            }
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
            {
                nextFloatValueArray = DecoderValueCache.CopyFloat32Array(payloadBytes, valueOffset, valueLength, floatValueArray);
                return nextFloatValueArray;
            }
            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
            {
                nextVector2ValueArray = DecoderValueCache.CopyVector2Array(payloadBytes, valueOffset, valueLength, vector2ValueArray);
                return nextVector2ValueArray;
            }
            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
            {
                nextVector3ValueArray = DecoderValueCache.CopyVector3Array(payloadBytes, valueOffset, valueLength, vector3ValueArray);
                return nextVector3ValueArray;
            }
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
            {
                nextQuaternionValueArray = DecoderValueCache.CopyQuaternionArray(payloadBytes, valueOffset, valueLength, quaternionValueArray);
                return nextQuaternionValueArray;
            }
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray)
            {
                nextStringValueArray = DecoderValueCache.CopyStringArray(payloadBytes, valueOffset, valueLength, stringValueArray);
                return nextStringValueArray;
            }

            return null;
        }
    }
}
