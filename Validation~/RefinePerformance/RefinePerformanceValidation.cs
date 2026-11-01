#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Direct production-shader A/B, not a stream or a VRChat performance test.
public sealed class RefinePerformanceValidation : MonoBehaviour
{
    const string AssetsRoot = "Assets/RefinePerformance";
    const string SessionKey = "TSMP.RefinePerformance";
    const int Batch = 32, Warm = 10, Samples = 30;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string[] Modes = { "Color256", "RGB16Fixed", "RGB16Variable" };
    static string Root => Environment.GetEnvironmentVariable("TSMP_REFINE_RESULTS");
    static string Evidence => Environment.GetEnvironmentVariable("TSMP_REFINE_EVIDENCE");
    static string[] SelectedModes()
    {
        string value = Environment.GetEnvironmentVariable("TSMP_REFINE_MODES");
        string[] selected = string.IsNullOrWhiteSpace(value) ? Modes : value.Split(',').Select(item => item.Trim()).ToArray();
        Check(selected.Length > 0 && selected.All(item => Modes.Contains(item)) && selected.Distinct().Count() == selected.Length,
            "TSMP_REFINE_MODES requires distinct Color256, RGB16Fixed or RGB16Variable names.");
        return selected;
    }
    static int[] TimingRadii()
    {
        string value = Environment.GetEnvironmentVariable("TSMP_REFINE_TIMING_RADII");
        string[] selected = (string.IsNullOrWhiteSpace(value) ? "2" : value).Split(',').Select(item => item.Trim()).ToArray();
        Check(selected.All(item => item == "1" || item == "2" || item == "3") && selected.Distinct().Count() == selected.Length,
            "TSMP_REFINE_TIMING_RADII requires distinct radii from 1,2,3.");
        return selected.Select(int.Parse).ToArray();
    }
    readonly List<Object> owned = new List<Object>();
    readonly List<string> details = new List<string>();
    readonly List<string> comparisons = new List<string>();
    readonly List<string> timings = new List<string>();
    readonly List<string> rawTimings = new List<string>();
    readonly List<string> sourceHashes = new List<string>();
    bool timingUnavailable;
    int cases, failures, serial;

    sealed class Pair
    {
        public string mode;
        public TSMPCodec aCodec, bCodec;
        public Material a, b;
        public int r = 4, g = 4, bBits = 4;
    }

    public static void Run()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour))
            throw new InvalidOperationException("Use a dedicated SDK-free validation project.");
        if (string.IsNullOrWhiteSpace(Root) || string.IsNullOrWhiteSpace(Evidence))
            throw new InvalidOperationException("TSMP_REFINE_RESULTS and TSMP_REFINE_EVIDENCE are required.");
        SelectedModes(); TimingRadii();
        Directory.CreateDirectory(Root);
        if (File.Exists(Path.Combine(Root, "result.txt"))) throw new InvalidOperationException("Use new results.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(SessionKey, true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false);
        new GameObject("Refine performance experiment").AddComponent<RefinePerformanceValidation>();
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, true);
        Profiler.enabled = true;
        Own(new GameObject("Refine GPU frame pump")).AddComponent<Camera>().cullingMask = 0;
        details.Add("Direct GPU shader-byte parity and GPU timestamp experiment only; no runtime package, protocol, OBS, service or client changes.");
        details.Add("GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; Unity=" + Application.unityVersion + "; ColorSpace=" + QualitySettings.activeColorSpace);
        details.Add("Both Color256 materials belong to separate cloned codecs, so both use current-source calibration LUT preparation. RGB16 Refine retains its original no-LUT policy.");
        details.Add("Forced diagnostic sample/radius settings compare shader algorithms; saved image headers are not rewritten and these are not new supported transport profiles.");
        details.Add("SelectedModes=" + string.Join(",", SelectedModes()) + "; RGB16TimingRadii=" + string.Join(",", TimingRadii()) + "; Color256TimingRadius=1");
        comparisons.Add("case,mode,input,sample,radius,width,height,bytes,baselineCrc32,candidateCrc32,rawCrc32,baselineMatchesRaw,byteMismatches,status");
        timings.Add("case,mode,pattern,width,height,bytes,sample,radius,batch,validPairs,status,baselineMeanUs,candidateMeanUs,baselineP50Us,candidateP50Us,baselineP95Us,candidateP95Us");
        rawTimings.Add("case,frame,order,validGpuPair,baselineGpuNs,candidateGpuNs,baselineBlocks,candidateBlocks");
        sourceHashes.Add("path,sha256");
        foreach (string file in Directory.GetFiles(AssetsRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            Hash(file);
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.color256", "com.kibalab.tsmp.codec.rgb16" })
            foreach (string file in Directory.GetFiles("Packages/" + package + "/Runtime", "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                Hash(file);
        var stack = new Stack<IEnumerator>();
        stack.Push(Validate());
        while (stack.Count > 0)
        {
            IEnumerator iterator = stack.Peek();
            bool next;
            try { next = iterator.MoveNext(); }
            catch (Exception error) { failures++; details.Add(error.ToString()); Finish(); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (iterator.Current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return iterator.Current;
        }
        Finish();
    }

    T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    void Drop(Object value)
    {
        owned.Remove(value);
        if (value is RenderTexture target) target.Release();
        if (value != null) Object.DestroyImmediate(value);
    }
    void Finish()
    {
        Flush();
        details.Add("Comparison cases=" + cases + "; mismatched cases=" + failures + "; timing=" + (timingUnavailable ? "UNAVAILABLE_FOR_SOME_CASES" : "GPU_TIMESTAMPS_AVAILABLE"));
        details.Add("PASS refers to byte parity. Candidate adoption additionally requires supported actual GPU timing improvement; no frame-time/VRChat improvement is inferred.");
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { failures == 0 ? "PASS" : "FAIL" }.Concat(details));
        foreach (Object value in owned.AsEnumerable().Reverse())
        {
            if (value is RenderTexture target) target.Release();
            if (value != null) Object.DestroyImmediate(value);
        }
        Profiler.enabled = false;
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
    void Flush()
    {
        File.WriteAllLines(Path.Combine(Root, "comparisons.csv"), comparisons);
        File.WriteAllLines(Path.Combine(Root, "timings.csv"), timings);
        File.WriteAllLines(Path.Combine(Root, "timing-pairs.csv"), rawTimings);
        File.WriteAllLines(Path.Combine(Root, "source-hashes.csv"), sourceHashes);
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static string C(object value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(C));
    static string Digest(byte[] bytes)
    {
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    void Hash(string path) { sourceHashes.Add(Row(Path.GetFullPath(path), Digest(File.ReadAllBytes(path)))); }
    static uint Crc(byte[] bytes) => Crc32Runtime.Compute(bytes, 0, bytes.Length, Crc32Runtime.EnsureTable(null));

    TSMPCodec Codec(string mode, Material material)
    {
        string codec = mode == "Color256" ? "Color256" : "RGB16";
        string package = "Packages/com.kibalab.tsmp.codec." + codec.ToLowerInvariant();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(package + "/Runtime/Codec_" + codec + ".prefab");
        Check(prefab != null, "Missing codec prefab " + package);
        var instance = Own(Object.Instantiate(prefab));
        TSMPCodec result = instance.GetComponent<TSMPCodec>();
        Check(result != null, "Missing codec component.");
        // Preparation modifies properties; never modify package material assets.
        if (result.calibrationMaterial != null) result.calibrationMaterial = Own(new Material(result.calibrationMaterial));
        if (result is TSMPCodecColor256 color) color.robustRefineByteDecodeMaterial = material;
        else if (result is TSMPCodecRGB16 rgb)
        {
            rgb.localRefine = true;
            if (mode == "RGB16Fixed") rgb.refine444ByteDecodeMaterial = material;
            else rgb.variableRefineByteDecodeMaterial = material;
        }
        return result;
    }
    Pair CreatePair(string mode)
    {
        var pair = new Pair { mode = mode };
        Shader original = AssetDatabase.LoadAssetAtPath<Shader>(AssetsRoot + "/Shaders/" + mode + ".Original.shader");
        Shader candidate = AssetDatabase.LoadAssetAtPath<Shader>(AssetsRoot + "/Shaders/" + mode + ".Candidate.shader");
        Check(original != null && candidate != null && original.isSupported && candidate.isSupported, "Shadow shaders unavailable: " + mode);
        pair.a = Own(new Material(original)); pair.b = Own(new Material(candidate));
        pair.aCodec = Codec(mode, pair.a); pair.bCodec = Codec(mode, pair.b);
        if (mode == "RGB16Variable") SetBits(pair, 5, 6, 4);
        return pair;
    }
    static void SetBits(Pair pair, int r, int g, int b)
    {
        pair.r = r; pair.g = g; pair.bBits = b;
        foreach (TSMPCodec codec in new[] { pair.aCodec, pair.bCodec })
            if (codec is TSMPCodecRGB16 rgb) { rgb.rBits = r; rgb.gBits = g; rgb.bBits = b; }
    }
    static RenderTexture Cache(TSMPCodec codec) => (RenderTexture)typeof(TSMPCodec).GetField("_calibrationLut", Private).GetValue(codec);
    RenderTexture Target(int bytes)
    {
        Check(bytes <= 8192, "Experiment payload exceeds its 2048-pixel RGBA8 target.");
        // Match the saved transport harness's production byte-target geometry.
        var result = Own(new RenderTexture(2048, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        result.filterMode = FilterMode.Point; result.wrapMode = TextureWrapMode.Clamp;
        Check(result.Create(), "Cannot create output texture.");
        return result;
    }
    void Setup(Pair pair, Texture input, int count, int sample, int radius, int flip, RenderTexture output)
    {
        foreach (Material material in new[] { pair.a, pair.b })
        {
            int columns = input.width / 8;
            material.SetTexture("_MainTex", input);
            material.SetFloat("_BlockSize", 8); material.SetFloat("_SampleSize", sample);
            material.SetFloat("_ActiveWidthBlocks", columns);
            material.SetFloat("_SourceWidth", input.width); material.SetFloat("_SourceHeight", input.height);
            material.SetFloat("_StartBlock", pair.aCodec.GetPayloadStartRow(input.width, 8) * columns);
            material.SetFloat("_ByteCount", count); material.SetFloat("_OutputWidth", output.width); material.SetFloat("_OutputHeight", 1);
            material.SetFloat("_FlipY", flip); material.SetFloat("_RefineRadius", radius);
            material.SetFloat("_RBits", pair.r); material.SetFloat("_GBits", pair.g); material.SetFloat("_BBits", pair.bBits);
            material.SetFloat("_Rgb16CalibrationStartBlock", 5 * columns);
            material.SetFloat("_ColorCalibrationStartBlock", 5 * columns);
            material.SetFloat("_InterleaveMode", 0); material.SetFloat("_TSMPHeaderPixels", 0);
        }
    }
    static byte[] Read(RenderTexture output, int count)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32);
        request.WaitForCompletion();
        Check(!request.hasError, "Readback failed.");
        return request.GetData<byte>().Take(count).ToArray();
    }
    void Compare(Pair pair, Texture input, byte[] raw, string label, int sample, int radius, int flip, bool requireExact = false)
    {
        var output = Target(raw.Length);
        Setup(pair, input, raw.Length, sample, radius, flip, output);
        pair.aCodec.PrepareDecode(input, pair.a);
        Graphics.Blit(input, output, pair.a); byte[] allA = Read(output, output.width * 4);
        pair.bCodec.PrepareDecode(input, pair.b);
        Graphics.Blit(input, output, pair.b); byte[] allB = Read(output, output.width * 4);
        byte[] a = allA.Take(raw.Length).ToArray(), b = allB.Take(raw.Length).ToArray();
        Check(pair.a.IsKeywordEnabled("TSMP_CALIBRATION_LUT") == (pair.mode == "Color256"), "Wrong original LUT policy.");
        Check(pair.b.IsKeywordEnabled("TSMP_CALIBRATION_LUT") == (pair.mode == "Color256"), "Wrong candidate LUT policy.");
        int changed = allA.Zip(allB, (left, right) => left != right ? 1 : 0).Sum();
        Check(allA.Skip(raw.Length).All(value => value == 0) && allB.Skip(raw.Length).All(value => value == 0), "Unused byte-target padding is not zero.");
        bool exact = a.SequenceEqual(raw);
        Check(!requireExact || exact, "Original exact-control shader did not recover independent bytes: " + pair.mode + "; " + label);
        cases++; if (changed != 0) failures++;
        comparisons.Add(Row(++serial, pair.mode, label + ";flip=" + flip, sample, radius, input.width, input.height, raw.Length,
            Crc(a).ToString("X8"), Crc(b).ToString("X8"), Crc(raw).ToString("X8"), exact, changed, changed == 0 ? "SAME_BYTES" : "DIFFERENT_BYTES"));
        Drop(output);
    }
    Texture2D Load(string path)
    {
        Hash(path);
        var image = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
        Check(image.LoadImage(File.ReadAllBytes(path)), "Cannot load " + path);
        image.filterMode = FilterMode.Point; image.wrapMode = TextureWrapMode.Clamp;
        return image;
    }
    Texture2D Noise(Texture2D source)
    {
        var random = new System.Random(317);
        Color[] pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            pixel.r = Mathf.Clamp01(pixel.r * .9431f + .0247f + (float)(random.NextDouble() - .5) * .008f);
            pixel.g = Mathf.Clamp01(pixel.g * .9613f + .0173f + (float)(random.NextDouble() - .5) * .008f);
            pixel.b = Mathf.Clamp01(pixel.b * .9217f + .0311f + (float)(random.NextDouble() - .5) * .008f);
            pixels[i] = pixel;
        }
        var image = Own(new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true));
        image.SetPixels(pixels); image.Apply(false, false); image.filterMode = FilterMode.Point;
        return image;
    }
    Texture2D VariableInput(Pair pair, byte[] raw, int width, int height)
    {
        var result = Own(new Texture2D(width, height, TextureFormat.RGBA32, false, true));
        result.filterMode = FilterMode.Point;
        var header = FrameHeader.CreateDefault();
        header.BlockSize = 8; header.ActiveWidthBlocks = (ushort)(width / 8); header.ActiveHeightBlocks = (ushort)(height / 8);
        header.CodecId = pair.aCodec.codecId; header.SymbolMode = (byte)pair.aCodec.SymbolMode;
        header.PayloadSize = (ushort)raw.Length; header.DecodeSampleSize = 4;
        byte[] options = pair.aCodec.GetCodecOptionBytes();
        header.CodecOptionLength = (byte)options.Length;
        header.CodecOption0 = options[0]; header.CodecOption1 = options[1]; header.CodecOption2 = options[2]; header.CodecOption3 = options[3]; header.CodecOption4 = options[4];
        var bytes = new byte[56]; header.WriteTo(bytes, 0);
        Check(FrameHeaderWriter.TryWritePayloadCrc32(bytes, 0, raw, raw.Length, Crc32Runtime.EnsureTable(null)), "Header CRC write failed.");
        Check(pair.aCodec.TryWriteFrame(result, 8, bytes, raw, out string error), error);
        return result;
    }

    IEnumerator Validate()
    {
        bool quick = Environment.GetEnvironmentVariable("TSMP_REFINE_QUICK") == "1";
        foreach (string mode in SelectedModes())
        {
            Pair pair = CreatePair(mode);
            string fixtureCodec = mode == "Color256" ? "color256" : "rgb16";
            foreach (string dimensions in new[] { "720p", "1440p" })
            foreach (string pattern in new[] { "structured", "random" })
            foreach (int frame in new[] { 1, 2, 3 })
            {
                string fixture = Path.Combine(Evidence, "fixtures", fixtureCodec + "-" + dimensions + "-" + pattern);
                string name = "frame-" + frame.ToString("000000");
                string payload = Path.Combine(fixture, "payload", name + ".bin");
                byte[] raw = File.ReadAllBytes(payload); Hash(payload);
                Texture2D input = mode == "RGB16Variable" ? VariableInput(pair, raw, dimensions == "720p" ? 1280 : 2560, dimensions == "720p" ? 720 : 1440)
                    : Load(Path.Combine(fixture, "input", name + ".png"));
                Texture2D noisy = Noise(input);
                foreach (int sample in quick ? new[] { 4 } : new[] { 2, 4 })
                foreach (int radius in mode == "Color256" ? new[] { 1 } : new[] { 1, 2, 3 })
                foreach (int flip in quick ? new[] { 1 } : new[] { 1, 0 })
                {
                    Compare(pair, input, raw, dimensions + "/" + pattern + "/exact", sample, radius, flip,
                        sample == 4 && flip == 1 && radius == (mode == "Color256" ? 1 : 2));
                    Compare(pair, noisy, raw, dimensions + "/" + pattern + "/noise", sample, radius, flip);
                }
                if (Environment.GetEnvironmentVariable("TSMP_REFINE_SKIP_TIMING") != "1")
                    foreach (int radius in mode == "Color256" ? new[] { 1 } : TimingRadii())
                        yield return Measure(pair, input, raw.Length, pattern, 4, radius);
                Drop(noisy); Drop(input); Flush(); yield return null;
            }
            // Real restored video frames include original failing payloads. A/B
            // must preserve their bytes too; this does not change them to PASS.
            if (mode != "RGB16Variable")
            foreach (string dimensions in new[] { "720p", "1440p" })
            foreach (string pattern in new[] { "structured", "random" })
            foreach (string profile in dimensions == "720p" ? new[] { "x264-veryfast-cbr3.5M-709limited", "x264-veryfast-crf18-709limited", "nvenc-p4-cbr3.5M-709limited" }
                : new[] { "nvenc-p4-cbr10M-709full", "nvenc-p4-cbr10M-709limited" })
            foreach (int frame in quick ? new[] { 1, 2, 3 } : new[] { 1, 2, 3, 22, 150, 300 })
            {
                string dataset = fixtureCodec + "-" + dimensions + "-" + pattern;
                string name = "frame-" + frame.ToString("000000");
                string payload = Path.Combine(Evidence, "fixtures", dataset, "payload", name + ".bin");
                byte[] raw = File.ReadAllBytes(payload); Hash(payload);
                Texture2D input = Load(Path.Combine(Evidence, "matrix", dataset, profile, "decoded", name + ".png"));
                Compare(pair, input, raw, dataset + "/" + profile + "/" + name, 4, mode == "Color256" ? 1 : 2, 1);
                Drop(input);
            }
            if (mode == "RGB16Variable")
            foreach (var bits in new[] { new[] { 1, 1, 1 }, new[] { 3, 3, 2 }, new[] { 8, 8, 4 } })
            {
                SetBits(pair, bits[0], bits[1], bits[2]);
                foreach (int count in new[] { 32, 2495 })
                {
                    byte[] raw = new byte[count]; new System.Random(774 + count).NextBytes(raw);
                    Texture2D input = VariableInput(pair, raw, 1280, 720);
                    Texture2D noisy = Noise(input);
                    foreach (int radius in new[] { 1, 2, 3 })
                    foreach (int sample in new[] { 2, 4 })
                    {
                        Compare(pair, input, raw, "variable-bits=" + string.Join("/", bits), sample, radius, 1);
                        Compare(pair, noisy, raw, "variable-noise-bits=" + string.Join("/", bits), sample, radius, 1);
                    }
                    Drop(noisy); Drop(input); yield return null;
                }
            }
            Flush(); details.Add("Completed " + mode); Debug.Log("Refine experiment completed " + mode);
        }
    }

    IEnumerator Measure(Pair pair, Texture input, int count, string pattern, int sample, int radius)
    {
        var output = Target(count);
        Setup(pair, input, count, sample, radius, 1, output);
        pair.aCodec.PrepareDecode(input, pair.a); pair.bCodec.PrepareDecode(input, pair.b);
        int id = ++serial;
        var samplerA = CustomSampler.Create("Refine.Original." + id, true);
        var samplerB = CustomSampler.Create("Refine.Candidate." + id, true);
        var a = Commands(pair.aCodec, pair.a, input, output, samplerA);
        var b = Commands(pair.bCodec, pair.b, input, output, samplerB);
        Recorder recorderA = samplerA.GetRecorder(), recorderB = samplerB.GetRecorder();
        recorderA.enabled = true; recorderB.enabled = true;
        var gpuA = new List<double>(); var gpuB = new List<double>();
        for (int frame = 0; frame < Warm + Samples * 2 && gpuA.Count < Samples; frame++)
        {
            if ((frame & 1) == 0) { Submit(a, output); Submit(b, output); }
            else { Submit(b, output); Submit(a, output); }
            yield return null;
            bool valid = recorderA.gpuSampleBlockCount == 1 && recorderB.gpuSampleBlockCount == 1
                && recorderA.gpuElapsedNanoseconds > 0 && recorderB.gpuElapsedNanoseconds > 0;
            rawTimings.Add(Row(id, frame, (frame & 1) == 0 ? "AB" : "BA", valid,
                recorderA.gpuElapsedNanoseconds, recorderB.gpuElapsedNanoseconds, recorderA.gpuSampleBlockCount, recorderB.gpuSampleBlockCount));
            if (frame < Warm || !valid) continue;
            gpuA.Add(recorderA.gpuElapsedNanoseconds / 1000.0 / Batch);
            gpuB.Add(recorderB.gpuElapsedNanoseconds / 1000.0 / Batch);
        }
        recorderA.enabled = false; recorderB.enabled = false; a.Dispose(); b.Dispose();
        bool available = gpuA.Count >= Samples;
        timingUnavailable |= !available;
        timings.Add(Row(id, pair.mode, pattern, input.width, input.height, count, sample, radius, Batch, gpuA.Count,
            available ? "GPU_TIMED" : "GPU_TIMING_UNAVAILABLE", Mean(gpuA), Mean(gpuB), Percentile(gpuA, .5), Percentile(gpuB, .5), Percentile(gpuA, .95), Percentile(gpuB, .95)));
        Flush(); Drop(output);
    }
    static string Mean(List<double> values) => values.Count == 0 ? "" : values.Average().ToString("F6", CultureInfo.InvariantCulture);
    static string Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return "";
        double[] ordered = values.OrderBy(value => value).ToArray();
        return ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(p * ordered.Length) - 1)].ToString("F6", CultureInfo.InvariantCulture);
    }
    static CommandBuffer Commands(TSMPCodec codec, Material material, Texture input, RenderTexture output, CustomSampler sampler)
    {
        var commands = new CommandBuffer(); commands.BeginSample(sampler);
        for (int index = 0; index < Batch; index++)
        {
            if (material.IsKeywordEnabled("TSMP_CALIBRATION_LUT")) commands.Blit(input, Cache(codec), codec.calibrationMaterial);
            commands.Blit(input, output, material);
        }
        commands.EndSample(sampler); return commands;
    }
    static void Submit(CommandBuffer commands, RenderTexture output)
    {
        Graphics.ExecuteCommandBuffer(commands);
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32);
        request.WaitForCompletion(); Check(!request.hasError, "GPU timing synchronization failed.");
    }
}
#endif
