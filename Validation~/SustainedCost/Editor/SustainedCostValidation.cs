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

// Validation only. Real compiled programs and VRC GPU readbacks, continuously admitted.
public static class SustainedCostValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string SessionKey = "TSMP.SustainedCost";
    const string ProbePath = "Assets/Validation/SustainedCost/SustainedCostProbe.asset";
    static string Root => Environment.GetEnvironmentVariable("TSMP_SUSTAINED_RESULTS");
    static readonly List<Object> owned = new List<Object>();
    static readonly List<Program> programs = new List<Program>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> hashes = new List<string>();
    static readonly Dictionary<string, string> bytecodeSizes = new Dictionary<string, string>();
    static readonly Dictionary<string, string> sourceBefore = new Dictionary<string, string>();
    static readonly List<VmSample> events = new List<VmSample>(100000);
    struct VmSample
    {
        public string name, owner, label;
        public int eventId, frame, pendingBefore, pendingAfter, callbackSlot, callbackSource, slot0Source, slot1Source, latestSource, retries, duplicates, applied, receiverCountBefore, receiverCountAfter;
        public long start, stop, nestedObservationTicks, callbackRequest, slot0Request, slot1Request;
        public double time, observationMs;
        public bool valid;
        public string Csv() => Row(name, eventId, owner, label, frame, time, start, stop, Ms(stop - start - nestedObservationTicks), pendingBefore, pendingAfter, callbackSlot, callbackRequest, callbackSource, slot0Request, slot1Request, slot0Source, slot1Source, latestSource, retries, duplicates, applied, valid, observationMs, Ms(stop - start), Ms(nestedObservationTicks), receiverCountBefore, receiverCountAfter, nestedObservationTicks);
    }
    const string EventHeader = "case,eventId,owner,event,frame,time,startTicks,endTicks,vmMs,pendingBefore,pendingAfter,callbackSlot,callbackRequest,callbackSource,slot0Request,slot1Request,slot0Source,slot1Source,latestSource,retryTotal,duplicateTotal,lastAppliedVariables,lastFrameValid,observationMs,grossVmMs,nestedObservationMs,receiverCountBefore,receiverCountAfter,nestedObservationTicks";
    static readonly List<string> frames = new List<string>();
    static readonly List<string> publications = new List<string>();
    static readonly List<string> applications = new List<string>();
    static readonly List<string> summaries = new List<string>();
    static IEnumerator work;
    static int assertions, depth, eventSequence, latestId, requestSequence, ambiguousAssociations;
    static long instrumentationTicks;
    static long[] requests = new long[2];
    static int[] requestSources = new int[2];
    static string caseName;
    static bool collecting;
    static Action step;
    static Program activeDecoder, activeReceiver;
    static Case activeCase;
    static byte[] activeHeader, activePayload;
    static int observedApplicationCount, checkedApplications, measuredReceived, endDuplicates, endAttempts, busySkipsAtStart, endBusySkips, endAdmissions, endAmbiguous;
    static readonly double[] sent = new double[32768];
    static readonly double[] published = new double[32768];
    static readonly List<double> ageSamples = new List<double>();
    static double begin, end;
    static int renderTicks, busyTicks, deadlineMisses, submitted, duplicatesAtStart, attemptsAtStart;
    
    [Serializable] public sealed class Plan { public Case[] cases; }
    [Serializable] public sealed class Case
    {
        public string name, codec, input, header, payload, schedule;
        public int bytes = 1024, renderHz = 60, sourceHz = 30, budget = 2, measuredStartId;
        public double seconds = 12, decodePhaseSeconds, sourceJitterSeconds, holdStartSeconds, holdSeconds;
        public bool stress;
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
            foreach (string scope in new[] { "Runtime", "Editor", "Shaders" })
            {
                string source = Path.Combine(info != null ? info.resolvedPath : directory, scope);
                if (!Directory.Exists(source)) continue;
                foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                    .Where(path => new[] { ".cs", ".shader", ".cginc", ".meta", ".asmdef", ".mat", ".prefab" }.Contains(Path.GetExtension(path))).OrderBy(path => path)) result[Resolve(path)] = H(File.ReadAllBytes(path));
            }
        }
        return result;
    }
    public static void Run()
    {
        Check(!string.IsNullOrWhiteSpace(Root), "Results required.");
        Directory.CreateDirectory(Root); Check(!File.Exists(Path.Combine(Root, "result.txt")), "Fresh result directory required.");
        File.WriteAllText(Path.Combine(Root, "result.txt"), "RUNNING");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbePath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/SustainedCost/Runtime/SustainedCostProbe.cs");
            Check(asset.sourceCsScript != null, "Probe missing."); AssetDatabase.CreateAsset(asset, ProbePath);
        }
        Compile(); AssetDatabase.SaveAssets(); SessionState.SetBool(SessionKey, true); Attach(); EditorApplication.EnterPlaymode();
    }
    static void Compile()
    {
        bool failed = false; Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Client Udon compilation failed.");
    }
    [InitializeOnLoadMethod] static void Attach() { EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered; }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false); work = Validate(); EditorApplication.update += Tick;
    }
    static void Tick() { try { if (work.MoveNext()) return; } catch (Exception error) { Finish(error); return; } Finish(null); }
    static void Finish(Exception error)
    {
        step = null; SustainedCostPump.FrameStep = null; collecting = false; EditorApplication.update -= Tick;
        foreach (var program in programs) program.Dispose(); programs.Clear(); RenderTexture.active = null;
        foreach (Object item in owned.AsEnumerable().Reverse()) { if (item is RenderTexture texture) texture.Release(); if (item != null) Object.DestroyImmediate(item); } owned.Clear();
        try
        {
            var after = Sources(); File.WriteAllLines(Path.Combine(Root, "source-after.csv"), new[] { "path,sha256" }.Concat(after.Select(pair => Row(pair.Key, pair.Value))));
            Check(after.Count == sourceBefore.Count && sourceBefore.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value), "Product source changed during measurement.");
        }
        catch (Exception auditError) { if (error == null) error = auditError; else notes.Add(auditError.ToString()); }
        if (error != null) notes.Add(error.ToString()); notes.Add("Assertions=" + assertions);
        Dump(); File.WriteAllLines(Path.Combine(Root, "artifact-hashes.csv"), hashes);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(notes)); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Dump()
    {
        File.WriteAllLines(Path.Combine(Root, "vm-events.csv"), new[] { EventHeader }.Concat(events.Select(e => e.Csv()))); File.WriteAllLines(Path.Combine(Root, "frames.csv"), frames);
        File.WriteAllLines(Path.Combine(Root, "publications.csv"), publications); File.WriteAllLines(Path.Combine(Root, "applications.csv"), applications); File.WriteAllLines(Path.Combine(Root, "summary.csv"), summaries);
        File.WriteAllLines(Path.Combine(Root, "bytecode-sizes.csv"), new[] { "path,bytes,sha256" }.Concat(bytecodeSizes.OrderBy(pair => pair.Key).Select(pair => pair.Value)));
    }
    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;
        public string role;
        public Program(string path, TSMPCodec codec = null, string role = null)
        {
            this.role = role;
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            Check(asset != null, "Missing program " + path);
            Code = asset.SerializedProgramAsset.RetrieveProgram();
            Check(Code != null && Code.ByteCode.Length > 0, "Missing bytecode " + path);
            string programHash = H(Code.ByteCode);
            hashes.Add(Row("bytecode", path, programHash));
            string sizeReceipt = Row(path, Code.ByteCode.Length, programHash);
            string previousReceipt;
            if (bytecodeSizes.TryGetValue(path, out previousReceipt)) Check(previousReceipt == sizeReceipt, "Loaded bytecode changed within one run: " + path);
            else bytecodeSizes.Add(path, sizeReceipt);
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(Code), "VM load failed.");
            Backing = new GameObject(asset.name + " refine cost VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved heap.");
            type.GetField("_program", Private).SetValue(Backing, Code);
            type.GetField("_udonVM", Private).SetValue(Backing, role != null ? new TimedVm(vm, Code, this) : vm);
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
        readonly IUdonVM inner; readonly Program owner; readonly Dictionary<uint, string> labels = new Dictionary<uint, string>();
        public TimedVm(IUdonVM vm, IUdonProgram program, Program owner) { inner = vm; this.owner = owner; foreach (string name in program.EntryPoints.GetExportedSymbols()) labels[program.EntryPoints.GetAddressFromSymbol(name)] = name; }
        public bool DebugLogging { get => inner.DebugLogging; set => inner.DebugLogging = value; }
        public bool LoadProgram(IUdonProgram program) => inner.LoadProgram(program);
        public IUdonProgram RetrieveProgram() => inner.RetrieveProgram();
        public void SetProgramCounter(uint counter) => inner.SetProgramCounter(counter);
        public uint GetProgramCounter() => inner.GetProgramCounter();
        public IUdonHeap InspectHeap() => inner.InspectHeap();
        public uint Interpret()
        {
            long observationStart = Stopwatch.GetTimestamp();
            bool top = depth++ == 0; string label; if (!labels.TryGetValue(inner.GetProgramCounter(), out label)) label = "unknown";
            bool record = top && collecting && activeDecoder != null;
            int pendingBefore = record ? activeDecoder.Get<int>("pendingDecodeCount") : 0;
            int receiverCountBefore = record ? activeReceiver.Get<int>("count") : 0;
            int[] oldStates = record ? (int[])activeDecoder.Get<int[]>("_slotStates").Clone() : null;
            object[] oldRequests = record ? ReadRequests(activeDecoder) : null;
            int frame = Time.frameCount; double stamp = Time.realtimeSinceStartupAsDouble;
            long nestedBefore = instrumentationTicks; long before = Stopwatch.GetTimestamp(); uint result;
            try { result = inner.Interpret(); } finally { depth--; }
            long after = Stopwatch.GetTimestamp(); long nestedTicks = instrumentationTicks - nestedBefore;
            if (owner.role == "Receiver" && label == "OnTSMPVariableReceived" && activeReceiver != null)
            {
                int applicationCount = activeReceiver.Get<int>("count");
                if (applicationCount > observedApplicationCount)
                {
                    int added = applicationCount - observedApplicationCount; Check(added == 1, "Expected one application per receiver callback");
                    byte[] value = activeReceiver.Get<byte[]>("packet");
                    if (activeCase.stress) Check(activeHeader.SequenceEqual(activeDecoder.Get<byte[]>("_headerBytes").Take(activeHeader.Length)) && activePayload.SequenceEqual(activeDecoder.Get<byte[]>("_payloadBytes").Take(activePayload.Length)), "Stress accepted bytes differ");
                    else
                    {
                        Check(value.Length == activeCase.bytes, "Applied size mismatch"); int receivedId = value[0] | (value[1] << 8) | (value[2] << 16) | (value[3] << 24);
                        uint state = unchecked((uint)receivedId * 2654435761u + 2246822519u);
                        for (int i = 4; i < value.Length; i++) if (value[i] != PatternByte(ref state)) throw new InvalidOperationException("Applied byte mismatch at " + i);
                    }
                    observedApplicationCount = applicationCount; checkedApplications++;
                    if (stamp >= begin && stamp < end) measuredReceived += added;
                }
            }
            if (record)
            {
                int[] states = activeDecoder.Get<int[]>("_slotStates"); object[] nowRequests = ReadRequests(activeDecoder);
                int callbackSlot = -1;
                for (int slot = 0; slot < 2; slot++) if (label.ToLowerInvariant().Contains("readback") && oldRequests[slot] != null && !ReferenceEquals(oldRequests[slot], nowRequests[slot])) callbackSlot = slot;
                long callbackRequest = callbackSlot >= 0 ? requests[callbackSlot] : -1;
                int callbackSource = callbackSlot >= 0 ? requestSources[callbackSlot] : -1;
                for (int slot = 0; slot < 2; slot++) if (states[slot] != 0 && oldStates[slot] == 0) { requests[slot] = ++requestSequence; requestSources[slot] = latestId; submitted++; }
                else if (owner.role == "Decoder" && label == "_update" && nowRequests[slot] != null && !ReferenceEquals(oldRequests[slot], nowRequests[slot])) { requests[slot] = -1; requestSources[slot] = -1; ambiguousAssociations++; }
                var sample = new VmSample { name = caseName, eventId = ++eventSequence, owner = owner.role, label = label, frame = frame, time = stamp,
                    start = before, stop = after, nestedObservationTicks = nestedTicks, pendingBefore = pendingBefore, pendingAfter = activeDecoder.Get<int>("pendingDecodeCount"), callbackSlot = callbackSlot, callbackRequest = callbackRequest, callbackSource = callbackSource,
                    slot0Request = requests[0], slot1Request = requests[1], slot0Source = requestSources[0], slot1Source = requestSources[1], latestSource = latestId,
                    retries = activeDecoder.Get<int>("payloadRecoveryAttemptCount"), duplicates = activeDecoder.Get<int>("skippedDuplicateFrameCount"), applied = activeDecoder.Get<int>("lastAppliedVariableCount"), valid = activeDecoder.Get<bool>("lastFrameValid"),
                    receiverCountBefore = receiverCountBefore, receiverCountAfter = activeReceiver.Get<int>("count") };
                sample.observationMs = Ms(Stopwatch.GetTimestamp() - observationStart - (after - before)); events.Add(sample);
            }
            instrumentationTicks += (before - observationStart) + (Stopwatch.GetTimestamp() - after);
            return result;
        }
    }
    static object[] ReadRequests(Program decoder) { var values = (Array)decoder.Code.Heap.GetHeapVariable(decoder.Code.SymbolTable.GetAddressFromSymbol("_slotRequests")); return new[] { values.GetValue(0), values.GetValue(1) }; }
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

    static byte PatternByte(ref uint state) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (byte)state; }
    static void Bind(Program owner, Program target)
    {
        var field = TransSyncMetadata.GetOrCreate(null, typeof(SustainedCostProbe)).Fields.Single();
        owner.Set("bindingTargets", new Component[0]); owner.Set("bindingUdonTargets", new[] { target.Backing });
        owner.Set("bindingNetworkIds", new ushort[] { 1 }); owner.Set("bindingVariableHashes", new[] { field.VariableHash });
        owner.Set("bindingValueTypes", new[] { (byte)field.ValueType }); owner.Set("bindingFieldNames", new[] { nameof(SustainedCostProbe.packet) });
        owner.Set("bindingDirections", new[] { 0 }); owner.Set("bindingPriorities", new[] { 0 });
    }
    static IEnumerator Validate()
    {
        Compile(); Application.runInBackground = true; QualitySettings.vSyncCount = 0;
        notes.Add("StopwatchFrequency=" + Stopwatch.Frequency + "; CPU=" + SystemInfo.processorType + "; Threads=" + SystemInfo.processorCount + "; OS=" + SystemInfo.operatingSystem);
        notes.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        notes.Add("Actual client Encoder/Decoder/codec/probe EditorVMs and real VRC GPU callbacks; no live VRChat/CDN/FPS result.");
        notes.Add("Clean changing-ID delivery and damaged fixed-ID repeated processing are separate regimes. Top-level VM events include nested work once; gross Interpret time and nested host observations are retained; net VM time subtracts measured nested receiver host checks. Render-frame counts are Time.frameCount, target rate is not achieved rate.");
        notes.Add("Logical request identity is assigned only on proven idle-to-pending transitions; callback object replacement retains that identity. Occupied-slot request replacement in _update is ambiguous and markedID/source=-1. Proven admissions is a lower bound, notexactcaptures. Sourceage is independent received-ID evidence. Repeated rejected frames can invalidate prediction. Frozen clean packets contain one bound variable; no event/RPC delivery claim.");
        notes.Add("Receiver frames are recorded inside each actual OnTSMPVariableReceived callback. Clean packets have exactly one RawBytes binding, so each receiver count increment is one accepted packet completion. Top-level receiver count intervals link those completions to Decoder dispatches; pending-count changes and GPU callback counts are separate observations.");
        foreach (var pair in Sources()) sourceBefore.Add(pair.Key, pair.Value);
        File.WriteAllLines(Path.Combine(Root, "source-before.csv"), new[] { "path,sha256" }.Concat(sourceBefore.Select(pair => Row(pair.Key, pair.Value))));
        frames.Add("case,frame,time,measured,latestSource,latestApplied,sourceAgeMs,pending,busy,gc0,gc1,gc2,ageSampleTime,latestSentTime");
        publications.Add("case,id,sent,published,measured"); applications.Add("case,id,applied,sent,measured,latencyMs,frame,ordinal");
        summaries.Add("case,codec,regime,schedule,budget,sourceTarget,renderTarget,seconds,begin,end,renderTicks,published,senderMisses,provenAdmissions,busyTicks,appliedInWindow,receivedAfterDrain,duplicates,retries,corrupt,ambiguousAssociations,busyDecodeSkips");
        hashes.Add("kind,path,sha256"); string planPath = Resolve(Environment.GetEnvironmentVariable("TSMP_SUSTAINED_PLAN")); var planBytes = File.ReadAllBytes(planPath);
        File.WriteAllBytes(Path.Combine(Root, "plan-used.json"), planBytes); hashes.Add(Row("plan", planPath, H(planBytes)));
        var plan = JsonUtility.FromJson<Plan>(System.Text.Encoding.UTF8.GetString(planBytes).TrimStart('\uFEFF')); Check(plan.cases != null && plan.cases.Length > 0, "Empty plan.");
        Check(Keep(new GameObject("Sustained frame pump")).AddComponent<SustainedCostPump>() != null, "Frame pump component missing"); SustainedCostPump.FrameStep = () => step?.Invoke(); Keep(new GameObject("Camera")).AddComponent<Camera>().cullingMask = 0;
        foreach (Case item in plan.cases) { var run = Measure(item); while (run.MoveNext()) yield return run.Current; Dump(); yield return null; GC.Collect(); }
    }
    static IEnumerator Measure(Case item)
    {
        int programStart = programs.Count, ownedStart = owned.Count;
        activeCase = item; observedApplicationCount = checkedApplications = measuredReceived = 0; endDuplicates = endAttempts = endBusySkips = endAdmissions = endAmbiguous = 0;
        caseName = item.name; collecting = false; latestId = 0; requestSequence = 0; requests = new long[2]; requestSources = new int[2]; submitted = 0; ambiguousAssociations = 0;
        Array.Clear(sent, 0, sent.Length); Array.Clear(published, 0, published.Length); ageSamples.Clear(); renderTicks = busyTicks = deadlineMisses = 0;
        Application.targetFrameRate = item.renderHz;
        var source = Target(1280, 720); var output = Target(2048, 1);
        var native = CloneCodec(item.codec); var lumaNative = item.codec == "Luma4" ? native : CloneCodec("Luma4");
        WarmGraphics(lumaNative, source, output); if (native != lumaNative) WarmGraphics(native, source, output);
        var codec = new Program("Packages/com.kibalab.tsmp.codec." + item.codec.ToLowerInvariant() + "/Runtime/Scripts/TSMPCodec" + item.codec + ".asset", native);
        var luma = codec; if (item.codec != "Luma4") luma = new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", lumaNative);
        var receiver = new Program(ProbePath, role: "Receiver"); activeReceiver = receiver; receiver.Set("networkId", (ushort)1); receiver.Set("ids", new int[32768]); receiver.Set("times", new float[32768]); receiver.Set("frames", new int[32768]);
        var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset", role: "Decoder"); activeDecoder = decoder;
        decoder.Set<Texture>("sourceTexture", source); decoder.Set("payloadByteTexture", output); decoder.Set("readbackPackMaterial", Resources.Load<Material>("TSMPReadbackPack"));
        decoder.Set("codecHandlers", codec == luma ? new[] { luma.Backing } : new[] { luma.Backing, codec.Backing }); Bind(decoder, receiver);
        decoder.Set("applyEveryFrame", true); decoder.Set("skipDuplicateFrames", !item.stress); decoder.Set("requirePayloadCrc32", true); decoder.Set("debugLog", false); decoder.Set("flipY", true);
        decoder.Set("recoverHeaderCrc", false); decoder.Set("maxCrcRecoveryAttempts", item.budget); decoder.Set("usePredictedReadback", true); decoder.Set("useCombinedByteOutput", true); decoder.Set("overlapReadbacks", true); decoder.Call("_onEnable");
        Program encoder = null, sender = null; byte[] packet = null;
        int frozenExpectedAttempts = -1; byte[] expectedPayload = null; byte[] expectedHeader = null;
        if (item.stress)
        {
            var image = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true)); byte[] bytes = File.ReadAllBytes(item.input); Check(image.LoadImage(bytes), "Invalid stress raster"); Graphics.Blit(image, source);
            expectedPayload = File.ReadAllBytes(item.payload); expectedHeader = File.ReadAllBytes(item.header); activePayload = expectedPayload; activeHeader = expectedHeader;
            foreach (string path in new[] { item.input, item.payload, item.header }) hashes.Add(Row("stress", path, H(File.ReadAllBytes(path))));
            // Frozen RefineCost selections declare these second-alternate wins; reference ID stays fixed.
            frozenExpectedAttempts = 2; receiver.Set<byte[]>("packet", null);
            decoder.Set("bindingVariableHashes", new uint[] { 100 }); decoder.Set("bindingFieldNames", new[] { nameof(SustainedCostProbe.packet) });
        }
        else
        {
            sender = new Program(ProbePath); sender.Set("networkId", (ushort)1); encoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset", role: "Encoder");
            encoder.Set("output", source); encoder.Set("autoEncode", false); encoder.Set("sampleSize", 4); encoder.Set("frameIndex", 1u); encoder.Set("blockExpandMaterial", Keep(new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"))));
            encoder.Set("luma4EncodeMaterial", Resources.Load<Material>("TSMPEncodeLuma4")); encoder.Set("useGpuLuma4", true); encoder.Set("selectedCodecUdonTarget", codec.Backing); encoder.Set("debugLog", false); Bind(encoder, sender);
            encoder.Set("bindingSendOnChange", new[] { false }); encoder.Set("bindingMinSendIntervals", new[] { 0f }); encoder.Call("_onEnable");
            packet = new byte[item.bytes];
        }
        bool measuredJumpApplied = false;
        Action publish = () =>
        {
            if (item.stress) return;
            double publicationTime = Time.realtimeSinceStartupAsDouble;
            if (item.measuredStartId > 0 && !measuredJumpApplied && publicationTime >= begin && publicationTime < end)
            {
                Check(item.measuredStartId > latestId && item.measuredStartId < sent.Length - 1, "Invalid measured ID start");
                latestId = item.measuredStartId - 1; measuredJumpApplied = true;
            }
            int id = ++latestId; Check(id < sent.Length, "Publication capacity"); for (int i = 0; i < 4; i++) packet[i] = (byte)(id >> (8 * i));
            uint pattern = unchecked((uint)id * 2654435761u + 2246822519u); for (int i = 4; i < packet.Length; i++) packet[i] = PatternByte(ref pattern);
            sender.Set("packet", packet); sent[id] = Time.realtimeSinceStartupAsDouble; encoder.Call("EncodeNow"); published[id] = Time.realtimeSinceStartupAsDouble;
            Check(encoder.Get<string>("lastError") == null || encoder.Get<string>("lastError") == "", "Encoder error.");
            publications.Add(Row(caseName, id, sent[id], published[id], sent[id] >= begin && sent[id] < end));
        };
        publish(); decoder.Call("DecodeNow"); double timeout = Time.realtimeSinceStartupAsDouble + 30;
        while (decoder.Get<bool>("readbackInFlight") && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
        Check(!decoder.Get<bool>("readbackInFlight"), "Preflight timeout.");
        if (!item.stress) Check(receiver.Get<int>("count") == 1 && receiver.Get<int>("corrupt") == 0, "Clean preflight failed " + item.name + " " + decoder.Get<string>("lastError"));
        else Check(decoder.Get<bool>("lastFrameValid") == (item.budget >= frozenExpectedAttempts), "Stress frozen verdict mismatch.");
        observedApplicationCount = receiver.Get<int>("count"); measuredReceived = 0;
        begin = Time.realtimeSinceStartupAsDouble + 2; end = begin + item.seconds; double due = Time.realtimeSinceStartupAsDouble; double decodeDue = due + item.decodePhaseSeconds;
        bool snapshotStarted = false, snapshotEnded = false;
        step = () =>
        {
            double now = Time.realtimeSinceStartupAsDouble; bool measured = now >= begin && now < end;
            if (measured && !snapshotStarted) { snapshotStarted = true; duplicatesAtStart = decoder.Get<int>("skippedDuplicateFrameCount"); attemptsAtStart = decoder.Get<int>("payloadRecoveryAttemptCount"); busySkipsAtStart = decoder.Get<int>("skippedBusyDecodeCount"); submitted = 0; ambiguousAssociations = 0; }
            if (snapshotStarted && !snapshotEnded && now >= end) { snapshotEnded = true; endDuplicates = decoder.Get<int>("skippedDuplicateFrameCount"); endAttempts = decoder.Get<int>("payloadRecoveryAttemptCount"); endBusySkips = decoder.Get<int>("skippedBusyDecodeCount"); endAdmissions = submitted; endAmbiguous = ambiguousAssociations; }
            collecting = true;
            if (!item.stress && now >= due)
            {
                int elapsedSlots = Math.Max(1, (int)Math.Floor((now - due) * item.sourceHz) + 1); if (measured) deadlineMisses += elapsedSlots - 1; due += elapsedSlots / (double)item.sourceHz;
                due += latestId % 2 == 0 ? item.sourceJitterSeconds : -item.sourceJitterSeconds;
                if (!(item.holdSeconds > 0 && now >= begin + item.holdStartSeconds && now < begin + item.holdStartSeconds + item.holdSeconds)) publish();
            }
            bool atCapacity = decoder.Get<int>("pendingDecodeCount") >= 2;
            bool decode = item.schedule != "poll30" || now >= decodeDue;
            if (decode) { if (item.schedule == "poll30") decodeDue += Math.Max(1, (int)Math.Floor((now - decodeDue) * item.sourceHz) + 1) / (double)item.sourceHz; decoder.Set("applyEveryFrame", true); decoder.Call("_update"); }
            else { decoder.Set("applyEveryFrame", false); decoder.Call("_update"); }
            int count = receiver.Get<int>("count"); int latest = count > 0 ? receiver.Get<int[]>("ids")[count - 1] : 0;
            double ageTime = Time.realtimeSinceStartupAsDouble; double latestSent = !item.stress && latest > 0 && latest < sent.Length ? sent[latest] : -1;
            double age = latestSent >= 0 ? (ageTime - latestSent) * 1000 : -1;
            if (measured) { renderTicks++; if (atCapacity) busyTicks++; if (age >= 0) ageSamples.Add(age); }
            frames.Add(Row(caseName, Time.frameCount, now, measured, latestId, latest, age, decoder.Get<int>("pendingDecodeCount"), atCapacity, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), ageTime, latestSent));
        };
        while (Time.realtimeSinceStartupAsDouble < end + 1) yield return null;
        step = null; collecting = false; decoder.Set("applyEveryFrame", false);
        timeout = Time.realtimeSinceStartupAsDouble + 10; while (decoder.Get<bool>("readbackInFlight") && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
        Check(!decoder.Get<bool>("readbackInFlight"), "Drain timeout " + item.name);
        int received = 0, inWindow = 0, sends = 0, previous = 0; var unique = new HashSet<int>();
        if (!item.stress)
        {
            for (int i = 1; i <= latestId; i++) if (sent[i] >= begin && sent[i] < end) sends++;
            int[] ids = receiver.Get<int[]>("ids"); float[] times = receiver.Get<float[]>("times"); int[] applicationFrames = receiver.Get<int[]>("frames");
            for (int i = 0; i < receiver.Get<int>("count"); i++)
            {
                int id = ids[i]; Check(id > previous && id <= latestId && unique.Add(id), "Invalid duplicate/out-of-order application."); previous = id;
                bool measured = sent[id] >= begin && sent[id] < end; if (measured) { received++; if (times[i] < end) inWindow++; }
                applications.Add(Row(caseName, id, times[i], sent[id], measured, (times[i] - sent[id]) * 1000, applicationFrames[i], i + 1));
            }
            Check(receiver.Get<int>("corrupt") == 0 && received > 0, "Corrupt/no application.");
        }
        else
        {
            bool valid = decoder.Get<bool>("lastFrameValid"); Check(valid == (item.budget >= frozenExpectedAttempts), "Stress final verdict mismatch");
            if (valid) Check(expectedHeader.SequenceEqual(decoder.Get<byte[]>("_headerBytes").Take(expectedHeader.Length)) && expectedPayload.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(expectedPayload.Length)), "Stress accepted bytes differ.");
            // The reference field is not the generated clean schema, so probe corruption counters are inapplicable to stress; full payload equality is the correctness control.
            received = measuredReceived; inWindow = -1;
        }
        summaries.Add(Row(caseName, item.codec, item.stress ? "fixed-damaged-repeat" : "changing-clean", item.schedule, item.budget, item.sourceHz, item.renderHz, item.seconds, begin, end, renderTicks, sends, deadlineMisses, endAdmissions, busyTicks, inWindow, received, endDuplicates - duplicatesAtStart, endAttempts - attemptsAtStart, item.stress ? -1 : receiver.Get<int>("corrupt"), endAmbiguous, endBusySkips - busySkipsAtStart));
        decoder.Call("_onDisable"); if (encoder != null) encoder.Call("_onDisable"); activeDecoder = null;
        foreach (Program program in programs.Skip(programStart).ToArray()) { program.Dispose(); programs.Remove(program); }
        foreach (Object itemOwned in owned.Skip(ownedStart).Reverse().ToArray()) { if (itemOwned is RenderTexture target) target.Release(); if (itemOwned != null) Object.DestroyImmediate(itemOwned); owned.Remove(itemOwned); }
        notes.Add("PASS " + caseName + "; accepted payload byte checks=" + checkedApplications); UnityEngine.Debug.Log("Sustained complete " + caseName);
    }
}
#endif
