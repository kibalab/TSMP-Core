using UnityEngine;
using VRC.Udon;

namespace K13A.TSMP
{
    public static class BindingTable
    {
        public static int GetTargetCount(Component[] componentTargets, UdonBehaviour[] udonTargets, bool preferUdonTargets)
        {
            if (preferUdonTargets && udonTargets != null)
            {
                if (udonTargets.Length > 0)
                    return udonTargets.Length;
            }

            if (componentTargets == null)
                return 0;

            return componentTargets.Length;
        }

        public static int GetReadableBindingCount(Component[] componentTargets, UdonBehaviour[] udonTargets, bool preferUdonTargets, ushort[] networkIds, uint[] variableHashes, string[] fieldNames)
        {
            int count = GetTargetCount(componentTargets, udonTargets, preferUdonTargets);
            if (count <= 0)
                return 0;

            if (!HasReadableMetadata(networkIds, variableHashes, fieldNames))
                return 0;

            return ClampReadableCount(count, networkIds, variableHashes, fieldNames);
        }

        public static int GetWritableBindingCount(Component[] componentTargets, UdonBehaviour[] udonTargets, bool preferUdonTargets, ushort[] networkIds, uint[] variableHashes, byte[] valueTypes, string[] fieldNames)
        {
            int count = GetTargetCount(componentTargets, udonTargets, preferUdonTargets);
            if (count <= 0)
                return 0;

            if (!HasWritableMetadata(networkIds, variableHashes, valueTypes, fieldNames))
                return 0;

            return ClampWritableCount(count, networkIds, variableHashes, valueTypes, fieldNames);
        }

        public static bool HasReadableMetadata(ushort[] networkIds, uint[] variableHashes, string[] fieldNames)
        {
            if (networkIds == null)
                return false;
            if (variableHashes == null)
                return false;
            if (fieldNames == null)
                return false;

            return true;
        }

        public static bool HasWritableMetadata(ushort[] networkIds, uint[] variableHashes, byte[] valueTypes, string[] fieldNames)
        {
            if (!HasReadableMetadata(networkIds, variableHashes, fieldNames))
                return false;
            if (valueTypes == null)
                return false;

            return true;
        }

        public static int ClampReadableCount(int count, ushort[] networkIds, uint[] variableHashes, string[] fieldNames)
        {
            count = ClampNetworkHashCount(count, networkIds, variableHashes);

            int fieldNameCount = fieldNames != null ? fieldNames.Length : 0;
            if (fieldNameCount < count)
                count = fieldNameCount;

            return count;
        }

        public static int ClampWritableCount(int count, ushort[] networkIds, uint[] variableHashes, byte[] valueTypes, string[] fieldNames)
        {
            count = ClampReadableCount(count, networkIds, variableHashes, fieldNames);

            int valueTypeCount = valueTypes != null ? valueTypes.Length : 0;
            if (valueTypeCount < count)
                count = valueTypeCount;

            return count;
        }

        public static int GetValueType(byte[] valueTypes, int index)
        {
            if (valueTypes == null)
                return NetworkFrameProtocol.ValueTypeUnsupported;
            if (index < 0)
                return NetworkFrameProtocol.ValueTypeUnsupported;
            if (index >= valueTypes.Length)
                return NetworkFrameProtocol.ValueTypeUnsupported;

            return valueTypes[index];
        }

        public static bool IsExpectedValueType(byte[] valueTypes, int index, int valueType)
        {
            int expectedValueType = GetValueType(valueTypes, index);
            if (expectedValueType == NetworkFrameProtocol.ValueTypeUnsupported)
                return false;

            return expectedValueType == valueType;
        }

        public static int ClampNetworkHashCount(int count, ushort[] networkIds, uint[] variableHashes)
        {
            int networkIdCount = networkIds != null ? networkIds.Length : 0;
            if (networkIdCount < count)
                count = networkIdCount;

            int variableHashCount = variableHashes != null ? variableHashes.Length : 0;
            if (variableHashCount < count)
                count = variableHashCount;

            return count;
        }

        public static string GetFieldName(string[] fieldNames, int index)
        {
            if (fieldNames == null)
                return string.Empty;
            if (index < 0)
                return string.Empty;
            if (index >= fieldNames.Length)
                return string.Empty;

            return fieldNames[index];
        }

        public static ushort ResolveNetworkId(Udon.TSMPNetworkBehaviour behaviour)
        {
            if (behaviour == null)
                return 0;
            if (behaviour.networkId != 0)
                return behaviour.networkId;

            TSMPNetworkIdentity identity = behaviour.GetComponent<TSMPNetworkIdentity>();
            if (identity == null)
                identity = behaviour.GetComponentInParent<TSMPNetworkIdentity>();
            if (identity == null)
                return 0;

            return identity.networkId;
        }

        public static bool IsDirection(int[] directions, int index, int direction)
        {
            if (directions == null)
                return false;
            if (index < 0)
                return false;
            if (index >= directions.Length)
                return false;

            return directions[index] == direction;
        }

        public static bool CanWriteBindingEntry(int[] directions, string[] fieldNames, UdonBehaviour target, int index)
        {
            if (IsDirection(directions, index, (int)NetworkSyncDirection.ReceiveOnly))
                return false;
            if (string.IsNullOrEmpty(GetFieldName(fieldNames, index)))
                return false;
            if (target == null)
                return false;

            return IsUdonTargetActive(target);
        }

        public static UdonBehaviour[] EnsureUdonTargetArray(UdonBehaviour[] values, int count)
        {
            if (count < 0)
                count = 0;
            if (values == null)
                return new UdonBehaviour[count];
            if (values.Length != count)
                return new UdonBehaviour[count];

            return values;
        }

        public static bool ContainsUdonTarget(UdonBehaviour[] values, int count, UdonBehaviour target)
        {
            if (values == null || target == null)
                return false;
            if (count > values.Length)
                count = values.Length;

            for (int i = 0; i < count; i++)
            {
                if (values[i] == target)
                    return true;
            }

            return false;
        }

        public static int AddUdonTarget(UdonBehaviour[] values, int count, UdonBehaviour target)
        {
            if (values == null || target == null)
                return count;
            if (count < 0)
                count = 0;
            if (count >= values.Length)
                return count;

            values[count] = target;
            return count + 1;
        }

        public static UdonBehaviour ResolveUdonTarget(Component[] componentTargets, UdonBehaviour[] udonTargets, int index)
        {
            UdonBehaviour cachedTarget = ResolveCachedUdonTarget(udonTargets, index);
            if (cachedTarget != null)
                return cachedTarget;

            if (componentTargets == null)
                return null;
            if (index < 0)
                return null;
            if (index >= componentTargets.Length)
                return null;

            return ResolveUdonTarget(componentTargets[index], udonTargets, index);
        }

        public static UdonBehaviour ResolveUdonTarget(Component component, UdonBehaviour[] udonTargets, int index)
        {
            UdonBehaviour cachedTarget = ResolveCachedUdonTarget(udonTargets, index);
            if (cachedTarget != null)
                return cachedTarget;

            if (component == null)
                return null;

            GameObject targetObject = component.gameObject;
            if (targetObject == null)
                return null;

            return targetObject.GetComponent<UdonBehaviour>();
        }

        public static UdonBehaviour ResolveCachedUdonTarget(UdonBehaviour[] udonTargets, int index)
        {
            if (udonTargets == null)
                return null;
            if (index < 0)
                return null;
            if (index >= udonTargets.Length)
                return null;

            return udonTargets[index];
        }

        public static bool IsUdonTargetCacheValid(UdonBehaviour[] targets, int count)
        {
            if (targets == null)
                return false;

            return targets.Length == count;
        }

        public static bool IsComponentTargetCacheValid(Component[] targets, int count)
        {
            if (targets == null)
                return false;

            return targets.Length == count;
        }

        public static UdonBehaviour[] BuildUdonTargetCache(Component[] componentTargets, UdonBehaviour[] udonTargets, int count)
        {
            if (count < 0)
                count = 0;

            UdonBehaviour[] resolved = new UdonBehaviour[count];
            for (int i = 0; i < count; i++)
                resolved[i] = ResolveUdonTarget(componentTargets, udonTargets, i);

            return resolved;
        }

        public static Component[] BuildComponentTargetCache(Component[] componentTargets, int count)
        {
            if (count < 0)
                count = 0;

            Component[] resolved = new Component[count];
            for (int i = 0; i < count; i++)
            {
                Component target = null;
                if (componentTargets != null && i < componentTargets.Length)
                    target = componentTargets[i];

                resolved[i] = target;
            }

            return resolved;
        }

        public static bool IsUdonTargetActive(UdonBehaviour target)
        {
            if (target == null)
                return false;

            GameObject targetObject = target.gameObject;
            if (targetObject == null)
                return false;

            return targetObject.activeInHierarchy;
        }

        public static bool IsComponentTargetActive(Component target)
        {
            if (target == null)
                return false;

            Behaviour behaviour = target as Behaviour;
            if (behaviour == null)
                return target.gameObject != null && target.gameObject.activeInHierarchy;

            return behaviour.enabled && behaviour.gameObject.activeInHierarchy;
        }
    }
}
