using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkAnimatorSync))]
    [CanEditMultipleObjects]
    public sealed class TSMPNetworkAnimatorSyncEditor : TSMPNetworkBehaviourEditor
    {
        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();

            DrawTSMPNetworkSection(false);
            DrawProperty("animator");

            TSMPNetworkAnimatorSync sync = (TSMPNetworkAnimatorSync)target;
            Animator animator = sync.animator != null ? sync.animator : sync.GetComponent<Animator>();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            if (animator == null)
            {
                EditorGUILayout.HelpBox("Assign an Animator to select parameters.", MessageType.Info);
                DrawProperty("parameterNames");
                DrawProperty("parameterTypes");
            }
            else
            {
                DrawParameterPicker(sync, animator);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            if (animator == null)
            {
                DrawProperty("layerIndices");
            }
            else
            {
                DrawLayerPicker(sync, animator);
            }

            DrawProperty("layerFadeDuration");
            DrawProperty("normalizedTimeApplyThreshold");

            using (new EditorGUI.DisabledScope(true))
            {
                DrawProperty("encodedParameterCount");
                DrawProperty("encodedLayerCount");
                DrawProperty("encodedAnimatorBytes");
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawParameterPicker(TSMPNetworkAnimatorSync sync, Animator animator)
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            if (parameters == null || parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("Animator has no parameters.", MessageType.None);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(48f)))
                SetAllParameters(sync, parameters, true);
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(56f)))
                SetAllParameters(sync, parameters, false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                byte type = ConvertParameterType(parameter.type);
                using (new EditorGUI.DisabledScope(type == 0))
                {
                    bool selected = ContainsParameter(sync, parameter.name);
                    GUIContent label = new GUIContent(parameter.name, type == 0 ? "Trigger parameters cannot be sampled from Animator." : parameter.type.ToString());
                    bool next = EditorGUILayout.ToggleLeft(label, selected);
                    if (next != selected)
                        SetParameter(sync, parameter.name, type, next);
                }
            }
        }

        private static void DrawLayerPicker(TSMPNetworkAnimatorSync sync, Animator animator)
        {
            int layerCount = animator.layerCount;
            if (layerCount <= 0)
            {
                EditorGUILayout.HelpBox("Animator has no layers.", MessageType.None);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(48f)))
                SetAllLayers(sync, animator, true);
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(56f)))
                SetAllLayers(sync, animator, false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < layerCount; i++)
            {
                bool selected = ContainsLayer(sync.layerIndices, i);
                bool next = EditorGUILayout.ToggleLeft(new GUIContent(animator.GetLayerName(i), "Sync current state, normalized time, and layer weight."), selected);
                if (next != selected)
                    SetLayer(sync, i, next);
            }
        }

        private static byte ConvertParameterType(AnimatorControllerParameterType type)
        {
            if (type == AnimatorControllerParameterType.Float)
                return (byte)AnimatorParameterType.Float;
            if (type == AnimatorControllerParameterType.Int)
                return (byte)AnimatorParameterType.Int;
            if (type == AnimatorControllerParameterType.Bool)
                return (byte)AnimatorParameterType.Bool;
            return 0;
        }

        private static bool ContainsParameter(TSMPNetworkAnimatorSync sync, string name)
        {
            if (sync.parameterNames == null)
                return false;

            for (int i = 0; i < sync.parameterNames.Length; i++)
            {
                if (sync.parameterNames[i] == name)
                    return true;
            }

            return false;
        }

        private static void SetParameter(TSMPNetworkAnimatorSync sync, string name, byte type, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP Animator Parameter Selection");

            int currentLength = sync.parameterNames != null ? sync.parameterNames.Length : 0;
            bool exists = ContainsParameter(sync, name);
            if (selected == exists)
                return;

            int nextLength = selected ? currentLength + 1 : Mathf.Max(0, currentLength - 1);
            string[] nextNames = new string[nextLength];
            byte[] nextTypes = new byte[nextLength];
            int cursor = 0;

            for (int i = 0; i < currentLength; i++)
            {
                string currentName = sync.parameterNames[i];
                if (!selected && currentName == name)
                    continue;

                if (cursor < nextLength)
                {
                    nextNames[cursor] = currentName;
                    nextTypes[cursor] = sync.parameterTypes != null && i < sync.parameterTypes.Length ? sync.parameterTypes[i] : (byte)0;
                    cursor++;
                }
            }

            if (selected && cursor < nextLength)
            {
                nextNames[cursor] = name;
                nextTypes[cursor] = type;
            }

            sync.parameterNames = nextNames;
            sync.parameterTypes = nextTypes;
            EditorUtility.SetDirty(sync);
        }

        private static void SetAllParameters(TSMPNetworkAnimatorSync sync, AnimatorControllerParameter[] parameters, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP Animator Parameter Selection");

            if (!selected)
            {
                sync.parameterNames = new string[0];
                sync.parameterTypes = new byte[0];
                EditorUtility.SetDirty(sync);
                return;
            }

            int count = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (ConvertParameterType(parameters[i].type) != 0)
                    count++;
            }

            string[] names = new string[count];
            byte[] types = new byte[count];
            int cursor = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                byte type = ConvertParameterType(parameters[i].type);
                if (type == 0)
                    continue;

                names[cursor] = parameters[i].name;
                types[cursor] = type;
                cursor++;
            }

            sync.parameterNames = names;
            sync.parameterTypes = types;
            EditorUtility.SetDirty(sync);
        }

        private static bool ContainsLayer(int[] layers, int layer)
        {
            if (layers == null)
                return false;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == layer)
                    return true;
            }

            return false;
        }

        private static void SetLayer(TSMPNetworkAnimatorSync sync, int layer, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP Animator Layer Selection");

            int currentLength = sync.layerIndices != null ? sync.layerIndices.Length : 0;
            bool exists = ContainsLayer(sync.layerIndices, layer);
            if (selected == exists)
                return;

            int nextLength = selected ? currentLength + 1 : Mathf.Max(0, currentLength - 1);
            int[] next = new int[nextLength];
            int cursor = 0;
            for (int i = 0; i < currentLength; i++)
            {
                int current = sync.layerIndices[i];
                if (!selected && current == layer)
                    continue;

                if (cursor < nextLength)
                    next[cursor++] = current;
            }

            if (selected && cursor < nextLength)
                next[cursor] = layer;

            sync.layerIndices = next;
            EditorUtility.SetDirty(sync);
        }

        private static void SetAllLayers(TSMPNetworkAnimatorSync sync, Animator animator, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP Animator Layer Selection");

            if (!selected)
            {
                sync.layerIndices = new int[0];
                EditorUtility.SetDirty(sync);
                return;
            }

            int[] layers = new int[animator.layerCount];
            for (int i = 0; i < layers.Length; i++)
                layers[i] = i;

            sync.layerIndices = layers;
            EditorUtility.SetDirty(sync);
        }
    }
}
