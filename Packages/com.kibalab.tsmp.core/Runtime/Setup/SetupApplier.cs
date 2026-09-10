#if !COMPILER_UDONSHARP
using UnityEngine;

namespace K13A.TSMP
{
    internal static class SetupApplier
    {
        private const string FieldWidth = TSMPEncoder.WidthFieldName;
        private const string FieldHeight = TSMPEncoder.HeightFieldName;
        private const string FieldBlockSize = TSMPEncoder.BlockSizeFieldName;
        private const string FieldSampleSize = TSMPEncoder.SampleSizeFieldName;
        private const string FieldSelectedCodec = TSMPEncoder.SelectedCodecFieldName;
        private const string FieldSelectedCodecUdonTarget = TSMPEncoder.SelectedCodecUdonTargetFieldName;
        private const string FieldPayloadSymbolMode = TSMPEncoder.PayloadSymbolModeFieldName;
        private const string FieldCodecId = TSMPEncoder.CodecIdFieldName;
        private const string FieldOutput = TSMPEncoder.OutputFieldName;
        private const string FieldBlockExpandMaterial = TSMPEncoder.BlockExpandMaterialFieldName;
        private const string FieldBindingTargets = TSMPEncoder.BindingTargetsFieldName;
        private const string FieldBindingUdonTargets = TSMPEncoder.BindingUdonTargetsFieldName;
        private const string FieldBindingNetworkIds = TSMPEncoder.BindingNetworkIdsFieldName;
        private const string FieldBindingVariableHashes = TSMPEncoder.BindingVariableHashesFieldName;
        private const string FieldBindingValueTypes = TSMPEncoder.BindingValueTypesFieldName;
        private const string FieldBindingFieldNames = TSMPEncoder.BindingFieldNamesFieldName;
        private const string FieldBindingDirections = TSMPEncoder.BindingDirectionsFieldName;
        private const string FieldBindingPriorities = Udon.TSMPDecoder.BindingPrioritiesFieldName;
        private const string FieldSourceTexture = Udon.TSMPDecoder.SourceTextureFieldName;
        private const string FieldPayloadByteTexture = Udon.TSMPDecoder.PayloadByteTextureFieldName;
        private const string FieldSourceWidth = Udon.TSMPDecoder.SourceWidthFieldName;
        private const string FieldSourceHeight = Udon.TSMPDecoder.SourceHeightFieldName;
        private const string FieldFlipY = Udon.TSMPDecoder.FlipYFieldName;
        private const string FieldCodecHandlers = Udon.TSMPDecoder.CodecHandlersFieldName;

        public static void ApplyEncoder(Component encoder, FrameLayout layout, int sampleSize, TSMPCodec selectedCodec, RenderTexture encoderOutput, Material blockExpandMaterial)
        {
            if (encoder == null)
                return;

            RecordObject(encoder, "Apply TSMP encoder setup");

            SetField(encoder, FieldWidth, layout.Width);
            SetField(encoder, FieldHeight, layout.Height);
            SetField(encoder, FieldBlockSize, layout.BlockSize);
            SetField(encoder, FieldSampleSize, sampleSize);

            if (selectedCodec != null)
            {
                SetField(encoder, FieldSelectedCodec, selectedCodec);
#if UDONSHARP || COMPILER_UDONSHARP
                SetField(encoder, FieldSelectedCodecUdonTarget, GetBackingUdonBindingTarget(selectedCodec));
#endif
                SetField(encoder, FieldPayloadSymbolMode, (int)selectedCodec.SymbolMode);
                SetField(encoder, FieldCodecId, selectedCodec.codecId);
            }

            if (encoderOutput != null)
                SetField(encoder, FieldOutput, encoderOutput);

            if (blockExpandMaterial != null)
                SetField(encoder, FieldBlockExpandMaterial, blockExpandMaterial);

            ApplyEncoderBindings(encoder);
            MarkDirty(encoder);
            UdonProxySyncBridge.Sync(encoder);
        }

        public static void ApplyDecoder(Component decoder, Texture sourceTexture, RenderTexture payloadByteTexture, FrameLayout layout, int sampleSize, bool flipY, TSMPCodec[] codecHandlers)
        {
            if (decoder == null)
                return;

            RecordObject(decoder, "Apply TSMP decoder setup");

            SetField(decoder, FieldSourceTexture, sourceTexture);
            SetField(decoder, FieldPayloadByteTexture, payloadByteTexture);
            SetField(decoder, FieldSourceWidth, layout.Width);
            SetField(decoder, FieldSourceHeight, layout.Height);
            SetField(decoder, FieldBlockSize, layout.BlockSize);
            SetField(decoder, FieldSampleSize, sampleSize);
            SetField(decoder, FieldFlipY, flipY);
            SetField(decoder, FieldCodecHandlers, codecHandlers);

            ApplyDecoderBindings(decoder);
            MarkDirty(decoder);
            UdonProxySyncBridge.Sync(decoder);
        }

        public static void ApplyMaterials(TSMPCodec[] codecs, CodecMaterialContext context)
        {
            if (codecs == null)
                return;

            for (int i = 0; i < codecs.Length; i++)
            {
                TSMPCodec codec = codecs[i];
                if (codec == null)
                    continue;

                RecordObject(codec, "Apply TSMP codec setup");
                codec.ConfigureMaterials(context);
                MarkCodecMaterialsDirty(codec);
                MarkDirty(codec);
                UdonProxySyncBridge.Sync(codec);
            }
        }

        public static void BlitEncoderOutputInEditor(Component encoder)
        {
#if UNITY_EDITOR
            if (Application.isPlaying || encoder == null)
                return;

            Texture outputTexture = ComponentReflection.GetMemberValue(encoder, TSMPEncoder.OutputTextureMemberName) as Texture;
            RenderTexture output = ComponentReflection.GetMemberValue(encoder, TSMPEncoder.OutputFieldName) as RenderTexture;
            if (outputTexture == null || output == null)
                return;

            Graphics.Blit(outputTexture, output);
#endif
        }

        public static bool GetEncoderAutoEncode(Component encoder)
        {
            return ComponentReflection.GetBoolMember(encoder, TSMPEncoder.AutoEncodeFieldName, false);
        }

        public static int GetEncoderFrameRate(Component encoder)
        {
            return ComponentReflection.GetIntMember(encoder, TSMPEncoder.FrameRateFieldName, 30);
        }

        public static int EstimateEncoderPayloadBytes(Component encoder)
        {
            int payloadBytes = ComponentReflection.GetIntMember(encoder, TSMPEncoder.PayloadBytesMemberName, 0);
            if (payloadBytes > 0)
                return payloadBytes;

            payloadBytes = ComponentReflection.GetIntMember(encoder, TSMPEncoder.PayloadBytesFieldName, 0);
            if (payloadBytes > 0)
                return payloadBytes;

            if (encoder != null)
                return Mathf.Max(256, ComponentReflection.GetIntMember(encoder, TSMPEncoder.MaxPayloadBytesFieldName, 0));

            return 0;
        }

        public static void InvokeEncode(Component encoder)
        {
            ComponentReflection.InvokeMethod(encoder, TSMPEncoder.EncodeNowMethodName);
        }

        private static void ApplyEncoderBindings(Component encoder)
        {
#if UNITY_EDITOR
            TransSyncBindingSnapshot snapshot = TransSyncBindingSnapshotBuilder.Build(false);
            SetField(encoder, FieldBindingTargets, snapshot.Targets);
#if UDONSHARP || COMPILER_UDONSHARP
            SetField(encoder, FieldBindingUdonTargets, snapshot.UdonTargets);
#endif
            SetField(encoder, FieldBindingNetworkIds, snapshot.NetworkIds);
            SetField(encoder, FieldBindingVariableHashes, snapshot.VariableHashes);
            SetField(encoder, FieldBindingValueTypes, snapshot.ValueTypes);
            SetField(encoder, FieldBindingFieldNames, snapshot.FieldNames);
            SetField(encoder, FieldBindingDirections, snapshot.Directions);
#endif
        }

        private static void ApplyDecoderBindings(Component decoder)
        {
#if UNITY_EDITOR
            TransSyncBindingSnapshot snapshot = TransSyncBindingSnapshotBuilder.Build(true);
            SetField(decoder, FieldBindingTargets, snapshot.Targets);
#if UDONSHARP || COMPILER_UDONSHARP
            SetField(decoder, FieldBindingUdonTargets, snapshot.UdonTargets);
#endif
            SetField(decoder, FieldBindingNetworkIds, snapshot.NetworkIds);
            SetField(decoder, FieldBindingVariableHashes, snapshot.VariableHashes);
            SetField(decoder, FieldBindingValueTypes, snapshot.ValueTypes);
            SetField(decoder, FieldBindingFieldNames, snapshot.FieldNames);
            SetField(decoder, FieldBindingDirections, snapshot.Directions);
            SetField(decoder, FieldBindingPriorities, snapshot.Priorities);
#endif
        }

#if UDONSHARP || COMPILER_UDONSHARP
        private static VRC.Udon.UdonBehaviour GetBackingUdonBindingTarget(Component component)
        {
            return ComponentReflection.GetBackingUdonBehaviour(component);
        }

#endif

        private static void MarkCodecMaterialsDirty(TSMPCodec codec)
        {
            if (codec == null)
                return;

            for (int i = 0; i < codec.DecodeMaterialCount; i++)
                MarkDirty(codec.GetDecodeMaterial(i));

            for (int i = 0; i < codec.DebugMaterialCount; i++)
                MarkDirty(codec.GetDebugMaterial(i));
        }

        private static void SetField(Component target, string fieldName, object value)
        {
            ComponentReflection.SetField(target, fieldName, value, "[TSMP Setup]");
        }

        private static void RecordObject(Object target, string name)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && target != null)
                UnityEditor.Undo.RecordObject(target, name);
#endif
        }

        private static void MarkDirty(Object target)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && target != null)
                UnityEditor.EditorUtility.SetDirty(target);
#endif
        }
    }
}
#endif
