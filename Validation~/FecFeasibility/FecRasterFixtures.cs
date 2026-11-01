#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using K13A.TSMP;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Measurement fixture only: a deliberately unsupported payload type prevents
// existing TSMP receivers from interpreting this experimental FEC envelope.
public static class FecRasterFixtures
{
    [Serializable] public sealed class Plan { public Fixture[] fixtures; }
    [Serializable] public sealed class Fixture
    {
        public string name, codecPrefabPath, sourceDirectory;
    }
    static string root;
    static IEnumerator work;
    static readonly List<Object> owned = new List<Object>();
    static readonly List<string> details = new List<string>();
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Digest(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    public static void Run()
    {
        Check(typeof(TSMPBehaviour).BaseType == typeof(MonoBehaviour), "Dedicated SDK-free project required.");
        root = Path.GetFullPath(Environment.GetEnvironmentVariable("TSMP_FEC_FIXTURE_RESULTS"));
        string plan = Environment.GetEnvironmentVariable("TSMP_FEC_FIXTURE_PLAN");
        Directory.CreateDirectory(root);
        Check(!File.Exists(Path.Combine(root, "result.txt")), "Fresh results required.");
        string json = File.ReadAllText(plan);
        File.WriteAllText(Path.Combine(root, "plan-used.json"), json);
        details.Clear(); owned.Clear();
        details.Add("Actual production CPU codec raster writer; experimental FEC envelope. No GPU/transport/client result.");
        work = Generate(JsonUtility.FromJson<Plan>(json));
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        foreach (Object item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
        if (error != null) details.Add(error.ToString());
        File.WriteAllLines(Path.Combine(root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(details));
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Generate(Plan plan)
    {
        Check(plan != null && plan.fixtures != null && plan.fixtures.Length > 0, "Plan fixtures required.");
        uint[] table = Crc32Runtime.EnsureTable(null);
        foreach (Fixture fixture in plan.fixtures)
        {
            Check(!string.IsNullOrEmpty(fixture.name) && fixture.name.All(c => char.IsLetterOrDigit(c) || c == '-'), "Simple fixture name required.");
            string target = Path.Combine(root, fixture.name);
            Check(!Directory.Exists(target), "Existing fixture must be preserved.");
            foreach (string directory in new[] { "input", "header", "payload" }) Directory.CreateDirectory(Path.Combine(target, directory));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture.codecPrefabPath);
            Check(prefab != null, "Codec prefab missing.");
            var instance = Object.Instantiate(prefab); owned.Add(instance);
            var codec = instance.GetComponent<TSMPCodec>(); Check(codec != null, "Codec missing.");
            byte[] options = codec.GetCodecOptionBytes();
            string[] paths = Directory.GetFiles(Path.Combine(fixture.sourceDirectory, "payload"), "frame-*.bin").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Check(paths.Length == 90, "Expected ninety independently prepared packets.");
            Texture2D image = null;
            var rows = new List<string> { "frame,bytes,headerSha256,payloadSha256,pngSha256" };
            for (int ordinal = 1; ordinal <= paths.Length; ordinal++)
            {
                string name = "frame-" + ordinal.ToString("000000", CultureInfo.InvariantCulture);
                Check(Path.GetFileName(paths[ordinal - 1]) == name + ".bin", "Contiguous frame bins required.");
                byte[] payload = File.ReadAllBytes(paths[ordinal - 1]);
                byte[] headerBytes = File.ReadAllBytes(Path.Combine(fixture.sourceDirectory, "header", name + ".bin"));
                FrameHeader header;
                Check(FrameHeader.TryRead(headerBytes, 0, out header), "Invalid independent header.");
                Check(header.FrameIndex == ordinal && header.PayloadSize == payload.Length && header.BlockSize == 8 && header.DecodeSampleSize == 4, "Header geometry/length mismatch.");
                Check(((int)header.PayloadType == 0xF100 && header.LayoutId == 0xFEC1) ||
                    ((int)header.PayloadType == 0xF101 && header.LayoutId == 0xFEC2), "Experimental payload marker missing.");
                Check((header.Flags & FrameFlags.PayloadCrc32) != 0 && Crc32Runtime.Compute(payload, 0, payload.Length, table) == header.PayloadCrc32, "Independent packet CRC mismatch.");
                int width = header.ActiveWidthBlocks * 8, height = header.ActiveHeightBlocks * 8;
                Check(width == 1280 && height == 720, "Native 720p required.");
                if (image == null) { image = new Texture2D(width, height, TextureFormat.RGBA32, false, true); image.filterMode = FilterMode.Point; owned.Add(image); }
                Check(payload.Length <= codec.GetPayloadCapacityBytes(width, height, 8), "FEC packet exceeds raster capacity.");
                header.CodecId = codec.codecId; header.SymbolMode = (byte)codec.SymbolMode;
                header.CodecOptionLength = (byte)options.Length;
                header.CodecOption0 = options.Length > 0 ? options[0] : (byte)0;
                header.CodecOption1 = options.Length > 1 ? options[1] : (byte)0;
                header.CodecOption2 = options.Length > 2 ? options[2] : (byte)0;
                header.CodecOption3 = options.Length > 3 ? options[3] : (byte)0;
                header.CodecOption4 = options.Length > 4 ? options[4] : (byte)0;
                headerBytes = new byte[56]; header.WriteTo(headerBytes, 0);
                string payloadHash = Digest(payload), headerHash = Digest(headerBytes);
                File.WriteAllBytes(Path.Combine(target, "payload", name + ".bin"), payload);
                File.WriteAllBytes(Path.Combine(target, "header", name + ".bin"), headerBytes);
                string error;
                Check(codec.TryWriteFrame(image, 8, headerBytes, payload, out error), "Actual raster failed: " + error);
                Check(Digest(payload) == payloadHash && Digest(headerBytes) == headerHash, "Raster mutated independent inputs.");
                byte[] png = image.EncodeToPNG(); Check(png != null && png.Length > 0, "PNG unavailable.");
                File.WriteAllBytes(Path.Combine(target, "input", name + ".png"), png);
                rows.Add(ordinal + "," + payload.Length + "," + headerHash + "," + payloadHash + "," + Digest(png));
                yield return null;
            }
            File.WriteAllLines(Path.Combine(target, "source-frames.csv"), rows);
            details.Add("PASS " + fixture.name + ": ninety actual writer rasters; codec=" + codec.codecId);
            Object.DestroyImmediate(image); Object.DestroyImmediate(instance);
        }
    }
}
#endif
