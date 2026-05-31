using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPEncoder))]
    [CanEditMultipleObjects]
    public sealed class TSMPEncoderEditor : UnityEditor.Editor
    {
        private SerializedProperty _networkBehaviours;
        private SerializedProperty _output;
        private SerializedProperty _blockExpandMaterial;
        private SerializedProperty _frameRate;
        private SerializedProperty _blockSize;
        private SerializedProperty _sampleSize;
        private SerializedProperty _autoEncode;
        private SerializedProperty _clearAfterEncode;
        private SerializedProperty _useBlockSymbolTexture;
        private SerializedProperty _transRpcRepeatFrames;
        private SerializedProperty _selectedCodec;
        private SerializedProperty _codecId;
        private SerializedProperty _maxPayloadBytes;
        private SerializedProperty _streamId;
        private SerializedProperty _layoutId;
        private SerializedProperty _autoBuildVariablesFromBindings;
        private SerializedProperty _debugLog;
        private SerializedProperty _debugErrorLogBudget;

        private bool _showAdvanced;
        private bool _showExplicitBehaviours;
        private bool _showRuntimeStatus;

        private void OnEnable()
        {
            _networkBehaviours = serializedObject.FindProperty("networkBehaviours");
            _output = serializedObject.FindProperty("output");
            _blockExpandMaterial = serializedObject.FindProperty("blockExpandMaterial");
            _frameRate = serializedObject.FindProperty("frameRate");
            _blockSize = serializedObject.FindProperty("blockSize");
            _sampleSize = serializedObject.FindProperty("sampleSize");
            _autoEncode = serializedObject.FindProperty("autoEncode");
            _clearAfterEncode = serializedObject.FindProperty("clearAfterEncode");
            _useBlockSymbolTexture = serializedObject.FindProperty("useBlockSymbolTexture");
            _transRpcRepeatFrames = serializedObject.FindProperty("transRpcRepeatFrames");
            _selectedCodec = serializedObject.FindProperty("selectedCodec");
            _codecId = serializedObject.FindProperty("codecId");
            _maxPayloadBytes = serializedObject.FindProperty("maxPayloadBytes");
            _streamId = serializedObject.FindProperty("streamId");
            _layoutId = serializedObject.FindProperty("layoutId");
            _autoBuildVariablesFromBindings = serializedObject.FindProperty("autoBuildVariablesFromBindings");
            _debugLog = serializedObject.FindProperty("debugLog");
            _debugErrorLogBudget = serializedObject.FindProperty("debugErrorLogBudget");
        }

        public override void OnInspectorGUI()
        {
            if (InspectorUI.DrawUdonSharpHeader(targets))
                return;

            serializedObject.Update();

            DrawReferenceSection();
            DrawEncodeSection();
            DrawLayoutSection();
            DrawNetworkSection();
            DrawDiagnosticsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReferenceSection()
        {
            InspectorUI.BeginSection("Reference");
            InspectorUI.Property(_output);
            InspectorUI.Property(_selectedCodec);

            if (targets.Length == 1)
                InspectorUI.ReadOnlyText("Output Size", GetOutputSizeLabel());

            InspectorUI.EndSection();
        }

        private void DrawEncodeSection()
        {
            InspectorUI.BeginSection("Encode");
            InspectorUI.Property(_autoEncode);
            InspectorUI.Property(_frameRate);
            InspectorUI.Property(_maxPayloadBytes);

            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                if (GUILayout.Button("Encode Now"))
                    ((TSMPEncoder)target).EncodeNow();
            }
            InspectorUI.EndSection();
        }

        private void DrawLayoutSection()
        {
            InspectorUI.BeginSection("Frame Layout");
            InspectorUI.Property(_blockSize);
            InspectorUI.Property(_sampleSize);
            InspectorUI.EndSection();
        }

        private void DrawNetworkSection()
        {
            InspectorUI.BeginSection("Network");
            InspectorUI.Property(_streamId);
            InspectorUI.Property(_layoutId);
            InspectorUI.Property(_autoBuildVariablesFromBindings);
            InspectorUI.Property(_transRpcRepeatFrames);

            if (_networkBehaviours != null)
            {
                _showExplicitBehaviours = InspectorUI.Foldout(_showExplicitBehaviours, "Explicit Network Behaviours");
                if (_showExplicitBehaviours)
                    EditorGUILayout.PropertyField(_networkBehaviours, true);
            }

            if (targets.Length == 1)
            {
                TSMPEncoder encoder = (TSMPEncoder)target;
                InspectorUI.ReadOnlyInt("Binding Count", encoder.bindingCount);
                InspectorUI.ReadOnlyInt("Auto Variables", encoder.autoVariableCount);
            }
            InspectorUI.EndSection();
        }

        private void DrawDiagnosticsSection()
        {
            InspectorUI.BeginSection("Diagnostics");
            InspectorUI.Property(_debugLog);
            InspectorUI.Property(_debugErrorLogBudget);

            if (targets.Length == 1)
            {
                TSMPEncoder encoder = (TSMPEncoder)target;
                InspectorUI.ReadOnlyText("Last Error", encoder.lastError);
                DrawFrameIndex(encoder);
                InspectorUI.ReadOnlyInt("Payload Bytes", encoder.payloadBytes);
                InspectorUI.ReadOnlyInt("Usable Payload Bytes", encoder.usablePayloadBytes);
                InspectorUI.ReadOnlyInt("Messages", encoder.messageCount);
            }

            _showAdvanced = InspectorUI.Foldout(_showAdvanced, "Advanced Rendering");
            if (_showAdvanced)
            {
                InspectorUI.Property(_codecId);
                InspectorUI.Property(_blockExpandMaterial);
                InspectorUI.Property(_clearAfterEncode);
                InspectorUI.Property(_useBlockSymbolTexture);
            }

            if (targets.Length == 1)
            {
                _showRuntimeStatus = InspectorUI.Foldout(_showRuntimeStatus, "Runtime Status");
                if (_showRuntimeStatus)
                    DrawRuntimeStatus((TSMPEncoder)target);
            }

            InspectorUI.EndSection();
        }

        private void DrawFrameIndex(TSMPEncoder encoder)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LongField("Frame Index", encoder.frameIndex);
            }

            if (GUILayout.Button("Reset", GUILayout.Width(56f)))
                ResetFrameIndexes();
            EditorGUILayout.EndHorizontal();
        }

        private void ResetFrameIndexes()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                TSMPEncoder encoder = targets[i] as TSMPEncoder;
                if (encoder == null)
                    continue;

                Undo.RecordObject(encoder, "Reset TSMP frame index");
                encoder.ResetFrameIndex();
                EditorUtility.SetDirty(encoder);
            }
        }

        private static void DrawRuntimeStatus(TSMPEncoder encoder)
        {
            InspectorUI.ReadOnlyInt("Usable Payload Bytes", encoder.usablePayloadBytes);
            InspectorUI.ReadOnlyInt("Variable Messages", encoder.variableMessageCount);
            InspectorUI.ReadOnlyInt("RPC Messages", encoder.rpcMessageCount);
            InspectorUI.ReadOnlyInt("Queued RPCs", encoder.queuedRpcCount);
            InspectorUI.ReadOnlyInt("Encoded Objects", encoder.encodedObjectCount);
            InspectorUI.ReadOnlyUInt("Frame Index", encoder.frameIndex);
            InspectorUI.ReadOnlyInt("Last Stage", encoder.lastEncodeStage);
            InspectorUI.ReadOnlyInt("Symbol Texture Width", encoder.symbolTextureWidth);
            InspectorUI.ReadOnlyInt("Symbol Texture Height", encoder.symbolTextureHeight);
        }

        private string GetOutputSizeLabel()
        {
            RenderTexture renderTexture = _output != null ? _output.objectReferenceValue as RenderTexture : null;
            if (renderTexture == null)
                return string.Empty;

            return renderTexture.width + " x " + renderTexture.height;
        }
    }
}
