#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Diagnostic shadow-shader comparison. Independent reference headers configure
// raw payload probes even when the received header is corrupt. These raw probes
// are not production end-to-end success; production is logged separately.
public sealed class RefineRobustnessValidation : MonoBehaviour
{
    [Serializable] public sealed class Plan
    {
        public Profile[] profiles;
        public Variant[] variants;
        public bool runProduction;
        public bool dumpRawBytes;
        public bool timing;
        public int timingFramesPerProfile = 3;
        public int maxFramesPerProfile;
        public int frameStride = 1;
        public FloatProperty[] decoderProperties;
        public ProductionVariant[] productionVariants;
    }
    [Serializable] public sealed class Profile
    {
        public string name, framesDirectory, expectedDirectory, expectedPayloadDirectory, expectedHeaderDirectory, codecPrefabPath;
        public FrameMapping[] frameMappings;
    }
    [Serializable] public sealed class FrameMapping
    {
        public string file, expectedFile, payloadFile, headerFile;
        public uint frameIndex;
        public bool frameIndexSpecified, expectedFrameUnknown;
    }
    [Serializable] public sealed class Variant
    {
        public string name, mode = "Payload", shaderAssetPath, materialField, timingGroup;
        public int codecId = -1, sampleSize = -1, radius = -1, lutWidth;
        public int sourceCodecId = -1;
        public bool disableLut;
        public FloatProperty[] properties;
        public string calibrationShaderAssetPath, calibrationTextureProperty = "_CalibrationLut", calibrationKeyword;
        public int calibrationWidth, calibrationHeight = 1;
    }
    [Serializable] public sealed class ProductionVariant { public string name; public FloatProperty[] decoderProperties; }
    [Serializable] public sealed class FloatProperty { public string name; public float value; }
    sealed class Item
    {
        public string file, input, payload, header;
        public uint frame;
        public bool unknown;
    }
    sealed class Probe
    {
        public Variant variant;
        public TSMPCodec codec;
        public Material material;
        public Material customPreparation;
        public RenderTexture customLut;
    }
    sealed class Stats
    {
        public int frames, exact, crcValid, wrongAccepted;
        public long bytes, exactBytes, mismatchBytes, mismatchBits;
    }

    const string SessionKey = "TSMP.RefineRobustness";
    const string LumaPath = "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Batch = 32, Warm = 10, Samples = 30;
    static string Root => Environment.GetEnvironmentVariable("TSMP_ROBUSTNESS_RESULTS");
    static string PlanPath => Environment.GetEnvironmentVariable("TSMP_ROBUSTNESS_PLAN");
    readonly List<Object> owned = new List<Object>();
    readonly List<string> rows = new List<string>(), production = new List<string>(), summaries = new List<string>(), timingRows = new List<string>(), rawTimingRows = new List<string>(), hashes = new List<string>(), details = new List<string>(), dumps = new List<string>();
    readonly HashSet<string> hashed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Stats> stats = new Dictionary<string, Stats>();
    uint[] crc;
    int serial, errors, wrongAccepted, processedInputs;
    Plan plan;

    public static void Run()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour)) throw new InvalidOperationException("Use a dedicated SDK-free project.");
        if (string.IsNullOrWhiteSpace(Root) || !File.Exists(PlanPath)) throw new InvalidOperationException("TSMP_ROBUSTNESS_RESULTS and existing TSMP_ROBUSTNESS_PLAN are required.");
        Directory.CreateDirectory(Root);
        if (File.Exists(Path.Combine(Root, "result.txt"))) throw new InvalidOperationException("Use fresh results.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(SessionKey, true); Attach(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered;
    }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false);
        new GameObject("Refine robustness validation").AddComponent<RefineRobustnessValidation>();
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        Profiler.enabled = false;
        Own(new GameObject("Refine robustness frame pump")).AddComponent<Camera>().cullingMask = 0;
        crc = Crc32Runtime.EnsureFastTable(null);
        rows.Add("profile,file,frame,variant,mode,codecId,width,height,bytes,sample,radius,lut,byteMismatches,bitMismatches,exact,crcValid,status,actualCrc32,expectedCrc32,inputSha256,referenceSha256");
        production.Add("profile,file,variant,expectedFrame,actualFrame,headerValid,payloadCrcVerified,frameValid,currentPayload,expectedBytes,bytes,byteMismatches,status,elapsedMs,headerAttempts,payloadAttempts,recoveredHeaders,recoveredPayloads,error");
        summaries.Add("profile,variant,mode,frames,exact,crcValid,wrongAccepted,bytes,exactBytes,byteMismatches,bitMismatches");
        timingRows.Add("case,profile,file,baselineVariant,variant,mode,width,height,bytes,sample,radius,lut,batch,validPairs,status,baselineMeanUs,candidateMeanUs,baselineP50Us,candidateP50Us,baselineP95Us,candidateP95Us");
        rawTimingRows.Add("case,frame,order,validGpuPair,baselineGpuNs,candidateGpuNs,baselineBlocks,candidateBlocks");
        hashes.Add("path,sha256");
        dumps.Add("profile,file,frame,variant,mode,input,header,reference,decoded,decodedSha256");
        details.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        details.Add("Raw shader probes use independent reference header geometry/options even after received-header CRC failure. They do not imply production frame acceptance.");
        details.Add("EXACT requires complete original bytes. CRC_ACCEPTED_WRONG is a validation failure. Rejected lossy inputs are measurements, not harness failures.");
        details.Add("Production is parse-only, strict payload CRC, actual package materials/decoder; no state/RPC/network/client execution.");
        details.Add("Parse-only production measurements do not exercise predicted/combined short paths; CoreRecoveryValidation separately checks these with actual state/RPC application.");
        details.Add("Timing includes per-draw calibration preparation when a LUT is bound; GPU timestamps are not CPU/readback wall time or VRChat FPS.");
        var stack = new Stack<IEnumerator>(); stack.Push(Validate());
        while (stack.Count > 0)
        {
            IEnumerator next = stack.Peek(); bool moved;
            try { moved = next.MoveNext(); }
            catch (Exception error) { errors++; details.Add(error.ToString()); Finish(); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next.Current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next.Current;
        }
        Finish();
    }
    T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    void Drop(Object item)
    {
        owned.Remove(item);
        if (item is RenderTexture rt)
        {
            if (RenderTexture.active == rt) RenderTexture.active = null;
            rt.Release();
        }
        if (item != null) Object.DestroyImmediate(item);
    }
    static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    static string C(object value) => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(C));
    static string Sha(byte[] bytes)
    {
        using (var digest = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(digest.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    string Hash(string path)
    {
        string full = Path.GetFullPath(path), digest = Sha(File.ReadAllBytes(full));
        if (hashed.Add(full)) hashes.Add(Row(full, digest)); return digest;
    }
    static string PathAt(string path, string relativeBase) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(relativeBase, path));
    static int Bits(byte value)
    {
        int count = 0; for (; value != 0; value >>= 1) count += value & 1; return count;
    }
    static int Differences(byte[] actual, byte[] expected, out int bits)
    {
        int mismatches = Math.Abs(actual.Length - expected.Length); bits = mismatches * 8;
        for (int i = 0; i < Math.Min(actual.Length, expected.Length); i++) { byte delta = (byte)(actual[i] ^ expected[i]); if (delta != 0) mismatches++; bits += Bits(delta); }
        return mismatches;
    }
    static T Field<T>(TSMPDecoder decoder, string name) => (T)typeof(TSMPDecoder).GetField(name, Private).GetValue(decoder);
    static int PublicInt(TSMPDecoder decoder, string name)
    {
        FieldInfo field = typeof(TSMPDecoder).GetField(name); return field == null ? -1 : (int)field.GetValue(decoder);
    }
    static int CounterDelta(int before, int after) => before < 0 || after < 0 ? -1 : after - before;
    static void DecoderProperties(TSMPDecoder decoder, FloatProperty[] properties)
    {
        if (properties == null) return;
        foreach (FloatProperty property in properties)
        {
            FieldInfo field = typeof(TSMPDecoder).GetField(property.name); Check(field != null, "Missing decoder property " + property.name);
            field.SetValue(decoder, field.FieldType == typeof(bool) ? (object)(property.value != 0) : field.FieldType == typeof(int) ? (object)(int)property.value : property.value);
        }
    }
    void Flush()
    {
        File.WriteAllLines(Path.Combine(Root, "raw-frames.csv"), rows);
        File.WriteAllLines(Path.Combine(Root, "production-frames.csv"), production);
        File.WriteAllLines(Path.Combine(Root, "timings.csv"), timingRows);
        File.WriteAllLines(Path.Combine(Root, "timing-pairs.csv"), rawTimingRows);
        File.WriteAllLines(Path.Combine(Root, "source-hashes.csv"), hashes);
        File.WriteAllLines(Path.Combine(Root, "raw-dumps.csv"), dumps);
    }
    void Finish()
    {
        foreach (var pair in stats)
        {
            string[] key = pair.Key.Split('\t'); Stats value = pair.Value;
            summaries.Add(Row(key[0], key[1], key[2], value.frames, value.exact, value.crcValid, value.wrongAccepted, value.bytes, value.exactBytes, value.mismatchBytes, value.mismatchBits));
        }
        File.WriteAllLines(Path.Combine(Root, "summary.csv"), summaries); Flush();
        details.Add("Raw cases=" + (rows.Count - 1) + "; production cases=" + (production.Count - 1) + "; errors=" + errors + "; CRC-accepted wrong outputs=" + wrongAccepted);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { errors + wrongAccepted == 0 ? "PASS" : "FAIL" }.Concat(details));
        foreach (Object item in owned.AsEnumerable().Reverse()) { if (item is RenderTexture rt) rt.Release(); if (item != null) Object.DestroyImmediate(item); }
        Profiler.enabled = false; EditorApplication.Exit(errors + wrongAccepted == 0 ? 0 : 1);
    }
    TSMPCodec CreateCodec(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath); Check(prefab != null, "Missing codec prefab " + prefabPath);
        TSMPCodec codec = Own(Object.Instantiate(prefab)).GetComponent<TSMPCodec>(); Check(codec != null, "Missing codec component.");
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (!copies.TryGetValue(original, out Material value)) copies[original] = value = Own(new Material(original)); return value;
        };
        foreach (FieldInfo field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] values) field.SetValue(codec, values.Select(clone).ToArray());
        }
        return codec;
    }
    RenderTexture Target(int width, int height)
    {
        var result = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        result.filterMode = FilterMode.Point; result.wrapMode = TextureWrapMode.Clamp; Check(result.Create(), "Cannot create texture."); return result;
    }
    List<Item> Items(Profile profile)
    {
        string planDirectory = Path.GetDirectoryName(Path.GetFullPath(PlanPath));
        string input = PathAt(profile.framesDirectory, planDirectory), expected = PathAt(profile.expectedDirectory, planDirectory);
        string payload = string.IsNullOrWhiteSpace(profile.expectedPayloadDirectory) ? Path.Combine(Path.GetDirectoryName(expected), "payload") : PathAt(profile.expectedPayloadDirectory, planDirectory);
        string header = string.IsNullOrWhiteSpace(profile.expectedHeaderDirectory) ? Path.Combine(Path.GetDirectoryName(expected), "header") : PathAt(profile.expectedHeaderDirectory, planDirectory);
        FrameMapping[] mappings = profile.frameMappings;
        if (mappings == null || mappings.Length == 0) mappings = Directory.GetFiles(input, "frame-*.png").OrderBy(path => path, StringComparer.Ordinal).Select(path => new FrameMapping { file = Path.GetFileName(path), expectedFile = Path.GetFileName(path) }).ToArray();
        int stride = Math.Max(1, plan.frameStride);
        IEnumerable<FrameMapping> selected = mappings.Where((item, index) => index % stride == 0);
        if (plan.maxFramesPerProfile > 0) selected = selected.Take(plan.maxFramesPerProfile);
        return selected.Select(map =>
        {
            string name = string.IsNullOrWhiteSpace(map.expectedFile) ? map.file : map.expectedFile;
            uint frame = map.frameIndex;
            if (!map.frameIndexSpecified && frame == 0 && !map.expectedFrameUnknown) frame = uint.Parse(Path.GetFileNameWithoutExtension(name).Substring(6), CultureInfo.InvariantCulture);
            return new Item { file = map.file, input = PathAt(map.file, input), payload = PathAt(string.IsNullOrWhiteSpace(map.payloadFile) ? Path.ChangeExtension(Path.GetFileName(name), ".bin") : map.payloadFile, payload),
                header = PathAt(string.IsNullOrWhiteSpace(map.headerFile) ? Path.ChangeExtension(Path.GetFileName(name), ".bin") : map.headerFile, header), frame = frame, unknown = map.expectedFrameUnknown };
        }).ToList();
    }
    Probe MakeProbe(Variant variant, string payloadPrefab)
    {
        TSMPCodec codec = CreateCodec(variant.mode == "Header" ? LumaPath : payloadPrefab);
        Material material = null;
        if (!string.IsNullOrWhiteSpace(variant.shaderAssetPath))
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(variant.shaderAssetPath); Check(shader != null && shader.isSupported, "Unavailable shader " + variant.shaderAssetPath);
            material = Own(new Material(shader)); Hash(variant.shaderAssetPath);
        }
        else if (!string.IsNullOrWhiteSpace(variant.materialField))
        {
            FieldInfo field = codec.GetType().GetField(variant.materialField); Check(field != null && field.FieldType == typeof(Material), "Missing material field " + variant.materialField);
            material = (Material)field.GetValue(codec);
        }
        // Blank material uses the production material chosen from each reference header.
        if (material != null && !string.IsNullOrWhiteSpace(variant.materialField)) codec.GetType().GetField(variant.materialField).SetValue(codec, material);
        var result = new Probe { variant = variant, codec = codec, material = material };
        if (!string.IsNullOrWhiteSpace(variant.calibrationShaderAssetPath))
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(variant.calibrationShaderAssetPath);
            Check(shader != null && shader.isSupported && variant.calibrationWidth > 0 && variant.calibrationHeight > 0, "Custom preparation unavailable.");
            result.customPreparation = Own(new Material(shader)); Hash(variant.calibrationShaderAssetPath);
            result.customLut = Own(new RenderTexture(variant.calibrationWidth, variant.calibrationHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            result.customLut.filterMode = FilterMode.Point; result.customLut.wrapMode = TextureWrapMode.Clamp;
            Check(result.customLut.Create(), "Cannot create custom calibration LUT.");
        }
        return result;
    }
    static void Set(Material material, string name, float value) { if (material.HasProperty(name)) material.SetFloat(name, value); }
    Material Setup(Probe probe, Texture image, FrameHeader header, RenderTexture output)
    {
        Variant variant = probe.variant; TSMPCodec codec = probe.codec;
        int block = header.BlockSize, columns = header.ActiveWidthBlocks;
        codec.codecOptionBytes = new[] { header.CodecOption0, header.CodecOption1, header.CodecOption2, header.CodecOption3, header.CodecOption4 };
        codec.activeWidthBlocks = columns; codec.calibrationStartBlock = 5 * columns; codec.byteCount = header.PayloadSize; codec.payloadInterleaved = false;
        codec.ApplyDecodeOptions();
        Material material = probe.material != null ? probe.material : codec.selectedDecodeMaterial;
        Check(material != null && material.shader != null && material.shader.isSupported, "Material unavailable " + variant.name);
        Set(material, "_BlockSize", block); Set(material, "_SampleSize", variant.sampleSize < 0 ? header.DecodeSampleSize : variant.sampleSize);
        Set(material, "_ActiveWidthBlocks", columns); Set(material, "_SourceWidth", image.width); Set(material, "_SourceHeight", image.height);
        Set(material, "_StartBlock", (variant.mode == "Header" ? 2 : codec.payloadStartRow) * columns);
        Set(material, "_ByteCount", variant.mode == "Header" ? FrameHeader.Size : header.PayloadSize);
        Set(material, "_OutputWidth", output.width); Set(material, "_OutputHeight", output.height); Set(material, "_FlipY", 1); Set(material, "_TSMPHeaderPixels", 0);
        Set(material, "_ColorCalibrationStartBlock", 5 * columns); Set(material, "_Rgb16CalibrationStartBlock", 5 * columns); Set(material, "_Rgb20CalibrationStartBlock", 5 * columns);
        Set(material, "_InterleaveMode", 0); Set(material, "_RBits", header.CodecOption0); Set(material, "_GBits", header.CodecOption1); Set(material, "_BBits", header.CodecOption2);
        if (variant.radius >= 0) Set(material, "_RefineRadius", variant.radius);
        else Set(material, "_RefineRadius", Math.Max(1, (int)header.CodecOption4));
        material.SetTexture("_MainTex", image);
        if (variant.properties != null) foreach (FloatProperty property in variant.properties) Set(material, property.name, property.value);
        codec.PrepareDecode(image, material);
        if (variant.disableLut) { material.DisableKeyword("TSMP_CALIBRATION_LUT"); material.SetTexture("_CalibrationLut", null); }
        if (variant.lutWidth > 0) typeof(TSMPCodec).GetMethod("PrepareCalibrationLut", Private).Invoke(codec, new object[] { image, material, variant.lutWidth });
        if (probe.customPreparation != null)
        {
            probe.customPreparation.CopyPropertiesFromMaterial(material);
            Graphics.Blit(image, probe.customLut, probe.customPreparation);
            material.SetTexture(variant.calibrationTextureProperty, probe.customLut);
            if (!string.IsNullOrWhiteSpace(variant.calibrationKeyword)) material.EnableKeyword(variant.calibrationKeyword);
        }
        return material;
    }
    static byte[] Read(RenderTexture output, int count)
    {
        AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32); readback.WaitForCompletion();
        Check(!readback.hasError, "Readback failed."); return readback.GetData<byte>().Take(count).ToArray();
    }
    byte[] Render(Probe probe, Texture image, FrameHeader header, RenderTexture output)
    {
        Material material = Setup(probe, image, header, output); Graphics.Blit(image, output, material);
        int count = probe.variant.mode == "Header" ? FrameHeader.Size : header.PayloadSize;
        byte[] bytes = Read(output, output.width * 4); Check(bytes.Skip(count).All(value => value == 0), "Output padding is nonzero: " + probe.variant.name);
        return bytes.Take(count).ToArray();
    }
    bool Valid(byte[] bytes, byte[] referenceHeader, bool isHeader)
    {
        if (isHeader) return FrameHeaderReader.TryValidate(bytes, 0, crc, out _, out _, out _, out _, out _, out _);
        return FrameHeaderReader.TryValidatePayload(referenceHeader, 0, bytes, bytes.Length, true, crc, out _, out _, out _, out _);
    }
    void Record(Profile profile, Item item, Probe probe, Texture image, byte[] actual, byte[] expected, byte[] referenceHeader, string inputHash, string referenceHash)
    {
        if (plan.dumpRawBytes)
        {
            string dumpDirectory = Path.Combine(Root, "bins", profile.name);
            Directory.CreateDirectory(dumpDirectory);
            string dump = Path.Combine(dumpDirectory, Path.GetFileNameWithoutExtension(item.file) + "." + probe.variant.name + ".bin");
            File.WriteAllBytes(dump, actual);
            dumps.Add(Row(profile.name, item.file, item.frame, probe.variant.name, probe.variant.mode,
                item.input, item.header, probe.variant.mode == "Header" ? item.header : item.payload, dump, Sha(actual)));
        }
        int mismatches = Differences(actual, expected, out int bitMismatches); bool exact = mismatches == 0;
        bool valid = Valid(actual, referenceHeader, probe.variant.mode == "Header");
        string status = exact ? "EXACT" : valid ? "CRC_ACCEPTED_WRONG" : "REJECTED";
        string key = profile.name + "\t" + probe.variant.name + "\t" + probe.variant.mode;
        if (!stats.TryGetValue(key, out Stats value)) stats[key] = value = new Stats();
        value.frames++; value.bytes += expected.Length; value.mismatchBytes += mismatches; value.mismatchBits += bitMismatches;
        if (exact) { value.exact++; value.exactBytes += expected.Length; } if (valid) value.crcValid++;
        if (!exact && valid) { value.wrongAccepted++; wrongAccepted++; }
        Material material = probe.material != null ? probe.material : probe.codec.selectedDecodeMaterial;
        rows.Add(Row(profile.name, item.file, item.frame, probe.variant.name, probe.variant.mode, probe.codec.codecId, image.width, image.height, expected.Length,
            material.GetFloat("_SampleSize"), material.HasProperty("_RefineRadius") ? material.GetFloat("_RefineRadius") : 0, material.IsKeywordEnabled("TSMP_CALIBRATION_LUT"),
            mismatches, bitMismatches, exact, valid, status, Crc32Runtime.Compute(actual, 0, actual.Length, crc).ToString("X8"), Crc32Runtime.Compute(expected, 0, expected.Length, crc).ToString("X8"), inputHash, referenceHash));
    }
    IEnumerator Validate()
    {
        string json = File.ReadAllText(PlanPath); File.WriteAllText(Path.Combine(Root, "plan-used.json"), json); Hash(PlanPath);
        plan = JsonUtility.FromJson<Plan>(json);
        Check(plan != null && plan.profiles != null && plan.profiles.Length > 0, "Profiles required.");
        ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, plan.timing); Profiler.enabled = plan.timing;
        details.Add("GPU timestamp profiling enabled=" + plan.timing + "; accuracy-only runs disable profiling.");
        Check((plan.variants != null && plan.variants.Length > 0) || plan.runProduction, "Variants or runProduction required.");
        Check(plan.profiles.Select(value => value.name).Distinct().Count() == plan.profiles.Length, "Duplicate profile names.");
        if (plan.variants == null) plan.variants = new Variant[0];
        Check(plan.variants.Select(value => value.name).Distinct().Count() == plan.variants.Length, "Duplicate variant names.");
        Check(plan.variants.All(value => value.mode == "Header" || value.mode == "Payload"), "Variant mode must be Header or Payload.");
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.luma4", "com.kibalab.tsmp.codec.color256", "com.kibalab.tsmp.codec.rgb16", "com.kibalab.tsmp.codec.rgb20" })
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + package);
            string runtime = Path.Combine(packageInfo != null ? packageInfo.resolvedPath : "Packages/" + package, "Runtime");
            if (Directory.Exists(runtime)) foreach (string file in Directory.GetFiles(runtime, "*", SearchOption.AllDirectories).Where(path => path.EndsWith(".cs") || path.EndsWith(".shader") || path.EndsWith(".cginc"))) Hash(file);
        }
        Hash("Assets/RefineRobustness/RefineRobustnessValidation.cs");
        string candidateDirectory = "Assets/RefineRobustness/Candidates";
        if (Directory.Exists(candidateDirectory))
            foreach (string file in Directory.GetFiles(candidateDirectory, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)) Hash(file);
        foreach (Profile profile in plan.profiles)
        {
            Check(!string.IsNullOrWhiteSpace(profile.name), "Profile name required.");
            string prefab = string.IsNullOrWhiteSpace(profile.codecPrefabPath) ? LumaPath : profile.codecPrefabPath;
            TSMPCodec payloadCodec = CreateCodec(prefab), lumaCodec = CreateCodec(LumaPath);
            Probe[] probes = plan.variants.Where(value => (value.sourceCodecId < 0 || value.sourceCodecId == payloadCodec.codecId) &&
                (value.codecId < 0 || value.mode == "Header" || value.codecId == payloadCodec.codecId)).Select(value => MakeProbe(value, prefab)).ToArray();
            TSMPDecoder decoder = null; RenderTexture source = null;
            if (plan.runProduction)
            {
                decoder = Own(new GameObject("Production robustness decoder")).AddComponent<TSMPDecoder>();
                decoder.applyEveryFrame = false; decoder.skipDuplicateFrames = false; decoder.requirePayloadCrc32 = true; decoder.decodeSafetyMode = 3;
                decoder.overlapReadbacks = false; decoder.debugLog = false; decoder.blockSize = 8; decoder.sampleSize = 4;
                decoder.codecHandlers = payloadCodec.codecId == 0 ? new[] { payloadCodec } : new[] { lumaCodec, payloadCodec }; decoder.payloadByteTexture = Target(2048, 1);
                DecoderProperties(decoder, plan.decoderProperties);
                yield return null;
            }
            List<Item> items = Items(profile); int timed = 0;
            foreach (Item item in items)
            {
                Check(File.Exists(item.input), "Missing input " + item.input); string inputHash = Hash(item.input);
                var image = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true)); Check(image.LoadImage(File.ReadAllBytes(item.input)), "Cannot load " + item.input);
                image.filterMode = FilterMode.Point; image.wrapMode = TextureWrapMode.Clamp;
                byte[] payload = null, headerBytes = null; FrameHeader header = default;
                if (!item.unknown)
                {
                    Check(File.Exists(item.payload) && File.Exists(item.header), "Independent payload/header missing " + item.file);
                    payload = File.ReadAllBytes(item.payload); headerBytes = File.ReadAllBytes(item.header); Hash(item.payload); Hash(item.header);
                    Check(FrameHeader.TryRead(headerBytes, 0, out header), "Reference header invalid.");
                    Check(header.CodecId == payloadCodec.codecId && header.PayloadSize == payload.Length, "Reference codec/size mismatch.");
                    Check(header.BlockSize > 0 && header.ActiveWidthBlocks > 0 && header.ActiveWidthBlocks * header.BlockSize <= image.width, "Reference geometry exceeds input.");
                    Check(payload.Length <= 8192, "Probe target supports at most 8192 bytes.");
                    foreach (Probe probe in probes)
                    {
                        byte[] expected = probe.variant.mode == "Header" ? headerBytes : payload; RenderTexture output = Target(probe.variant.mode == "Header" ? 14 : 2048, 1);
                        byte[] actual = Render(probe, image, header, output); Record(profile, item, probe, image, actual, expected, headerBytes, inputHash, Sha(expected)); Drop(output);
                    }
                    if (plan.timing && timed < plan.timingFramesPerProfile)
                    {
                        foreach (var timingGroup in probes.GroupBy(value => value.variant.mode + "\t" + (string.IsNullOrWhiteSpace(value.variant.timingGroup) ? value.variant.mode : value.variant.timingGroup)))
                        {
                            Probe[] group = timingGroup.ToArray();
                            if (group.Length > 1) foreach (Probe candidate in group.Skip(1)) yield return Measure(profile, item, group[0], candidate, image, header);
                        }
                        timed++;
                    }
                }
                else details.Add("No independent reference for " + profile.name + "/" + item.file + "; raw probe omitted; production still evaluated.");
                if (decoder != null)
                {
                    if (source == null || source.width != image.width || source.height != image.height)
                    {
                        if (source != null) Drop(source); source = Target(image.width, image.height); decoder.sourceTexture = source;
                    }
                    ProductionVariant[] productionVariants = plan.productionVariants != null && plan.productionVariants.Length > 0 ? plan.productionVariants : new[] { new ProductionVariant { name = "Production" } };
                    foreach (ProductionVariant productionVariant in productionVariants)
                    {
                        DecoderProperties(decoder, productionVariant.decoderProperties);
                        Graphics.Blit(image, source); decoder.lastHeaderValid = false; decoder.lastFrameValid = false; decoder.lastPayloadCrcVerified = false;
                        int recoveredHeaderBefore = PublicInt(decoder, "recoveredHeaderCount"), recoveredPayloadBefore = PublicInt(decoder, "recoveredPayloadCount");
                        int headerAttemptsBefore = PublicInt(decoder, "headerRecoveryAttemptCount"), payloadAttemptsBefore = PublicInt(decoder, "payloadRecoveryAttemptCount");
                        double start = EditorApplication.timeSinceStartup, deadline = start + 30; decoder.DecodeNow();
                        while (decoder.readbackInFlight && EditorApplication.timeSinceStartup < deadline) yield return null;
                        Check(!decoder.readbackInFlight, "Production readback timeout.");
                        bool current = decoder.lastHeaderValid && (decoder.lastPayloadCrcVerified || (decoder.lastError ?? "").StartsWith("Payload CRC mismatch.", StringComparison.Ordinal));
                        bool valid = decoder.lastHeaderValid && decoder.lastFrameValid && decoder.lastPayloadCrcVerified;
                        int length = Field<int>(decoder, "_payloadDataBytes"), mismatch = -1;
                        if (current && payload != null)
                        {
                            byte[] buffer = Field<byte[]>(decoder, "_payloadBytes"); Check(buffer != null && length >= 0 && length <= buffer.Length, "Invalid production payload buffer.");
                            mismatch = Differences(buffer.Take(length).ToArray(), payload, out _);
                        }
                        string status = !valid ? "REJECTED" : item.unknown || decoder.lastFrameIndex != item.frame ? "WRONG" : mismatch == 0 ? "EXACT" : "CORRUPT";
                        if (status == "WRONG" || status == "CORRUPT") wrongAccepted++;
                        production.Add(Row(profile.name, item.file, productionVariant.name, item.unknown ? "" : item.frame.ToString(CultureInfo.InvariantCulture), decoder.lastHeaderValid ? decoder.lastFrameIndex.ToString(CultureInfo.InvariantCulture) : "",
                            decoder.lastHeaderValid, decoder.lastPayloadCrcVerified, decoder.lastFrameValid, current, payload == null ? "" : payload.Length.ToString(CultureInfo.InvariantCulture), current ? length.ToString(CultureInfo.InvariantCulture) : "", mismatch < 0 ? "" : mismatch.ToString(CultureInfo.InvariantCulture), status,
                            ((EditorApplication.timeSinceStartup - start) * 1000).ToString("F6", CultureInfo.InvariantCulture), CounterDelta(headerAttemptsBefore, PublicInt(decoder, "headerRecoveryAttemptCount")), CounterDelta(payloadAttemptsBefore, PublicInt(decoder, "payloadRecoveryAttemptCount")),
                            CounterDelta(recoveredHeaderBefore, PublicInt(decoder, "recoveredHeaderCount")), CounterDelta(recoveredPayloadBefore, PublicInt(decoder, "recoveredPayloadCount")), decoder.lastError));
                    }
                }
                Drop(image); if (++processedInputs % 32 == 0) Flush(); yield return null;
            }
            details.Add("Completed profile=" + profile.name + "; inputs=" + items.Count); Flush(); Debug.Log("Refine robustness completed " + profile.name);
            if (decoder != null) Drop(decoder.gameObject); if (source != null) Drop(source);
            Drop(payloadCodec.gameObject); Drop(lumaCodec.gameObject); foreach (Probe probe in probes) Drop(probe.codec.gameObject);
        }
    }
    static RenderTexture Lut(Probe probe) => (RenderTexture)typeof(TSMPCodec).GetField("_calibrationLut", Private).GetValue(probe.codec);
    static CommandBuffer Commands(Probe probe, Material material, Texture image, RenderTexture output, CustomSampler sampler)
    {
        var result = new CommandBuffer(); result.BeginSample(sampler);
        for (int i = 0; i < Batch; i++)
        {
            if (probe.customPreparation != null) result.Blit(image, probe.customLut, probe.customPreparation);
            bool customReplacesLut = probe.customPreparation != null && probe.variant.calibrationTextureProperty == "_CalibrationLut";
            if (material.IsKeywordEnabled("TSMP_CALIBRATION_LUT") && !customReplacesLut) result.Blit(image, Lut(probe), probe.codec.calibrationMaterial);
            result.Blit(image, output, material);
        }
        result.EndSample(sampler); return result;
    }
    static void Submit(CommandBuffer commands, RenderTexture output)
    {
        Graphics.ExecuteCommandBuffer(commands); AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32);
        request.WaitForCompletion(); Check(!request.hasError, "Timing synchronization failed.");
    }
    IEnumerator Measure(Profile profile, Item item, Probe baseline, Probe candidate, Texture image, FrameHeader header)
    {
        bool isHeader = baseline.variant.mode == "Header"; var output = Target(isHeader ? 14 : 2048, 1);
        Material a = Setup(baseline, image, header, output), b = Setup(candidate, image, header, output); int id = ++serial;
        CustomSampler samplerA = CustomSampler.Create("Robustness.Original." + id, true), samplerB = CustomSampler.Create("Robustness.Candidate." + id, true);
        CommandBuffer commandsA = Commands(baseline, a, image, output, samplerA), commandsB = Commands(candidate, b, image, output, samplerB);
        Recorder recorderA = samplerA.GetRecorder(), recorderB = samplerB.GetRecorder(); recorderA.enabled = true; recorderB.enabled = true;
        var valuesA = new List<double>(); var valuesB = new List<double>();
        for (int frame = 0; frame < Warm + Samples * 2 && valuesA.Count < Samples; frame++)
        {
            if ((frame & 1) == 0) { Submit(commandsA, output); Submit(commandsB, output); } else { Submit(commandsB, output); Submit(commandsA, output); }
            yield return null;
            bool valid = recorderA.gpuSampleBlockCount == 1 && recorderB.gpuSampleBlockCount == 1 && recorderA.gpuElapsedNanoseconds > 0 && recorderB.gpuElapsedNanoseconds > 0;
            rawTimingRows.Add(Row(id, frame, (frame & 1) == 0 ? "AB" : "BA", valid, recorderA.gpuElapsedNanoseconds, recorderB.gpuElapsedNanoseconds, recorderA.gpuSampleBlockCount, recorderB.gpuSampleBlockCount));
            if (frame < Warm || !valid) continue; valuesA.Add(recorderA.gpuElapsedNanoseconds / 1000.0 / Batch); valuesB.Add(recorderB.gpuElapsedNanoseconds / 1000.0 / Batch);
        }
        recorderA.enabled = false; recorderB.enabled = false; commandsA.Dispose(); commandsB.Dispose();
        bool available = valuesA.Count >= Samples;
        timingRows.Add(Row(id, profile.name, item.file, baseline.variant.name, candidate.variant.name, candidate.variant.mode, image.width, image.height, isHeader ? FrameHeader.Size : header.PayloadSize,
            b.GetFloat("_SampleSize"), b.HasProperty("_RefineRadius") ? b.GetFloat("_RefineRadius") : 0, b.IsKeywordEnabled("TSMP_CALIBRATION_LUT"), Batch, valuesA.Count,
            available ? "GPU_TIMED" : "GPU_TIMING_UNAVAILABLE", Mean(valuesA), Mean(valuesB), Percentile(valuesA, .5), Percentile(valuesB, .5), Percentile(valuesA, .95), Percentile(valuesB, .95)));
        Flush(); Drop(output);
    }
    static string Mean(List<double> values) => values.Count == 0 ? "" : values.Average().ToString("F6", CultureInfo.InvariantCulture);
    static string Percentile(List<double> values, double fraction)
    {
        if (values.Count == 0) return ""; double[] sorted = values.OrderBy(value => value).ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(fraction * sorted.Length) - 1)].ToString("F6", CultureInfo.InvariantCulture);
    }
}
#endif
