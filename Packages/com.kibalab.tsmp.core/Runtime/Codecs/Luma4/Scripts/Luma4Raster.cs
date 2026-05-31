using UnityEngine;

namespace K13A.TSMP
{
    public static class Luma4Raster
    {
        public const int FinderRow = 0;
        public const int CalibrationRow = 1;
        public const int HeaderARow = 2;
        public const int HeaderRows = 3;
        public const int PayloadStartRow = 5;
        public const int ReservedEndRows = 1;
        public const ushort LayoutInterleavedPayload = 1 << 0;

        public static int GetHeaderCapacityBytes(int width, int blockSize)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            return activeWidthBlocks * HeaderRows * ProtocolConstants.Luma4BitsPerSymbol / 8;
        }

        public static int GetPayloadCapacityBytes(int width, int height, int blockSize)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(height, blockSize);
            int payloadRows = Mathf.Max(0, activeHeightBlocks - PayloadStartRow - ReservedEndRows);
            return activeWidthBlocks * payloadRows * ProtocolConstants.Luma4BitsPerSymbol / 8;
        }

        public static int GetPayloadBlocksForBytes(int byteCount)
        {
            return Mathf.Max(0, byteCount) * 2;
        }

        public static Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            return texture;
        }

        public static bool TryWriteFrame(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
        {
            error = string.Empty;

            if (!ValidateWrite(texture, blockSize, headerBytes, payloadBytes, out error))
                return false;

            int headerCapacity = GetHeaderCapacityBytes(texture.width, blockSize);
            if (headerBytes.Length > headerCapacity)
            {
                error = $"Header requires {headerBytes.Length} bytes but header region only fits {headerCapacity} bytes.";
                return false;
            }

            int payloadCapacity = GetPayloadCapacityBytes(texture.width, texture.height, blockSize);
            int payloadCapacityBlocks = GetPayloadCapacityBlocks(texture.width, texture.height, blockSize);
            int requiredPayloadBlocks = GetPayloadBlocksForBytes(payloadBytes.Length);
            if (payloadBytes.Length > payloadCapacity || requiredPayloadBlocks > payloadCapacityBlocks)
            {
                error = $"Payload requires {payloadBytes.Length} bytes / {requiredPayloadBlocks} blocks but payload region only fits {payloadCapacity} bytes / {payloadCapacityBlocks} blocks.";
                return false;
            }

            Color32[] pixels = FrameRaster.CreateClearedPixels(texture.width, texture.height);
            WriteBaseRegions(pixels, texture.width, texture.height, blockSize, headerBytes);
            WritePayload(pixels, texture.width, texture.height, blockSize, payloadBytes, payloadBytes.Length);
            FrameRaster.WriteEndMarker(pixels, texture.width, texture.height, blockSize);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return true;
        }

        public static bool ValidateWrite(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
        {
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

            if (headerBytes == null)
            {
                error = "Header bytes are null.";
                return false;
            }

            if (payloadBytes == null)
            {
                error = "Payload bytes are null.";
                return false;
            }

            return true;
        }

        public static void WriteBaseRegions(Color32[] pixels, int width, int height, int blockSize, byte[] headerBytes)
        {
            FrameRaster.WriteFinder(pixels, width, height, blockSize);
            FrameRaster.WriteLuma4Calibration(pixels, width, height, blockSize);
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            FrameRaster.WriteLuma4Bytes(pixels, width, height, blockSize, HeaderARow * activeWidthBlocks, activeWidthBlocks * HeaderRows, headerBytes, 0, headerBytes != null ? headerBytes.Length : 0);
        }

        public static void WritePayload(Color32[] pixels, int width, int height, int blockSize, byte[] payloadBytes, int payloadByteCount)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(height, blockSize);
            int payloadRows = Mathf.Max(0, activeHeightBlocks - PayloadStartRow - ReservedEndRows);
            int payloadStartBlock = PayloadStartRow * activeWidthBlocks;
            int maxBlocks = activeWidthBlocks * payloadRows;
            FrameRaster.WriteLuma4Bytes(pixels, width, height, blockSize, payloadStartBlock, maxBlocks, payloadBytes, 0, payloadByteCount);
        }

        private static int GetPayloadCapacityBlocks(int width, int height, int blockSize)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(height, blockSize);
            int payloadRows = Mathf.Max(0, activeHeightBlocks - PayloadStartRow - ReservedEndRows);
            return activeWidthBlocks * payloadRows;
        }
    }

    public static class FrameRaster
    {
        public static Color32[] CreateClearedPixels(int width, int height)
        {
            var pixels = new Color32[Mathf.Max(0, width * height)];
            Color32 neutral = new Color32(128, 128, 128, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = neutral;

            return pixels;
        }

        public static void WriteFinder(Color32[] pixels, int width, int height, int blockSize)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(height, blockSize);
            int markerLength = Mathf.Min(8, activeWidthBlocks);

            for (int i = 0; i < markerLength; i++)
            {
                WriteLuma4Block(pixels, width, height, blockSize, i, 0, (byte)(i % 2 == 0 ? 15 : 0));
                WriteLuma4Block(pixels, width, height, blockSize, activeWidthBlocks - markerLength + i, 0, (byte)(i % 2 == 0 ? 0 : 15));
                WriteLuma4Block(pixels, width, height, blockSize, i, activeHeightBlocks - 1, (byte)((i / 2) % 2 == 0 ? 15 : 0));
                WriteLuma4Block(pixels, width, height, blockSize, activeWidthBlocks - markerLength + i, activeHeightBlocks - 1, (byte)((i / 2) % 2 == 0 ? 0 : 15));
            }
        }

        public static void WriteLuma4Calibration(Color32[] pixels, int width, int height, int blockSize)
        {
            int blocks = Mathf.Min(FrameCapacity.GetActiveWidthBlocks(width, blockSize), 32);
            for (int i = 0; i < blocks; i++)
                WriteLuma4Block(pixels, width, height, blockSize, i, Luma4Raster.CalibrationRow, (byte)(i / 2));
        }

        public static void WriteEndMarker(Color32[] pixels, int width, int height, int blockSize)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            int activeHeightBlocks = FrameCapacity.GetActiveHeightBlocks(height, blockSize);

            for (int x = 0; x < Mathf.Min(16, activeWidthBlocks); x++)
                WriteLuma4Block(pixels, width, height, blockSize, x, activeHeightBlocks - 1, (byte)(x % 2 == 0 ? 15 : 0));
        }

        public static void WriteLuma4Bytes(Color32[] pixels, int width, int height, int blockSize, int startBlock, int maxBlocks, byte[] bytes, int byteOffset, int byteCount)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            if (activeWidthBlocks <= 0 || maxBlocks <= 0)
                return;

            int blockCursor = startBlock;
            int maxBlockExclusive = startBlock + maxBlocks;
            int end = Mathf.Min(byteOffset + byteCount, bytes != null ? bytes.Length : 0);

            for (int i = byteOffset; i < end && blockCursor + 1 < maxBlockExclusive; i++)
            {
                byte value = bytes[i];
                int x0 = blockCursor % activeWidthBlocks;
                int y0 = blockCursor / activeWidthBlocks;
                WriteLuma4Block(pixels, width, height, blockSize, x0, y0, SymbolCodec.ByteToLowNibble(value));
                blockCursor++;

                int x1 = blockCursor % activeWidthBlocks;
                int y1 = blockCursor / activeWidthBlocks;
                WriteLuma4Block(pixels, width, height, blockSize, x1, y1, SymbolCodec.ByteToHighNibble(value));
                blockCursor++;
            }
        }

        public static void WriteLuma4BytesToBlockTexture(Color32[] pixels, int widthBlocks, int heightBlocks, int startBlock, int maxBlocks, byte[] bytes, int byteOffset, int byteCount, Color32[] luma4Colors)
        {
            if (widthBlocks <= 0 || heightBlocks <= 0 || pixels == null || bytes == null)
                return;

            if (luma4Colors == null || luma4Colors.Length < 16)
            {
                WriteLuma4Bytes(pixels, widthBlocks, heightBlocks, 1, startBlock, maxBlocks, bytes, byteOffset, byteCount);
                return;
            }

            int blockCursor = startBlock;
            int maxBlockExclusive = startBlock + maxBlocks;
            int totalBlocks = widthBlocks * heightBlocks;
            if (maxBlockExclusive > totalBlocks)
                maxBlockExclusive = totalBlocks;

            int availableBlocks = maxBlockExclusive - blockCursor;
            if (availableBlocks < 2)
                return;

            int writeCount = availableBlocks / 2;
            int end = Mathf.Min(byteOffset + byteCount, bytes.Length);
            int requestedCount = end - byteOffset;
            if (requestedCount <= 0)
                return;

            if (writeCount > requestedCount)
                writeCount = requestedCount;

            int blockY = blockCursor / widthBlocks;
            if (blockY < 0 || blockY >= heightBlocks)
                return;

            int blockX = blockCursor - blockY * widthBlocks;
            int pixelIndex = (heightBlocks - 1 - blockY) * widthBlocks + blockX;

            if ((widthBlocks & 1) == 0 && blockX == 0)
            {
                int byteIndex = byteOffset;
                int remaining = writeCount;
                int bytesPerRow = widthBlocks / 2;

                while (remaining > 0 && blockY < heightBlocks)
                {
                    int rowByteCount = bytesPerRow;
                    if (rowByteCount > remaining)
                        rowByteCount = remaining;

                    pixelIndex = (heightBlocks - 1 - blockY) * widthBlocks;
                    for (int i = 0; i < rowByteCount; i++)
                    {
                        byte value = bytes[byteIndex];
                        byteIndex++;
                        pixels[pixelIndex] = luma4Colors[value & 0x0F];
                        pixelIndex++;
                        pixels[pixelIndex] = luma4Colors[(value >> 4) & 0x0F];
                        pixelIndex++;
                    }

                    remaining -= rowByteCount;
                    blockY++;
                }

                return;
            }

            for (int i = 0; i < writeCount; i++)
            {
                byte value = bytes[byteOffset + i];
                pixels[pixelIndex] = luma4Colors[value & 0x0F];

                blockX++;
                if (blockX >= widthBlocks)
                {
                    blockY++;
                    if (blockY >= heightBlocks)
                        return;

                    blockX = 0;
                    pixelIndex = (heightBlocks - 1 - blockY) * widthBlocks;
                }
                else
                {
                    pixelIndex++;
                }

                pixels[pixelIndex] = luma4Colors[(value >> 4) & 0x0F];

                blockX++;
                if (blockX >= widthBlocks)
                {
                    blockY++;
                    if (blockY >= heightBlocks)
                        return;

                    blockX = 0;
                    pixelIndex = (heightBlocks - 1 - blockY) * widthBlocks;
                }
                else
                {
                    pixelIndex++;
                }
            }
        }

        public static void WriteLuma4Block(Color32[] pixels, int width, int height, int blockSize, int blockX, int blockY, byte symbol)
        {
            byte luma = ProtocolConstants.GetLuma4PaletteValue(symbol);
            WriteColorBlock(pixels, width, height, blockSize, blockX, blockY, new Color32(luma, luma, luma, 255));
        }

        public static void WriteColorBlockAtIndex(Color32[] pixels, int width, int height, int blockSize, int blockIndex, Color32 color)
        {
            int activeWidthBlocks = FrameCapacity.GetActiveWidthBlocks(width, blockSize);
            if (activeWidthBlocks <= 0)
                return;

            if (blockSize == 1)
            {
                int blockY = blockIndex / activeWidthBlocks;
                int blockX = blockIndex - blockY * activeWidthBlocks;
                int pixelIndex = (height - 1 - blockY) * width + blockX;
                if (pixels != null && pixelIndex >= 0 && pixelIndex < pixels.Length)
                    pixels[pixelIndex] = color;
                return;
            }

            WriteColorBlock(pixels, width, height, blockSize, blockIndex % activeWidthBlocks, blockIndex / activeWidthBlocks, color);
        }

        public static void WriteColorBlock(Color32[] pixels, int width, int height, int blockSize, int blockX, int blockY, Color32 color)
        {
            if (pixels == null)
                return;

            if (blockSize == 1)
            {
                int pixelX = blockX;
                int pixelY = height - 1 - blockY;
                int pixelIndex = pixelY * width + pixelX;
                if (pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height && pixelIndex >= 0 && pixelIndex < pixels.Length)
                    pixels[pixelIndex] = color;
                return;
            }

            int startX = blockX * blockSize;
            int topY = blockY * blockSize;

            for (int y = 0; y < blockSize; y++)
            {
                int pixelY = height - 1 - (topY + y);
                if (pixelY < 0 || pixelY >= height)
                    continue;

                int rowOffset = pixelY * width;
                for (int x = 0; x < blockSize; x++)
                {
                    int pixelX = startX + x;
                    if (pixelX < 0 || pixelX >= width)
                        continue;

                    int pixelIndex = rowOffset + pixelX;
                    if (pixelIndex >= 0 && pixelIndex < pixels.Length)
                        pixels[pixelIndex] = color;
                }
            }
        }

        public static uint ReadBits(byte[] bytes, int byteCount, int bitOffset, int bitCount)
        {
            int byteIndex = bitOffset / 8;
            int bitShift = bitOffset - byteIndex * 8;
            uint bits = 0u;
            int bytesNeeded = (bitShift + bitCount + 7) / 8;

            for (int i = 0; i < bytesNeeded && i < 4; i++)
            {
                int index = byteIndex + i;
                if (bytes != null && index >= 0 && index < byteCount && index < bytes.Length)
                    bits |= (uint)bytes[index] << (i * 8);
            }

            uint mask = bitCount >= 32 ? 0xFFFFFFFFu : (1u << bitCount) - 1u;
            return (bits >> bitShift) & mask;
        }
    }
}
