using K13A.TSMP.Udon;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkBehaviour), true)]
    [CanEditMultipleObjects]
    public class TSMPNetworkBehaviourEditor : UnityEditor.Editor
    {
        private const float NetworkIdButtonWidth = 22f;
        private static readonly GUIContent NetworkIdLabel = new GUIContent("Network ID");

        private static readonly string[] BasePropertyNames =
        {
            "m_Script",
            "networkId",
            "receiveInterpolation",
            "continuousInterpolationRate",
            "transRpcEncoder",
            "lastVariableHash",
            "lastRpcHash",
            "lastRpcNetworkId",
            "lastRpcArgumentCount",
            "lastRpcMethodName"
        };

        private bool _networkIdUnlocked;

        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();
            DrawTSMPNetworkSection(true);
            DrawRemainingProperties(BasePropertyNames);
            serializedObject.ApplyModifiedProperties();
        }

        protected bool DrawUdonSharpHeader()
        {
            if (!HasUdonSharpTargets())
                return false;

            return UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets);
        }

        protected void DrawTSMPNetworkSection(bool supportsContinuous)
        {
            EditorGUILayout.Space(2f);
            DrawNetworkIdProperty();
            NetworkEditorUtil.DrawReceiveProperties(serializedObject, supportsContinuous);
            UdonSharpGUI.DrawUILine();
        }

        protected void DrawProperty(string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, true);
        }

        private void DrawNetworkIdProperty()
        {
            SerializedProperty property = serializedObject.FindProperty("networkId");
            if (property == null)
                return;

            Rect rect = EditorGUILayout.GetControlRect();
            Rect buttonRect = new Rect(rect.xMax - NetworkIdButtonWidth, rect.y, NetworkIdButtonWidth, rect.height);
            Rect fieldRect = rect;
            fieldRect.xMax = buttonRect.x - 2f;

            using (new EditorGUI.DisabledScope(!_networkIdUnlocked))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                int value = EditorGUI.IntField(fieldRect, NetworkIdLabel, property.intValue);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                    property.intValue = Mathf.Clamp(value, 0, ushort.MaxValue);
            }

            if (GUI.Button(buttonRect, GetNetworkIdLockButtonContent(), GUI.skin.button))
                _networkIdUnlocked = !_networkIdUnlocked;
        }

        private GUIContent GetNetworkIdLockButtonContent()
        {
            GUIContent content = EditorGUIUtility.IconContent(_networkIdUnlocked ? "LockIcon-On" : "LockIcon");
            if (content.image == null)
                content.text = _networkIdUnlocked ? "L" : "U";

            content.tooltip = _networkIdUnlocked ? "Lock Network ID" : "Unlock Network ID";
            return content;
        }

        protected void DrawRemainingProperties(string[] excludedProperties)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (IsExcluded(iterator.propertyPath, excludedProperties))
                    continue;

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        private bool HasUdonSharpTargets()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (!(targets[i] is UdonSharpBehaviour))
                    return false;
            }

            return targets.Length > 0;
        }

        private static bool IsExcluded(string propertyPath, string[] excludedProperties)
        {
            if (excludedProperties == null)
                return false;

            for (int i = 0; i < excludedProperties.Length; i++)
            {
                if (propertyPath == excludedProperties[i])
                    return true;
            }

            return false;
        }
    }

    internal static class NetworkEditorUtil
    {
        public static void DrawReceiveProperties(SerializedObject serializedObject, bool supportsContinuous)
        {
            SerializedProperty mode = serializedObject.FindProperty("receiveInterpolation");
            if (mode == null)
                return;

            EditorGUILayout.PropertyField(mode, true);
            if (mode.enumValueIndex != (int)ReceiveInterpolationMode.Continuous)
                return;

            if (supportsContinuous)
            {
                SerializedProperty rate = serializedObject.FindProperty("continuousInterpolationRate");
                if (rate != null)
                    EditorGUILayout.PropertyField(rate, true);
            }
            else
            {
                EditorGUILayout.HelpBox("Continuous receive mode is not implemented for this component; values are applied on receive.", MessageType.Info);
            }
        }
    }
}
