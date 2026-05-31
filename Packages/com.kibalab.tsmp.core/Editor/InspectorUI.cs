using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    internal static class InspectorUI
    {
        public static bool DrawUdonSharpHeader(Object[] targets)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (!(targets[i] is UdonSharpBehaviour))
                    return false;
            }

            return targets.Length > 0 && UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets);
        }

        public static void BeginSection(string title)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect titleRect = EditorGUILayout.GetControlRect(false, 20f);
            titleRect.xMin += 2f;
            EditorGUI.LabelField(titleRect, title, EditorStyles.boldLabel);
        }

        public static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        public static void Property(SerializedProperty property)
        {
            if (property != null)
                EditorGUILayout.PropertyField(property, true);
        }

        public static void ReadOnlyInt(string label, int value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(label, value);
            }
        }

        public static void ReadOnlyUInt(string label, uint value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LongField(label, value);
            }
        }

        public static void ReadOnlyBool(string label, bool value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(label, value);
            }
        }

        public static void ReadOnlyText(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, string.IsNullOrEmpty(value) ? string.Empty : value);
            }
        }

        public static bool Foldout(bool value, string label)
        {
            EditorGUILayout.Space(2f);
            EditorGUI.indentLevel++;
            bool nextValue = EditorGUILayout.Foldout(value, label, true);
            EditorGUI.indentLevel--;
            return nextValue;
        }
    }
}
