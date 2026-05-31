using UnityEngine;

#if UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class DecoderVariableRuntime
    {
        public static int ApplyVariableValue(
            byte[] payloadBytes,
            ushort networkId,
            uint variableHash,
            int valueType,
            int valueOffset,
            int valueLength,
            int bindingCount,
#if UDONSHARP
            UdonBehaviour[] targets,
#else
            Component[] targets,
#endif
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            byte[] bindingValueTypes,
            string[] bindingFieldNames,
            ushort[] bindingLookupNetworkIds,
            uint[] bindingLookupVariableHashes,
            int[] bindingLookupBindingIndices,
            int bindingLookupCount,
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
            out string[] nextStringValueArray,
            out int rejectedValueTypeCount)
        {
            nextBoolValueArray = boolValueArray;
            nextIntValueArray = intValueArray;
            nextFloatValueArray = floatValueArray;
            nextVector2ValueArray = vector2ValueArray;
            nextVector3ValueArray = vector3ValueArray;
            nextQuaternionValueArray = quaternionValueArray;
            nextStringValueArray = stringValueArray;
            rejectedValueTypeCount = 0;

            int lookupStart = BindingLookup.FindStart(bindingLookupNetworkIds, bindingLookupVariableHashes, bindingLookupCount, networkId, variableHash);
            if (lookupStart < 0)
                return 0;

            int appliedCount = 0;
            for (int lookup = lookupStart; lookup < bindingLookupCount; lookup++)
            {
                if (!BindingLookup.LookupMatches(bindingLookupNetworkIds, bindingLookupVariableHashes, bindingLookupCount, lookup, networkId, variableHash))
                    break;

                int bindingIndex;
                string fieldName;
                if (!BindingLookup.TryResolveMatchingBinding(
                        bindingLookupNetworkIds,
                        bindingLookupVariableHashes,
                        bindingLookupBindingIndices,
                        bindingLookupCount,
                        bindingNetworkIds,
                        bindingVariableHashes,
                        bindingFieldNames,
                        bindingCount,
                        lookup,
                        networkId,
                        variableHash,
                        out bindingIndex,
                        out fieldName))
                    continue;

                if (!BindingTable.IsExpectedValueType(bindingValueTypes, bindingIndex, valueType))
                {
                    rejectedValueTypeCount++;
                    continue;
                }

                if (!DecoderVariableDispatcher.IsTargetActive(targets, bindingIndex))
                    continue;

                object decodedValue = DecoderValueRuntime.DecodeProgramVariableValue(
                    payloadBytes,
                    bindingIndex,
                    valueType,
                    valueOffset,
                    valueLength,
                    rawByteValueArrays,
                    nextBoolValueArray,
                    nextIntValueArray,
                    nextFloatValueArray,
                    nextVector2ValueArray,
                    nextVector3ValueArray,
                    nextQuaternionValueArray,
                    nextStringValueArray,
                    out nextBoolValueArray,
                    out nextIntValueArray,
                    out nextFloatValueArray,
                    out nextVector2ValueArray,
                    out nextVector3ValueArray,
                    out nextQuaternionValueArray,
                    out nextStringValueArray);

                if (DecoderVariableDispatcher.Apply(targets, bindingIndex, fieldName, variableHash, decodedValue))
                    appliedCount++;
            }

            return appliedCount;
        }
    }
}
