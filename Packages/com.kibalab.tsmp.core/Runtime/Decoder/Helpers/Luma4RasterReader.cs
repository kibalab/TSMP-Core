#if !COMPILER_UDONSHARP
using UnityEngine;

namespace K13A.TSMP
{
    public static class Luma4RasterReader
    {
        public static bool TryReadFrame(
            Texture2D texture,
            int blockSize,
            int headerByteCount,
            int payloadByteCount,
            out FrameHeader header,
            out byte[] payload,
            out string error)
        {
            header = default;
            payload = null;
            error = string.Empty;

            if (texture == null)
            {
                error = "Texture is null.";
                return false;
            }

            if (blockSize <= 0)
            {
                error = "Block size must be positive.";
                return false;
            }

            byte[] measuredPalette = ReadCalibrationPalette(texture, blockSize);
            if (!ValidatePalette(measuredPalette, out error))
                return false;

            if (!ValidateFinder(texture, blockSize, measuredPalette))
            {
                error = "Finder markers are invalid.";
                return false;
            }

            byte[] headerBytes = ReadBytes(texture, blockSize, Luma4Raster.HeaderARow, Luma4Raster.HeaderRows, headerByteCount, measuredPalette);
            if (!FrameHeader.TryRead(headerBytes, 0, out header))
            {
                error = "Header magic mismatch.";
                return false;
            }

            int bytesPerPayload = payloadByteCount;
            if (header.PayloadSize > 0)
                bytesPerPayload = header.PayloadSize;

            payload = ReadPayload(texture, blockSize, bytesPerPayload, measuredPalette);
            return true;
        }

        public static byte[] ReadCalibrationPalette(Texture2D texture, int blockSize)
        {
            var palette = new byte[16];
            var sums = new int[16];
            var counts = new int[16];

            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(texture.width, blockSize);
            int blocks = Mathf.Min(activeWidthBlocks, 32);

            for (int i = 0; i < blocks; i++)
            {
                int symbol = Mathf.Clamp(i / 2, 0, 15);
                sums[symbol] += SampleBlockLuma(texture, blockSize, i, Luma4Raster.CalibrationRow);
                counts[symbol]++;
            }

            for (int i = 0; i < 16; i++)
            {
                if (counts[i] > 0)
                    palette[i] = (byte)Mathf.RoundToInt(sums[i] / (float)counts[i]);
                else
                    palette[i] = ProtocolConstants.GetLuma4PaletteValue(i);
            }

            return palette;
        }

        public static bool ValidatePalette(byte[] palette, out string error)
        {
            error = string.Empty;

            if (palette == null || palette.Length < 16)
            {
                error = "Calibration palette is missing.";
                return false;
            }

            for (int i = 1; i < 16; i++)
            {
                if (palette[i] <= palette[i - 1])
                {
                    error = $"Calibration palette is not monotonic at symbol {i}.";
                    return false;
                }

                if (palette[i] - palette[i - 1] < 4)
                {
                    error = $"Calibration symbols {i - 1} and {i} are too close.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateFinder(Texture2D texture, int blockSize, byte[] palette)
        {
            int w = FrameCapacity.GetActiveWidthBlocks(texture.width, blockSize);
            int markerLength = Mathf.Min(8, w);

            for (int i = 0; i < markerLength; i++)
            {
                if (ReadSymbol(texture, blockSize, i, 0, palette) != (byte)(i % 2 == 0 ? 15 : 0))
                    return false;

                if (ReadSymbol(texture, blockSize, w - markerLength + i, 0, palette) != (byte)(i % 2 == 0 ? 0 : 15))
                    return false;
            }

            return true;
        }

        private static byte[] ReadBytes(
            Texture2D texture,
            int blockSize,
            int startRow,
            int rowCount,
            int byteCount,
            byte[] palette)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(texture.width, blockSize);
            int startBlock = startRow * activeWidthBlocks;
            int maxBlocks = activeWidthBlocks * rowCount;
            return ReadByteSequence(texture, blockSize, startBlock, maxBlocks, byteCount, palette);
        }

        private static byte[] ReadPayload(
            Texture2D texture,
            int blockSize,
            int byteCount,
            byte[] palette)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(texture.width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(texture.height, blockSize);
            int payloadRows = Mathf.Max(0, activeHeightBlocks - Luma4Raster.PayloadStartRow - Luma4Raster.ReservedEndRows);
            int payloadStartBlock = Luma4Raster.PayloadStartRow * activeWidthBlocks;
            int maxBlocks = activeWidthBlocks * payloadRows;
            return ReadByteSequence(texture, blockSize, payloadStartBlock, maxBlocks, byteCount, palette);
        }

        private static byte[] ReadByteSequence(
            Texture2D texture,
            int blockSize,
            int startBlock,
            int maxBlocks,
            int byteCount,
            byte[] palette)
        {
            var bytes = new byte[byteCount];
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(texture.width, blockSize);
            int blockCursor = startBlock;
            int maxBlockExclusive = startBlock + maxBlocks;

            for (int i = 0; i < byteCount && blockCursor + 1 < maxBlockExclusive; i++)
            {
                int x0 = blockCursor % activeWidthBlocks;
                int y0 = blockCursor / activeWidthBlocks;
                byte low = ReadSymbol(texture, blockSize, x0, y0, palette);
                blockCursor++;

                int x1 = blockCursor % activeWidthBlocks;
                int y1 = blockCursor / activeWidthBlocks;
                byte high = ReadSymbol(texture, blockSize, x1, y1, palette);
                blockCursor++;

                bytes[i] = SymbolCodec.NibblesToByte(low, high);
            }

            return bytes;
        }

        private static byte ReadSymbol(Texture2D texture, int blockSize, int blockX, int blockY, byte[] palette)
        {
            byte luma = SampleBlockLuma(texture, blockSize, blockX, blockY);
            return SymbolCodec.ClassifyLuma4(luma, palette);
        }

        private static byte SampleBlockLuma(Texture2D texture, int blockSize, int blockX, int blockY)
        {
            int sampleSize = blockSize >= 8 ? 4 : 3;
            int startOffset = (blockSize - sampleSize) / 2;
            int startX = blockX * blockSize + startOffset;
            int topY = blockY * blockSize + startOffset;
            int sum = 0;
            int count = 0;

            for (int y = 0; y < sampleSize; y++)
            {
                int pixelY = texture.height - 1 - (topY + y);
                if (pixelY < 0 || pixelY >= texture.height)
                    continue;

                for (int x = 0; x < sampleSize; x++)
                {
                    int pixelX = startX + x;
                    if (pixelX < 0 || pixelX >= texture.width)
                        continue;

                    Color32 color = texture.GetPixel(pixelX, pixelY);
                    sum += (color.r + color.g + color.b) / 3;
                    count++;
                }
            }

            if (count == 0)
                return 0;

            return (byte)Mathf.RoundToInt(sum / (float)count);
        }
    }
}
#endif
