using UnityEngine;

namespace K13A.TSMP
{
    public static class ByteTextureReader
    {
        public static int GetRequiredPixelCount(int byteCount)
        {
            if (byteCount <= 0)
                return 0;

            return (byteCount + 3) / 4;
        }

        public static int GetCapacityBytes(int textureWidth, int textureHeight)
        {
            if (textureWidth <= 0)
                return 0;
            if (textureHeight <= 0)
                return 0;

            return textureWidth * textureHeight * 4;
        }

        public static bool TryGetReadbackRegion(
            int byteCount,
            int textureWidth,
            int textureHeight,
            out int requiredPixels,
            out int readWidth,
            out int readHeight,
            out int readbackPixelCount)
        {
            requiredPixels = GetRequiredPixelCount(byteCount);
            if (requiredPixels < 1)
                requiredPixels = 1;

            readWidth = 0;
            readHeight = 0;
            readbackPixelCount = 0;

            if (textureWidth <= 0)
                return false;
            if (textureHeight <= 0)
                return false;

            if (requiredPixels < textureWidth)
                readWidth = requiredPixels;
            else
                readWidth = textureWidth;

            readHeight = (requiredPixels + textureWidth - 1) / textureWidth;
            if (readHeight < 1)
                readHeight = 1;
            if (readHeight > textureHeight)
                return false;

            readbackPixelCount = readWidth * readHeight;
            return true;
        }

        public static bool CanCopyBytes(Color32[] pixels, byte[] destination, int byteCount)
        {
            if (pixels == null)
                return false;
            if (destination == null)
                return false;
            if (byteCount < 0)
                return false;
            if (destination.Length < byteCount)
                return false;

            return pixels.Length >= GetRequiredPixelCount(byteCount);
        }

        public static bool CopyBytes(Color32[] pixels, byte[] destination, int byteCount)
        {
            if (!CanCopyBytes(pixels, destination, byteCount))
                return false;

            int fullPixelCount = byteCount / 4;
            int byteIndex = 0;
            for (int i = 0; i < fullPixelCount; i++)
            {
                Color32 pixel = pixels[i];
                destination[byteIndex++] = pixel.r;
                destination[byteIndex++] = pixel.g;
                destination[byteIndex++] = pixel.b;
                destination[byteIndex++] = pixel.a;
            }

            int remaining = byteCount - byteIndex;
            if (remaining > 0)
            {
                Color32 pixel = pixels[fullPixelCount];
                destination[byteIndex++] = pixel.r;
                if (remaining > 1)
                    destination[byteIndex++] = pixel.g;
                if (remaining > 2)
                    destination[byteIndex] = pixel.b;
            }

            return true;
        }
    }
}
