#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

// Reuses the existing ContinuousFrames editor-VM execution approach. Product
// programs, codec materials, CRC checks, callbacks and field dispatch are real.
public static class RefineCostValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string SessionKey = "TSMP.RefineCost";
    const string ReceiverPath = "Assets/Validation/RefineCost/RefineCostReceiver.asset";
    static string Root => Environment.GetEnvironmentVariable("TSMP_REFINE_COST_RESULTS");
    static readonly List<Object> owned = new List<Object>();
    static readonly List<Program> programs = new List<Program>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> rows = new List<string>();
    static readonly List<string> calls = new List<string>();
    static readonly List<string> hashes = new List<string>();
    static readonly Dictionary<string, string> sourceBefore = new Dictionary<string, string>();
    static IEnumerator work;
    static int assertions;

    [Serializable] public sealed class Plan { public Case[] cases; public int warmup = 5; public int samples = 30; }
    [Serializable] public sealed class Case
    {
        public string name, codec, input, header, payload;
        public int minimumAttempts;
    }
    sealed class Observation
    {
        public long startTicks, endTicks;
        public double submitVmMs, callbackVmMs, otherVmMs;
        public int callbacks;
        public readonly List<string> eventRows = new List<string>();
    }

    public static void Run()
    {
        Check(!string.IsNullOrWhiteSpace(Root), "TSMP_REFINE_COST_RESULTS required.");
        Directory.CreateDirectory(Root);
        Check(!File.Exists(Path.Combine(Root, "result.txt")), "Use fresh results.");
        File.WriteAllText(Path.Combine(Root, "result.txt"), "RUNNING\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var receiver = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ReceiverPath);
        if (receiver == null)
        {
            receiver = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            receiver.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/RefineCost/Runtime/RefineCostReceiver.cs");
            Check(receiver.sourceCsScript != null, "Receiver source missing.");
            AssetDatabase.CreateAsset(receiver, ReceiverPath);
        }
        Compile(); AssetDatabase.SaveAssets();
        SessionState.SetBool(SessionKey, true); Attach(); EditorApplication.EnterPlaymode();
    }
    static void Compile()
    {
        bool failed = false;
        Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Full Udon client compilation failed.");
    }
    [InitializeOnLoadMethod] static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false); work = Validate(); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }
    static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    static string H(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    static string Q(object value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(Q));
    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
    static string Resolve(string path) => Path.GetFullPath(path);

    static Dictionary<string, string> Sources()
    {
        var result = new Dictionary<string, string>();
        foreach (string package in new[] { "core", "codec.luma4", "codec.rgb16", "codec.color256", "codec.rgb20" })
        {
            string directory = "Packages/com.kibalab.tsmp." + package;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(directory);
            foreach (string scope in new[] { "Runtime", "Editor" })
            {
                string source = Path.Combine(info != null ? info.resolvedPath : directory, scope);
                if (!Directory.Exists(source)) continue;
                foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                    .Where(path => new[] { ".cs", ".shader", ".cginc", ".meta", ".asmdef" }.Contains(Path.GetExtension(path))).OrderBy(path => path))
                    result[Resolve(path)] = H(File.ReadAllBytes(path));
            }
        }
        return result;
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        foreach (var program in programs) program.Dispose(); programs.Clear();
        RenderTexture.active = null;
        foreach (Object item in owned.AsEnumerable().Reverse())
        {
            if (item is RenderTexture texture) texture.Release();
            if (item != null) Object.DestroyImmediate(item);
        }
        owned.Clear();
        try
        {
            var after = Sources();
            File.WriteAllLines(Path.Combine(Root, "source-after.csv"), new[] { "path,sha256" }.Concat(after.Select(pair => Row(pair.Key, pair.Value))));
            Check(after.Count == sourceBefore.Count && sourceBefore.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value), "Product sources changed during measurement.");
        }
        catch (Exception auditError) { if (error == null) error = auditError; else notes.Add(auditError.ToString()); }
        if (error != null) notes.Add(error.ToString());
        notes.Add("Assertions=" + assertions);
        File.WriteAllLines(Path.Combine(Root, "samples.csv"), rows);
        File.WriteAllLines(Path.Combine(Root, "vm-events.csv"), calls);
        File.WriteAllLines(Path.Combine(Root, "artifact-hashes.csv"), hashes);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(notes));
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;
        public Observation observation;
        public Program(string path, TSMPCodec codec = null, bool timedDecoder = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            Check(asset != null, "Missing program " + path);
            Code = asset.SerializedProgramAsset.RetrieveProgram();
            Check(Code != null && Code.ByteCode.Length > 0, "Missing bytecode " + path);
            hashes.Add(Row("bytecode", path, H(Code.ByteCode)));
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(Code), "VM load failed.");
            Backing = new GameObject(asset.name + " refine cost VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved heap.");
            type.GetField("_program", Private).SetValue(Backing, Code);
            type.GetField("_udonVM", Private).SetValue(Backing, timedDecoder ? new TimedVm(vm, Code, this) : vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true); type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null)
                foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address)) Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
            programs.Add(this);
        }
        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM error in " + name);
        }
        public void Dispose() { if (Backing != null) Object.DestroyImmediate(Backing.gameObject); }
    }
    sealed class TimedVm : IUdonVM
    {
        readonly IUdonVM inner;
        readonly Program owner;
        readonly Dictionary<uint, string> labels = new Dictionary<uint, string>();
        readonly uint readbackAddress;
        public TimedVm(IUdonVM vm, IUdonProgram program, Program owner)
        {
            inner = vm; this.owner = owner; readbackAddress = program.SymbolTable.GetAddressFromSymbol("readbackInFlight");
            foreach (string name in program.EntryPoints.GetExportedSymbols()) labels[program.EntryPoints.GetAddressFromSymbol(name)] = name;
        }
        public bool DebugLogging { get => inner.DebugLogging; set => inner.DebugLogging = value; }
        public bool LoadProgram(IUdonProgram program) => inner.LoadProgram(program);
        public IUdonProgram RetrieveProgram() => inner.RetrieveProgram();
        public void SetProgramCounter(uint counter) => inner.SetProgramCounter(counter);
        public uint GetProgramCounter() => inner.GetProgramCounter();
        public IUdonHeap InspectHeap() => inner.InspectHeap();
        public uint Interpret()
        {
            string label; if (!labels.TryGetValue(inner.GetProgramCounter(), out label)) label = "unknown";
            long before = Stopwatch.GetTimestamp(); uint result = inner.Interpret(); long after = Stopwatch.GetTimestamp();
            var observation = owner.observation;
            if (observation != null)
            {
                double duration = Ms(after - before);
                if (label == "DecodeNow") observation.submitVmMs += duration;
                else if (label.ToLowerInvariant().Contains("readback"))
                {
                    observation.callbackVmMs += duration; observation.callbacks++;
                    if (!(bool)owner.Code.Heap.GetHeapVariable(readbackAddress)) observation.endTicks = after;
                }
                else observation.otherVmMs += duration;
                observation.eventRows.Add(Row(label, duration));
            }
            return result;
        }
    }

    static TSMPCodec CloneCodec(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab");
        Check(prefab != null, "Missing codec prefab " + name);
        var codec = Keep(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            Material copy; if (copies.TryGetValue(original, out copy)) return copy;
            copy = Keep(new Material(original)); copies.Add(original, copy); return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials) field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return codec;
    }
    static RenderTexture Target(int width, int height)
    {
        var texture = Keep(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        Check(texture.Create(), "RenderTexture allocation failed."); return texture;
    }
    static void WarmGraphics(TSMPCodec codec, RenderTexture source, RenderTexture output)
    {
        long started = Stopwatch.GetTimestamp(); var previous = RenderTexture.active;
        var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        try
        {
            for (int variant = 0; variant < codec.DecodeMaterialCount; variant++)
            for (int mode = 0; mode <= 2; mode++)
            {
                var material = codec.GetDecodeMaterial(variant); Check(material != null, "Missing warm material.");
                material.SetFloat("_BlockSize", 8); material.SetFloat("_SampleSize", 4);
                material.SetFloat("_StartBlock", codec.payloadStartRow * (source.width / 8)); material.SetFloat("_ByteCount", 5375);
                material.SetFloat("_ActiveWidthBlocks", source.width / 8); material.SetFloat("_SourceWidth", source.width); material.SetFloat("_SourceHeight", source.height);
                material.SetFloat("_OutputWidth", output.width); material.SetFloat("_OutputHeight", output.height); material.SetFloat("_FlipY", 1);
                if (material.HasProperty("_RecoveryMode")) material.SetFloat("_RecoveryMode", mode);
                codec.PrepareDecode(source, material); Graphics.Blit(source, output, material);
            }
            RenderTexture.active = output; pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            notes.Add("Graphics warmup outside VM/case timing " + codec.displayName + "=" + Ms(Stopwatch.GetTimestamp() - started).ToString("F3", CultureInfo.InvariantCulture) + "ms");
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixel); }
    }
    static byte[] ExpectedField(byte[] payload)
    {
        int count, offset; Check(NetworkFrameReader.TryReadNetworkFrameHeader(payload, out count, out offset) && count == 1, "Expected one field message.");
        ushort network; int type, start, end, next;
        Check(NetworkFrameReader.TryReadMessageHeader(payload, offset, payload.Length, out network, out type, out start, out end, out next) && network == 1 && type == NetworkFrameProtocol.MessageTypeVariableState && next == payload.Length, "Expected generated VariableState schema.");
        Check(NetworkFrameReader.TryReadVariableStateHeader(payload, start, end, out count, out offset) && count == 1, "Expected one generated field.");
        uint hash; int valueType, valueOffset, valueLength;
        Check(NetworkFrameReader.TryReadVariableEntry(payload, offset, end, out hash, out valueType, out valueOffset, out valueLength, out next) && hash == 100 && valueType == NetworkFrameProtocol.ValueTypeRawBytes && next == end, "Unexpected RawBytes field schema.");
        return payload.Skip(valueOffset).Take(valueLength).ToArray();
    }
    static Program Decoder(Program receiver, Program[] codecs, RenderTexture source, RenderTexture output, int budget)
    {
        var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset", timedDecoder: true);
        decoder.Set<Texture>("sourceTexture", source); decoder.Set("payloadByteTexture", output);
        decoder.Set("readbackPackMaterial", Resources.Load<Material>("TSMPReadbackPack"));
        decoder.Set("codecHandlers", codecs.Select(program => program.Backing).ToArray());
        decoder.Set("bindingTargets", new Component[0]); decoder.Set("bindingUdonTargets", new[] { receiver.Backing });
        decoder.Set("bindingNetworkIds", new ushort[] { 1 }); decoder.Set("bindingVariableHashes", new uint[] { 100 });
        decoder.Set("bindingValueTypes", new[] { (byte)NetworkFrameProtocol.ValueTypeRawBytes }); decoder.Set("bindingFieldNames", new[] { "receivedValue" });
        decoder.Set("bindingDirections", new int[1]); decoder.Set("bindingPriorities", new int[1]);
        decoder.Set("applyEveryFrame", false); decoder.Set("skipDuplicateFrames", false); decoder.Set("requirePayloadCrc32", true);
        decoder.Set("decodeSafetyMode", 0); decoder.Set("recoverHeaderCrc", false); decoder.Set("maxCrcRecoveryAttempts", budget);
        decoder.Set("usePredictedReadback", true); decoder.Set("useCombinedByteOutput", true); decoder.Set("overlapReadbacks", true);
        decoder.Set("flipY", true); decoder.Set("debugLog", false); decoder.Call("_onEnable"); return decoder;
    }
    static IEnumerator Validate()
    {
        Compile(); Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
        notes.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        notes.Add("CoreRevision=" + Environment.GetEnvironmentVariable("TSMP_REFINE_COST_REVISION"));
        notes.Add("Actual freshly compiled client Udon Decoder+codec+receiver programs and VRC GPU callbacks in SDK Editor; no real VRChat client/service/FPS measurement.");
        notes.Add("Controlled sequential repeated original inputs, duplicate skipping disabled for full application. Timings are controlled invocations, not unique update Hz.");
        notes.Add("Submit and callback CPU are decoder VM Interpret wall times, including nested codec/receiver work and synchronous GPU driver calls; callback sum includes every recovery callback. Wall latency ends at final callback return, excluding later harness polling. No GPU time inferred from CPU/wall.");
        notes.Add("Input upload, input checks, heap observation, compilation, shader warmup, first five per-budget invocations and file IO are outside measured scope. Each case/budget warms independently; prediction can remain unavailable on repeatedly rejected inputs. Host instrumentation/Editor scheduling can affect timings.");
        foreach (var pair in Sources()) sourceBefore.Add(pair.Key, pair.Value);
        File.WriteAllLines(Path.Combine(Root, "source-before.csv"), new[] { "path,sha256" }.Concat(sourceBefore.Select(pair => Row(pair.Key, pair.Value))));
        rows.Add("case,codec,budget,sample,warmup,expectedMinimumAttempts,exact,bytes,fieldBytes,submitVmMs,callbackVmMs,totalVmMs,wallLatencyMs,callbacks,payloadAttempts,headerAttempts,notifications,predictedReadbacks,fallbacks,error");
        calls.Add("case,budget,sample,warmup,event,vmMs"); hashes.Add("kind,path,sha256");
        string planPath = Resolve(Environment.GetEnvironmentVariable("TSMP_REFINE_COST_PLAN")); byte[] planBytes = File.ReadAllBytes(planPath);
        hashes.Add(Row("plan", planPath, H(planBytes))); File.WriteAllBytes(Path.Combine(Root, "plan-used.json"), planBytes);
        var plan = JsonUtility.FromJson<Plan>(System.Text.Encoding.UTF8.GetString(planBytes).TrimStart('\uFEFF'));
        Check(plan.cases != null && plan.cases.Length > 0 && plan.warmup >= 5 && plan.samples >= 30, "Invalid case plan.");
        Keep(new GameObject("Refine cost frame pump")).AddComponent<Camera>().cullingMask = 0;
        foreach (Case item in plan.cases)
        {
            var casePrograms = new List<Program>();
            var image = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            byte[] input = File.ReadAllBytes(item.input), header = File.ReadAllBytes(item.header), payload = File.ReadAllBytes(item.payload);
            foreach (var artifact in new[] { Tuple.Create(item.input, input), Tuple.Create(item.header, header), Tuple.Create(item.payload, payload) }) hashes.Add(Row("case", Resolve(artifact.Item1), H(artifact.Item2)));
            Check(image.LoadImage(input), "Invalid input " + item.name); image.filterMode = FilterMode.Point; image.wrapMode = TextureWrapMode.Clamp;
            FrameHeader reference; Check(FrameHeader.TryRead(header, 0, out reference) && reference.PayloadSize == payload.Length, "Invalid independent header.");
            Check(reference.BlockSize == 8 && reference.DecodeSampleSize == 4 && payload.Length <= 8192, "Unexpected fixture geometry.");
            byte[] expectedField = ExpectedField(payload);
            var source = Target(image.width, image.height); Graphics.Blit(image, source);
            var lumaNative = CloneCodec("Luma4"); var native = item.codec == "Luma4" ? lumaNative : CloneCodec(item.codec);
            var warmTarget = Target(2048, 1); WarmGraphics(lumaNative, source, warmTarget); if (native != lumaNative) WarmGraphics(native, source, warmTarget);
            var luma = new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", lumaNative); casePrograms.Add(luma);
            Program codec = luma;
            if (item.codec != "Luma4") { codec = new Program("Packages/com.kibalab.tsmp.codec." + item.codec.ToLowerInvariant() + "/Runtime/Scripts/TSMPCodec" + item.codec + ".asset", native); casePrograms.Add(codec); }
            var receivers = new Program[3]; var decoders = new Program[3];
            for (int budget = 0; budget <= 2; budget++)
            {
                receivers[budget] = new Program(ReceiverPath); casePrograms.Add(receivers[budget]);
                decoders[budget] = Decoder(receivers[budget], codec == luma ? new[] { luma } : new[] { luma, codec }, source, Target(2048, 1), budget); casePrograms.Add(decoders[budget]);
            }
            for (int sample = 0; sample < plan.warmup + plan.samples; sample++)
            for (int position = 0; position < 3; position++)
            {
                int budget = (sample + position) % 3; var decoder = decoders[budget]; var receiver = receivers[budget];
                receiver.Set<byte[]>("receivedValue", null); int beforeNotifications = receiver.Get<int>("notifications");
                int beforeAttempts = decoder.Get<int>("payloadRecoveryAttemptCount"), beforeHeaders = decoder.Get<int>("headerRecoveryAttemptCount");
                int beforePrediction = decoder.Get<int>("predictedReadbackCount"), beforeFallback = decoder.Get<int>("predictionFallbackCount");
                var observation = new Observation { startTicks = Stopwatch.GetTimestamp() }; decoder.observation = observation;
                decoder.Call("DecodeNow"); double deadline = EditorApplication.timeSinceStartup + 30;
                while (decoder.Get<bool>("readbackInFlight") && EditorApplication.timeSinceStartup < deadline) yield return null;
                decoder.observation = null;
                Check(!decoder.Get<bool>("readbackInFlight") && observation.endTicks > observation.startTicks && observation.callbacks > 0, "Missing final callback " + item.name);
                bool exact = decoder.Get<bool>("lastHeaderValid") && decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcVerified");
                bool expected = item.minimumAttempts >= 0 && budget >= item.minimumAttempts;
                Check(exact == expected, "Budget verdict differs from frozen Native expectation " + item.name + " budget=" + budget + " error=" + decoder.Get<string>("lastError"));
                int notifications = receiver.Get<int>("notifications") - beforeNotifications;
                if (exact)
                {
                    Check(decoder.Get<uint>("lastFrameIndex") == reference.FrameIndex && decoder.Get<int>("_payloadDataBytes") == payload.Length, "Frame identity/length mismatch.");
                    Check(header.SequenceEqual(decoder.Get<byte[]>("_headerBytes").Take(header.Length)) && payload.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(payload.Length)), "Accepted bytes differ from independent originals.");
                    Check(expectedField.SequenceEqual(receiver.Get<byte[]>("receivedValue")) && notifications == 1 && decoder.Get<int>("lastAppliedVariableCount") == 1, "Full field not applied exactly once.");
                }
                else Check(receiver.Get<byte[]>("receivedValue") == null && notifications == 0 && decoder.Get<int>("lastAppliedVariableCount") == 0, "Rejected payload applied state.");
                int attempts = decoder.Get<int>("payloadRecoveryAttemptCount") - beforeAttempts;
                Check(attempts == (item.minimumAttempts < 0 ? budget : Math.Min(budget, item.minimumAttempts)), "Unexpected retry count.");
                Check(decoder.Get<int>("headerRecoveryAttemptCount") == beforeHeaders, "Unexpected header recovery.");
                bool warmup = sample < plan.warmup;
                rows.Add(Row(item.name, item.codec, budget, sample, warmup, item.minimumAttempts, exact, payload.Length, expectedField.Length,
                    observation.submitVmMs, observation.callbackVmMs, observation.submitVmMs + observation.callbackVmMs + observation.otherVmMs,
                    Ms(observation.endTicks - observation.startTicks), observation.callbacks, attempts, 0, notifications,
                    decoder.Get<int>("predictedReadbackCount") - beforePrediction, decoder.Get<int>("predictionFallbackCount") - beforeFallback, decoder.Get<string>("lastError")));
                foreach (string eventRow in observation.eventRows) calls.Add(Row(item.name, budget, sample, warmup) + "," + eventRow);
            }
            // Preserve the normal duplicate-on policy as a distinct correctness control.
            if (item.minimumAttempts == 0)
            {
                var decoder = decoders[2]; var receiver = receivers[2]; int notifications = receiver.Get<int>("notifications");
                int duplicate = decoder.Get<int>("skippedDuplicateFrameCount"); decoder.Set("skipDuplicateFrames", true); decoder.Call("DecodeNow");
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (decoder.Get<bool>("readbackInFlight") && EditorApplication.timeSinceStartup < deadline) yield return null;
                Check(!decoder.Get<bool>("readbackInFlight") && decoder.Get<int>("skippedDuplicateFrameCount") == duplicate + 1 && receiver.Get<int>("notifications") == notifications, "Duplicate-on control failed.");
                notes.Add("PASS duplicate-on control " + item.name);
            }
            foreach (Program decoder in decoders) decoder.Call("_onDisable");
            foreach (Program program in casePrograms) { program.Dispose(); programs.Remove(program); }
            File.WriteAllLines(Path.Combine(Root, "samples.csv"), rows); File.WriteAllLines(Path.Combine(Root, "vm-events.csv"), calls);
            notes.Add("PASS case " + item.name); UnityEngine.Debug.Log("Refine cost completed " + item.name); yield return null;
        }
    }
}
#endif
