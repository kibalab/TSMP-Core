using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkTransformSync))]
    [CanEditMultipleObjects]
    public sealed class TSMPNetworkTransformSyncEditor : TSMPNetworkBehaviourEditor
    {
        private readonly BoxBoundsHandle _positionBoundsHandle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();

            DrawTSMPNetworkSection(true);
            DrawProperty("target");
            DrawProperty("useLocalSpace");

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Rigidbody", EditorStyles.boldLabel);
            DrawProperty("syncRigidbody");

            SerializedProperty syncRigidbodyProperty = serializedObject.FindProperty("syncRigidbody");
            bool shouldSyncRigidbody = syncRigidbodyProperty != null && syncRigidbodyProperty.boolValue;
            using (new EditorGUI.DisabledScope(!shouldSyncRigidbody))
                DrawProperty("targetRigidbody");

            TSMPNetworkTransformSync sync = (TSMPNetworkTransformSync)target;
            Rigidbody resolved = ResolveRigidbody(sync);
            if (shouldSyncRigidbody && resolved == null)
            {
                EditorGUILayout.HelpBox("No Rigidbody is assigned or found on the target.", MessageType.Info);
            }
            else if (shouldSyncRigidbody && sync.targetRigidbody == null && resolved != null)
            {
                EditorGUILayout.HelpBox("A Rigidbody on the target will be used automatically.", MessageType.None);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Compression", EditorStyles.boldLabel);
            DrawProperty("compressionMode");

            SerializedProperty compressionMode = serializedObject.FindProperty("compressionMode");
            bool showQuantizedRanges = compressionMode != null && compressionMode.enumValueIndex == (int)CompressionMode.Full;
            if (showQuantizedRanges)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Quantized Position Range", EditorStyles.miniBoldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawProperty("quantizedPositionMin");
                    DrawProperty("quantizedPositionMax");
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Quantized Scale Range", EditorStyles.miniBoldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawProperty("quantizedScaleMin");
                    DrawProperty("quantizedScaleMax");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            TSMPNetworkTransformSync sync = (TSMPNetworkTransformSync)target;
            if (sync.compressionMode != CompressionMode.Full)
                return;

            Transform handleTransform = sync.useLocalSpace && sync.target != null ? sync.target : null;
            Matrix4x4 matrix = handleTransform != null ? handleTransform.localToWorldMatrix : Matrix4x4.identity;

            Vector3 min = sync.quantizedPositionMin;
            Vector3 max = GetSafeMax(sync.quantizedPositionMin, sync.quantizedPositionMax);
            _positionBoundsHandle.center = (min + max) * 0.5f;
            _positionBoundsHandle.size = max - min;

            using (new Handles.DrawingScope(Color.cyan, matrix))
            {
                EditorGUI.BeginChangeCheck();
                _positionBoundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(sync, "Change TSMP Transform Range");
                    Vector3 center = _positionBoundsHandle.center;
                    Vector3 size = Abs(_positionBoundsHandle.size);
                    sync.quantizedPositionMin = center - size * 0.5f;
                    sync.quantizedPositionMax = center + size * 0.5f;
                    EditorUtility.SetDirty(sync);
                }
            }

            Handles.Label(matrix.MultiplyPoint(_positionBoundsHandle.center), "TSMP Position Range");
        }

        private static Rigidbody ResolveRigidbody(TSMPNetworkTransformSync sync)
        {
            if (sync == null)
                return null;

            if (sync.targetRigidbody != null)
                return sync.targetRigidbody;

            Transform source = sync.target != null ? sync.target : sync.transform;
            return source != null ? source.GetComponent<Rigidbody>() : null;
        }

        private static Vector3 GetSafeMax(Vector3 min, Vector3 max)
        {
            return new Vector3(
                max.x > min.x ? max.x : min.x + 0.0001f,
                max.y > min.y ? max.y : min.y + 0.0001f,
                max.z > min.z ? max.z : min.z + 0.0001f);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }

}
