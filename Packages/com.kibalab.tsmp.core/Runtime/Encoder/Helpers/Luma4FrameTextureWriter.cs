using UnityEngine;

namespace K13A.TSMP
{
    public static class Luma4FrameTextureWriter
    {
        public static void WriteBaseRegions(
            Color32[] pixels,
            int width,
            int height,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            bool pixelsAreBlocks,
            byte[] headerBytes,
            Color32[] luma4Colors)
        {
            WriteStaticRegions(pixels, width, height, blockSize, activeWidthBlocks, activeHeightBlocks, pixelsAreBlocks);
            WriteHeader(pixels, width, height, blockSize, activeWidthBlocks, activeHeightBlocks, pixelsAreBlocks, headerBytes, luma4Colors);
        }

        public static void WriteStaticRegions(
            Color32[] pixels,
            int width,
            int height,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            bool pixelsAreBlocks)
        {
            int rasterWidth = GetRasterWriteWidth(width, activeWidthBlocks, pixelsAreBlocks);
            int rasterHeight = GetRasterWriteHeight(height, activeHeightBlocks, pixelsAreBlocks);
            int rasterBlockSize = GetRasterWriteBlockSize(blockSize, pixelsAreBlocks);

            FrameRaster.WriteFinder(pixels, rasterWidth, rasterHeight, rasterBlockSize);
            FrameRaster.WriteLuma4Calibration(pixels, rasterWidth, rasterHeight, rasterBlockSize);
            FrameRaster.WriteEndMarker(pixels, rasterWidth, rasterHeight, rasterBlockSize);
        }

        public static void WriteHeader(
            Color32[] pixels,
            int width,
            int height,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            bool pixelsAreBlocks,
            byte[] headerBytes,
            Color32[] luma4Colors)
        {
            WriteBytesAtRows(pixels, width, height, blockSize, activeWidthBlocks, activeHeightBlocks, pixelsAreBlocks, Luma4Raster.HeaderARow, Luma4Raster.HeaderRows, headerBytes, GetByteCount(headerBytes), luma4Colors);
        }

        public static void WritePayload(
            Color32[] pixels,
            int width,
            int height,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            bool pixelsAreBlocks,
            int payloadStartRow,
            byte[] payloadBytes,
            int payloadByteCount,
            Color32[] luma4Colors)
        {
            int payloadRows = Mathf.Max(0, activeHeightBlocks - payloadStartRow - Luma4Raster.ReservedEndRows);
            int startBlock = payloadStartRow * activeWidthBlocks;
            int maxBlocks = activeWidthBlocks * payloadRows;
            WriteLuma4ByteSequence(pixels, width, height, blockSize, activeWidthBlocks, activeHeightBlocks, pixelsAreBlocks, startBlock, maxBlocks, payloadBytes, payloadByteCount, luma4Colors);
        }

        public static void WriteEndMarker(Color32[] pixels, int width, int height, int blockSize, int activeWidthBlocks, int activeHeightBlocks, bool pixelsAreBlocks)
        {
            int rasterWidth = GetRasterWriteWidth(width, activeWidthBlocks, pixelsAreBlocks);
            int rasterHeight = GetRasterWriteHeight(height, activeHeightBlocks, pixelsAreBlocks);
            int rasterBlockSize = GetRasterWriteBlockSize(blockSize, pixelsAreBlocks);
            FrameRaster.WriteEndMarker(pixels, rasterWidth, rasterHeight, rasterBlockSize);
        }

        private static void WriteBytesAtRows(
            Color32[] pixels,
            int width,
            int height,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            bool pixelsAreBlocks,
            int startRow,
            int rowCount,
            byte[] bytes,
            int byteCount,
            Color32[] luma4Colors)
        {
            int startBlock = startRow * activeWidthBlocks;
            int maxBlocks = activeWidthBlocks * rowCount;
            WriteLuma4ByteSequence(pixels, width, height, blockSize, activeWidthBlocks, activeHeightBlocks, pixelsAreBlocks, startBlock, maxBlocks, bytes, byteCount, luma4Colors);
        }

        private static void WriteLuma4ByteSequence(
            Color32[] pixels,
            int width,
            int height,
            int blockSize,
            int activeWidthBlocks,
            int activeHeightBlocks,
            bool pixelsAreBlocks,
            int startBlock,
            int maxBlocks,
            byte[] bytes,
            int byteCount,
            Color32[] luma4Colors)
        {
            if (bytes == null)
                return;

            if (pixelsAreBlocks)
            {
                FrameRaster.WriteLuma4BytesToBlockTexture(pixels, activeWidthBlocks, activeHeightBlocks, startBlock, maxBlocks, bytes, 0, byteCount, luma4Colors);
                return;
            }

            FrameRaster.WriteLuma4Bytes(pixels, width, height, blockSize, startBlock, maxBlocks, bytes, 0, byteCount);
        }

        private static int GetRasterWriteWidth(int width, int activeWidthBlocks, bool pixelsAreBlocks)
        {
            if (pixelsAreBlocks)
                return activeWidthBlocks;

            return width;
        }

        private static int GetRasterWriteHeight(int height, int activeHeightBlocks, bool pixelsAreBlocks)
        {
            if (pixelsAreBlocks)
                return activeHeightBlocks;

            return height;
        }

        private static int GetRasterWriteBlockSize(int blockSize, bool pixelsAreBlocks)
        {
            if (pixelsAreBlocks)
                return 1;

            return blockSize;
        }

        private static int GetByteCount(byte[] bytes)
        {
            return bytes != null ? bytes.Length : 0;
        }
    }
}
