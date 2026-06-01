using System.Collections.Generic;
using System.Reflection;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;

namespace K13A.TSMP.Editor
{
    [InitializeOnLoad]
    public static class TransSyncBindingBuilder
    {
        private static bool _rebuildQueued;
        private static readonly Dictionary<int, string> TransSyncCollisionWarnings = new Dictionary<int, string>();
        private static readonly HashSet<string> LoggedTransSyncCollisions = new HashSet<string>();
        private const string FieldNetworkBehaviours = "networkBehaviours";
        private const string FieldBindingTargets = "bindingTargets";
        private const string FieldBindingUdonTargets = "bindingUdonTargets";
        private const string FieldBindingNetworkIds = "bindingNetworkIds";
        private const string FieldBindingVariableHashes = "bindingVariableHashes";
        private const string FieldBindingValueTypes = "bindingValueTypes";
        private const string FieldBindingFieldNames = "bindingFieldNames";
        private const string FieldBindingDirections = "bindingDirections";
        private const string FieldBindingPriorities = "bindingPriorities";

        static TransSyncBindingBuilder()
        {
            UdonProxySyncBridge.SyncAction = SyncUdonProxy;
            UdonProxySyncBridge.ResolveProxyAction = ResolveUdonProxy;
            EditorApplication.hierarchyChanged -= QueueAutomaticRebuild;
            EditorApplication.hierarchyChanged += QueueAutomaticRebuild;
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        private struct BindingKey
        {
            public ushort NetworkId;
            public uint VariableHash;

            public BindingKey(ushort networkId, uint variableHash)
            {
                NetworkId = networkId;
                VariableHash = variableHash;
            }
        }

        private struct BindingOwner
        {
            public TSMPNetworkBehaviour Behaviour;
            public string FieldName;
            public string FieldPath;
        }

        [MenuItem("TSMP/Debug/Rebuild TransSync Bindings In Scene")]
        public static void RebuildSceneBindings()
        {
            RebuildSceneBindings(true);
        }

        [MenuItem("TSMP/Debug/Resolve Network IDs In Scene")]
        public static void ResolveSceneNetworkIds()
        {
            ResolveSceneNetworkIds(true);
        }

        [MenuItem("TSMP/Regenerate Network ID")]
        public static void RegenerateNetworkId()
        {
            ResolveSceneNetworkIds(true);
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            QueueAutomaticRebuild();
        }

        private static void RebuildSceneBindings(bool logResult)
        {
            TSMPNetworkBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<TSMPNetworkBehaviour>(true);
            Component[] encoders = FindComponents("K13A.TSMP.TSMPEncoder");
            Component[] decoders = FindComponents("K13A.TSMP.Udon.TSMPDecoder");

            SortBehaviours(behaviours);
            int assignedNetworkIds = AssignNetworkIds(behaviours);
            int assignedTransRpcEncoders = AssignTransRpcEncoders(encoders, behaviours);
            SyncBackingUdon(behaviours);
            RefreshTransSyncCollisionDiagnostics(behaviours, logResult);

            int assignedEncoders = 0;
            int totalEncoderBindings = 0;
            for (int i = 0; i < encoders.Length; i++)
            {
                bool assigned = AssignEncoder(encoders[i], behaviours);
                if (AssignEncoderBindings(encoders[i], behaviours, out int bindingCount))
                    assigned = true;
                if (assigned)
                    assignedEncoders++;
                totalEncoderBindings += bindingCount;
                SyncBackingUdon(encoders[i]);
            }

            int totalBindings = 0;
            for (int i = 0; i < decoders.Length; i++)
            {
                totalBindings += RebuildDecoder(decoders[i], behaviours);
                SyncBackingUdon(decoders[i]);
            }

            if (logResult)
                Debug.Log("[TSMP] scene network rebuilt. encoders=" + assignedEncoders + " encoderBindings=" + totalEncoderBindings + " decoders=" + decoders.Length + " bindings=" + totalBindings + " assignedNetworkIds=" + assignedNetworkIds + " assignedTransRpcEncoders=" + assignedTransRpcEncoders);
        }

        public static int ResolveSceneNetworkIds(bool logResult)
        {
            TSMPNetworkBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<TSMPNetworkBehaviour>(true);
            SortBehaviours(behaviours);
            int assignedNetworkIds = AssignNetworkIds(behaviours);
            SyncBackingUdon(behaviours);
            RefreshTransSyncCollisionDiagnostics(behaviours, logResult);

            if (logResult)
                Debug.Log("[TSMP] scene Network IDs resolved. assignedNetworkIds=" + assignedNetworkIds);

            return assignedNetworkIds;
        }

        public static bool TryGetTransSyncCollisionWarning(TSMPNetworkBehaviour behaviour, out string warning)
        {
            RefreshTransSyncCollisionDiagnostics(false);

            warning = null;
            if (behaviour == null)
                return false;

            return TransSyncCollisionWarnings.TryGetValue(behaviour.GetInstanceID(), out warning) && !string.IsNullOrEmpty(warning);
        }

        public static void RefreshTransSyncCollisionDiagnostics(bool logCollisions)
        {
            TSMPNetworkBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<TSMPNetworkBehaviour>(true);
            SortBehaviours(behaviours);
            RefreshTransSyncCollisionDiagnostics(behaviours, logCollisions);
        }

        private static void QueueAutomaticRebuild()
        {
            if (_rebuildQueued || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            _rebuildQueued = true;
            EditorApplication.delayCall += RunQueuedAutomaticRebuild;
        }

        private static void RunQueuedAutomaticRebuild()
        {
            _rebuildQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            RebuildSceneBindings(false);
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return modifications;

            if (ContainsNetworkIdChange(modifications))
                QueueAutomaticRebuild();

            return modifications;
        }

        private static bool ContainsNetworkIdChange(UndoPropertyModification[] modifications)
        {
            if (modifications == null)
                return false;

            for (int i = 0; i < modifications.Length; i++)
            {
                PropertyModification modification = modifications[i].currentValue;
                if (modification == null)
                    continue;

                if (modification.propertyPath != "networkId")
                    continue;

                if (modification.target is TSMPNetworkBehaviour || modification.target is TSMPNetworkIdentity)
                    return true;
            }

            return false;
        }

        private static void SortBehaviours(TSMPNetworkBehaviour[] behaviours)
        {
            System.Array.Sort(behaviours, CompareBehaviours);
        }

        private static int CompareBehaviours(TSMPNetworkBehaviour left, TSMPNetworkBehaviour right)
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

            int transformCompare = GetTransformInstanceId(left).CompareTo(GetTransformInstanceId(right));
            if (transformCompare != 0)
                return transformCompare;

            return left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private static int GetTransformInstanceId(TSMPNetworkBehaviour behaviour)
        {
            if (behaviour == null || behaviour.transform == null)
                return 0;

            return behaviour.transform.GetInstanceID();
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

        private static int AssignNetworkIds(TSMPNetworkBehaviour[] behaviours)
        {
            var usedIds = new HashSet<ushort>();
            var idOwners = new Dictionary<ushort, string>();
            int assignedCount = 0;
            int index = 0;

            while (index < behaviours.Length)
            {
                TSMPNetworkBehaviour behaviour = behaviours[index];
                if (behaviour == null)
                {
                    index++;
                    continue;
                }

                Transform objectTransform = behaviour.transform;
                string objectPath = GetHierarchyPath(objectTransform);
                int nextIndex = FindNextObjectGroupIndex(behaviours, index, objectTransform);
                ushort requestedId = ResolveRequestedNetworkId(behaviours, index, nextIndex);
                ushort objectId = ResolveUniqueNetworkId(requestedId, usedIds, idOwners, objectPath, behaviour);
                if (objectId != 0)
                {
                    usedIds.Add(objectId);
                    if (!idOwners.ContainsKey(objectId))
                        idOwners.Add(objectId, objectPath);
                }

                assignedCount += AssignObjectGroupNetworkId(behaviours, index, nextIndex, objectId);
                index = nextIndex;
            }

            return assignedCount;
        }

        private static int FindNextObjectGroupIndex(TSMPNetworkBehaviour[] behaviours, int startIndex, Transform objectTransform)
        {
            int index = startIndex + 1;
            while (index < behaviours.Length)
            {
                TSMPNetworkBehaviour behaviour = behaviours[index];
                if (behaviour == null)
                {
                    index++;
                    continue;
                }

                if (behaviour.transform != objectTransform)
                    break;

                index++;
            }

            return index;
        }

        private static ushort ResolveRequestedNetworkId(TSMPNetworkBehaviour[] behaviours, int startIndex, int endIndex)
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.networkId != 0)
                    return behaviour.networkId;
            }

            TSMPNetworkIdentity identity = behaviours[startIndex] != null ? behaviours[startIndex].GetComponent<TSMPNetworkIdentity>() : null;
            return identity != null ? identity.networkId : (ushort)0;
        }

        private static ushort ResolveUniqueNetworkId(
            ushort requestedId,
            HashSet<ushort> usedIds,
            Dictionary<ushort, string> idOwners,
            string objectPath,
            TSMPNetworkBehaviour context)
        {
            if (requestedId == 0)
                return AllocateNetworkId(usedIds);

            if (!usedIds.Contains(requestedId))
                return requestedId;

            string ownerPath;
            if (!idOwners.TryGetValue(requestedId, out ownerPath))
                ownerPath = "<unknown>";

            ushort resolvedId = AllocateNetworkId(usedIds);
            if (resolvedId != 0)
                Debug.LogWarning("[TSMP] Network ID collision resolved. id=" + requestedId + " first='" + ownerPath + "' duplicate='" + objectPath + "' reassigned=" + resolvedId + ".", context);

            return resolvedId;
        }

        private static int AssignObjectGroupNetworkId(TSMPNetworkBehaviour[] behaviours, int startIndex, int endIndex, ushort networkId)
        {
            int assignedCount = 0;
            for (int i = startIndex; i < endIndex; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.networkId == networkId)
                    continue;

                Undo.RecordObject(behaviour, "Assign TSMP network id");
                behaviour.networkId = networkId;
                EditorUtility.SetDirty(behaviour);
                assignedCount++;
            }

            return assignedCount;
        }

        private static ushort AllocateNetworkId(HashSet<ushort> usedIds)
        {
            for (int i = 1; i <= ushort.MaxValue; i++)
            {
                ushort candidate = (ushort)i;
                if (!usedIds.Contains(candidate))
                    return candidate;
            }

            Debug.LogError("[TSMP] could not allocate a NetworkId. All ushort ids are in use.");
            return 0;
        }

        private static void RefreshTransSyncCollisionDiagnostics(TSMPNetworkBehaviour[] behaviours, bool logCollisions)
        {
            TransSyncCollisionWarnings.Clear();
            if (logCollisions)
                LoggedTransSyncCollisions.Clear();

            if (behaviours == null)
                return;

            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs = Object.FindObjectsOfType<TSMPNetworkVrchatAvatarPoseSync>(true);
            ScanTransSyncCollisions(behaviours, avatarPoseSyncs, true, logCollisions);
            ScanTransSyncCollisions(behaviours, avatarPoseSyncs, false, logCollisions);
        }

        private static void ScanTransSyncCollisions(TSMPNetworkBehaviour[] behaviours, TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs, bool sendTable, bool logCollisions)
        {
            var owners = new Dictionary<BindingKey, BindingOwner>();
            string tableName = sendTable ? "send" : "receive";

            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                if (IsAvatarPosePoolBehaviour(behaviour, avatarPoseSyncs))
                    continue;

                ushort networkId = ResolveNetworkId(behaviour);
                FieldInfo[] fields = behaviour.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int f = 0; f < fields.Length; f++)
                {
                    FieldInfo field = fields[f];
                    TransSyncAttribute sync = field.GetCustomAttribute<TransSyncAttribute>(true);
                    if (sync == null)
                        continue;
                    if (sendTable && sync.Direction == NetworkSyncDirection.ReceiveOnly)
                        continue;
                    if (!sendTable && sync.Direction == NetworkSyncDirection.SendOnly)
                        continue;
                    if (!IsTransSyncFieldEnabled(behaviour, sync, field))
                        continue;
                    if (!NetworkValueCodec.TryGetValueType(field.FieldType, out _))
                        continue;

                    uint variableHash = StableHash.VariableHash(behaviour.GetType(), field.Name, sync.Key);
                    var key = new BindingKey(networkId, variableHash);
                    BindingOwner current = CreateBindingOwner(behaviour, field);
                    if (owners.TryGetValue(key, out BindingOwner existing))
                    {
                        RegisterTransSyncCollision(tableName, key, existing, current, logCollisions);
                        continue;
                    }

                    owners.Add(key, current);
                }
            }
        }

        private static bool AssignEncoder(Component encoder, TSMPNetworkBehaviour[] behaviours)
        {
            if (encoder == null)
                return false;

            if (!HasPublicField(encoder, FieldNetworkBehaviours))
                return false;

            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs = Object.FindObjectsOfType<TSMPNetworkVrchatAvatarPoseSync>(true);
            var senders = new List<Component>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour != null && !IsAvatarPosePoolBehaviour(behaviour, avatarPoseSyncs))
                    senders.Add(behaviour);
            }

            System.Array current = GetFieldValue(encoder, FieldNetworkBehaviours) as System.Array;
            if (SameArray(current, senders))
                return true;

            Undo.RecordObject(encoder, "Assign TSMP network behaviours");
            SetComponentArrayFieldValue(encoder, FieldNetworkBehaviours, senders);
            EditorUtility.SetDirty(encoder);
            return true;
        }

        private static int AssignTransRpcEncoders(Component[] encoders, TSMPNetworkBehaviour[] behaviours)
        {
            TSMPEncoder encoder = null;
            if (encoders != null)
            {
                for (int i = 0; i < encoders.Length; i++)
                {
                    encoder = encoders[i] as TSMPEncoder;
                    if (encoder != null)
                        break;
                }
            }

            if (encoder == null || behaviours == null)
                return 0;

            int assigned = 0;
            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs = Object.FindObjectsOfType<TSMPNetworkVrchatAvatarPoseSync>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                if (IsAvatarPosePoolBehaviour(behaviour, avatarPoseSyncs))
                    continue;
                if (behaviour.transRpcEncoder == encoder)
                    continue;

                Undo.RecordObject(behaviour, "Assign TSMP TransRPC encoder");
                behaviour.transRpcEncoder = encoder;
                EditorUtility.SetDirty(behaviour);
                assigned++;
            }

            return assigned;
        }

        private static bool AssignEncoderBindings(Component encoder, TSMPNetworkBehaviour[] behaviours, out int bindingCount)
        {
            bindingCount = 0;
            if (encoder == null)
                return false;

            var targets = new List<Component>();
            var udonTargets = new List<UdonBehaviour>();
            var networkIds = new List<ushort>();
            var variableHashes = new List<uint>();
            var valueTypes = new List<byte>();
            var fieldNames = new List<string>();
            var directions = new List<int>();
            var collisions = new Dictionary<BindingKey, BindingOwner>();
            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs = Object.FindObjectsOfType<TSMPNetworkVrchatAvatarPoseSync>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (IsAvatarPosePoolBehaviour(behaviour, avatarPoseSyncs))
                    continue;

                ushort networkId = ResolveNetworkId(behaviour);
                FieldInfo[] fields = behaviour.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int f = 0; f < fields.Length; f++)
                {
                    FieldInfo field = fields[f];
                    TransSyncAttribute sync = field.GetCustomAttribute<TransSyncAttribute>(true);
                    if (sync == null || sync.Direction == NetworkSyncDirection.ReceiveOnly)
                        continue;

                    if (!IsTransSyncFieldEnabled(behaviour, sync, field))
                        continue;

                    if (!NetworkValueCodec.TryGetValueType(field.FieldType, out NetworkValueType valueType))
                    {
                        Debug.LogWarning("[TSMP] TransSync unsupported field type: " + behaviour.GetType().Name + "." + field.Name + " type=" + field.FieldType.Name, behaviour);
                        continue;
                    }

                    uint variableHash = StableHash.VariableHash(behaviour.GetType(), field.Name, sync.Key);
                    var key = new BindingKey(networkId, variableHash);
                    BindingOwner current = CreateBindingOwner(behaviour, field);
                    if (collisions.TryGetValue(key, out BindingOwner existing))
                    {
                        RegisterTransSyncCollision("send", key, existing, current, true);
                        continue;
                    }
                    collisions.Add(key, current);

                    targets.Add(behaviour);
                    udonTargets.Add(GetBackingUdonBindingTarget(behaviour));
                    networkIds.Add(networkId);
                    variableHashes.Add(variableHash);
                    valueTypes.Add((byte)valueType);
                    fieldNames.Add(field.Name);
                    directions.Add((int)sync.Direction);
                }
            }

            Undo.RecordObject(encoder, "Assign TSMP encoder bindings");
            SetComponentArrayFieldValue(encoder, FieldBindingTargets, targets);
            SetFieldValue(encoder, FieldBindingUdonTargets, udonTargets.ToArray());
            SetFieldValue(encoder, FieldBindingNetworkIds, networkIds.ToArray());
            SetFieldValue(encoder, FieldBindingVariableHashes, variableHashes.ToArray());
            SetFieldValue(encoder, FieldBindingValueTypes, valueTypes.ToArray());
            SetFieldValue(encoder, FieldBindingFieldNames, fieldNames.ToArray());
            SetFieldValue(encoder, FieldBindingDirections, directions.ToArray());
            EditorUtility.SetDirty(encoder);
            bindingCount = targets.Count;
            return true;
        }

        private static int RebuildDecoder(Component decoder, TSMPNetworkBehaviour[] behaviours)
        {
            if (decoder == null)
                return 0;

            var targets = new List<Component>();
            var udonTargets = new List<UdonBehaviour>();
            var networkIds = new List<ushort>();
            var variableHashes = new List<uint>();
            var valueTypes = new List<byte>();
            var fieldNames = new List<string>();
            var directions = new List<int>();
            var priorities = new List<int>();
            var collisions = new Dictionary<BindingKey, BindingOwner>();
            TSMPNetworkVrchatAvatarPoseSync[] avatarPoseSyncs = Object.FindObjectsOfType<TSMPNetworkVrchatAvatarPoseSync>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (IsAvatarPosePoolBehaviour(behaviour, avatarPoseSyncs))
                    continue;

                ushort networkId = ResolveNetworkId(behaviour);
                int targetStartCount = targets.Count;
                FieldInfo[] fields = behaviour.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int f = 0; f < fields.Length; f++)
                {
                    FieldInfo field = fields[f];
                    TransSyncAttribute sync = field.GetCustomAttribute<TransSyncAttribute>(true);
                    if (sync == null || sync.Direction == NetworkSyncDirection.SendOnly)
                        continue;

                    if (!IsTransSyncFieldEnabled(behaviour, sync, field))
                        continue;

                    if (!NetworkValueCodec.TryGetValueType(field.FieldType, out NetworkValueType valueType))
                    {
                        Debug.LogWarning("[TSMP] TransSync unsupported field type: " + behaviour.GetType().Name + "." + field.Name + " type=" + field.FieldType.Name, behaviour);
                        continue;
                    }

                    uint variableHash = StableHash.VariableHash(behaviour.GetType(), field.Name, sync.Key);
                    var key = new BindingKey(networkId, variableHash);
                    BindingOwner current = CreateBindingOwner(behaviour, field);
                    if (collisions.TryGetValue(key, out BindingOwner existing))
                    {
                        RegisterTransSyncCollision("receive", key, existing, current, true);
                        continue;
                    }
                    collisions.Add(key, current);

                    targets.Add(behaviour);
                    udonTargets.Add(GetBackingUdonBindingTarget(behaviour));
                    networkIds.Add(networkId);
                    variableHashes.Add(variableHash);
                    valueTypes.Add((byte)valueType);
                    fieldNames.Add(field.Name);
                    directions.Add((int)sync.Direction);
                    priorities.Add(sync.Priority);
                }

                if (targets.Count == targetStartCount)
                    AddRpcOnlyTarget(behaviour, networkId, targets, udonTargets, networkIds, variableHashes, valueTypes, fieldNames, directions, priorities);
            }

            Undo.RecordObject(decoder, "Rebuild TSMP TransSync bindings");
            SetComponentArrayFieldValue(decoder, FieldBindingTargets, targets);
            SetFieldValue(decoder, FieldBindingUdonTargets, udonTargets.ToArray());
            SetFieldValue(decoder, FieldBindingNetworkIds, networkIds.ToArray());
            SetFieldValue(decoder, FieldBindingVariableHashes, variableHashes.ToArray());
            SetFieldValue(decoder, FieldBindingValueTypes, valueTypes.ToArray());
            SetFieldValue(decoder, FieldBindingFieldNames, fieldNames.ToArray());
            SetFieldValue(decoder, FieldBindingDirections, directions.ToArray());
            SetFieldValue(decoder, FieldBindingPriorities, priorities.ToArray());
            EditorUtility.SetDirty(decoder);

            return targets.Count;
        }

        private static void AddRpcOnlyTarget(
            TSMPNetworkBehaviour behaviour,
            ushort networkId,
            List<Component> targets,
            List<UdonBehaviour> udonTargets,
            List<ushort> networkIds,
            List<uint> variableHashes,
            List<byte> valueTypes,
            List<string> fieldNames,
            List<int> directions,
            List<int> priorities)
        {
            targets.Add(behaviour);
            udonTargets.Add(GetBackingUdonBindingTarget(behaviour));
            networkIds.Add(networkId);
            variableHashes.Add(0u);
            valueTypes.Add((byte)NetworkFrameProtocol.ValueTypeUnsupported);
            fieldNames.Add(string.Empty);
            directions.Add((int)NetworkSyncDirection.ReceiveOnly);
            priorities.Add(0);
        }

        private static BindingOwner CreateBindingOwner(TSMPNetworkBehaviour behaviour, FieldInfo field)
        {
            BindingOwner owner = new BindingOwner();
            owner.Behaviour = behaviour;
            owner.FieldName = field != null ? field.Name : string.Empty;
            owner.FieldPath = GetBindingOwnerPath(behaviour, owner.FieldName);
            return owner;
        }

        private static void RegisterTransSyncCollision(string tableName, BindingKey key, BindingOwner first, BindingOwner duplicate, bool logCollision)
        {
            string firstPath = string.IsNullOrEmpty(first.FieldPath) ? "<unknown>" : first.FieldPath;
            string duplicatePath = string.IsNullOrEmpty(duplicate.FieldPath) ? "<unknown>" : duplicate.FieldPath;
            string duplicateField = string.IsNullOrEmpty(duplicate.FieldName) ? "<unknown>" : duplicate.FieldName;

            AppendTransSyncCollisionWarning(first.Behaviour, CreateTransSyncCollisionWarning(first.FieldName, key));
            AppendTransSyncCollisionWarning(duplicate.Behaviour, CreateTransSyncCollisionWarning(duplicate.FieldName, key));

            if (!logCollision)
                return;

            string logKey = tableName + ":" + key.NetworkId + ":" + key.VariableHash + ":" + firstPath + ":" + duplicatePath;
            if (LoggedTransSyncCollisions.Contains(logKey))
                return;

            LoggedTransSyncCollisions.Add(logKey);
            Debug.LogError("[TSMP] TransSync collision: networkId=" + key.NetworkId + " hash=" + key.VariableHash + " field=" + duplicateField + " table=" + tableName + " conflictsWith=" + firstPath, duplicate.Behaviour);
        }

        private static string CreateTransSyncCollisionWarning(string fieldName, BindingKey key)
        {
            string variableName = string.IsNullOrEmpty(fieldName) ? "<unknown>" : fieldName;
            return "TransSync target variable " + variableName + " conflicts with another variable using Network ID " + key.NetworkId + " and TransSync ID " + key.VariableHash + ".";
        }

        private static void AppendTransSyncCollisionWarning(TSMPNetworkBehaviour behaviour, string message)
        {
            if (behaviour == null || string.IsNullOrEmpty(message))
                return;

            int id = behaviour.GetInstanceID();
            if (TransSyncCollisionWarnings.TryGetValue(id, out string current) && !string.IsNullOrEmpty(current))
                TransSyncCollisionWarnings[id] = current + "\n" + message;
            else
                TransSyncCollisionWarnings[id] = message;
        }

        private static string GetBindingOwnerPath(TSMPNetworkBehaviour behaviour, string fieldName)
        {
            if (behaviour == null)
                return string.Empty;

            string path = GetHierarchyPath(behaviour.transform);
            return path + "/" + behaviour.GetType().Name + "." + fieldName;
        }

        private static bool IsTransSyncFieldEnabled(TSMPNetworkBehaviour behaviour, TransSyncAttribute sync, FieldInfo syncedField)
        {
            if (sync == null || string.IsNullOrEmpty(sync.EnabledBy))
                return true;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type type = behaviour.GetType();

            FieldInfo field = type.GetField(sync.EnabledBy, Flags);
            if (field != null)
            {
                if (field.FieldType == typeof(bool))
                    return (bool)field.GetValue(behaviour);

                Debug.LogWarning("[TSMP] TransSync EnabledBy field is not bool: " + behaviour.GetType().Name + "." + syncedField.Name + " EnabledBy=" + sync.EnabledBy, behaviour);
                return true;
            }

            PropertyInfo property = type.GetProperty(sync.EnabledBy, Flags);
            if (property != null)
            {
                if (property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
                    return (bool)property.GetValue(behaviour);

                Debug.LogWarning("[TSMP] TransSync EnabledBy property is not bool: " + behaviour.GetType().Name + "." + syncedField.Name + " EnabledBy=" + sync.EnabledBy, behaviour);
                return true;
            }

            Debug.LogWarning("[TSMP] TransSync EnabledBy member was not found: " + behaviour.GetType().Name + "." + syncedField.Name + " EnabledBy=" + sync.EnabledBy, behaviour);
            return true;
        }

        private static ushort ResolveNetworkId(TSMPNetworkBehaviour behaviour)
        {
            if (behaviour.networkId != 0)
                return behaviour.networkId;

            TSMPNetworkIdentity identity = behaviour.GetComponent<TSMPNetworkIdentity>();
            if (identity == null)
                identity = behaviour.GetComponentInParent<TSMPNetworkIdentity>();

            return identity != null ? identity.networkId : (ushort)0;
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

        private static UdonBehaviour GetBackingUdonBindingTarget(TSMPNetworkBehaviour behaviour)
        {
            if (behaviour == null)
                return null;

            Component component = behaviour;
            UdonSharpBehaviour proxy = component as UdonSharpBehaviour;
            if (proxy == null)
                return null;

            return UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        }

        private static bool SameArray(System.Array current, List<Component> next)
        {
            if (current == null)
                return next.Count == 0;

            if (current.Length != next.Count)
                return false;

            for (int i = 0; i < current.Length; i++)
            {
                if (current.GetValue(i) as Component != next[i])
                    return false;
            }

            return true;
        }

        private static Component[] FindComponents(string fullName)
        {
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            var matches = new List<Component>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                System.Type type = behaviour.GetType();
                if (type.FullName == fullName)
                    matches.Add(behaviour);
            }

            return matches.ToArray();
        }

        private static void SyncBackingUdon(TSMPNetworkBehaviour[] behaviours)
        {
            if (behaviours == null)
                return;

            for (int i = 0; i < behaviours.Length; i++)
                SyncBackingUdon(behaviours[i]);
        }

        private static void SyncBackingUdon(Component component)
        {
            UdonProxySyncBridge.Sync(component);
        }

        private static void SyncUdonProxy(Component component)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            UdonSharpBehaviour proxy = component as UdonSharpBehaviour;
            if (proxy == null)
                return;

            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.All);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[TSMP] Failed to sync Udon proxy state for " + component.GetType().Name + ": " + exception.Message, component);
            }
        }

        private static Component ResolveUdonProxy(UdonBehaviour behaviour)
        {
            if (behaviour == null)
                return null;

            return UdonSharpEditorUtility.GetProxyBehaviour(behaviour);
        }

        private static object GetFieldValue(Component target, string fieldName)
        {
            if (target == null)
                return null;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            return field != null ? field.GetValue(target) : null;
        }

        private static bool HasPublicField(Component target, string fieldName)
        {
            if (target == null)
                return false;

            return target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public) != null;
        }

        private static void SetFieldValue(Component target, string fieldName, object value)
        {
            if (target == null)
                return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                Debug.LogWarning("[TSMP] Binding field '" + fieldName + "' was not found on " + target.GetType().Name + ".", target);
                return;
            }

            field.SetValue(target, value);
        }

        private static void SetComponentArrayFieldValue(Component target, string fieldName, List<Component> values)
        {
            if (target == null)
                return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null || !field.FieldType.IsArray)
            {
                Debug.LogWarning("[TSMP] Binding array field '" + fieldName + "' is missing or invalid on " + target.GetType().Name + ".", target);
                return;
            }

            System.Type elementType = field.FieldType.GetElementType();
            if (elementType == null || !typeof(Component).IsAssignableFrom(elementType))
                return;

            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                Component value = values[i];
                if (value != null && elementType.IsAssignableFrom(value.GetType()))
                    count++;
            }

            System.Array array = System.Array.CreateInstance(elementType, count);
            int cursor = 0;
            for (int i = 0; i < values.Count; i++)
            {
                Component value = values[i];
                if (value == null || !elementType.IsAssignableFrom(value.GetType()))
                    continue;

                array.SetValue(value, cursor);
                cursor++;
            }

            field.SetValue(target, array);
        }
    }
}
