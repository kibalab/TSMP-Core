using UnityEngine;

namespace K13A.TSMP
{
    public static class DecoderReadbackRuntime
    {
        public static bool ValidateSetup(Texture sourceTexture, RenderTexture payloadByteTexture, TSMPCodec[] codecHandlers, out string error)
        {
            error = string.Empty;

            if (sourceTexture == null)
            {
                error = "Source texture is not assigned.";
                return false;
            }

            if (CodecBridge.FindHandler(codecHandlers, 0) == null)
            {
                error = "Luma4 codec handler is not assigned.";
                return false;
            }

            if (payloadByteTexture == null)
            {
                error = "Payload byte RenderTexture is not assigned.";
                return false;
            }

            return true;
        }

        public static byte[] EnsureByteBuffer(byte[] buffer, int byteCount)
        {
            if (byteCount < 0)
                byteCount = 0;
            if (buffer == null || buffer.Length != byteCount)
                return new byte[byteCount];

            return buffer;
        }

        public static byte[] EnsureCodecOptionBuffer(byte[] buffer)
        {
            return EnsureByteBuffer(buffer, FrameHeader.MaxCodecOptionBytes);
        }

        public static Color32[] EnsurePixelBuffer(Color32[] pixels, int pixelCount)
        {
            if (pixelCount < 0)
                pixelCount = 0;
            if (pixels == null || pixels.Length < pixelCount)
                return new Color32[pixelCount];

            return pixels;
        }

        public static bool TryGetReadbackRegion(
            RenderTexture payloadByteTexture,
            int byteCount,
            out int requiredPixels,
            out int readWidth,
            out int readHeight,
            out int readbackPixelCount,
            out string error)
        {
            requiredPixels = 0;
            readWidth = 0;
            readHeight = 0;
            readbackPixelCount = 0;
            error = string.Empty;

            if (payloadByteTexture == null)
            {
                error = "Payload byte RenderTexture is not assigned.";
                return false;
            }

            int textureWidth = payloadByteTexture.width;
            int textureHeight = payloadByteTexture.height;
            if (textureWidth <= 0 || textureHeight <= 0)
            {
                error = "Payload byte RenderTexture has invalid dimensions. width=" + textureWidth + " height=" + textureHeight;
                return false;
            }

            if (!ByteTextureReader.TryGetReadbackRegion(byteCount, textureWidth, textureHeight, out requiredPixels, out readWidth, out readHeight, out readbackPixelCount))
            {
                error = "Byte texture is too small. requiredPixels=" + requiredPixels + " capacityPixels=" + (textureWidth * textureHeight) + " byteCount=" + byteCount;
                return false;
            }

            return true;
        }

        public static bool TryCopyBytes(Color32[] pixels, byte[] destination, int byteCount, out int expectedPixels, out string error)
        {
            expectedPixels = ByteTextureReader.GetRequiredPixelCount(byteCount);
            error = string.Empty;

            if (pixels == null || destination == null)
            {
                error = "Readback buffers are not initialized.";
                return false;
            }

            if (pixels.Length < expectedPixels)
            {
                error = "Byte texture is too small. requiredPixels=" + expectedPixels + " actualPixels=" + pixels.Length + " byteCount=" + byteCount;
                return false;
            }

            if (!ByteTextureReader.CopyBytes(pixels, destination, byteCount))
            {
                error = "Failed to copy byte texture data. byteCount=" + byteCount;
                return false;
            }

            return true;
        }
    }
}
