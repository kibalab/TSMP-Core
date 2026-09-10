using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class CodecBridge
    {
        public static TSMPCodec FindHandler(TSMPCodec[] codecHandlers, int codecId)
        {
            if (codecHandlers == null)
                return null;

            int count = codecHandlers.Length;
            for (int i = 0; i < count; i++)
            {
                TSMPCodec handler = codecHandlers[i];
                if (handler == null)
                    continue;
                if (handler.codecId == codecId)
                    return handler;
            }

            return null;
        }

        public static TSMPCodec PrepareDecodeHandler(
            TSMPCodec[] codecHandlers,
            int codecId,
            byte[] codecOptionBytes,
            int activeWidthBlocks,
            int decodeStage,
            bool payloadInterleaved,
            int byteCount)
        {
            TSMPCodec handler = FindHandler(codecHandlers, codecId);
            if (handler == null)
                return null;

            handler.codecOptionBytes = codecOptionBytes;
            handler.activeWidthBlocks = activeWidthBlocks;
            handler.calibrationStartBlock = Luma4Raster.PayloadStartRow * activeWidthBlocks;
            handler.decodeStage = decodeStage;
            handler.payloadInterleaved = payloadInterleaved;
            handler.byteCount = byteCount;
            handler.ApplyDecodeOptions();
            return handler;
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public static UdonBehaviour ResolveUdonTarget(TSMPCodec selectedCodec, UdonBehaviour selectedCodecUdonTarget)
        {
            if (selectedCodecUdonTarget != null)
                return selectedCodecUdonTarget;

            if (selectedCodec == null)
                return null;

            GameObject selectedObject = selectedCodec.gameObject;
            if (selectedObject == null)
                return null;

            return selectedObject.GetComponent<UdonBehaviour>();
        }

        public static void QueryEncoder(UdonBehaviour codec, int width, int height, int blockSize)
        {
            if (codec == null)
                return;

            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderRequestWidthFieldName, width);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderRequestHeightFieldName, height);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderRequestBlockSizeFieldName, blockSize);
            TSMPBehaviour.SendCustomEvent(codec, TSMPCodec.OnEncoderQueryEventName);
        }

        public static bool WriteEncoderPayload(
            UdonBehaviour codec,
            int width,
            int height,
            int blockSize,
            Color32[] pixels,
            bool pixelsAreBlocks,
            byte[] payloadBytes,
            int payloadByteCount)
        {
            if (codec == null)
                return false;

            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderRequestWidthFieldName, width);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderRequestHeightFieldName, height);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderRequestBlockSizeFieldName, blockSize);
            return WriteEncoderPayloadInternal(codec, pixels, pixelsAreBlocks, payloadBytes, payloadByteCount, true);
        }

        public static bool WritePreparedEncoderPayload(
            UdonBehaviour codec,
            Color32[] pixels,
            bool pixelsAreBlocks,
            byte[] payloadBytes,
            int payloadByteCount)
        {
            return WriteEncoderPayloadInternal(codec, pixels, pixelsAreBlocks, payloadBytes, payloadByteCount, false);
        }

        private static bool WriteEncoderPayloadInternal(
            UdonBehaviour codec,
            Color32[] pixels,
            bool pixelsAreBlocks,
            byte[] payloadBytes,
            int payloadByteCount,
            bool copyReturnedPixels)
        {
            if (codec == null)
                return false;

            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderPixelsFieldName, pixels);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderPixelsAreBlocksFieldName, pixelsAreBlocks);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderPayloadBytesFieldName, payloadBytes);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderPayloadByteCountFieldName, payloadByteCount);
            TSMPBehaviour.SetProgramVariable(codec, TSMPCodec.EncoderWriteResultFieldName, false);
            TSMPBehaviour.SendCustomEvent(codec, TSMPCodec.OnEncoderWritePayloadEventName);
            bool result = GetBool(codec, TSMPCodec.EncoderWriteResultFieldName, false);
            if (result && copyReturnedPixels)
                CopyReturnedPixels(codec, pixels);

            return result;
        }

        public static int GetEncoderCodecId(UdonBehaviour codec, int fallback)
        {
            return GetInt(codec, TSMPCodec.EncoderCodecIdFieldName, fallback);
        }

        public static int GetEncoderSymbolMode(UdonBehaviour codec, int fallback)
        {
            return GetInt(codec, TSMPCodec.EncoderSymbolModeFieldName, fallback);
        }

        public static int GetEncoderPayloadStartRow(UdonBehaviour codec, int fallback)
        {
            return GetInt(codec, TSMPCodec.EncoderPayloadStartRowFieldName, fallback);
        }

        public static int GetEncoderPayloadCapacityBytes(UdonBehaviour codec, int fallback)
        {
            return GetInt(codec, TSMPCodec.EncoderPayloadCapacityBytesFieldName, fallback);
        }

        public static int GetEncoderCodecOptionByteCount(UdonBehaviour codec)
        {
            return GetInt(codec, TSMPCodec.EncoderCodecOptionByteCountFieldName, 0);
        }

        public static int GetEncoderCodecOptionByte(UdonBehaviour codec, int index)
        {
            string fieldName = TSMPCodec.GetEncoderCodecOptionByteFieldName(index);
            if (fieldName == null)
                return 0;

            return GetInt(codec, fieldName, 0);
        }

        public static int GetInt(UdonBehaviour codec, string fieldName, int fallback)
        {
            if (codec == null)
                return fallback;

            object value = TSMPBehaviour.GetProgramVariable(codec, fieldName);
            if (value == null)
                return fallback;

            return (int)value;
        }

        public static bool GetBool(UdonBehaviour codec, string fieldName, bool fallback)
        {
            if (codec == null)
                return fallback;

            object value = TSMPBehaviour.GetProgramVariable(codec, fieldName);
            if (value == null)
                return fallback;

            return (bool)value;
        }

        private static void CopyReturnedPixels(UdonBehaviour codec, Color32[] pixels)
        {
            if (codec == null || pixels == null)
                return;

            object value = TSMPBehaviour.GetProgramVariable(codec, TSMPCodec.EncoderPixelsFieldName);
            if (value == null)
                return;

            Color32[] returnedPixels = (Color32[])value;
            if (returnedPixels == null)
                return;

            int count = returnedPixels.Length < pixels.Length ? returnedPixels.Length : pixels.Length;
            for (int i = 0; i < count; i++)
                pixels[i] = returnedPixels[i];
        }
#endif
    }
}
