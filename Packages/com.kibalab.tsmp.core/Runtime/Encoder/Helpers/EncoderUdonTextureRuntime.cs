using UnityEngine;

namespace K13A.TSMP
{
    public static class EncoderUdonTextureRuntime
    {
        public static int ClampBlockSize(int blockSize)
        {
            return Mathf.Max(1, blockSize);
        }

        public static int ClampDimension(int value, int blockSize)
        {
            return Mathf.Max(blockSize, value);
        }

        public static int ClampFrameRate(int frameRate)
        {
            return Mathf.Clamp(frameRate, 1, 120);
        }

        public static int GetActiveBlockCount(int pixels, int blockSize)
        {
            return Mathf.Max(0, pixels / blockSize);
        }

        public static bool ShouldUseBlockTexture(bool useBlockSymbolTexture, Material blockExpandMaterial)
        {
            return useBlockSymbolTexture && blockExpandMaterial != null;
        }

        public static int GetSymbolTextureSize(int pixels, int activeBlocks, bool usingBlockTexture)
        {
            int size = usingBlockTexture ? activeBlocks : pixels;
            return Mathf.Max(1, size);
        }

        public static Color32[] EnsurePixelBuffer(Color32[] pixels, int width, int height)
        {
            int pixelCount = width * height;
            if (pixels == null || pixels.Length != pixelCount)
                return new Color32[pixelCount];

            return pixels;
        }

        public static void ClearPixelBuffer(Color32[] pixels)
        {
            if (pixels == null)
                return;

            Color32 clear = new Color32(0, 0, 0, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;
        }

        public static void CopyPixelBuffer(Color32[] source, Color32[] destination)
        {
            if (source == null || destination == null)
                return;

            int count = source.Length < destination.Length ? source.Length : destination.Length;
            for (int i = 0; i < count; i++)
                destination[i] = source[i];
        }

        public static Texture2D EnsureOutputTexture(Texture2D outputTexture, int width, int height)
        {
            if (outputTexture != null && outputTexture.width == width && outputTexture.height == height)
                return outputTexture;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        public static Color32[] EnsureLuma4Colors(Color32[] colors)
        {
            if (colors != null && colors.Length == 16)
                return colors;

            return SymbolCodec.CreateLuma4Colors();
        }

        public static void BlitEncodedTexture(
            Texture2D outputTexture,
            RenderTexture output,
            bool usingBlockTexture,
            Material blockExpandMaterial,
            int activeWidthBlocks,
            int activeHeightBlocks)
        {
            if (output == null || outputTexture == null)
                return;

            if (usingBlockTexture && blockExpandMaterial != null)
            {
                blockExpandMaterial.SetFloat(ShaderProperties.SourceBlockWidth, activeWidthBlocks);
                blockExpandMaterial.SetFloat(ShaderProperties.SourceBlockHeight, activeHeightBlocks);
                GraphicsBridge.Blit(outputTexture, output, blockExpandMaterial);
                return;
            }

            GraphicsBridge.Blit(outputTexture, output);
        }
    }
}
