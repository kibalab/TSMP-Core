#if !COMPILER_UDONSHARP
using System.Collections.Generic;
using System.Reflection;
using K13A.TSMP.Udon;
using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class TransSyncMetadata
    {
        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public sealed class Cache
        {
            public MethodInfo BeforeEncodeMethod;
            public Field[] Fields;
        }

        public sealed class Field
        {
            public FieldInfo FieldInfo;
            public TransSyncAttribute Sync;
            public NetworkValueType ValueType;
            public uint VariableHash;
            public FieldInfo EnabledField;
            public PropertyInfo EnabledProperty;
        }

        public static Cache GetOrCreate(Dictionary<System.Type, Cache> cacheTable, System.Type type)
        {
            if (type == null)
                type = typeof(TSMPNetworkBehaviour);

            Cache cache;
            if (cacheTable != null)
            {
                if (cacheTable.TryGetValue(type, out cache))
                    return cache;
            }

            cache = Build(type);
            if (cacheTable != null)
                cacheTable[type] = cache;

            return cache;
        }

        public static bool IsFieldEnabled(TSMPNetworkBehaviour behaviour, Field field)
        {
            if (field == null)
                return true;
            if (field.Sync == null)
                return true;
            if (string.IsNullOrEmpty(field.Sync.EnabledBy))
                return true;

            if (field.EnabledField != null)
                return (bool)field.EnabledField.GetValue(behaviour);

            if (field.EnabledProperty != null)
                return (bool)field.EnabledProperty.GetValue(behaviour);

            return true;
        }

        public static bool CanSend(Field field)
        {
            if (field == null)
                return false;
            if (field.Sync == null)
                return false;

            return field.Sync.Direction != NetworkSyncDirection.ReceiveOnly;
        }

        public static bool CanReceive(Field field)
        {
            if (field == null)
                return false;
            if (field.Sync == null)
                return false;

            return field.Sync.Direction != NetworkSyncDirection.SendOnly;
        }

        private static Cache Build(System.Type type)
        {
            Cache cache = new Cache();
            MethodInfo beforeEncode = type.GetMethod(TSMPNetworkBehaviour.BeforeEncodeEventName, MemberFlags);
            if (beforeEncode != null)
            {
                if (beforeEncode.GetParameters().Length == 0)
                    cache.BeforeEncodeMethod = beforeEncode;
            }

            FieldInfo[] fields = type.GetFields(MemberFlags);
            List<Field> cachedFields = new List<Field>(fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo fieldInfo = fields[i];
                TransSyncAttribute sync = fieldInfo.GetCustomAttribute<TransSyncAttribute>(true);
                if (sync == null)
                    continue;

                NetworkValueType valueType;
                if (!NetworkValueCodec.TryGetValueType(fieldInfo.FieldType, out valueType))
                    continue;

                Field field = new Field();
                field.FieldInfo = fieldInfo;
                field.Sync = sync;
                field.ValueType = valueType;
                field.VariableHash = StableHash.VariableHash(type, fieldInfo.Name, sync.Key);
                ResolveEnabledMember(type, sync, field);
                cachedFields.Add(field);
            }

            cache.Fields = cachedFields.ToArray();
            return cache;
        }

        private static void ResolveEnabledMember(System.Type type, TransSyncAttribute sync, Field field)
        {
            if (sync == null)
                return;
            if (string.IsNullOrEmpty(sync.EnabledBy))
                return;

            FieldInfo enabledField = type.GetField(sync.EnabledBy, MemberFlags);
            if (enabledField != null)
            {
                if (enabledField.FieldType == typeof(bool))
                {
                    field.EnabledField = enabledField;
                    return;
                }
            }

            PropertyInfo enabledProperty = type.GetProperty(sync.EnabledBy, MemberFlags);
            if (enabledProperty == null)
                return;
            if (enabledProperty.PropertyType != typeof(bool))
                return;
            if (enabledProperty.GetIndexParameters().Length != 0)
                return;

            field.EnabledProperty = enabledProperty;
        }
    }

    public sealed class TransSyncBindingSnapshot
    {
        public sealed class Collision
        {
            public TSMPNetworkBehaviour FirstTarget;
            public string FirstFieldName;
            public TSMPNetworkBehaviour DuplicateTarget;
            public string DuplicateFieldName;
            public ushort NetworkId;
            public uint VariableHash;
        }

        public Component[] Targets;
#if UDONSHARP || COMPILER_UDONSHARP
        public UdonBehaviour[] UdonTargets;
#endif
        public ushort[] NetworkIds;
        public uint[] VariableHashes;
        public byte[] ValueTypes;
        public string[] FieldNames;
        public int[] Directions;
        public int[] Priorities;
        public bool[] SendOnChange;
        public float[] MinSendIntervals;
        public string[] SentEvents;
        public Collision[] Collisions;
    }

    public static class TransSyncBindingSnapshotBuilder
    {
        public static TransSyncBindingSnapshot Build(bool receive)
        {
            TSMPNetworkBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<TSMPNetworkBehaviour>(true);
            TransSyncBindingSnapshot snapshot = Build(receive, behaviours);
            for (int i = 0; i < snapshot.Collisions.Length; i++)
            {
                TransSyncBindingSnapshot.Collision collision = snapshot.Collisions[i];
                string firstPath = GetHierarchyPath(collision.FirstTarget.transform) + "/" + collision.FirstTarget.GetType().Name + "." + collision.FirstFieldName;
                Debug.LogError("[TSMP] TransSync collision: networkId=" + collision.NetworkId + " hash=" + collision.VariableHash + " field=" + collision.DuplicateFieldName + " table=" + (receive ? "receive" : "send") + " conflictsWith=" + firstPath, collision.DuplicateTarget);
            }
            return snapshot;
        }

        // Both setup and automatic editor rebuilds compile the same ordered table.
        // This overload returns diagnostics without logging so the editor can deduplicate them.
        public static TransSyncBindingSnapshot Build(bool receive, TSMPNetworkBehaviour[] sourceBehaviours)
        {
            TSMPNetworkBehaviour[] behaviours = sourceBehaviours != null
                ? (TSMPNetworkBehaviour[])sourceBehaviours.Clone()
                : new TSMPNetworkBehaviour[0];
            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs = UnityEngine.Object.FindObjectsOfType<TSMPNetworkVrchatAvatarPoseSync>(true);
            System.Array.Sort(behaviours, CompareNetworkBehaviours);

            List<Component> targets = new List<Component>();
#if UDONSHARP || COMPILER_UDONSHARP
            List<UdonBehaviour> udonTargets = new List<UdonBehaviour>();
#endif
            List<ushort> networkIds = new List<ushort>();
            List<uint> variableHashes = new List<uint>();
            List<byte> valueTypes = new List<byte>();
            List<string> fieldNames = new List<string>();
            List<int> directions = new List<int>();
            List<int> priorities = new List<int>();
            List<bool> sendOnChange = new List<bool>();
            List<float> minSendIntervals = new List<float>();
            List<string> sentEvents = new List<string>();
            List<TransSyncBindingSnapshot.Collision> collisions = new List<TransSyncBindingSnapshot.Collision>();
            Dictionary<ulong, int> owners = new Dictionary<ulong, int>();

            for (int i = 0; i < behaviours.Length; i++)
                AddBehaviour(behaviours[i], avatarPoseSyncs, receive, targets,
#if UDONSHARP || COMPILER_UDONSHARP
                    udonTargets,
#endif
                    networkIds, variableHashes, valueTypes, fieldNames, directions, priorities, sendOnChange, minSendIntervals, sentEvents, owners, collisions);

            TransSyncBindingSnapshot snapshot = new TransSyncBindingSnapshot();
            snapshot.Targets = targets.ToArray();
#if UDONSHARP || COMPILER_UDONSHARP
            snapshot.UdonTargets = udonTargets.ToArray();
#endif
            snapshot.NetworkIds = networkIds.ToArray();
            snapshot.VariableHashes = variableHashes.ToArray();
            snapshot.ValueTypes = valueTypes.ToArray();
            snapshot.FieldNames = fieldNames.ToArray();
            snapshot.Directions = directions.ToArray();
            snapshot.Priorities = priorities.ToArray();
            snapshot.SendOnChange = sendOnChange.ToArray();
            snapshot.MinSendIntervals = minSendIntervals.ToArray();
            snapshot.SentEvents = sentEvents.ToArray();
            snapshot.Collisions = collisions.ToArray();
            return snapshot;
        }

        private static void AddBehaviour(
            TSMPNetworkBehaviour behaviour,
            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs,
            bool receive,
            List<Component> targets,
#if UDONSHARP || COMPILER_UDONSHARP
            List<UdonBehaviour> udonTargets,
#endif
            List<ushort> networkIds,
            List<uint> variableHashes,
            List<byte> valueTypes,
            List<string> fieldNames,
            List<int> directions,
            List<int> priorities,
            List<bool> sendOnChange,
            List<float> minSendIntervals,
            List<string> sentEvents,
            Dictionary<ulong, int> owners,
            List<TransSyncBindingSnapshot.Collision> collisions)
        {
            if (behaviour == null)
                return;
            if (IsAvatarPosePoolBehaviour(behaviour, avatarPoseSyncs))
                return;

            ushort networkId = BindingTable.ResolveNetworkId(behaviour);
            TransSyncMetadata.Field[] fields = TransSyncMetadata.GetOrCreate(null, behaviour.GetType()).Fields;
            int targetStartCount = targets.Count;
            for (int f = 0; f < fields.Length; f++)
            {
                TransSyncMetadata.Field field = fields[f];
                if (receive)
                {
                    if (!TransSyncMetadata.CanReceive(field))
                        continue;
                }
                else if (!TransSyncMetadata.CanSend(field))
                {
                    continue;
                }

                if (!TransSyncMetadata.IsFieldEnabled(behaviour, field))
                    continue;

                ulong key = ((ulong)networkId << 32) | field.VariableHash;
                if (owners.TryGetValue(key, out int firstIndex))
                {
                    collisions.Add(new TransSyncBindingSnapshot.Collision
                    {
                        FirstTarget = (TSMPNetworkBehaviour)targets[firstIndex],
                        FirstFieldName = fieldNames[firstIndex],
                        DuplicateTarget = behaviour,
                        DuplicateFieldName = field.FieldInfo.Name,
                        NetworkId = networkId,
                        VariableHash = field.VariableHash
                    });
                    continue;
                }
                owners.Add(key, targets.Count);

                targets.Add(behaviour);
#if UDONSHARP || COMPILER_UDONSHARP
                udonTargets.Add(ComponentReflection.GetBackingUdonBehaviour(behaviour));
#endif
                networkIds.Add(networkId);
                variableHashes.Add(field.VariableHash);
                valueTypes.Add((byte)field.ValueType);
                fieldNames.Add(field.FieldInfo.Name);
                directions.Add((int)field.Sync.Direction);
                priorities.Add(field.Sync.Priority);
                sendOnChange.Add(field.Sync.SendOnChange);
                minSendIntervals.Add(TransSyncSendScheduler.NormalizeInterval(field.Sync.MinSendInterval));
                sentEvents.Add(field.Sync.SentEvent ?? string.Empty);
            }

            if (receive && targets.Count == targetStartCount)
                AddRpcOnlyTarget(behaviour, networkId, targets,
#if UDONSHARP || COMPILER_UDONSHARP
                    udonTargets,
#endif
                    networkIds, variableHashes, valueTypes, fieldNames, directions, priorities, sendOnChange, minSendIntervals, sentEvents);
        }

        private static void AddRpcOnlyTarget(
            TSMPNetworkBehaviour behaviour,
            ushort networkId,
            List<Component> targets,
#if UDONSHARP || COMPILER_UDONSHARP
            List<UdonBehaviour> udonTargets,
#endif
            List<ushort> networkIds,
            List<uint> variableHashes,
            List<byte> valueTypes,
            List<string> fieldNames,
            List<int> directions,
            List<int> priorities,
            List<bool> sendOnChange,
            List<float> minSendIntervals,
            List<string> sentEvents)
        {
            targets.Add(behaviour);
#if UDONSHARP || COMPILER_UDONSHARP
            udonTargets.Add(ComponentReflection.GetBackingUdonBehaviour(behaviour));
#endif
            networkIds.Add(networkId);
            variableHashes.Add(0u);
            valueTypes.Add((byte)NetworkFrameProtocol.ValueTypeUnsupported);
            fieldNames.Add(string.Empty);
            directions.Add((int)NetworkSyncDirection.ReceiveOnly);
            priorities.Add(0);
            sendOnChange.Add(false);
            minSendIntervals.Add(0f);
            sentEvents.Add(string.Empty);
        }

        private static int CompareNetworkBehaviours(TSMPNetworkBehaviour left, TSMPNetworkBehaviour right)
        {
            if (left == right)
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int pathCompare = string.CompareOrdinal(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform));
            if (pathCompare != 0)
                return pathCompare;

            int transformCompare = left.transform.GetInstanceID().CompareTo(right.transform.GetInstanceID());
            if (transformCompare != 0)
                return transformCompare;

            return left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private static bool IsAvatarPosePoolBehaviour(TSMPNetworkBehaviour behaviour, TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs)
        {
            if (behaviour == null || avatarPoseSyncs == null)
                return false;
            if (behaviour is TSMPNetworkVrchatAvatarPoseSync)
                return false;

            Transform behaviourTransform = behaviour.transform;
            if (behaviourTransform == null)
                return false;

            for (int i = 0; i < avatarPoseSyncs.Length; i++)
            {
                TSMPNetworkVrchatAvatarPoseSync sync = avatarPoseSyncs[i];
                if (sync == null)
                    continue;

                if (sync.avatarPoolRoot != null && behaviourTransform.IsChildOf(sync.avatarPoolRoot))
                    return true;

                if (sync.avatarPool == null)
                    continue;

                for (int p = 0; p < sync.avatarPool.Length; p++)
                {
                    GameObject avatar = sync.avatarPool[p];
                    if (avatar != null && avatar.transform != null && behaviourTransform.IsChildOf(avatar.transform))
                        return true;
                }
            }

            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
#endif
