using UnityEngine;

namespace K13A.TSMP
{
    public static class FrameCapacity
    {
        public static int GetActiveWidthBlocks(int width, int blockSize)
        {
            return Mathf.Max(0, width / Mathf.Max(1, blockSize));
        }

        public static int GetActiveHeightBlocks(int height, int blockSize)
        {
            return Mathf.Max(0, height / Mathf.Max(1, blockSize));
        }

        public static int GetUsableBytes(
            int width,
            int height,
            int blockSize,
            int bitsPerSymbol,
            int reservedRows)
        {
            int activeWidthBlocks = GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = GetActiveHeightBlocks(height, blockSize);
            int usableRows = Mathf.Max(0, activeHeightBlocks - reservedRows);
            int usableBlocks = activeWidthBlocks * usableRows;
            return usableBlocks * Mathf.Max(1, bitsPerSymbol) / 8;
        }

        public static bool Fits(
            ResolutionProfile resolution,
            int blockSize,
            int bitsPerSymbol,
            int reservedRows,
            int frameBytes,
            float safetyFactor)
        {
            int usableBytes = GetUsableBytes(resolution.Width, resolution.Height, blockSize, bitsPerSymbol, reservedRows);
            return usableBytes >= Mathf.CeilToInt(frameBytes * Mathf.Max(1f, safetyFactor));
        }
    }
}
