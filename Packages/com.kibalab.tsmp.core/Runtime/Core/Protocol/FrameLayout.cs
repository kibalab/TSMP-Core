using System;

namespace K13A.TSMP
{
    [Serializable]
    public struct FrameLayout
    {
        public int Width;
        public int Height;
        public int BlockSize;
        public int ActiveWidthBlocks;
        public int ActiveHeightBlocks;
        public int Luma4PayloadStartRow;
        public int Luma4PayloadStartBlock;

        public static FrameLayout Calculate(int width, int height, int blockSize)
        {
            blockSize = Math.Max(1, blockSize);

            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(height, blockSize);
            int basePayloadRow = Luma4Raster.PayloadStartRow;

            return new FrameLayout
            {
                Width = width,
                Height = height,
                BlockSize = blockSize,
                ActiveWidthBlocks = activeWidthBlocks,
                ActiveHeightBlocks = activeHeightBlocks,
                Luma4PayloadStartRow = basePayloadRow,
                Luma4PayloadStartBlock = basePayloadRow * activeWidthBlocks
            };
        }

        public int GetPayloadStartRow()
        {
            return Luma4PayloadStartRow;
        }

        public int GetPayloadStartBlock()
        {
            return Luma4PayloadStartBlock;
        }
    }
}
