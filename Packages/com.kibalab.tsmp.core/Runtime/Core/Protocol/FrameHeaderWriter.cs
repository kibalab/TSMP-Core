namespace K13A.TSMP
{
    public static class FrameHeaderWriter
    {
        private static int GetCodecOptionByte(byte[] codecOptions, int index)
        {
            if (codecOptions == null)
                return 0;
            if (index < 0)
                return 0;
            if (index >= codecOptions.Length)
                return 0;

            return codecOptions[index];
        }

#if !COMPILER_UDONSHARP
        public static uint WriteNetworkFrameHeaderWithCodecOptionsAndCrc(
            byte[] buffer,
            int offset,
            int versionMajor,
            int versionMinor,
            int profile,
            int symbolMode,
            int flags,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            int layoutId,
            uint streamId,
            uint frameIndex,
            uint timestampMs,
            int codecId,
            byte[] codecOptions,
            int payloadType,
            int payloadSize,
            int decodeSampleSize,
            int quantizationMode,
            uint[] crcTable)
        {
            int codecOptionLength = codecOptions == null ? 0 : codecOptions.Length;
            int optionCount = ClampRange(codecOptionLength, 0, FrameHeader.MaxCodecOptionBytes);
            return WriteNetworkFrameHeaderWithCrc(
                buffer,
                offset,
                versionMajor,
                versionMinor,
                profile,
                symbolMode,
                flags,
                blockSize,
                activeWidthBlocks,
                activeHeightBlocks,
                layoutId,
                streamId,
                frameIndex,
                timestampMs,
                codecId,
                optionCount,
                GetCodecOptionByte(codecOptions, 0),
                GetCodecOptionByte(codecOptions, 1),
                GetCodecOptionByte(codecOptions, 2),
                GetCodecOptionByte(codecOptions, 3),
                GetCodecOptionByte(codecOptions, 4),
                payloadType,
                payloadSize,
                decodeSampleSize,
                quantizationMode,
                crcTable);
        }
#endif

        private static void WriteNetworkFrameHeader(
            byte[] buffer,
            int offset,
            int versionMajor,
            int versionMinor,
            int profile,
            int symbolMode,
            int flags,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            int layoutId,
            uint streamId,
            uint frameIndex,
            uint timestampMs,
            int codecId,
            int codecOptionLength,
            int codecOption0,
            int codecOption1,
            int codecOption2,
            int codecOption3,
            int codecOption4,
            int payloadType,
            int payloadSize,
            int decodeSampleSize,
            int quantizationMode)
        {
            if (buffer == null)
                return;
            if (offset < 0)
                return;
            if (offset + FrameHeader.Size > buffer.Length)
                return;

            Binary.WriteUInt32LE(buffer, offset + FrameHeader.MagicOffset, ProtocolConstants.Magic);
            buffer[offset + FrameHeader.VersionMajorOffset] = ClampByte(versionMajor);
            buffer[offset + FrameHeader.VersionMinorOffset] = ClampByte(versionMinor);
            buffer[offset + FrameHeader.ProfileOffset] = ClampByte(profile);
            buffer[offset + FrameHeader.SymbolModeOffset] = ClampByte(symbolMode);
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.HeaderSizeOffset, FrameHeader.Size);
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.FlagsOffset, ClampUInt16(flags));
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.BlockSizeOffset, ClampUInt16(blockSize));
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.ActiveWidthBlocksOffset, ClampUInt16(activeWidthBlocks));
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.ActiveHeightBlocksOffset, ClampUInt16(activeHeightBlocks));
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.LayoutIdOffset, ClampUInt16(layoutId));
            Binary.WriteUInt32LE(buffer, offset + FrameHeader.StreamIdOffset, streamId);
            Binary.WriteUInt32LE(buffer, offset + FrameHeader.FrameIndexOffset, frameIndex);
            Binary.WriteUInt32LE(buffer, offset + FrameHeader.TimestampMsOffset, timestampMs);
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.CodecIdOffset, ClampUInt16(codecId));

            int optionCount = ClampRange(codecOptionLength, 0, FrameHeader.MaxCodecOptionBytes);
            buffer[offset + FrameHeader.CodecOptionLengthOffset] = (byte)optionCount;
            buffer[offset + FrameHeader.CodecOptionsOffset + 0] = ClampByte(codecOption0);
            buffer[offset + FrameHeader.CodecOptionsOffset + 1] = ClampByte(codecOption1);
            buffer[offset + FrameHeader.CodecOptionsOffset + 2] = ClampByte(codecOption2);
            buffer[offset + FrameHeader.CodecOptionsOffset + 3] = ClampByte(codecOption3);
            buffer[offset + FrameHeader.CodecOptionsOffset + 4] = ClampByte(codecOption4);

            Binary.WriteUInt16LE(buffer, offset + FrameHeader.PayloadTypeOffset, ClampUInt16(payloadType));
            Binary.WriteUInt16LE(buffer, offset + FrameHeader.PayloadSizeOffset, ClampUInt16(payloadSize));
            for (int i = 0; i < FrameHeader.PayloadReservedBytes; i++)
                buffer[offset + FrameHeader.PayloadReservedOffset + i] = 0;

            buffer[offset + FrameHeader.DecodeSampleSizeOffset] = FrameHeader.ClampDecodeSampleSize(decodeSampleSize);
            buffer[offset + FrameHeader.QuantizationModeOffset] = ClampByte(quantizationMode);
            WriteHeaderCrc(buffer, offset, 0u);
        }

        public static uint WriteNetworkFrameHeaderWithCrc(
            byte[] buffer,
            int offset,
            int versionMajor,
            int versionMinor,
            int profile,
            int symbolMode,
            int flags,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            int layoutId,
            uint streamId,
            uint frameIndex,
            uint timestampMs,
            int codecId,
            int codecOptionLength,
            int codecOption0,
            int codecOption1,
            int codecOption2,
            int codecOption3,
            int codecOption4,
            int payloadType,
            int payloadSize,
            int decodeSampleSize,
            int quantizationMode,
            uint[] crcTable)
        {
            WriteNetworkFrameHeader(
                buffer,
                offset,
                versionMajor,
                versionMinor,
                profile,
                symbolMode,
                flags,
                blockSize,
                activeWidthBlocks,
                activeHeightBlocks,
                layoutId,
                streamId,
                frameIndex,
                timestampMs,
                codecId,
                codecOptionLength,
                codecOption0,
                codecOption1,
                codecOption2,
                codecOption3,
                codecOption4,
                payloadType,
                payloadSize,
                decodeSampleSize,
                quantizationMode);
            return WriteComputedHeaderCrc(buffer, offset, crcTable);
        }

        private static void WriteHeaderCrc(byte[] buffer, int offset, uint crc)
        {
            if (buffer == null)
                return;
            if (offset < 0)
                return;
            if (offset + FrameHeader.Size > buffer.Length)
                return;

            Binary.WriteUInt32LE(buffer, offset + FrameHeader.CrcOffset, crc);
        }

        private static uint WriteComputedHeaderCrc(byte[] buffer, int offset, uint[] crcTable)
        {
            uint[] table = Crc32Runtime.EnsureTable(crcTable);
            uint crc = Crc32Runtime.Compute(buffer, offset, FrameHeader.BytesBeforeCrc, table);
            WriteHeaderCrc(buffer, offset, crc);
            return crc;
        }

        private static ushort ClampUInt16(int value)
        {
            if (value <= 0)
                return 0;
            if (value >= 65535)
                return 65535;

            return (ushort)value;
        }

        private static byte ClampByte(int value)
        {
            if (value <= 0)
                return 0;
            if (value >= 255)
                return 255;

            return (byte)value;
        }

        private static int ClampRange(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;

            return value;
        }
    }
}
