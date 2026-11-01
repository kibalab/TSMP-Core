#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

// Correctness only. Managed SDK proxy execution is distinct from client VM.
public static class Color256EncoderValidation
{
    const string CodecPath = "Packages/com.kibalab.tsmp.codec.color256/Runtime/Scripts/TSMPCodecColor256";
    const string Session = "TSMP.Color256EncoderValidation";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Root => Environment.GetEnvironmentVariable("TSMP_COLOR256_ENCODER_RESULTS");
    static string Reference => Environment.GetEnvironmentVariable("TSMP_COLOR256_ENCODER_REFERENCE");
    static readonly List<string> Notes = new List<string>();
    static readonly List<string> Rows = new List<string> { "name,width,height,blockSize,blocks,pixelLength,payloadLength,count,vm,nativeResult,nativeException,oracleResult,oracleException,nativeSha256,oracleSha256,vmSha256,payloadSha256" };
    static int checks;
    [Serializable] public sealed class Case
    {
        public string name;
        public int width, height, blockSize = 8, pixelLength, payloadLength, count;
        public bool blocks = true, vm = true;
    }
    [Serializable] public sealed class Plan { public Case[] cases; }
    sealed class Outcome { public bool result; public string exception = ""; public byte[] pixels; }
    static void Check(bool value, string text) { checks++; if (!value) throw new InvalidOperationException(text); }
    static string H(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static string Row(params object[] values) => string.Join(",", values.Select(v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static Color32[] Pixels(int length)
    {
        var result = new Color32[length];
        for (int i = 0; i < length; i++) result[i] = new Color32((byte)(i * 17 + 3), (byte)(i * 7 + 101), (byte)(i * 13 + 55), 201);
        return result;
    }
    static byte[] Bytes(Color32[] pixels)
    {
        var result = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++) { int at = i * 4; result[at] = pixels[i].r; result[at + 1] = pixels[i].g; result[at + 2] = pixels[i].b; result[at + 3] = pixels[i].a; }
        return result;
    }
    static List<Case> Cases()
    {
        var result = new List<Case>();
        Action<string, int, int, int, int, int, bool, bool> add = (name, w, h, length, sourceLength, count, blocks, vm) =>
            result.Add(new Case { name = name, width = w * 8, height = h * 8, pixelLength = length, payloadLength = sourceLength, count = count, blocks = blocks, vm = vm });
        add("all256", 80, 45, 3600, 256, 256, true, true);
        add("empty", 160, 90, 14400, 0, 0, true, true);
        foreach (int count in new[] { 1, 159, 160, 161, 1024, 5120, 13119, 13120, 13121 }) add("row160-count" + count, 160, 90, 14400, count, count, true, true);
        add("partial-row161", 161, 91, 14651, 13363, 13363, true, true);
        add("dirty-extra-tail", 80, 45, 3617, 1024, 1024, true, true);
        add("short-block", 80, 45, 3150, 512, 512, true, true);
        add("zero-block", 80, 45, 0, 256, 256, true, true);
        add("narrow1", 1, 6, 6, 0, 0, true, true);
        add("narrow7", 7, 8, 56, 0, 0, true, true);
        add("partial-calibration", 38, 11, 418, 0, 0, true, true);
        add("small-full-fallback", 40, 15, 320 * 120, 31, 31, false, true);
        add("full-fallback1KB", 80, 45, 640 * 360, 1024, 1024, false, false);
        add("full-fallback5KB", 160, 90, 1280 * 720, 5120, 5120, false, false);
        add("invalid-count", 80, 45, 3600, 3, 4, true, false);
        add("invalid-count-zero-pixels", 80, 45, 0, 1, 2, true, false);
        add("invalid-short-full", 80, 45, 1, 1, 1, false, false);
        add("negative-count", 80, 45, 3600, 0, -1, true, false);
        return result;
    }
    // An independent fixed integer palette and coordinate oracle. It does not
    // call production SymbolToColor256, codec block helpers or the candidate.
    static Color32 Palette(byte symbol)
    {
        byte[] rg = { 24, 54, 83, 113, 143, 173, 202, 232 }, b = { 24, 93, 163, 232 };
        return new Color32(rg[symbol & 7], rg[(symbol >> 3) & 7], b[symbol >> 6], 255);
    }
    static bool Oracle(Case c, Color32[] pixels, byte[] source)
    {
        int width = Math.Max(0, c.width / Math.Max(1, c.blockSize)), height = Math.Max(0, c.height / Math.Max(1, c.blockSize));
        if (width <= 0 || height <= 0) return false;
        Action<int, Color32> write = (block, color) =>
        {
            int x = block % width, y = block / width;
            if (c.blocks)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                int index = (height - 1 - y) * width + x;
                if (index >= 0 && index < pixels.Length) pixels[index] = color;
            }
            else
            {
                for (int dy = 0; dy < c.blockSize; dy++)
                {
                    int py = c.height - 1 - (y * c.blockSize + dy);
                    if (py < 0 || py >= c.height) continue;
                    for (int dx = 0; dx < c.blockSize; dx++)
                    {
                        int px = x * c.blockSize + dx;
                        if (px >= 0 && px < c.width) pixels[py * c.width + px] = color;
                    }
                }
            }
        };
        for (int symbol = 0; symbol < 256; symbol++) write(5 * width + symbol, Palette((byte)symbol));
        int startRow = 5 + (256 + width - 1) / width, capacity = width * Math.Max(0, height - startRow - 1);
        for (int i = 0; i < c.count && i < capacity; i++) write(startRow * width + i, Palette(source[i]));
        return true;
    }
    static Outcome Execute(Case c, Func<Color32[], bool> action)
    {
        var pixels = Pixels(c.pixelLength); var outcome = new Outcome();
        try { outcome.result = action(pixels); } catch (Exception error) { outcome.exception = error.GetType().FullName; }
        outcome.pixels = Bytes(pixels); return outcome;
    }
    sealed class Vm : IDisposable
    {
        readonly IUdonProgram code; readonly UdonBehaviour backing;
        public Vm()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CodecPath + ".asset");
            code = asset.SerializedProgramAsset.RetrieveProgram(); Check(code != null && code.ByteCode.Length > 0, "Client codec missing");
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(code), "VM load failed");
            backing = new GameObject("Color256 writer client VM").AddComponent<UdonBehaviour>(); Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(backing, new object[] { code.SymbolTable, code.Heap }), "Unresolved heap");
            type.GetField("_program", Private).SetValue(backing, code); type.GetField("_udonVM", Private).SetValue(backing, vm);
            type.GetField("_udonManager", Private).SetValue(backing, UdonManager.Instance); type.GetField("_isReady", Private).SetValue(backing, true); type.GetField("_hasDoneStart", Private).SetValue(backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(backing);
            foreach (string name in code.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { code.EntryPoints.GetAddressFromSymbol(name) };
            Notes.Add("ClientProgram=" + CodecPath + ".asset; Target=client; PlayMode=" + Application.isPlaying + "; BytecodeBytes=" + code.ByteCode.Length + "; SHA256=" + H(code.ByteCode));
        }
        void Put<T>(string key, T value) => code.Heap.SetHeapVariable(code.SymbolTable.GetAddressFromSymbol(key), value, typeof(T));
        T Get<T>(string key) => (T)code.Heap.GetHeapVariable(code.SymbolTable.GetAddressFromSymbol(key));
        public Outcome Run(Case c, byte[] payload)
        {
            Put("encoderRequestWidth", c.width); Put("encoderRequestHeight", c.height); Put("encoderRequestBlockSize", c.blockSize);
            Put("encoderPixels", Pixels(c.pixelLength)); Put("encoderPixelsAreBlocks", c.blocks); Put("encoderPayloadBytes", payload); Put("encoderPayloadByteCount", c.count); Put("encoderWriteResult", false);
            backing.SendCustomEvent("OnTSMPEncoderWritePayload");
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(backing), "VM error " + c.name);
            return new Outcome { result = Get<bool>("encoderWriteResult"), pixels = Bytes(Get<Color32[]>("encoderPixels")) };
        }
        public void Dispose() { Object.DestroyImmediate(backing.gameObject); }
    }
    static void Compile()
    {
        bool error = false; Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!error, "Client compilation failed");
    }
    public static void Run()
    {
        Check(!string.IsNullOrWhiteSpace(Root) && !File.Exists(Path.Combine(Root, "result.txt")), "Fresh result directory required");
        Directory.CreateDirectory(Root); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Compile(); AssetDatabase.SaveAssets(); SessionState.SetBool(Session, true); Attach(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Attach() { EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered; }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Session, false)) return;
        SessionState.SetBool(Session, false); EditorApplication.delayCall += Validate;
    }
    static void Validate()
    {
        Exception failure = null; GameObject owner = null;
        try
        {
            Compile(); Check(Application.isPlaying, "Client VM must run after Play entry");
            Notes.Add("Unity=" + Application.unityVersion + "; Stage=" + (string.IsNullOrEmpty(Reference) ? "baseline" : "candidate"));
            Notes.Add("SDK managed C# proxy and freshly compiled client codec VM; coordinate/palette oracle independent. Full array checks include untouched header/static/tail sentinel regions. Actual protected packet header/application is separately covered by SustainedCost Encoder->Decoder A/B. No GPU byte oracle/performance/VRChat measurement here.");
            Notes.Add("CodecSourceSHA256=" + H(File.ReadAllBytes(CodecPath + ".cs")));
            var cases = Cases(); string plan = JsonUtility.ToJson(new Plan { cases = cases.ToArray() }, true);
            File.WriteAllText(Path.Combine(Root, "vectors.json"), plan);
            if (!string.IsNullOrEmpty(Reference)) Check(File.ReadAllText(Path.Combine(Reference, "vectors.json")) == plan, "Vector plan differs from immutable baseline");
            owner = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.color256/Runtime/Codec_Color256.prefab"));
            var codec = owner.GetComponent<TSMPCodec>(); Check(codec != null, "Missing managed codec proxy");
            using (var vm = new Vm()) foreach (Case c in cases)
            {
                byte[] payload = Enumerable.Range(0, c.payloadLength).Select(i => (byte)i).ToArray();
                Outcome native = Execute(c, pixels => { codec.encoderPixelsAreBlocks = c.blocks; return codec.WriteEncoderPayload(pixels, c.width, c.height, c.blockSize, payload, c.count); });
                Outcome oracle = Execute(c, pixels => Oracle(c, pixels, payload));
                Check(native.result == oracle.result && native.exception == oracle.exception && native.pixels.SequenceEqual(oracle.pixels), "Managed codec/oracle mismatch " + c.name + ": " + native.exception + "/" + oracle.exception);
                var outcomes = new Dictionary<string, byte[]> { { "managed", native.pixels }, { "oracle", oracle.pixels }, { "payload", payload } };
                string vmHash = "";
                if (c.vm)
                {
                    Check(native.exception == "", "Invalid VM vector " + c.name); Outcome actual = vm.Run(c, payload);
                    Check(actual.result == native.result && actual.pixels.SequenceEqual(native.pixels), "Client VM/full managed pixels mismatch " + c.name);
                    outcomes.Add("vm", actual.pixels); vmHash = H(actual.pixels);
                }
                string exceptionFile = c.name + ".exception.txt", outcomeText = native.result + "\n" + native.exception;
                File.WriteAllText(Path.Combine(Root, exceptionFile), outcomeText);
                if (!string.IsNullOrEmpty(Reference)) Check(File.ReadAllText(Path.Combine(Reference, exceptionFile)) == outcomeText, "Managed result/exception parity failed " + c.name);
                foreach (var pair in outcomes)
                {
                    string file = c.name + "." + pair.Key + ".bin"; File.WriteAllBytes(Path.Combine(Root, file), pair.Value);
                    if (!string.IsNullOrEmpty(Reference)) Check(File.ReadAllBytes(Path.Combine(Reference, file)).SequenceEqual(pair.Value), "Original full-byte parity failed " + file);
                }
                Rows.Add(Row(c.name, c.width, c.height, c.blockSize, c.blocks, c.pixelLength, c.payloadLength, c.count, c.vm, native.result, native.exception, oracle.result, oracle.exception, H(native.pixels), H(oracle.pixels), vmHash, H(payload)));
                File.WriteAllLines(Path.Combine(Root, "vectors.csv"), Rows);
            }
            Notes.Add("Vectors=" + cases.Count + "; ClientVmVectors=" + cases.Count(c => c.vm));
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (failure != null) Notes.Add(failure.ToString()); Notes.Add("Assertions=" + checks);
            File.WriteAllLines(Path.Combine(Root, "vectors.csv"), Rows);
            File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { failure == null ? "PASS" : "FAIL" }.Concat(Notes));
            EditorApplication.Exit(failure == null ? 0 : 1);
        }
    }
}
#endif
