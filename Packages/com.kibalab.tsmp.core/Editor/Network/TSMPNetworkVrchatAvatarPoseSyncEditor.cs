using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkVrchatAvatarPoseSync))]
    [CanEditMultipleObjects]
    public sealed class TSMPNetworkVrchatAvatarPoseSyncEditor : TSMPNetworkBehaviourEditor
    {
        private static readonly GUIContent PoseModeLabel = new GUIContent("Pose Detail");
        private static readonly GUIContent[] PoseModeOptions =
        {
            new GUIContent("Root Only"),
            new GUIContent("Tracking Points"),
            new GUIContent("Full Humanoid")
        };

        private SerializedProperty _poseMode;
        private SerializedProperty _includeFingerBones;
        private SerializedProperty _maxPlayers;
        private SerializedProperty _avatarPrefab;
        private SerializedProperty _avatarPoolRoot;

        private void OnEnable()
        {
            _poseMode = serializedObject.FindProperty("poseMode");
            _includeFingerBones = serializedObject.FindProperty("includeFingerBones");
            _maxPlayers = serializedObject.FindProperty("maxPlayers");
            _avatarPrefab = serializedObject.FindProperty("avatarPrefab");
            _avatarPoolRoot = serializedObject.FindProperty("avatarPoolRoot");
        }

        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();

            DrawTSMPNetworkSection(true);
            DrawPoseSection();
            DrawAvatarPoolSection();
            DrawRuntimeStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPoseSection()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Avatar Pose", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawPoseMode();
            if (GetSelectedMode() == VrchatAvatarPoseMode.HumanoidBones)
                EditorGUILayout.PropertyField(_includeFingerBones);

            if (_maxPlayers != null)
                EditorGUILayout.IntSlider(_maxPlayers, 1, 80, new GUIContent("Max Players"));
            EditorGUI.indentLevel--;
            DrawCapacityEstimate();
        }

        // Cold-start estimate for the current sender policy: compact humanoid,
        // no names, and one complete root pose per player before caches are warm.
        internal static int EstimateColdFrameBytes(VrchatAvatarPoseMode mode, bool fingers, int players)
        {
            int records = mode == VrchatAvatarPoseMode.HumanoidBones ? (fingers ? 52 : 22)
                : mode == VrchatAvatarPoseMode.TrackingPoints ? 6 : 0;
            int recordBytes = mode == VrchatAvatarPoseMode.HumanoidBones ? 6 : 18;
            int framing = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes
                + NetworkFrameProtocol.VariableStateBodyHeaderBytes + NetworkFrameProtocol.VariableValueHeaderBytes + 6;
            return framing + Mathf.Clamp(players, 1, 80) * (5 + 17 + records * recordBytes);
        }

        private void DrawCapacityEstimate()
        {
            if (targets.Length != 1 || _maxPlayers == null || _poseMode == null)
                return;

            int required = EstimateColdFrameBytes(GetSelectedMode(), _includeFingerBones != null && _includeFingerBones.boolValue, _maxPlayers.intValue);
            TSMPEncoder encoder = ((TSMPNetworkVrchatAvatarPoseSync)target).transRpcEncoder;
            TSMPCodec codec = encoder != null ? encoder.selectedCodec : null;
            int width = encoder != null && encoder.output != null ? encoder.output.width : encoder != null ? encoder.width : 0;
            int height = encoder != null && encoder.output != null ? encoder.output.height : encoder != null ? encoder.height : 0;
            int capacity = codec != null && width > 0 && height > 0 && encoder.blockSize > 0
                ? Mathf.Min(NetworkFrameProtocol.MaximumPayloadBytes, codec.GetPayloadCapacityBytes(width, height, encoder.blockSize)) : 0;

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Sender Capacity Estimate", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(new GUIContent("Cold Frame Bytes", "One avatar pose field including NetworkFrame framing, with all configured players and their roots."), required);
                if (capacity > 0)
                {
                    EditorGUILayout.IntField("Codec Capacity", capacity);
                    EditorGUILayout.IntField(new GUIContent("Pose-only Headroom", "Other TransSync fields and RPCs consume additional bytes."), capacity - required);
                }
            }
            EditorGUILayout.HelpBox("Initial pose estimate includes every player's root, with compact humanoid records and no display names. Other fields and RPCs need additional capacity. Byte fit does not certify real-time player support.", MessageType.Info);
            if (encoder == null || codec == null)
                EditorGUILayout.HelpBox("Allow TSMPSetup to prepare the sender and selected codec to compare this estimate with its capacity.", MessageType.Info);
            else if (capacity <= 0 || required > capacity)
                EditorGUILayout.HelpBox("The initial pose alone exceeds this encoder's payload capacity. Reduce player count or pose detail, or increase codec capacity before testing.", MessageType.Warning);
        }

        private void DrawAvatarPoolSection()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Avatar Pool", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_avatarPrefab);
            EditorGUILayout.PropertyField(_avatarPoolRoot);
            EditorGUI.indentLevel--;
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying || targets.Length != 1)
                return;

            TSMPNetworkVrchatAvatarPoseSync sync = (TSMPNetworkVrchatAvatarPoseSync)target;
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Active Avatars", sync.activeAvatarCount);
                EditorGUILayout.IntField("Encoded Players", sync.encodedPlayerCount);
                EditorGUILayout.IntField("Encoded Bytes", sync.encodedPoseBytes);
                EditorGUILayout.IntField("Skipped Player Entries", sync.skippedPlayerEntryCount);
                EditorGUILayout.IntField("Deferred Root Samples", sync.deferredRootPoseSampleCount);
                EditorGUILayout.IntField("Decoded Players", sync.decodedPlayerCount);
                EditorGUILayout.IntField("Last Error", sync.lastAvatarPoseError);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawPoseMode()
        {
            if (_poseMode == null)
                return;

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = _poseMode.hasMultipleDifferentValues;
            int selected = EditorGUILayout.Popup(PoseModeLabel, Mathf.Clamp(_poseMode.enumValueIndex, 0, PoseModeOptions.Length - 1), PoseModeOptions);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                _poseMode.enumValueIndex = selected;
        }

        private VrchatAvatarPoseMode GetSelectedMode()
        {
            if (_poseMode == null)
                return VrchatAvatarPoseMode.HumanoidBones;

            return (VrchatAvatarPoseMode)_poseMode.enumValueIndex;
        }
    }
}
