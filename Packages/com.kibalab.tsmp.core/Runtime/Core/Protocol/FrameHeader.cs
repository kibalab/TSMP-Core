namespace K13A.TSMP
{
    public struct FrameHeader
    {
        public const int BytesBeforeCrc = 52;
        public const int CrcOffset = 52;
        public const int Size = 56;
        public const int MagicOffset = 0;
        public const int VersionMajorOffset = 4;
        public const int VersionMinorOffset = 5;
        public const int ProfileOffset = 6;
        public const int SymbolModeOffset = 7;
        public const int HeaderSizeOffset = 8;
        public const int FlagsOffset = 10;
        public const int BlockSizeOffset = 12;
        public const int ActiveWidthBlocksOffset = 14;
        public const int ActiveHeightBlocksOffset = 16;
        public const int LayoutIdOffset = 18;
        public const int StreamIdOffset = 20;
        public const int FrameIndexOffset = 24;
        public const int TimestampMsOffset = 28;
        public const int CodecIdOffset = 32;
        public const int CodecOptionLengthOffset = 34;
        public const int CodecOptionsOffset = 35;
        public const int MaxCodecOptionBytes = 5;
        public const int PayloadTypeOffset = 40;
        public const int PayloadSizeOffset = 42;
        public const int PayloadReservedOffset = 44;
        public const int PayloadReservedBytes = 6;
        public const int DecodeSampleSizeOffset = 50;
        public const int QuantizationModeOffset = 51;
        public const int MaxDecodeSampleSize = 8;

        public byte VersionMajor;
        public byte VersionMinor;
        public byte Profile;
        public byte SymbolMode;
        public ushort HeaderSize;
        public FrameFlags Flags;
        public ushort BlockSize;
        public ushort ActiveWidthBlocks;
        public ushort ActiveHeightBlocks;
        public ushort LayoutId;
        public uint StreamId;
        public uint FrameIndex;
        public uint TimestampMs;
        public ushort CodecId;
        public byte CodecOptionLength;
        public byte CodecOption0;
        public byte CodecOption1;
        public byte CodecOption2;
        public byte CodecOption3;
        public byte CodecOption4;
        public PayloadType PayloadType;
        public ushort PayloadSize;
        public byte DecodeSampleSize;
        public byte QuantizationMode;
        public uint HeaderCrc32;

        public static FrameHeader CreateDefault()
        {
            return new FrameHeader
            {
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                SymbolMode = (byte)K13A.TSMP.SymbolMode.Luma4,
                HeaderSize = Size,
                BlockSize = ProtocolConstants.DefaultBlockSize,
                PayloadType = PayloadType.NetworkFrame
            };
        }

        public void WriteTo(byte[] buffer, int offset)
        {
            if (buffer == null || offset < 0 || offset + Size > buffer.Length)
                return;

            HeaderCrc32 = FrameHeaderWriter.WriteNetworkFrameHeaderWithCrc(
                buffer,
                offset,
                VersionMajor,
                VersionMinor,
                Profile,
                SymbolMode,
                (int)Flags,
                BlockSize,
                ActiveWidthBlocks,
                ActiveHeightBlocks,
                LayoutId,
                StreamId,
                FrameIndex,
                TimestampMs,
                CodecId,
                CodecOptionLength,
                CodecOption0,
                CodecOption1,
                CodecOption2,
                CodecOption3,
                CodecOption4,
                (int)PayloadType,
                PayloadSize,
                DecodeSampleSize,
                QuantizationMode,
                null);
        }

#if !COMPILER_UDONSHARP
        public static bool TryRead(byte[] buffer, int offset, out FrameHeader header)
        {
            int status;
            uint decodedMagic;
            ushort decodedHeaderSize;
            uint computedCrc;
            return FrameHeaderReader.TryRead(
                buffer,
                offset,
                null,
                out header,
                out status,
                out decodedMagic,
                out decodedHeaderSize,
                out computedCrc);
        }
#endif

        public static byte ClampDecodeSampleSize(int sampleSize)
        {
            return (byte)System.Math.Max(0, System.Math.Min(MaxDecodeSampleSize, sampleSize));
        }

        public static byte ClampDecodeSampleSize(int sampleSize, int blockSize)
        {
            if (sampleSize <= 0)
                return 0;

            int max = blockSize < MaxDecodeSampleSize ? blockSize : MaxDecodeSampleSize;
            if (max < 1)
                max = 1;

            if (sampleSize > max)
                return (byte)max;

            return (byte)sampleSize;
        }
    }
}
