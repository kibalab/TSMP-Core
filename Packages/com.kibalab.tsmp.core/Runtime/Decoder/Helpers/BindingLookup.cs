namespace K13A.TSMP
{
    public static class BindingLookup
    {
        public static int ComputeSignature(int targetCount, ushort[] networkIds, uint[] variableHashes)
        {
            return BindingTable.ClampNetworkHashCount(targetCount, networkIds, variableHashes);
        }

        public static bool IsCacheValid(
            ushort[] lookupNetworkIds,
            uint[] lookupVariableHashes,
            int[] lookupBindingIndices,
            int lookupCount,
            int lookupSignature,
            int expectedCount,
            int expectedSignature)
        {
            if (lookupNetworkIds == null)
                return false;
            if (lookupVariableHashes == null)
                return false;
            if (lookupBindingIndices == null)
                return false;
            if (lookupNetworkIds.Length != expectedCount)
                return false;
            if (lookupVariableHashes.Length != expectedCount)
                return false;
            if (lookupBindingIndices.Length != expectedCount)
                return false;
            if (lookupCount != expectedCount)
                return false;
            if (lookupSignature != expectedSignature)
                return false;

            return true;
        }

        public static void Build(
            ushort[] lookupNetworkIds,
            uint[] lookupVariableHashes,
            int[] lookupBindingIndices,
            int count,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes)
        {
            if (lookupNetworkIds == null)
                return;
            if (lookupVariableHashes == null)
                return;
            if (lookupBindingIndices == null)
                return;
            if (bindingNetworkIds == null)
                return;
            if (bindingVariableHashes == null)
                return;

            int capacity = lookupNetworkIds.Length;
            if (lookupVariableHashes.Length < capacity)
                capacity = lookupVariableHashes.Length;
            if (lookupBindingIndices.Length < capacity)
                capacity = lookupBindingIndices.Length;
            if (bindingNetworkIds.Length < capacity)
                capacity = bindingNetworkIds.Length;
            if (bindingVariableHashes.Length < capacity)
                capacity = bindingVariableHashes.Length;
            if (count > capacity)
                count = capacity;
            if (count < 0)
                count = 0;

            for (int i = 0; i < count; i++)
            {
                lookupNetworkIds[i] = bindingNetworkIds[i];
                lookupVariableHashes[i] = bindingVariableHashes[i];
                lookupBindingIndices[i] = i;
            }

            Sort(lookupNetworkIds, lookupVariableHashes, lookupBindingIndices, count);
        }

        public static int FindStart(ushort[] lookupNetworkIds, uint[] lookupVariableHashes, int lookupCount, ushort networkId, uint variableHash)
        {
            if (lookupNetworkIds == null)
                return -1;
            if (lookupVariableHashes == null)
                return -1;
            if (lookupCount <= 0)
                return -1;

            int capacity = lookupNetworkIds.Length;
            if (lookupVariableHashes.Length < capacity)
                capacity = lookupVariableHashes.Length;
            if (lookupCount > capacity)
                lookupCount = capacity;

            int low = 0;
            int high = lookupCount;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (KeyLess(lookupNetworkIds[mid], lookupVariableHashes[mid], networkId, variableHash))
                    low = mid + 1;
                else
                    high = mid;
            }

            if (low >= lookupCount)
                return -1;

            if (lookupNetworkIds[low] == networkId)
            {
                if (lookupVariableHashes[low] == variableHash)
                    return low;
            }

            return -1;
        }

        public static bool LookupMatches(ushort[] lookupNetworkIds, uint[] lookupVariableHashes, int lookupCount, int lookup, ushort networkId, uint variableHash)
        {
            if (lookup < 0)
                return false;
            if (lookup >= lookupCount)
                return false;
            if (lookupNetworkIds == null)
                return false;
            if (lookupVariableHashes == null)
                return false;
            if (lookup >= lookupNetworkIds.Length)
                return false;
            if (lookup >= lookupVariableHashes.Length)
                return false;

            if (lookupNetworkIds[lookup] != networkId)
                return false;
            if (lookupVariableHashes[lookup] != variableHash)
                return false;

            return true;
        }

        public static bool IsBindingIndexInRange(int index, int bindingCount)
        {
            if (index < 0)
                return false;

            return index < bindingCount;
        }

        public static bool BindingEntryMatches(ushort[] networkIds, uint[] variableHashes, int index, ushort networkId, uint variableHash)
        {
            if (networkIds == null)
                return false;
            if (variableHashes == null)
                return false;
            if (index < 0)
                return false;
            if (index >= networkIds.Length)
                return false;
            if (index >= variableHashes.Length)
                return false;

            if (networkIds[index] != networkId)
                return false;
            if (variableHashes[index] != variableHash)
                return false;

            return true;
        }

        public static bool TryGetMatchingBindingIndex(
            ushort[] lookupNetworkIds,
            uint[] lookupVariableHashes,
            int[] lookupBindingIndices,
            int lookupCount,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            int bindingCount,
            int lookup,
            ushort networkId,
            uint variableHash,
            out int bindingIndex)
        {
            bindingIndex = -1;
            if (!LookupMatches(lookupNetworkIds, lookupVariableHashes, lookupCount, lookup, networkId, variableHash))
                return false;
            if (lookupBindingIndices == null)
                return false;
            if (lookup < 0 || lookup >= lookupBindingIndices.Length)
                return false;

            int index = lookupBindingIndices[lookup];
            if (!IsBindingIndexInRange(index, bindingCount))
                return false;
            if (!BindingEntryMatches(bindingNetworkIds, bindingVariableHashes, index, networkId, variableHash))
                return false;

            bindingIndex = index;
            return true;
        }

        public static bool TryResolveMatchingBinding(
            ushort[] lookupNetworkIds,
            uint[] lookupVariableHashes,
            int[] lookupBindingIndices,
            int lookupCount,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            string[] bindingFieldNames,
            int bindingCount,
            int lookup,
            ushort networkId,
            uint variableHash,
            out int bindingIndex,
            out string fieldName)
        {
            fieldName = null;
            if (!TryGetMatchingBindingIndex(
                    lookupNetworkIds,
                    lookupVariableHashes,
                    lookupBindingIndices,
                    lookupCount,
                    bindingNetworkIds,
                    bindingVariableHashes,
                    bindingCount,
                    lookup,
                    networkId,
                    variableHash,
                    out bindingIndex))
                return false;

            fieldName = BindingTable.GetFieldName(bindingFieldNames, bindingIndex);
            return !string.IsNullOrEmpty(fieldName);
        }

        private static void Sort(ushort[] networkIds, uint[] variableHashes, int[] bindingIndices, int count)
        {
            for (int i = 1; i < count; i++)
            {
                ushort networkId = networkIds[i];
                uint variableHash = variableHashes[i];
                int bindingIndex = bindingIndices[i];
                int j = i - 1;
                while (j >= 0)
                {
                    if (!KeyGreater(networkIds[j], variableHashes[j], networkId, variableHash))
                        break;

                    networkIds[j + 1] = networkIds[j];
                    variableHashes[j + 1] = variableHashes[j];
                    bindingIndices[j + 1] = bindingIndices[j];
                    j--;
                }

                networkIds[j + 1] = networkId;
                variableHashes[j + 1] = variableHash;
                bindingIndices[j + 1] = bindingIndex;
            }
        }

        private static bool KeyLess(ushort leftNetworkId, uint leftVariableHash, ushort rightNetworkId, uint rightVariableHash)
        {
            if (leftNetworkId < rightNetworkId)
                return true;
            if (leftNetworkId > rightNetworkId)
                return false;

            return leftVariableHash < rightVariableHash;
        }

        private static bool KeyGreater(ushort leftNetworkId, uint leftVariableHash, ushort rightNetworkId, uint rightVariableHash)
        {
            if (leftNetworkId > rightNetworkId)
                return true;
            if (leftNetworkId < rightNetworkId)
                return false;

            return leftVariableHash > rightVariableHash;
        }
    }
}
