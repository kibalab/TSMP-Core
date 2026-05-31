using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkBlendShapesSync))]
    [CanEditMultipleObjects]
    public sealed class TSMPNetworkBlendShapesSyncEditor : TSMPNetworkBehaviourEditor
    {
        private string _search = string.Empty;
        private Vector2 _scroll;

        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();

            DrawTSMPNetworkSection(true);
            DrawProperty("targetRenderer");

            SerializedProperty encodedCount = serializedObject.FindProperty("encodedBlendShapeCount");
            using (new EditorGUI.DisabledScope(true))
            {
                if (encodedCount != null)
                    EditorGUILayout.PropertyField(encodedCount);
            }

            serializedObject.ApplyModifiedProperties();

            TSMPNetworkBlendShapesSync sync = (TSMPNetworkBlendShapesSync)target;
            SkinnedMeshRenderer renderer = sync.targetRenderer;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;

            if (mesh == null)
            {
                serializedObject.Update();
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("blendShapeIndices"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("blendShapeNames"), true);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (sync.blendShapeCount != mesh.blendShapeCount)
            {
                Undo.RecordObject(sync, "Refresh BlendShape Count");
                sync.blendShapeCount = mesh.blendShapeCount;
                EditorUtility.SetDirty(sync);
            }

            if (sync.blendShapeIndices != null && sync.blendShapeIndices.Length > 0 && sync.blendShapeCount <= 0)
                EditorGUILayout.HelpBox("BlendShape count is not cached. The editor will refresh it from the assigned mesh before upload.", MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("BlendShapes", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(48f)))
                    SelectAll(sync, mesh, true);
                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(54f)))
                    SelectAll(sync, mesh, false);
            }

            if (mesh.blendShapeCount == 0)
            {
                EditorGUILayout.HelpBox("The selected mesh has no blendshapes.", MessageType.Info);
                return;
            }

            _search = EditorGUILayout.TextField("Search", _search);
            int selectedCount = sync.blendShapeIndices != null ? sync.blendShapeIndices.Length : 0;
            EditorGUILayout.LabelField(selectedCount + " / " + mesh.blendShapeCount + " selected", EditorStyles.miniLabel);

            float listHeight = Mathf.Min(320f, Mathf.Max(96f, mesh.blendShapeCount * 21f));
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(listHeight)))
            {
                _scroll = scroll.scrollPosition;
                DrawBlendShapeList(sync, mesh);
            }
        }

        private void DrawBlendShapeList(TSMPNetworkBlendShapesSync sync, Mesh mesh)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string shapeName = mesh.GetBlendShapeName(i);
                if (!MatchesSearch(i, shapeName))
                    continue;

                bool selected = ContainsIndex(sync.blendShapeIndices, i);
                string label = i + "  " + shapeName;
                bool nextSelected = EditorGUILayout.ToggleLeft(label, selected);
                if (nextSelected == selected)
                    continue;

                Undo.RecordObject(sync, "Change TSMP BlendShape Selection");
                SetBlendShapeSelection(sync, mesh, i, nextSelected);
                EditorUtility.SetDirty(sync);
            }
        }

        private static bool ContainsIndex(int[] indices, int index)
        {
            if (indices == null)
                return false;

            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] == index)
                    return true;
            }

            return false;
        }

        private static void SetBlendShapeSelection(TSMPNetworkBlendShapesSync sync, Mesh mesh, int changedIndex, bool selected)
        {
            int newCount = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                bool enabled = i == changedIndex ? selected : ContainsIndex(sync.blendShapeIndices, i);
                if (enabled)
                    newCount++;
            }

            int[] indices = new int[newCount];
            string[] names = new string[newCount];
            int cursor = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                bool enabled = i == changedIndex ? selected : ContainsIndex(sync.blendShapeIndices, i);
                if (!enabled)
                    continue;

                indices[cursor] = i;
                names[cursor] = mesh.GetBlendShapeName(i);
                cursor++;
            }

            sync.blendShapeIndices = indices;
            sync.blendShapeNames = names;
            sync.blendShapeCount = mesh.blendShapeCount;
        }

        private static void SelectAll(TSMPNetworkBlendShapesSync sync, Mesh mesh, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP BlendShape Selection");

            if (!selected)
            {
                sync.blendShapeIndices = new int[0];
                sync.blendShapeNames = new string[0];
                sync.blendShapeCount = mesh.blendShapeCount;
                EditorUtility.SetDirty(sync);
                return;
            }

            int count = Mathf.Min(mesh.blendShapeCount, 255);
            int[] indices = new int[count];
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
                names[i] = mesh.GetBlendShapeName(i);
            }

            sync.blendShapeIndices = indices;
            sync.blendShapeNames = names;
            sync.blendShapeCount = mesh.blendShapeCount;
            EditorUtility.SetDirty(sync);
        }

        private bool MatchesSearch(int index, string shapeName)
        {
            if (string.IsNullOrEmpty(_search))
                return true;

            if (index.ToString().Contains(_search))
                return true;

            return !string.IsNullOrEmpty(shapeName) && shapeName.ToLowerInvariant().Contains(_search.ToLowerInvariant());
        }
    }
}
