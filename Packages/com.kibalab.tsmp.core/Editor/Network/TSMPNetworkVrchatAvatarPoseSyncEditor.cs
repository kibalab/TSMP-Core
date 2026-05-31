using K13A.TSMP.Udon;
using UdonSharpEditor;
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
