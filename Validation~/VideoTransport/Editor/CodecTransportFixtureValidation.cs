#if !UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Produces measurement inputs with the production CPU raster implementations.
// Expected bytes come from independent, pre-raster source bins, never decoded images.
public static class CodecTransportFixtureValidation
{
    [Serializable] public sealed class Plan { public Fixture[] fixtures; }
    [Serializable] public sealed class Fixture
    {
        public string name;
        public string codecPrefabPath;
        public string sourceFixtureDirectory;
    }
    [Serializable] sealed class SourceMetadata
    {
        public string status, fps, pattern;
        public int width, height, blockSize, sampleSize, frames;
        public float durationSeconds;
    }
    [Serializable] sealed class FileHash { public string path, sha256; }
    [Serializable] sealed class Metadata
    {
        public string status, scope, startedUtc, finishedUtc, unity, graphicsApi, colorSpace;
        public int width, height, blockSize, sampleSize, frames, codecId, symbolMode;
        public string fps, pattern, sourceFixtureDirectory, codecPrefabPath, codecName;
        public float durationSeconds;
        public int capacityBytes, payloadStartRow;
        public byte[] codecOptions;
        public FileHash[] sourceHashes;
    }
    [Serializable] sealed class Manifest { public Profile[] profiles; }
    [Serializable] sealed class Profile
    {
        public string name, framesDirectory, expectedDirectory, expectedPayloadDirectory, codecPrefabPath;
    }

    static readonly List<Object> Owned = new List<Object>();
    static readonly List<string> Details = new List<string>();
    static readonly List<Profile> Profiles = new List<Profile>();
    static readonly Regex Name = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);
    static readonly Regex FrameName = new Regex(@"^frame-(\d{6})\.bin$", RegexOptions.CultureInvariant);
    static string root, planPath;
    static IEnumerator work;

    public static void Run()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour))
            throw new InvalidOperationException("Codec fixture generation requires a dedicated SDK-free project.");
        root = EnvironmentPath("TSMP_CODEC_FIXTURE_RESULTS");
        planPath = EnvironmentPath("TSMP_CODEC_FIXTURE_PLAN");
        Directory.CreateDirectory(root);
        Check(!File.Exists(Path.Combine(root, "result.txt")), "Choose a fresh results directory.");
        Owned.Clear(); Details.Clear(); Profiles.Clear();
        Details.Add("Production CPU raster fixture generation only; GPU decode and compressed transport require separate measurements.");
        Details.Add("Independent source payload bins are reused byte-for-byte across codecs. Header codec ID, symbol mode and actual prefab options are restamped with valid CRC.");
        Application.runInBackground = true;
        work = Generate();
        EditorApplication.update += Tick;
    }

    static string EnvironmentPath(string variable)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        Check(!string.IsNullOrWhiteSpace(value), variable + " is required.");
        return Path.GetFullPath(value);
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static T Own<T>(T value) where T : Object { Owned.Add(value); return value; }
    static string Csv(object value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    static string Row(params object[] values) { return string.Join(",", values.Select(Csv)); }
    static string Digest(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static FileHash Hash(string path) { return new FileHash { path = Path.GetFullPath(path), sha256 = Digest(File.ReadAllBytes(path)) }; }
    static void Json(string path, object value) { File.WriteAllText(path, JsonUtility.ToJson(value, true)); }
    static string Resolve(string value) { return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(Path.GetDirectoryName(planPath), value)); }

    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        foreach (Object item in Owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
        if (error != null) Details.Add(error.ToString());
        File.WriteAllLines(Path.Combine(root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(Details));
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    static IEnumerable<FileHash> PackageSources(string prefabPath)
    {
        string codecPackage = prefabPath.Substring(0, prefabPath.IndexOf('/', "Packages/".Length));
        foreach (string directory in new[] { "Packages/com.kibalab.tsmp.core", codecPackage }.Distinct())
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".cs" || extension == ".shader" || extension == ".cginc" || extension == ".prefab" || extension == ".mat" || Path.GetFileName(file) == "package.json")
                    yield return Hash(file);
            }
        }
        string own = "Assets/Validation/VideoTransport/Editor/CodecTransportFixtureValidation.cs";
        if (File.Exists(own)) yield return Hash(own);
    }

    static IEnumerator Generate()
    {
        string planJson = File.ReadAllText(planPath);
        File.WriteAllText(Path.Combine(root, "plan-used.json"), planJson);
        Plan plan = JsonUtility.FromJson<Plan>(planJson);
        Check(plan != null && plan.fixtures != null && plan.fixtures.Length > 0, "Plan fixtures are required.");
        Check(plan.fixtures.All(f => f != null && !string.IsNullOrWhiteSpace(f.name) && Name.IsMatch(f.name)), "Fixture names must be simple letters/digits/hyphens/underscores.");
        Check(plan.fixtures.Select(f => f.name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == plan.fixtures.Length, "Fixture names must be unique.");
        uint[] crcTable = Crc32Runtime.EnsureTable(null);
        foreach (Fixture fixture in plan.fixtures)
        {
            Check(!string.IsNullOrWhiteSpace(fixture.codecPrefabPath) && fixture.codecPrefabPath.StartsWith("Packages/", StringComparison.Ordinal)
                && fixture.codecPrefabPath.EndsWith(".prefab", StringComparison.Ordinal), "A production package codec prefab path is required.");
            Check(!string.IsNullOrWhiteSpace(fixture.sourceFixtureDirectory), "sourceFixtureDirectory is required.");
            string source = Resolve(fixture.sourceFixtureDirectory);
            string target = Path.Combine(root, fixture.name);
            Check(!Directory.Exists(target) && !File.Exists(target), "Fixture destination already exists: " + target);
            string sourceMetadataPath = Path.Combine(source, "fixture.json");
            SourceMetadata original = JsonUtility.FromJson<SourceMetadata>(File.ReadAllText(sourceMetadataPath));
            Check(original != null && original.status == "PASS" && original.blockSize == 8 && original.sampleSize == 4 && original.fps == "30"
                && original.frames > 0 && original.frames <= 999999 && original.width > 0 && original.height > 0
                && original.width % 8 == 0 && original.height % 8 == 0, "Expected completed native block8/sample4/30fps source fixture: " + source);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture.codecPrefabPath);
            Check(prefab != null, "Codec prefab missing: " + fixture.codecPrefabPath);
            var instance = Own(Object.Instantiate(prefab));
            var codec = instance.GetComponent<TSMPCodec>();
            Check(codec != null, "TSMPCodec missing: " + fixture.codecPrefabPath);
            var image = Own(new Texture2D(original.width, original.height, TextureFormat.RGBA32, false, true));
            image.filterMode = FilterMode.Point;
            string[] payloads = Directory.GetFiles(Path.Combine(source, "payload"), "*.bin").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Check(payloads.Length == original.frames && Directory.GetFiles(Path.Combine(source, "header"), "*.bin").Length == original.frames
                && Directory.GetFiles(Path.Combine(source, "input"), "*.png").Length == original.frames, "Source header/payload/PNG counts must match fixture metadata.");
            byte[] options = codec.GetCodecOptionBytes() ?? new byte[0];
            Check(options.Length <= FrameHeader.MaxCodecOptionBytes, "Codec options exceed the header option region.");
            var hashes = PackageSources(fixture.codecPrefabPath).ToList();
            hashes.Add(Hash(sourceMetadataPath)); hashes.Add(Hash(planPath));
            var metadata = new Metadata { status = "GENERATING", scope = "Production CPU raster generation only; independent raw source bins. No GPU decode, compression, OBS, network, state/RPC or VRChat execution.",
                startedUtc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString(),
                width = original.width, height = original.height, blockSize = 8, sampleSize = 4, frames = original.frames, fps = "30", durationSeconds = original.frames / 30f,
                pattern = original.pattern, sourceFixtureDirectory = source, codecPrefabPath = fixture.codecPrefabPath, codecName = codec.displayName, codecId = codec.codecId,
                symbolMode = codec.SymbolMode, codecOptions = options, capacityBytes = codec.GetPayloadCapacityBytes(original.width, original.height, 8), payloadStartRow = codec.GetPayloadStartRow(original.width, 8) };
            foreach (string directory in new[] { "input", "header", "payload" }) Directory.CreateDirectory(Path.Combine(target, directory));
            Json(Path.Combine(target, "fixture.json"), metadata);
            var rows = new List<string> { "frame,timestampMs,bytes,headerCrc32,payloadCrc32,pngSHA256,payloadSHA256,headerSHA256" };
            for (int ordinal = 1; ordinal <= original.frames; ordinal++)
            {
                string name = "frame-" + ordinal.ToString("000000", CultureInfo.InvariantCulture);
                Check(FrameName.IsMatch(Path.GetFileName(payloads[ordinal - 1])) && Path.GetFileNameWithoutExtension(payloads[ordinal - 1]) == name, "Source raw bins must be contiguous 1..N six-digit frame IDs.");
                string sourcePayload = Path.Combine(source, "payload", name + ".bin");
                string sourceHeader = Path.Combine(source, "header", name + ".bin");
                string sourcePng = Path.Combine(source, "input", name + ".png");
                byte[] payload = File.ReadAllBytes(sourcePayload);
                byte[] headerBytes = File.ReadAllBytes(sourceHeader);
                FrameHeader header;
                Check(headerBytes.Length == FrameHeader.Size, "Original independent header has invalid length: " + sourceHeader);
                Check(FrameHeader.TryRead(headerBytes, 0, out header), "Original independent header is invalid: " + sourceHeader);
                Check(header.FrameIndex == ordinal && header.BlockSize == 8 && header.DecodeSampleSize == 4 && header.ActiveWidthBlocks * 8 == original.width && header.ActiveHeightBlocks * 8 == original.height,
                    "Original header ID/geometry mismatch: " + sourceHeader);
                Check((header.Flags & FrameFlags.PayloadCrc32) != 0 && header.PayloadSize == payload.Length
                    && Crc32Runtime.Compute(payload, 0, payload.Length, crcTable) == header.PayloadCrc32, "Original independent payload CRC mismatch: " + sourcePayload);
                Check(payload.Length <= metadata.capacityBytes, "Payload exceeds codec capacity: " + fixture.name + "/" + name);
                hashes.Add(Hash(sourceHeader)); hashes.Add(Hash(sourcePayload)); hashes.Add(Hash(sourcePng));
                header.CodecId = codec.codecId; header.SymbolMode = (byte)codec.SymbolMode;
                header.CodecOptionLength = (byte)options.Length;
                header.CodecOption0 = options.Length > 0 ? options[0] : (byte)0;
                header.CodecOption1 = options.Length > 1 ? options[1] : (byte)0;
                header.CodecOption2 = options.Length > 2 ? options[2] : (byte)0;
                header.CodecOption3 = options.Length > 3 ? options[3] : (byte)0;
                header.CodecOption4 = options.Length > 4 ? options[4] : (byte)0;
                headerBytes = new byte[FrameHeader.Size]; header.WriteTo(headerBytes, 0);
                string payloadHash = Digest(payload), headerHash = Digest(headerBytes);
                // These references are committed to evidence before any raster call.
                File.WriteAllBytes(Path.Combine(target, "payload", name + ".bin"), payload);
                File.WriteAllBytes(Path.Combine(target, "header", name + ".bin"), headerBytes);
                string error;
                Check(codec.TryWriteFrame(image, 8, headerBytes, payload, out error), "Production raster failed: " + fixture.name + "/" + name + ": " + error);
                Check(Digest(payload) == payloadHash && Digest(headerBytes) == headerHash, "Raster unexpectedly mutated independent reference bins.");
                byte[] png = image.EncodeToPNG();
                Check(png != null && png.Length > 0, "PNG encoding failed.");
                File.WriteAllBytes(Path.Combine(target, "input", name + ".png"), png);
                rows.Add(Row(ordinal, header.TimestampMs, payload.Length, Binary.ReadUInt32LE(headerBytes, FrameHeader.CrcOffset).ToString("X8"), header.PayloadCrc32.ToString("X8"), Digest(png), payloadHash, headerHash));
                if (ordinal % 30 == 0) { File.WriteAllLines(Path.Combine(target, "source-frames.csv"), rows); Debug.Log("Codec fixture " + fixture.name + ": " + ordinal + "/" + original.frames); }
                yield return null;
            }
            File.WriteAllLines(Path.Combine(target, "source-frames.csv"), rows);
            var profile = new Profile { name = fixture.name + "-png-control", framesDirectory = Path.Combine(target, "input"), expectedDirectory = Path.Combine(target, "input"), expectedPayloadDirectory = Path.Combine(target, "payload"), codecPrefabPath = fixture.codecPrefabPath };
            Profiles.Add(profile);
            Json(Path.Combine(target, "manifest.json"), new Manifest { profiles = new[] { profile } });
            Json(Path.Combine(root, "manifest.json"), new Manifest { profiles = Profiles.ToArray() });
            metadata.sourceHashes = hashes.ToArray(); metadata.finishedUtc = DateTime.UtcNow.ToString("o"); metadata.status = "PASS";
            Json(Path.Combine(target, "fixture.json"), metadata);
            Details.Add("PASS " + fixture.name + ": frames=" + original.frames + "; bytes from=" + source + "; codec=" + codec.displayName + "; capacity=" + metadata.capacityBytes + "; payloadStartRow=" + metadata.payloadStartRow);
            Object.DestroyImmediate(image); Object.DestroyImmediate(instance);
        }
    }
}
#endif
