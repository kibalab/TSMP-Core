using UnityEngine;
using VRC.Udon;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public static class EncoderUdonBindingRuntime
    {
        public static int GetWritableBindingCount(
            Component[] bindingTargets,
            UdonBehaviour[] bindingUdonTargets,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            byte[] bindingValueTypes,
            string[] bindingFieldNames)
        {
            return BindingTable.GetWritableBindingCount(bindingTargets, bindingUdonTargets, true, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
        }

        public static UdonBehaviour[] EnsureBindingTargetCache(
            Component[] bindingTargets,
            UdonBehaviour[] bindingUdonTargets,
            int count,
            UdonBehaviour[] cachedTargets,
            int cachedCount,
            out int nextCachedCount)
        {
            if (count < 0)
                count = 0;

            nextCachedCount = cachedCount;

            bool cacheValid = true;
            if (cachedCount != count)
                cacheValid = false;
            if (!BindingTable.IsUdonTargetCacheValid(cachedTargets, count))
                cacheValid = false;

            if (cacheValid)
                return cachedTargets;

            nextCachedCount = count;
            return BindingTable.BuildUdonTargetCache(bindingTargets, bindingUdonTargets, count);
        }

        public static UdonBehaviour[] EnsureBeforeEncodeTargetCache(UdonBehaviour[] targets, int count)
        {
            if (count < 0)
                count = 0;

            return BindingTable.EnsureUdonTargetArray(targets, count);
        }

        public static bool CanWriteBinding(string[] bindingFieldNames, int[] bindingDirections, UdonBehaviour target, int index)
        {
            return BindingTable.CanWriteBindingEntry(bindingDirections, bindingFieldNames, target, index);
        }

        public static int SendBeforeEncodeOnce(UdonBehaviour target, UdonBehaviour[] targets, int targetCount)
        {
            if (target == null)
                return targetCount;

            if (BindingTable.ContainsUdonTarget(targets, targetCount, target))
                return targetCount;

            TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.BeforeEncodeEventName);
            return BindingTable.AddUdonTarget(targets, targetCount, target);
        }
    }
}
