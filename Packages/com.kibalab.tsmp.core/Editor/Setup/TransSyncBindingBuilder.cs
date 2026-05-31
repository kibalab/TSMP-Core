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

        [MenuItem("Tools/TSMP/Rebuild TransSync Bindings In Scene")]
        public static void RebuildSceneBindings()
        {
            RebuildSceneBindings(true);
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

            return left.GetInstanceID().CompareTo(right.GetInstanceID());
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
            var explicitIdOwners = new Dictionary<ushort, string>();
            string lastObjectPath = null;
            ushort currentObjectId = 0;
            int assignedCount = 0;

            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                string objectPath = GetHierarchyPath(behaviour.transform);
                if (objectPath != lastObjectPath)
                {
                    currentObjectId = ResolveRequestedNetworkId(behaviour);
                    if (currentObjectId != 0)
                    {
                        if (explicitIdOwners.TryGetValue(currentObjectId, out string ownerPath))
                        {
                            Debug.LogError("[TSMP] explicit NetworkId collision. id=" + currentObjectId + " first='" + ownerPath + "' duplicate='" + objectPath + "'. Keeping the explicit id; fix one of the components to avoid ambiguous bindings.", behaviour);
                        }
                        else
                        {
                            explicitIdOwners.Add(currentObjectId, objectPath);
                            usedIds.Add(currentObjectId);
                        }
                    }
                    else
                    {
                        currentObjectId = AllocateNetworkId(usedIds);
                        usedIds.Add(currentObjectId);
                    }
                    lastObjectPath = objectPath;
                }

                if (behaviour.networkId != currentObjectId)
                {
                    Undo.RecordObject(behaviour, "Assign TSMP network id");
                    behaviour.networkId = currentObjectId;
                    EditorUtility.SetDirty(behaviour);
                    assignedCount++;
                }
            }

            return assignedCount;
        }

        private static ushort ResolveRequestedNetworkId(TSMPNetworkBehaviour behaviour)
        {
            if (behaviour.networkId != 0)
                return behaviour.networkId;

            TSMPNetworkIdentity identity = behaviour.GetComponent<TSMPNetworkIdentity>();
            return identity != null ? identity.networkId : (ushort)0;
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
            var collisions = new HashSet<BindingKey>();
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
                    if (collisions.Contains(key))
                    {
                        Debug.LogError("[TSMP] TransSync collision: networkId=" + networkId + " hash=" + variableHash + " field=" + field.Name, behaviour);
                        continue;
                    }
                    collisions.Add(key);

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
            var collisions = new HashSet<BindingKey>();
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
                    if (collisions.Contains(key))
                    {
                        Debug.LogError("[TSMP] TransSync collision: networkId=" + networkId + " hash=" + variableHash + " field=" + field.Name, behaviour);
                        continue;
                    }
                    collisions.Add(key);

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

            return UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
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
