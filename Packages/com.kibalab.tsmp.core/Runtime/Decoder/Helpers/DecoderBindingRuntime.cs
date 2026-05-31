namespace K13A.TSMP
{
    public static class DecoderBindingRuntime
    {
        public static int ComputeBindingLookupSignature(int targetCount, ushort[] bindingNetworkIds, uint[] bindingVariableHashes)
        {
            return BindingLookup.ComputeSignature(targetCount, bindingNetworkIds, bindingVariableHashes);
        }

        public static bool IsRawByteValueCacheValid(byte[][] rawByteValueArrays, int targetCount)
        {
            if (rawByteValueArrays == null)
                return false;

            return rawByteValueArrays.Length == targetCount;
        }

        public static byte[][] EnsureRawByteValueCache(byte[][] rawByteValueArrays, int targetCount)
        {
            if (targetCount < 0)
                targetCount = 0;
            if (rawByteValueArrays == null)
                return new byte[targetCount][];
            if (rawByteValueArrays.Length != targetCount)
                return new byte[targetCount][];

            return rawByteValueArrays;
        }

        public static void EnsureBindingLookup(
            int targetCount,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            ushort[] bindingLookupNetworkIds,
            uint[] bindingLookupVariableHashes,
            int[] bindingLookupBindingIndices,
            int bindingLookupCount,
            int bindingLookupSignature,
            out ushort[] nextBindingLookupNetworkIds,
            out uint[] nextBindingLookupVariableHashes,
            out int[] nextBindingLookupBindingIndices,
            out int nextBindingLookupCount,
            out int nextBindingLookupSignature)
        {
            int signature = ComputeBindingLookupSignature(targetCount, bindingNetworkIds, bindingVariableHashes);
            int count = BindingTable.ClampNetworkHashCount(targetCount, bindingNetworkIds, bindingVariableHashes);

            nextBindingLookupNetworkIds = bindingLookupNetworkIds;
            nextBindingLookupVariableHashes = bindingLookupVariableHashes;
            nextBindingLookupBindingIndices = bindingLookupBindingIndices;
            nextBindingLookupCount = bindingLookupCount;
            nextBindingLookupSignature = bindingLookupSignature;

            if (BindingLookup.IsCacheValid(bindingLookupNetworkIds, bindingLookupVariableHashes, bindingLookupBindingIndices, bindingLookupCount, bindingLookupSignature, count, signature))
                return;

            nextBindingLookupNetworkIds = new ushort[count];
            nextBindingLookupVariableHashes = new uint[count];
            nextBindingLookupBindingIndices = new int[count];
            nextBindingLookupCount = count;
            nextBindingLookupSignature = signature;

            BindingLookup.Build(nextBindingLookupNetworkIds, nextBindingLookupVariableHashes, nextBindingLookupBindingIndices, count, bindingNetworkIds, bindingVariableHashes);
        }
    }
}
