namespace K13A.TSMP
{
    public static class FrameHeaderReader
    {
        public const int StatusOk = 0;
        public const int StatusInvalidBuffer = 1;
        public const int StatusMagicMismatch = 2;
        public const int StatusHeaderSizeMismatch = 3;
        public const int StatusVersionMismatch = 4;
        public const int StatusCrcMismatch = 5;

#if !COMPILER_UDONSHARP
        public static bool TryRead(
            byte[] buffer,
            int offset,
            uint[] crcTable,
            out FrameHeader header,
            out int status,
            out uint decodedMagic,
            out ushort decodedHeaderSize,
            out uint computedCrc)
        {
            header = default;
            status = StatusInvalidBuffer;
            decodedMagic = 0u;
            decodedHeaderSize = 0;
            computedCrc = 0u;

            if (!TryReadFields(buffer, offset, out header, out status, out decodedMagic, out decodedHeaderSize))
                return false;

            uint[] table = Crc32Runtime.EnsureTable(crcTable);
            computedCrc = Crc32Runtime.Compute(buffer, offset, FrameHeader.BytesBeforeCrc, table);
            if (header.HeaderCrc32 != computedCrc)
            {
                status = StatusCrcMismatch;
                return false;
            }

            status = StatusOk;
            return true;
        }

        private static bool TryReadFields(
            byte[] buffer,
            int offset,
            out FrameHeader header,
            out int status,
            out uint decodedMagic,
            out ushort decodedHeaderSize)
        {
            header = default;
            status = StatusInvalidBuffer;
            decodedMagic = 0u;
            decodedHeaderSize = 0;

            int versionMajor;
            if (!TryValidatePrefix(buffer, offset, out status, out decodedMagic, out decodedHeaderSize, out versionMajor))
                return false;

            header.VersionMajor = (byte)versionMajor;
            header.VersionMinor = buffer[offset + FrameHeader.VersionMinorOffset];
            header.Profile = buffer[offset + FrameHeader.ProfileOffset];
            header.SymbolMode = buffer[offset + FrameHeader.SymbolModeOffset];
            header.HeaderSize = decodedHeaderSize;
            header.Flags = (FrameFlags)Binary.ReadUInt16LE(buffer, offset + FrameHeader.FlagsOffset);
            header.BlockSize = Binary.ReadUInt16LE(buffer, offset + FrameHeader.BlockSizeOffset);
            header.ActiveWidthBlocks = Binary.ReadUInt16LE(buffer, offset + FrameHeader.ActiveWidthBlocksOffset);
            header.ActiveHeightBlocks = Binary.ReadUInt16LE(buffer, offset + FrameHeader.ActiveHeightBlocksOffset);
            header.LayoutId = Binary.ReadUInt16LE(buffer, offset + FrameHeader.LayoutIdOffset);
            header.StreamId = Binary.ReadUInt32LE(buffer, offset + FrameHeader.StreamIdOffset);
            header.FrameIndex = Binary.ReadUInt32LE(buffer, offset + FrameHeader.FrameIndexOffset);
            header.TimestampMs = Binary.ReadUInt32LE(buffer, offset + FrameHeader.TimestampMsOffset);
            header.CodecId = Binary.ReadUInt16LE(buffer, offset + FrameHeader.CodecIdOffset);
            header.CodecOptionLength = buffer[offset + FrameHeader.CodecOptionLengthOffset];
            if (header.CodecOptionLength > FrameHeader.MaxCodecOptionBytes)
                header.CodecOptionLength = FrameHeader.MaxCodecOptionBytes;
            header.CodecOption0 = buffer[offset + FrameHeader.CodecOptionsOffset + 0];
            header.CodecOption1 = buffer[offset + FrameHeader.CodecOptionsOffset + 1];
            header.CodecOption2 = buffer[offset + FrameHeader.CodecOptionsOffset + 2];
            header.CodecOption3 = buffer[offset + FrameHeader.CodecOptionsOffset + 3];
            header.CodecOption4 = buffer[offset + FrameHeader.CodecOptionsOffset + 4];
            header.PayloadType = (PayloadType)Binary.ReadUInt16LE(buffer, offset + FrameHeader.PayloadTypeOffset);
            header.PayloadSize = Binary.ReadUInt16LE(buffer, offset + FrameHeader.PayloadSizeOffset);
            header.DecodeSampleSize = buffer[offset + FrameHeader.DecodeSampleSizeOffset];
            header.QuantizationMode = buffer[offset + FrameHeader.QuantizationModeOffset];
            header.HeaderCrc32 = Binary.ReadUInt32LE(buffer, offset + FrameHeader.CrcOffset);

            status = StatusOk;
            return true;
        }
#endif

        public static bool TryValidate(
            byte[] buffer,
            int offset,
            uint[] crcTable,
            out int status,
            out uint decodedMagic,
            out ushort decodedHeaderSize,
            out int versionMajor,
            out uint expectedCrc,
            out uint computedCrc)
        {
            status = StatusInvalidBuffer;
            decodedMagic = 0u;
            decodedHeaderSize = 0;
            versionMajor = 0;
            expectedCrc = 0u;
            computedCrc = 0u;

            if (!TryValidatePrefix(buffer, offset, out status, out decodedMagic, out decodedHeaderSize, out versionMajor))
                return false;

            uint[] table = Crc32Runtime.EnsureTable(crcTable);
            expectedCrc = Binary.ReadUInt32LE(buffer, offset + FrameHeader.CrcOffset);
            computedCrc = Crc32Runtime.Compute(buffer, offset, FrameHeader.BytesBeforeCrc, table);
            if (expectedCrc != computedCrc)
            {
                status = StatusCrcMismatch;
                return false;
            }

            status = StatusOk;
            return true;
        }

        private static bool TryValidatePrefix(byte[] buffer, int offset, out int status, out uint decodedMagic, out ushort decodedHeaderSize, out int versionMajor)
        {
            status = StatusInvalidBuffer;
            decodedMagic = 0u;
            decodedHeaderSize = 0;
            versionMajor = 0;

            if (buffer == null)
                return false;
            if (offset < 0)
                return false;
            if (offset + FrameHeader.Size > buffer.Length)
                return false;

            decodedMagic = Binary.ReadUInt32LE(buffer, offset + FrameHeader.MagicOffset);
            if (decodedMagic != ProtocolConstants.Magic)
            {
                status = StatusMagicMismatch;
                return false;
            }

            decodedHeaderSize = Binary.ReadUInt16LE(buffer, offset + FrameHeader.HeaderSizeOffset);
            if (decodedHeaderSize != FrameHeader.Size)
            {
                status = StatusHeaderSizeMismatch;
                return false;
            }

            versionMajor = buffer[offset + FrameHeader.VersionMajorOffset];
            if (versionMajor != ProtocolConstants.VersionMajor)
            {
                status = StatusVersionMismatch;
                return false;
            }

            status = StatusOk;
            return true;
        }

        public static int ReadSymbolMode(byte[] buffer, int offset)
        {
            return buffer[offset + FrameHeader.SymbolModeOffset];
        }

        public static ushort ReadBlockSize(byte[] buffer, int offset)
        {
            return Binary.ReadUInt16LE(buffer, offset + FrameHeader.BlockSizeOffset);
        }

        public static ushort ReadActiveWidthBlocks(byte[] buffer, int offset)
        {
            return Binary.ReadUInt16LE(buffer, offset + FrameHeader.ActiveWidthBlocksOffset);
        }

        public static uint ReadStreamId(byte[] buffer, int offset)
        {
            return Binary.ReadUInt32LE(buffer, offset + FrameHeader.StreamIdOffset);
        }

        public static uint ReadFrameIndex(byte[] buffer, int offset)
        {
            return Binary.ReadUInt32LE(buffer, offset + FrameHeader.FrameIndexOffset);
        }

        public static ushort ReadCodecId(byte[] buffer, int offset)
        {
            return Binary.ReadUInt16LE(buffer, offset + FrameHeader.CodecIdOffset);
        }

        public static int CopyCodecOptions(byte[] buffer, int offset, byte[] destination)
        {
            if (destination == null)
                return 0;

            int length = ReadCodecOptionLength(buffer, offset);
            for (int i = 0; i < destination.Length; i++)
                destination[i] = 0;

            if (length > destination.Length)
                length = destination.Length;

            for (int i = 0; i < length; i++)
                destination[i] = ReadCodecOption(buffer, offset, i);

            return length;
        }

        private static int ReadCodecOptionLength(byte[] buffer, int offset)
        {
            int length = buffer[offset + FrameHeader.CodecOptionLengthOffset];
            if (length > FrameHeader.MaxCodecOptionBytes)
                length = FrameHeader.MaxCodecOptionBytes;

            return length;
        }

        private static byte ReadCodecOption(byte[] buffer, int offset, int index)
        {
            if (index < 0 || index >= FrameHeader.MaxCodecOptionBytes)
                return 0;

            return buffer[offset + FrameHeader.CodecOptionsOffset + index];
        }

        public static ushort ReadPayloadType(byte[] buffer, int offset)
        {
            return Binary.ReadUInt16LE(buffer, offset + FrameHeader.PayloadTypeOffset);
        }

        public static ushort ReadPayloadSize(byte[] buffer, int offset)
        {
            return Binary.ReadUInt16LE(buffer, offset + FrameHeader.PayloadSizeOffset);
        }

        public static int ReadDecodeSampleSize(byte[] buffer, int offset)
        {
            return buffer[offset + FrameHeader.DecodeSampleSizeOffset];
        }
    }
}
