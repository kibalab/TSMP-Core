using System;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPGpuOutput))]
    public sealed class TSMPGpuOutputEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (InspectorUI.DrawUdonSharpHeader(targets))
                return;

            serializedObject.Update();
            var input = serializedObject.FindProperty("input");
            EditorGUILayout.PropertyField(input);
            if (input.enumValueIndex == (int)GpuOutputInput.TransSyncField)
            {
                var id = serializedObject.FindProperty("networkId");
                var hash = serializedObject.FindProperty("variableHash");
                EditorGUILayout.PropertyField(id);
                EditorGUILayout.PropertyField(hash);
                var decoder = ((TSMPGpuOutput)target).GetComponentInParent<TSMPDecoder>(true);
                if (decoder != null && decoder.bindingFieldNames != null &&
                    EditorGUILayout.DropdownButton(new GUIContent("Select Decoder Binding"), FocusType.Keyboard))
                    ShowBindings(decoder);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("material"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("destination"));
            serializedObject.ApplyModifiedProperties();
        }

        private void ShowBindings(TSMPDecoder decoder)
        {
            var menu = new GenericMenu();
            int count = Math.Min(decoder.bindingFieldNames.Length,
                Math.Min(decoder.bindingNetworkIds?.Length ?? 0, decoder.bindingVariableHashes?.Length ?? 0));
            if (count == 0)
                menu.AddDisabledItem(new GUIContent("No bindings"));
            for (int i = 0; i < count; i++)
            {
                ushort id = decoder.bindingNetworkIds[i];
                uint hash = decoder.bindingVariableHashes[i];
                menu.AddItem(new GUIContent(id + "/" + decoder.bindingFieldNames[i] + " (" + hash + ")"), false, () =>
                {
                    serializedObject.Update();
                    serializedObject.FindProperty("networkId").intValue = id;
                    serializedObject.FindProperty("variableHash").longValue = hash;
                    serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }
    }
}
