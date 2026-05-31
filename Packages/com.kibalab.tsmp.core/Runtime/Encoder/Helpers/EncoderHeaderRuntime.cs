using UnityEngine;

namespace K13A.TSMP
{
    public static class EncoderHeaderRuntime
    {
#if !COMPILER_UDONSHARP
        public static uint[] WriteNativeNetworkHeader(
            byte[] header,
            int blockSize,
            int width,
            int height,
            ushort layoutId,
            uint streamId,
            uint frameIndex,
            int symbolMode,
            ushort codecId,
            byte[] codecOptions,
            int payloadBytes,
            int sampleSize,
            uint[] crc32Table)
        {
            uint timestampMs = GetTimestampMilliseconds();
            crc32Table = Crc32Runtime.EnsureTable(crc32Table);
            FrameHeaderWriter.WriteNetworkFrameHeaderWithCodecOptionsAndCrc(
                header,
                0,
                ProtocolConstants.VersionMajor,
                ProtocolConstants.VersionMinor,
                0,
                Mathf.Clamp(symbolMode, 0, 255),
                (int)FrameFlags.None,
                blockSize,
                FrameCapacity.GetActiveWidthBlocks(width, blockSize),
                FrameCapacity.GetActiveHeightBlocks(height, blockSize),
                layoutId,
                streamId,
                frameIndex,
                timestampMs,
                codecId,
                codecOptions,
                NetworkFrameProtocol.PayloadTypeNetworkFrame,
                payloadBytes,
                sampleSize,
                0,
                crc32Table);
            return crc32Table;
        }
#endif

        public static uint[] WriteCachedNetworkHeader(
            byte[] header,
            int mode,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            ushort layoutId,
            uint streamId,
            uint frameIndex,
            int codecId,
            int optionByteCount,
            int optionByte0,
            int optionByte1,
            int optionByte2,
            int optionByte3,
            int optionByte4,
            int payloadBytes,
            int sampleSize,
            uint[] crc32Table)
        {
            int resolvedCodecId = codecId;
            if (resolvedCodecId < 0)
                resolvedCodecId = 0;

            uint timestampMs = (uint)Mathf.Max(0, Mathf.RoundToInt(Time.time * 1000f));
            crc32Table = Crc32Runtime.EnsureTable(crc32Table);
            FrameHeaderWriter.WriteNetworkFrameHeaderWithCrc(
                header,
                0,
                ProtocolConstants.VersionMajor,
                ProtocolConstants.VersionMinor,
                0,
                mode,
                0,
                blockSize,
                activeWidthBlocks,
                activeHeightBlocks,
                layoutId,
                streamId,
                frameIndex,
                timestampMs,
                resolvedCodecId,
                optionByteCount,
                optionByte0,
                optionByte1,
                optionByte2,
                optionByte3,
                optionByte4,
                NetworkFrameProtocol.PayloadTypeNetworkFrame,
                payloadBytes,
                sampleSize,
                0,
                crc32Table);
            return crc32Table;
        }

        private static uint GetTimestampMilliseconds()
        {
#if COMPILER_UDONSHARP
            return (uint)Mathf.Max(0, Mathf.RoundToInt(Time.time * 1000f));
#else
            float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            return (uint)Mathf.Max(0, Mathf.RoundToInt(time * 1000f));
#endif
        }
    }
}
