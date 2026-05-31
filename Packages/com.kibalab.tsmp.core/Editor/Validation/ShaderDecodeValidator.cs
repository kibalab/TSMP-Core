using System.IO;
using K13A.TSMP;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    public static class ShaderDecodeValidator
    {
        private const string SamplePath = "Assets/TSMP/Generated/tsmp_sample_640x360.png";
        private const string ShaderName = "Hidden/TSMP/Decode Luma4 Bytes";

        [MenuItem("Tools/TSMP/Validate Shader Byte Decode")]
        public static void ValidateShaderByteDecode()
        {
            if (!File.Exists(SamplePath))
            {
                Debug.LogError($"[TSMP] sample PNG is missing: {SamplePath}");
                return;
            }

            AssetDatabase.ImportAsset(SamplePath, ImportAssetOptions.ForceUpdate);
            ConfigureSourceImport();

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(SamplePath);
            if (source == null)
            {
                Debug.LogError($"[TSMP] Failed to load sample PNG: {SamplePath}");
                return;
            }
            source.filterMode = FilterMode.Point;
            source.wrapMode = TextureWrapMode.Clamp;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[TSMP] Shader not found: {ShaderName}");
                return;
            }

            Material material = new Material(shader);
            try
            {
                const int activeWidthBlocks = 80;
                const int headerByteCount = FrameHeader.Size;

                bool selectedFlipY = true;
                byte[] header = DecodeBytes(
                    source,
                    material,
                    startBlock: 2 * activeWidthBlocks,
                    byteCount: headerByteCount,
                    flipY: true);

                if (!FrameHeader.TryRead(header, 0, out FrameHeader frameHeader))
                {
                    byte[] noFlipHeader = DecodeBytes(
                        source,
                        material,
                        startBlock: 2 * activeWidthBlocks,
                        byteCount: headerByteCount,
                        flipY: false);

                    if (!FrameHeader.TryRead(noFlipHeader, 0, out frameHeader))
                    {
                        Debug.LogError(
                            "[TSMP] Shader byte decode failed: header validation mismatch. "
                            + $"flipY header[0..15]={FormatBytes(header, 0, Mathf.Min(16, header.Length))}, "
                            + $"noFlip header[0..15]={FormatBytes(noFlipHeader, 0, Mathf.Min(16, noFlipHeader.Length))}");
                        return;
                    }

                    header = noFlipHeader;
                    selectedFlipY = false;
                }

                byte[] payload = DecodeBytes(
                    source,
                    material,
                    startBlock: Luma4Raster.PayloadStartRow * activeWidthBlocks,
                    byteCount: frameHeader.PayloadSize,
                    flipY: selectedFlipY);

                if (!NetworkFrameReader.TryReadNetworkFrameHeader(
                        payload,
                        out int messageCount,
                        out _))
                {
                    Debug.LogError("[TSMP] shader byte decode validation failed: network frame header is invalid.");
                    return;
                }

                Debug.Log(
                    "[TSMP] shader byte decode validation passed. "
                    + $"frameIndex={frameHeader.FrameIndex}, "
                    + $"payloadBytes={payload.Length}, "
                    + $"messageCount={messageCount}");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static byte[] DecodeBytes(Texture2D source, Material material, int startBlock, int byteCount, bool flipY)
        {
            int width = Mathf.CeilToInt(byteCount / 4f);
            var descriptor = new RenderTextureDescriptor(width, 1, RenderTextureFormat.ARGB32, 0)
            {
                autoGenerateMips = false,
                depthBufferBits = 0,
                enableRandomWrite = false,
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                volumeDepth = 1
            };

            var rt = new RenderTexture(descriptor)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            material.SetTexture("_MainTex", source);
            material.SetFloat("_BlockSize", ProtocolConstants.DefaultBlockSize);
            material.SetFloat("_StartBlock", startBlock);
            material.SetFloat("_ByteCount", byteCount);
            material.SetFloat("_ActiveWidthBlocks", source.width / ProtocolConstants.DefaultBlockSize);
            material.SetFloat("_SourceWidth", source.width);
            material.SetFloat("_SourceHeight", source.height);
            material.SetFloat("_OutputWidth", width);
            material.SetFloat("_OutputHeight", 1);
            material.SetFloat("_FlipY", flipY ? 1f : 0f);

            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;

            try
            {
                bool previousSrgbWrite = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = false;
                    Graphics.Blit(source, rt, material);
                }
                finally
                {
                    GL.sRGBWrite = previousSrgbWrite;
                }

                RenderTexture.active = rt;

                readback = new Texture2D(width, 1, TextureFormat.RGBA32, false, true);
                readback.ReadPixels(new Rect(0, 0, width, 1), 0, 0, false);
                readback.Apply(false, false);

                Color32[] pixels = readback.GetPixels32();
                byte[] bytes = new byte[byteCount];
                int availableByteCount = Mathf.Min(byteCount, pixels.Length * 4);
                ByteTextureReader.CopyBytes(pixels, bytes, availableByteCount);
                return bytes;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null)
                    Object.DestroyImmediate(readback);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        private static void ConfigureSourceImport()
        {
            TextureImporter importer = AssetImporter.GetAtPath(SamplePath) as TextureImporter;
            if (importer == null)
                return;

            bool dirty = false;

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                dirty = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                dirty = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                dirty = true;
            }

            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                dirty = true;
            }

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                dirty = true;
            }

            if (dirty)
                importer.SaveAndReimport();
        }

        private static string FormatBytes(byte[] bytes, int offset, int count)
        {
            if (bytes == null || count <= 0)
                return string.Empty;

            System.Text.StringBuilder builder = new System.Text.StringBuilder(count * 3);
            int end = Mathf.Min(bytes.Length, offset + count);

            for (int i = offset; i < end; i++)
            {
                if (i > offset)
                    builder.Append(' ');

                builder.Append(bytes[i].ToString("X2"));
            }

            return builder.ToString();
        }
    }
}
