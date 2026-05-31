namespace K13A.TSMP
{
    public static class DecoderHeaderRuntime
    {
        public static void ReadHeaderFields(
            byte[] headerBytes,
            byte[] codecOptionBytes,
            out ushort headerBlockSize,
            out ushort headerActiveWidthBlocks,
            out ushort payloadType,
            out ushort payloadSize,
            out int headerSampleSize,
            out int symbolMode,
            out int codecId,
            out uint streamId,
            out uint frameIndex)
        {
            headerBlockSize = FrameHeaderReader.ReadBlockSize(headerBytes, 0);
            headerActiveWidthBlocks = FrameHeaderReader.ReadActiveWidthBlocks(headerBytes, 0);
            payloadType = FrameHeaderReader.ReadPayloadType(headerBytes, 0);
            payloadSize = FrameHeaderReader.ReadPayloadSize(headerBytes, 0);
            headerSampleSize = FrameHeaderReader.ReadDecodeSampleSize(headerBytes, 0);
            symbolMode = FrameHeaderReader.ReadSymbolMode(headerBytes, 0);
            codecId = FrameHeaderReader.ReadCodecId(headerBytes, 0);
            streamId = FrameHeaderReader.ReadStreamId(headerBytes, 0);
            frameIndex = FrameHeaderReader.ReadFrameIndex(headerBytes, 0);
            FrameHeaderReader.CopyCodecOptions(headerBytes, 0, codecOptionBytes);
        }

        public static void ResolvePayloadLayout(
            bool useHeaderPayloadLayout,
            int sourceWidth,
            ushort headerBlockSize,
            ushort headerActiveWidthBlocks,
            int headerSampleSize,
            ushort payloadSize,
            int codecPayloadStartRow,
            int currentBlockSize,
            int currentSampleSize,
            int currentActiveWidthBlocks,
            int currentPayloadDataBytes,
            byte[] currentPayloadBytes,
            out int nextBlockSize,
            out int nextSampleSize,
            out int nextActiveWidthBlocks,
            out int nextPayloadDataBytes,
            out byte[] nextPayloadBytes,
            out int nextPayloadStartBlock,
            out int nextPayloadStartRow)
        {
            nextBlockSize = currentBlockSize;
            nextSampleSize = currentSampleSize;
            nextActiveWidthBlocks = currentActiveWidthBlocks;
            nextPayloadDataBytes = currentPayloadDataBytes;
            nextPayloadBytes = currentPayloadBytes;
            nextPayloadStartBlock = codecPayloadStartRow * currentActiveWidthBlocks;
            nextPayloadStartRow = codecPayloadStartRow;

            if (!useHeaderPayloadLayout)
                return;

            if (headerBlockSize > 0)
                nextBlockSize = headerBlockSize;

            nextSampleSize = FrameHeader.ClampDecodeSampleSize(headerSampleSize, nextBlockSize);

            if (headerActiveWidthBlocks > 0)
                nextActiveWidthBlocks = headerActiveWidthBlocks;
            else if (nextBlockSize > 0)
                nextActiveWidthBlocks = sourceWidth / nextBlockSize;

            if (payloadSize > 0)
            {
                nextPayloadDataBytes = payloadSize;
                if (nextPayloadBytes == null || nextPayloadBytes.Length != nextPayloadDataBytes)
                    nextPayloadBytes = new byte[nextPayloadDataBytes];
            }

            nextPayloadStartBlock = codecPayloadStartRow * nextActiveWidthBlocks;
        }
    }
}
