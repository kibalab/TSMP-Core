using UnityEngine;

namespace K13A.TSMP
{
    public static class DecoderPredictionRuntime
    {
        public static bool HeadersMatch(byte[] previous, byte[] current)
        {
            if (previous == null || current == null || previous.Length < FrameHeader.Size || current.Length < FrameHeader.Size)
                return false;

            for (int i = 0; i < FrameHeader.BytesBeforeCrc; i++)
            {
                if (i >= FrameHeader.FrameIndexOffset && i < FrameHeader.CodecIdOffset)
                    continue;
                if (i >= FrameHeader.PayloadSizeOffset && i < FrameHeader.PayloadReservedOffset)
                    continue;
                if (i >= FrameHeader.PayloadCrc32Offset && i < FrameHeader.PayloadCrc32Offset + FrameHeader.PayloadCrc32Bytes)
                    continue;
                if ((int)previous[i] != (int)current[i])
                    return false;
            }
            return true;
        }

        public static RenderTexture EnsureTexture(RenderTexture texture, int width, int height)
        {
            if (texture == null)
            {
                texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                texture.name = "TSMP Predicted Readback";
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.useMipMap = false;
                texture.autoGenerateMips = false;
                texture.antiAliasing = 1;
            }
            else if (texture.width != width || texture.height != height)
            {
                texture.Release();
                texture.width = width;
                texture.height = height;
            }

            if (!texture.IsCreated() && !texture.Create())
            {
                DecoderSnapshotRuntime.Release(texture);
                return null;
            }
            return texture;
        }
    }
}
