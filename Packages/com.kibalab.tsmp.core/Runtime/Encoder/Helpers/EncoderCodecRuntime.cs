using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class EncoderCodecRuntime
    {
        public const int QueryValueCount = 10;
        public const int QueryCodecId = 0;
        public const int QuerySymbolMode = 1;
        public const int QueryPayloadStartRow = 2;
        public const int QueryPayloadCapacityBytes = 3;
        public const int QueryOptionByteCount = 4;
        public const int QueryOptionByte0 = 5;
        public const int QueryOptionByte1 = 6;
        public const int QueryOptionByte2 = 7;
        public const int QueryOptionByte3 = 8;
        public const int QueryOptionByte4 = 9;

        public static int[] EnsureQueryValues(int[] values)
        {
            if (values == null || values.Length != QueryValueCount)
                return new int[QueryValueCount];

            return values;
        }

        public static int GetDefaultPayloadCapacityBytes(int activeWidthBlocks, int activeHeightBlocks)
        {
            int payloadRows = Mathf.Max(0, activeHeightBlocks - Luma4Raster.PayloadStartRow - Luma4Raster.ReservedEndRows);
            return activeWidthBlocks * payloadRows * 4 / 8;
        }

        public static void QueryDirect(TSMPCodec codec, int width, int height, int blockSize, int[] values)
        {
            codec.encoderRequestWidth = width;
            codec.encoderRequestHeight = height;
            codec.encoderRequestBlockSize = blockSize;
            values[QueryCodecId] = codec.codecId;
            values[QuerySymbolMode] = codec.GetEncoderSymbolMode();
            values[QueryPayloadStartRow] = codec.GetEncoderPayloadStartRow(width, blockSize);
            values[QueryPayloadCapacityBytes] = codec.GetEncoderPayloadCapacityBytes(width, height, blockSize);
            int optionByteCount = Mathf.Clamp(codec.GetEncoderCodecOptionByteCount(), 0, TSMPCodec.EncoderCodecOptionByteCapacity);
            values[QueryOptionByteCount] = optionByteCount;
            values[QueryOptionByte0] = optionByteCount > 0 ? codec.GetEncoderCodecOptionByte(0) : 0;
            values[QueryOptionByte1] = optionByteCount > 1 ? codec.GetEncoderCodecOptionByte(1) : 0;
            values[QueryOptionByte2] = optionByteCount > 2 ? codec.GetEncoderCodecOptionByte(2) : 0;
            values[QueryOptionByte3] = optionByteCount > 3 ? codec.GetEncoderCodecOptionByte(3) : 0;
            values[QueryOptionByte4] = optionByteCount > 4 ? codec.GetEncoderCodecOptionByte(4) : 0;
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public static void QueryBridge(UdonBehaviour codec, int width, int height, int blockSize, int fallbackCodecId, int fallbackCapacityBytes, int[] values)
        {
            CodecBridge.QueryEncoder(codec, width, height, blockSize);
            values[QueryCodecId] = CodecBridge.GetEncoderCodecId(codec, fallbackCodecId);
            values[QuerySymbolMode] = CodecBridge.GetEncoderSymbolMode(codec, (int)K13A.TSMP.SymbolMode.Luma4);
            values[QueryPayloadStartRow] = CodecBridge.GetEncoderPayloadStartRow(codec, Luma4Raster.PayloadStartRow);
            values[QueryPayloadCapacityBytes] = CodecBridge.GetEncoderPayloadCapacityBytes(codec, fallbackCapacityBytes);
            int optionByteCount = Mathf.Clamp(CodecBridge.GetEncoderCodecOptionByteCount(codec), 0, TSMPCodec.EncoderCodecOptionByteCapacity);
            values[QueryOptionByteCount] = optionByteCount;
            values[QueryOptionByte0] = optionByteCount > 0 ? CodecBridge.GetEncoderCodecOptionByte(codec, 0) : 0;
            values[QueryOptionByte1] = optionByteCount > 1 ? CodecBridge.GetEncoderCodecOptionByte(codec, 1) : 0;
            values[QueryOptionByte2] = optionByteCount > 2 ? CodecBridge.GetEncoderCodecOptionByte(codec, 2) : 0;
            values[QueryOptionByte3] = optionByteCount > 3 ? CodecBridge.GetEncoderCodecOptionByte(codec, 3) : 0;
            values[QueryOptionByte4] = optionByteCount > 4 ? CodecBridge.GetEncoderCodecOptionByte(codec, 4) : 0;
        }

#endif

        public static bool WriteDirectPayload(TSMPCodec codec, Color32[] pixels, bool pixelsAreBlocks, byte[] payloadBytes, int payloadByteCount, int width, int height, int blockSize)
        {
            codec.encoderRequestWidth = width;
            codec.encoderRequestHeight = height;
            codec.encoderRequestBlockSize = blockSize;
            codec.encoderPixels = pixels;
            codec.encoderPixelsAreBlocks = pixelsAreBlocks;
            codec.encoderPayloadBytes = payloadBytes;
            codec.encoderPayloadByteCount = payloadByteCount;
            codec.encoderWriteResult = codec.WriteEncoderPayload(pixels, width, height, blockSize, payloadBytes, payloadByteCount);
            return codec.encoderWriteResult;
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public static bool WriteBridgePayload(UdonBehaviour codec, int width, int height, int blockSize, Color32[] pixels, bool pixelsAreBlocks, byte[] payloadBytes, int payloadByteCount)
        {
            if (codec == null)
                return false;

            return CodecBridge.WritePreparedEncoderPayload(codec, pixels, pixelsAreBlocks, payloadBytes, payloadByteCount);
        }
#endif
    }
}
