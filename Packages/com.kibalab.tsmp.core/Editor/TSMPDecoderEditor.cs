using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPDecoder))]
    [CanEditMultipleObjects]
    public sealed class TSMPDecoderEditor : UnityEditor.Editor
    {
        private static readonly GUIContent[] DecodeSafetyLabels =
        {
            new GUIContent("Off"),
            new GUIContent("Header Only"),
            new GUIContent("Payload Readback Only"),
            new GUIContent("Parse Only")
        };

        private static readonly int[] DecodeSafetyValues = { 0, 1, 2, 3 };

        private SerializedProperty _sourceTexture;
        private SerializedProperty _payloadByteTexture;
        private SerializedProperty _codecHandlers;
        private SerializedProperty _applyEveryFrame;
        private SerializedProperty _validateBindingCacheOnReceive;
        private SerializedProperty _skipDuplicateFrames;
        private SerializedProperty _frameWindowSize;
        private SerializedProperty _blockSize;
        private SerializedProperty _sampleSize;
        private SerializedProperty _flipY;
        private SerializedProperty _useHeaderPayloadLayout;
        private SerializedProperty _payloadBytesOverride;
        private SerializedProperty _decodeSafetyMode;
        private SerializedProperty _usePredictedReadback;
        private SerializedProperty _useCombinedByteOutput;
        private SerializedProperty _overlapReadbacks;
        private SerializedProperty _debugLog;
        private SerializedProperty _debugErrorLogBudget;

        private bool _showCodecTable;
        private bool _showAdvanced;
        private bool _showRuntimeStatus;

        private void OnEnable()
        {
            _sourceTexture = serializedObject.FindProperty("sourceTexture");
            _payloadByteTexture = serializedObject.FindProperty("payloadByteTexture");
            _codecHandlers = serializedObject.FindProperty("codecHandlers");
            _applyEveryFrame = serializedObject.FindProperty("applyEveryFrame");
            _validateBindingCacheOnReceive = serializedObject.FindProperty("validateBindingCacheOnReceive");
            _skipDuplicateFrames = serializedObject.FindProperty("skipDuplicateFrames");
            _frameWindowSize = serializedObject.FindProperty("frameWindowSize");
            _blockSize = serializedObject.FindProperty("blockSize");
            _sampleSize = serializedObject.FindProperty("sampleSize");
            _flipY = serializedObject.FindProperty("flipY");
            _useHeaderPayloadLayout = serializedObject.FindProperty("useHeaderPayloadLayout");
            _payloadBytesOverride = serializedObject.FindProperty("payloadBytesOverride");
            _decodeSafetyMode = serializedObject.FindProperty("decodeSafetyMode");
            _usePredictedReadback = serializedObject.FindProperty("usePredictedReadback");
            _useCombinedByteOutput = serializedObject.FindProperty("useCombinedByteOutput");
            _overlapReadbacks = serializedObject.FindProperty("overlapReadbacks");
            _debugLog = serializedObject.FindProperty("debugLog");
            _debugErrorLogBudget = serializedObject.FindProperty("debugErrorLogBudget");
        }

        public override void OnInspectorGUI()
        {
            if (InspectorUI.DrawUdonSharpHeader(targets))
                return;

            serializedObject.Update();

            DrawReferenceSection();
            DrawLayoutSection();
            DrawApplicationSection();
            DrawDiagnosticsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReferenceSection()
        {
            InspectorUI.BeginSection("Reference");
            InspectorUI.Property(_sourceTexture);
            InspectorUI.Property(_payloadByteTexture);
            var channel = serializedObject.FindProperty("shaderChannel");
            InspectorUI.Property(channel);
            if (targets.Length == 1 && channel.enumValueIndex > 0 &&
                EditorGUILayout.DropdownButton(new GUIContent("Copy Shader Lookup"), FocusType.Keyboard))
                ShowShaderLookups((TSMPDecoder)target, channel.enumValueIndex - 1);

            if (targets.Length == 1)
                InspectorUI.ReadOnlyText("Source Size", GetSourceSizeLabel());

            int codecCount = _codecHandlers == null || !_codecHandlers.isArray ? 0 : _codecHandlers.arraySize;
            _showCodecTable = InspectorUI.Foldout(_showCodecTable, "Codec Runtime Table (" + codecCount + ")");
            if (_showCodecTable)
                InspectorUI.Property(_codecHandlers);

            InspectorUI.EndSection();
        }

        private void DrawApplicationSection()
        {
            InspectorUI.BeginSection("Decode");
            InspectorUI.Property(_applyEveryFrame);
            InspectorUI.Property(_validateBindingCacheOnReceive);
            EditorGUILayout.PropertyField(_skipDuplicateFrames, new GUIContent("Filter Frames", "Skip duplicate and older frames, with a frame-zero restart exception based on Window Size."));
            using (new EditorGUI.DisabledScope(!_skipDuplicateFrames.hasMultipleDifferentValues && !_skipDuplicateFrames.boolValue))
                EditorGUILayout.PropertyField(_frameWindowSize, new GUIContent("Window Size", "Frame count used to recognize a restart at frame zero. Default: 256."));

            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                if (GUILayout.Button("Decode Now"))
                    ((TSMPDecoder)target).DecodeNow();
            }
            InspectorUI.EndSection();
        }

        private void DrawLayoutSection()
        {
            InspectorUI.BeginSection("Frame Layout");
            InspectorUI.Property(_useHeaderPayloadLayout);
            InspectorUI.Property(_blockSize);
            InspectorUI.Property(_sampleSize);
            InspectorUI.Property(_flipY);
            InspectorUI.EndSection();
        }

        private void DrawDiagnosticsSection()
        {
            InspectorUI.BeginSection("Diagnostics");
            InspectorUI.Property(_debugLog);
            InspectorUI.Property(_debugErrorLogBudget);

            if (targets.Length == 1)
            {
                TSMPDecoder decoder = (TSMPDecoder)target;
                InspectorUI.ReadOnlyText("Last Error", decoder.lastError);
                InspectorUI.ReadOnlyBool("Frame Valid", decoder.lastFrameValid);
                InspectorUI.ReadOnlyBool("Header Valid", decoder.lastHeaderValid);
                InspectorUI.ReadOnlyUInt("Frame Index", decoder.lastFrameIndex);
                InspectorUI.ReadOnlyInt("Payload Bytes", decoder.lastPayloadSizeFromHeader);
                InspectorUI.ReadOnlyInt("Messages", decoder.lastNetworkMessageCount);
                InspectorUI.ReadOnlyInt("RPC Calls", decoder.lastRpcCallCount);
            }

            _showAdvanced = InspectorUI.Foldout(_showAdvanced, "Advanced Decode");
            if (_showAdvanced)
                DrawAdvancedDecode();

            if (targets.Length == 1)
            {
                _showRuntimeStatus = InspectorUI.Foldout(_showRuntimeStatus, "Runtime Status");
                if (_showRuntimeStatus)
                    DrawRuntimeStatus((TSMPDecoder)target);
            }

            InspectorUI.EndSection();
        }

        private void DrawAdvancedDecode()
        {
            InspectorUI.Property(_usePredictedReadback);
            InspectorUI.Property(_useCombinedByteOutput);
            InspectorUI.Property(_overlapReadbacks);
            InspectorUI.Property(_payloadBytesOverride);
            if (_decodeSafetyMode != null)
                EditorGUILayout.IntPopup(_decodeSafetyMode, DecodeSafetyLabels, DecodeSafetyValues);
        }

        private static void DrawRuntimeStatus(TSMPDecoder decoder)
        {
            InspectorUI.ReadOnlyInt("Predicted Readbacks", decoder.predictedReadbackCount);
            InspectorUI.ReadOnlyInt("Combined Byte Outputs", decoder.combinedByteOutputCount);
            InspectorUI.ReadOnlyInt("Pending Frames", decoder.pendingDecodeCount);
            InspectorUI.ReadOnlyInt("Skipped Busy Captures", decoder.skippedBusyDecodeCount);
            InspectorUI.ReadOnlyInt("Prediction Fallbacks", decoder.predictionFallbackCount);
            InspectorUI.ReadOnlyBool("Readback In Flight", decoder.readbackInFlight);
            InspectorUI.ReadOnlyInt("Payload Bytes", decoder.lastPayloadSizeFromHeader);
            InspectorUI.ReadOnlyInt("Available Payload Bytes", decoder.lastPayloadAvailableBytes);
            InspectorUI.ReadOnlyInt("Requested Bytes", decoder.lastRequestedByteCount);
            InspectorUI.ReadOnlyInt("Messages", decoder.lastNetworkMessageCount);
            InspectorUI.ReadOnlyInt("Applied Variables", decoder.lastAppliedVariableCount);
            InspectorUI.ReadOnlyInt("RPC Calls", decoder.lastRpcCallCount);
            InspectorUI.ReadOnlyInt("Skipped Duplicate Frames", decoder.skippedDuplicateFrameCount);
            InspectorUI.ReadOnlyInt("Skipped Older Frames", decoder.skippedOutOfOrderFrameCount);
            InspectorUI.ReadOnlyText("Last RPC", decoder.lastRpcMethodName);
        }

        private static void ShowShaderLookups(TSMPDecoder decoder, int channel)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("NetworkFrame"), false,
                () => EditorGUIUtility.systemCopyBuffer = $"TSMPTryGetFrame({channel}u, view)");
            int count = Mathf.Min(decoder.bindingFieldNames?.Length ?? 0, decoder.bindingNetworkIds?.Length ?? 0);
            count = Mathf.Min(count, decoder.bindingVariableHashes?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                string field = decoder.bindingFieldNames[i];
                if (string.IsNullOrEmpty(field))
                    continue;
                ushort id = decoder.bindingNetworkIds[i];
                uint hash = decoder.bindingVariableHashes[i];
                menu.AddItem(new GUIContent($"Fields/{id}/{field} (0x{hash:X8})"), false,
                    () => EditorGUIUtility.systemCopyBuffer = $"TSMPTryGetField({channel}u, {id}u, 0x{hash:X8}u, view)");
            }
            menu.ShowAsContext();
        }

        private string GetSourceSizeLabel()
        {
            Texture texture = _sourceTexture != null ? _sourceTexture.objectReferenceValue as Texture : null;
            if (texture == null)
                return string.Empty;

            return texture.width + " x " + texture.height;
        }
    }
}
