using UnityEngine;
#if COMPILER_UDONSHARP
using VRC.SDKBase;
#endif

namespace K13A.TSMP
{
    public enum ShaderReceiveChannel { Off, Channel0, Channel1, Channel2, Channel3 }

    public static class ShaderReceiveAtlas
    {
        public const int EntryWords = 5;
        private const int HeaderWords = 5;
        private const int MaxFields = 1024;
        private const int MaxStateBytes = 1048576;
        private const int Width = 256;

        public static int Stage(ref uint[] entries, int count, ushort id, uint hash, int type, int offset, int length)
        {
            if (count < 0)
                return -1;
            int low = 0;
            int high = count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                int i = mid * EntryWords;
                uint key = entries[i] & 65535u;
                if (key < (uint)id || (key == (uint)id && entries[i + 1] < hash))
                    low = mid + 1;
                else
                    high = mid;
            }
            int entry = low * EntryWords;
            if (low == count || (entries[entry] & 65535u) != (uint)id || entries[entry + 1] != hash)
            {
                if (count == MaxFields)
                    return -1;
                if (entries == null || entries.Length < (count + 1) * EntryWords)
                {
                    uint[] expanded = new uint[Mathf.Max(8, (count + 1) * 2) * EntryWords];
                    if (count > 0)
                        System.Array.Copy(entries, expanded, count * EntryWords);
                    entries = expanded;
                }
                if (low < count)
                    System.Array.Copy(entries, entry, entries, entry + EntryWords, (count - low) * EntryWords);
                count++;
            }
            entries[entry] = (uint)id | ((uint)type << 16);
            entries[entry + 1] = hash;
            entries[entry + 3] = (uint)length;
            entries[entry + 4] = (uint)offset;
            return count;
        }

        public static bool Render(Material material, RenderTexture bytes, int prefix, int length, uint stream, uint frame,
            uint[] entries, int count, RenderTexture previous, ref RenderTexture target, ref Texture2D directory, ref byte[] upload)
        {
            if (count < 0 || material == null || bytes == null)
                return false;
            int words = HeaderWords + count * EntryWords;
            int next = words * 4 + ((length + 3) & ~3);
            int start = next;
            for (int i = 0; i < count * EntryWords; i += EntryWords)
            {
                entries[i + 2] = (uint)next;
                next += ((int)entries[i + 3] + 3) & ~3;
            }
            if (next - start > MaxStateBytes)
                return false;
            int rows = (words + Width - 1) / Width;
            if (directory == null || directory.height < rows)
            {
                GpuLuma4Writer.ReleaseTexture(directory);
                directory = new Texture2D(Width, rows, TextureFormat.RGBA32, false, true);
                directory.filterMode = FilterMode.Point;
                upload = new byte[Width * rows * 4];
            }
            Binary.WriteUInt32LE(upload, 0, 0x504d5354u);
            Binary.WriteUInt32LE(upload, 4, stream);
            Binary.WriteUInt32LE(upload, 8, frame);
            Binary.WriteUInt32LE(upload, 12, (uint)length);
            Binary.WriteUInt32LE(upload, 16, (uint)count);
            for (int i = 0; i < count * EntryWords; i++)
                Binary.WriteUInt32LE(upload, (HeaderWords + i) * 4, entries[i]);
            directory.LoadRawTextureData(upload);
            directory.Apply(false, false);

            int height = (next + Width * 4 - 1) / (Width * 4);
            if (target == null || target.height < height || !target.IsCreated())
                target = DecoderPredictionRuntime.EnsureTexture(target, Width, height);
            if (target == null)
                return false;

            material.SetTexture("_Directory", directory);
            material.SetTexture("_Previous", previous != null ? previous : bytes);
            material.SetVector("_Layout", new Vector4(count, length, prefix, bytes.width));
            material.SetFloat("_End", next);
#if !COMPILER_UDONSHARP
            RenderTexture active = RenderTexture.active;
#endif
            GraphicsBridge.Blit(bytes, target, material, 0);
#if !COMPILER_UDONSHARP
            RenderTexture.active = active;
#endif
            return true;
        }

        public static int PropertyId(ShaderReceiveChannel channel)
        {
            string name = "_UdonTSMPChannel" + ((int)channel - 1);
#if COMPILER_UDONSHARP
            return VRCShader.PropertyToID(name);
#else
            return Shader.PropertyToID(name);
#endif
        }

        public static void Bind(int propertyId, Texture texture)
        {
#if COMPILER_UDONSHARP
            VRCShader.SetGlobalTexture(propertyId, texture);
#else
            Shader.SetGlobalTexture(propertyId, texture);
#endif
        }
    }
}
