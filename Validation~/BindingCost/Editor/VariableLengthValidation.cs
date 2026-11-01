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

public static class VariableLengthValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ProbePath = "Assets/Validation/BindingCost/VariableLengthProbe.asset";
    private const string Session = "TSMP.VariableLength";
    private static string Root => Environment.GetEnvironmentVariable("TSMP_VARIABLE_LENGTH_RESULTS");
    private static readonly List<Object> Owned = new List<Object>();
    private static readonly List<Program> Programs = new List<Program>();
    private static readonly List<string> Notes = new List<string>();
    private static readonly List<string> Artifacts = new List<string> { "path,bytes,sha256" };
    private static readonly HashSet<string> ArtifactPaths = new HashSet<string>();
    private static Dictionary<string, string> before;
    private static BindingCostTelemetry telemetry;
    private static CaseBuffer activeBuffer;
    private static IEnumerator work;
    private static int assertions, lastFrame = -1, activeIteration = -1;
    private static string activeCase;
    private static readonly uint DataHash = StableHash.Fnv1A32("variable-length.data");
    [Serializable] public sealed class Plan { public Case[] cases; }
    [Serializable] public sealed class Case { public string name, sourceMode; public int warm, samples; public int[] lengths; public bool unchanged; }
    private struct Sample
    {
        public int iteration, frame, length, payloadBytes, snapshotCapacity, snapshotReportedLength, receiverLength, callbacks, applies;
        public double clock;
        public long sourceStart, sourceStop, encodeStart, encodeStop, decodeStart, decodeStop;
        public bool sourceAllocated, sourceChanged, snapshotChanged, receiverAChanged, receiverBChanged, gpu, completed;
    }
    private sealed class CaseBuffer
    {
        public Case item;
        public Sample[] rows;
        public byte[][] packets, headers;
        public int captured, flushed;
    }
    private static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static T Keep<T>(T value) where T : Object { Owned.Add(value); return value; }
    private static string Hash(byte[] value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    private static string Row(params object[] values) => string.Join(",", values.Select(v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    private static double Ms(long value) => value * 1000.0 / Stopwatch.Frequency;
    private static void Compile()
    {
        bool failed = false;
        Application.LogCallback listener = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Client compilation failed");
    }
    public static void Run()
    {
        try
        {
            Check(!string.IsNullOrWhiteSpace(Root), "Set TSMP_VARIABLE_LENGTH_RESULTS"); Directory.CreateDirectory(Root);
            Check(!File.Exists(Path.Combine(Root, "result.txt")) && !File.Exists(Path.Combine(Root, "samples.csv")), "Preserve existing results");
            File.WriteAllText(Path.Combine(Root, "result.txt"), "RUNNING");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbePath) == null)
            {
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/BindingCost/Runtime/VariableLengthProbe.cs");
                Check(asset.sourceCsScript != null, "Copy Runtime/VariableLengthProbe.cs to the fixed fixture path");
                AssetDatabase.CreateAsset(asset, ProbePath);
            }
            Compile(); AssetDatabase.SaveAssets(); SessionState.SetBool(Session, true); Attach(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }
    [InitializeOnLoadMethod] private static void Attach() { EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered; }
    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Session, false)) return;
        SessionState.SetBool(Session, false); work = Validate(); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
        try { if (work.MoveNext()) return; } catch (Exception error) { Finish(error); return; } Finish(null);
    }
    private static Dictionary<string, string> Sources()
    {
        var result = new Dictionary<string, string>();
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.luma4" })
        {
            string path = "Packages/" + package; var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            string directory = info == null ? path : info.resolvedPath;
            foreach (string scope in new[] { "Runtime", "Editor", "Shaders" })
            {
                string folder = Path.Combine(directory, scope); if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    if (new[] { ".cs", ".shader", ".cginc", ".meta", ".asmdef", ".mat", ".prefab" }.Contains(Path.GetExtension(file))) result[Path.GetFullPath(file)] = Hash(File.ReadAllBytes(file));
            }
        }
        return result;
    }
    private static IEnumerator Validate()
    {
        Compile(); Application.runInBackground = true; Application.targetFrameRate = 90; QualitySettings.vSyncCount = 0;
        Notes.Add("Unity=" + Application.unityVersion + "; CPU=" + SystemInfo.processorType + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; StopwatchFrequency=" + Stopwatch.Frequency);
        Notes.Add("Scope=Actual client Encoder GPU output and protected Decoder byte injection, two independent RawBytes receiver bindings. Not real avatar execution, receiver GPU timing, live VRChat, source Hz or zero allocation.");
        Notes.Add("Source generation/allocations and validation are outside Encode/Decode timers, but share the process and may influence later GC. Fixed-size injection arrays/raw capture buffers are prepared before each case; files/CSV are written afterward.");
        Notes.Add("Data=ID little endian in bytes 0..3; xorshift32 seeded by ((id+1)*0x9e3779b9)^length for all bytes, then overwrite ID. Length alternation is synthetic, not an avatar wire format.");
        before = Sources(); File.WriteAllLines(Path.Combine(Root, "source-before.csv"), new[] { "path,sha256" }.Concat(before.Select(x => Row(x.Key, x.Value))));
        byte[] planBytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("TSMP_VARIABLE_LENGTH_PLAN")); File.WriteAllBytes(Path.Combine(Root, "plan-used.json"), planBytes);
        var plan = JsonUtility.FromJson<Plan>(System.Text.Encoding.UTF8.GetString(planBytes).TrimStart('\uFEFF'));
        Check(plan != null && plan.cases != null && plan.cases.Length > 0 && plan.cases.Length <= 8, "Use a bounded explicit plan");
        int snapshots = 0; var names = new HashSet<string>();
        foreach (Case item in plan.cases)
        {
            Check(!string.IsNullOrEmpty(item.name) && item.name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "Invalid case name");
            Check(names.Add(item.name), "Duplicate case name would overwrite raw evidence");
            Check(item.warm >= 1 && item.samples >= 1 && item.warm + item.samples <= 20000, "Invalid sample count");
            Check(item.lengths != null && item.lengths.Length > 0 && item.lengths.Length <= 4, "Use one to four source lengths");
            foreach (int length in item.lengths) Check(length >= 4 && length <= 4096, "Source length outside bounded fixture capacity");
            Check(item.sourceMode == "reuse" || item.sourceMode == "fresh" || item.sourceMode == "pooled", "Unknown source mode");
            Check(!item.unchanged || item.lengths.All(n => n == item.lengths[0]), "Unchanged control must keep one logical length");
            snapshots = checked(snapshots + 6 * (item.warm + item.samples));
        }
        File.WriteAllText(Path.Combine(Root, "samples.csv"), "case,sample,measured,frame,clock,length,sourceMode,unchanged,sourceStartTicks,sourceEndTicks,sourceMs,encodeStartTicks,encodeEndTicks,encodeMs,decodeStartTicks,decodeEndTicks,decodeMs,payloadBytes,sourceAllocated,sourceRefChanged,snapshotRefChanged,snapshotCapacity,snapshotValidBytes,snapshotReportedValidBytes,receiverARefChanged,receiverBRefChanged,receiverLength,beforeCallbacks,appliedBindings,usedGpu,packet,header,completed\n");
        telemetry = BindingCostTelemetry.Create(Root, Notes, snapshots);
        if (telemetry != null) { IEnumerator init = telemetry.Initialize(); while (init.MoveNext()) yield return init.Current; }
        foreach (Case item in plan.cases)
        {
            IEnumerator measure = Measure(item); while (measure.MoveNext()) yield return measure.Current;
            measure = null; // Release the completed iterator's bounded raw arrays before collecting.
            if (telemetry != null) telemetry.Write(); GC.Collect(); yield return null;
        }
    }
    private sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;
        private static readonly FieldInfo HasError = typeof(UdonBehaviour).GetField("_hasError", Private);
        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path); Check(asset != null, "Missing program " + path);
            Code = asset.SerializedProgramAsset.RetrieveProgram(); Check(Code != null && Code.ByteCode.Length > 0, "Missing client bytecode " + path);
            if (ArtifactPaths.Add(path)) Artifacts.Add(Row(path, Code.ByteCode.Length, Hash(Code.ByteCode)));
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(Code), "VM load failed");
            Backing = new GameObject("Variable length " + asset.name).AddComponent<UdonBehaviour>(); Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved VM references");
            type.GetField("_program", Private).SetValue(Backing, Code); type.GetField("_udonVM", Private).SetValue(Backing, vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance); type.GetField("_isReady", Private).SetValue(Backing, true); type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null) foreach (FieldInfo field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address)) Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
            Programs.Add(this);
        }
        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Timed(string name, out long start, out long stop) { start = Stopwatch.GetTimestamp(); Backing.SendCustomEvent(name); stop = Stopwatch.GetTimestamp(); assertions++; if ((bool)HasError.GetValue(Backing)) throw new InvalidOperationException("VM event failed: " + name); }
        public void Call(string name) { Timed(name, out long ignoredStart, out long ignoredStop); }
        public void Dispose() { Programs.Remove(this); if (Backing != null) Object.DestroyImmediate(Backing.gameObject); }
    }
    private static Program Codec()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        Check(prefab != null, "Luma4 prefab missing"); var codec = Keep(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = material => { if (material == null) return null; if (copies.TryGetValue(material, out Material found)) return found; var copy = Keep(new Material(material)); copies.Add(material, copy); return copy; };
        foreach (FieldInfo field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials) field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", codec);
    }
    private static Program Encoder(Program codec)
    {
        var encoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
        var output = Keep(new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }); Check(output.Create(), "Output allocation failed");
        encoder.Set("output", output); encoder.Set("autoEncode", false); encoder.Set("debugLog", false); encoder.Set("sampleSize", 4); encoder.Set("frameIndex", 1u);
        encoder.Set("selectedCodecUdonTarget", codec.Backing); encoder.Set("useGpuLuma4", true); encoder.Set("luma4EncodeMaterial", Keep(new Material(Resources.Load<Material>("TSMPEncodeLuma4"))));
        encoder.Set("blockExpandMaterial", Keep(new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"))));
        encoder.Set("transSyncRefreshInterval", 0f); encoder.Set("targetPayloadBytes", 0); encoder.Call("_onEnable"); return encoder;
    }
    private static void Bind(Program owner, Program[] targets, bool encoder)
    {
        int count = targets.Length;
        owner.Set("bindingTargets", new Component[0]); owner.Set("bindingUdonTargets", targets.Select(t => t.Backing).ToArray());
        owner.Set("bindingNetworkIds", Enumerable.Repeat((ushort)1, count).ToArray()); owner.Set("bindingVariableHashes", Enumerable.Repeat(DataHash, count).ToArray());
        owner.Set("bindingValueTypes", Enumerable.Repeat((byte)NetworkFrameProtocol.ValueTypeRawBytes, count).ToArray()); owner.Set("bindingFieldNames", Enumerable.Repeat("data", count).ToArray());
        owner.Set("bindingDirections", new int[count]); owner.Set("bindingPriorities", new int[count]);
        if (encoder) { owner.Set("bindingSendOnChange", Enumerable.Repeat(true, count).ToArray()); owner.Set("bindingMinSendIntervals", new float[count]); }
    }
    private sealed class Injection
    {
        private readonly byte[] raw;
        private readonly int[] states = new int[2], stages = new int[2], counts = new int[2], readCounts = new int[2], safety = new int[2];
        private readonly uint[] frames = new uint[2], streams = { 1u, 0u };
        private readonly bool[] valid = { true, false }, require = { true, false };
        private readonly byte[][] readbacks, payloads, headers;
        public Injection(int maximum)
        {
            raw = new byte[(maximum + 3) / 4 * 4]; readbacks = new[] { raw, (byte[])null }; payloads = new[] { new byte[maximum], (byte[])null }; headers = new[] { new byte[56], (byte[])null };
        }
        public void Prepare(Program decoder, byte[] packet, int length, byte[] header, uint frame)
        {
            int padded = (length + 3) / 4 * 4; Array.Copy(packet, raw, length); Array.Clear(raw, length, padded - length); Array.Copy(header, headers[0], 56);
            states[0] = 2; stages[0] = 2; counts[0] = length; readCounts[0] = padded / 4; frames[0] = frame;
            decoder.Set("_slotHead", 0); decoder.Set("pendingDecodeCount", 1); decoder.Set("_slotStates", states); decoder.Set("_slotStages", stages);
            decoder.Set("_slotReadbackBytes", readbacks); decoder.Set("_slotHeaders", headers); decoder.Set("_slotPayloads", payloads); decoder.Set("_slotPayloadCounts", counts); decoder.Set("_slotReadCounts", readCounts);
            decoder.Set("_slotRequirePayloadCrc32", require); decoder.Set("_slotSafetyModes", safety); decoder.Set("_slotHeaderValid", valid); decoder.Set("_slotStreamIds", streams); decoder.Set("_slotFrameIndices", frames); decoder.Set("lastFrameValid", false);
        }
        public bool Aliases(byte[] value) => ReferenceEquals(value, raw) || ReferenceEquals(value, payloads[0]) || ReferenceEquals(value, headers[0]);
    }
    private static void Boundary(Case item, int iteration, string boundary)
    {
        if (telemetry != null) telemetry.Record(item.name, iteration, iteration >= item.warm, boundary, telemetry.Take());
    }
    private static IEnumerator Measure(Case item)
    {
        int firstProgram = Programs.Count, firstOwned = Owned.Count;
        Program codec = Codec(), encoder = Encoder(codec), sender = new Program(ProbePath), a = new Program(ProbePath), b = new Program(ProbePath);
        Bind(encoder, new[] { sender }, true);
        var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset"); decoder.Set("applyEveryFrame", false); decoder.Set("debugLog", false); decoder.Set("_crc32Table", Crc32Runtime.EnsureFastTable(null)); Bind(decoder, new[] { a, b }, false);
        int total = item.warm + item.samples, maximum = item.lengths.Max() + 25;
        var rows = new Sample[total]; var packets = new byte[total][]; var headers = new byte[total][];
        for (int i = 0; i < total; i++) if (!item.unchanged || i == 0) { packets[i] = new byte[maximum]; headers[i] = new byte[56]; }
        var trace = activeBuffer = new CaseBuffer { item = item, rows = rows, packets = packets, headers = headers };
        var injection = new Injection(maximum); var pool = new byte[item.lengths.Length][];
        if (item.sourceMode == "pooled") for (int i = 0; i < pool.Length; i++) pool[i] = new byte[item.lengths[i]];
        byte[] source = null, oldSource = null, oldSnapshot = null, oldA = null, oldB = null; int oldSnapshotValidLength = 0;
        bool snapshotLengthMetadata = encoder.Code.SymbolTable.TryGetAddressFromSymbol("_sendPreviousLengths", out uint snapshotLengthAddress);
        Notes.Add("SnapshotValidLengthSource=" + item.name + ":" + (snapshotLengthMetadata ? "_sendPreviousLengths[0]" : "7+UInt16LE(snapshot,5)") + "; capacity is recorded separately");
        for (int i = 0; i < total; i++)
        {
            activeCase = item.name; activeIteration = i;
            int lengthIndex = i % item.lengths.Length, length = item.lengths[lengthIndex], id = item.unchanged ? 0 : i;
            Sample row = new Sample { iteration = i, frame = Time.frameCount, clock = Time.realtimeSinceStartupAsDouble, length = length };
            trace.captured = i + 1; // Preserve even a partial failing row; completed distinguishes it.
            try
            {
            Boundary(item, i, "BeforeSource"); row.sourceStart = Stopwatch.GetTimestamp();
            if (item.sourceMode == "pooled") source = pool[lengthIndex];
            else if (item.sourceMode == "fresh" || source == null || source.Length != length) { source = new byte[length]; row.sourceAllocated = true; }
            Fill(source, id); row.sourceStop = Stopwatch.GetTimestamp(); Check(source.Length == length, "Public source must be exact-length"); row.sourceChanged = !ReferenceEquals(oldSource, source); oldSource = source;
            Boundary(item, i, "AfterSource"); sender.Set("data", source);
            int beforeCallbacks = sender.Get<int>("beforeCount"), beforeA = a.Get<int>("receivedCount"), beforeB = b.Get<int>("receivedCount"); uint frame = encoder.Get<uint>("frameIndex");
            Boundary(item, i, "BeforeEncode"); encoder.Timed("EncodeNow", out row.encodeStart, out row.encodeStop); Boundary(item, i, "AfterEncode");
            Check(string.IsNullOrEmpty(encoder.Get<string>("lastError")), "Encode failed"); row.callbacks = sender.Get<int>("beforeCount") - beforeCallbacks; Check(row.callbacks == 1, "BeforeEncode callback count");
            bool emitted = !item.unchanged || i == 0; Check(encoder.Get<uint>("frameIndex") == frame + (emitted ? 1u : 0u), "Unexpected output count"); Check(encoder.Get<int>("autoVariableCount") == (emitted ? 1 : 0), "Field count differs");
            if (emitted)
            {
                byte[] header = encoder.Get<byte[]>("_headerBytes"), payload = encoder.Get<byte[]>("_payloadBytes"); int capturedLength = U16(header, 42);
                Check(capturedLength <= payload.Length, "Protected payload exceeds its backing storage");
                if (capturedLength > packets[i].Length) packets[i] = new byte[capturedLength]; // Failure-only preservation of unexpected oversized output.
                Array.Copy(payload, packets[i], capturedLength); Array.Copy(header, headers[i], 56); row.payloadBytes = capturedLength;
                Check(row.payloadBytes == length + 25, "RawBytes packet length differs");
            }
            byte[] snapshot = (byte[])encoder.Get<object[]>("_sendPrevious")[0]; Check(snapshot != null && snapshot.Length >= length + 7, "Sender snapshot shorter than valid bytes");
            row.snapshotChanged = !ReferenceEquals(snapshot, oldSnapshot); row.snapshotCapacity = snapshot.Length;
            row.snapshotReportedLength = snapshotLengthMetadata ? encoder.Get<int[]>("_sendPreviousLengths")[0] : 7 + U16(snapshot, 5);
            Check(row.snapshotReportedLength == length + 7, "Private snapshot valid length differs");
            if (oldSnapshot != null && oldSnapshotValidLength == length + 7) Check(ReferenceEquals(snapshot, oldSnapshot), "Fixed-length sender snapshot not reused");
            Check(!ReferenceEquals(snapshot, source) && !ReferenceEquals(snapshot, encoder.Get<byte[]>("_payloadBytes")), "Sender snapshot aliases public source/frame builder"); Check(U32(snapshot, 0) == DataHash && snapshot[4] == NetworkFrameProtocol.ValueTypeRawBytes && U16(snapshot, 5) == length, "Snapshot variable header differs");
            for (int j = 0; j < length; j++) Check(snapshot[7 + j] == source[j], "Sender snapshot content differs"); oldSnapshot = snapshot; oldSnapshotValidLength = length + 7;
            if (emitted)
            {
                row.gpu = encoder.Get<bool>("lastFrameUsedGpuLuma4"); Check(row.gpu, "Actual GPU Encoder path not used");
                injection.Prepare(decoder, packets[i], row.payloadBytes, headers[i], frame);
                Boundary(item, i, "BeforeDecode"); decoder.Timed("_update", out row.decodeStart, out row.decodeStop); Boundary(item, i, "AfterDecode");
                Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcVerified"), "Protected Decode failed");
                row.applies = decoder.Get<int>("lastAppliedVariableCount"); Check(row.applies == 2, "Both duplicate-key receiver bindings must apply");
                byte[] receivedA = a.Get<byte[]>("data"), receivedB = b.Get<byte[]>("data"); Check(receivedA.Length == length && receivedB.Length == length, "Public receiver array is not exact-length");
                Check(!ReferenceEquals(receivedA, receivedB) && !ReferenceEquals(receivedA, source) && !ReferenceEquals(receivedB, source) && !ReferenceEquals(receivedA, snapshot) && !ReferenceEquals(receivedB, snapshot)
                    && !ReferenceEquals(receivedA, packets[i]) && !ReferenceEquals(receivedB, packets[i]) && !injection.Aliases(receivedA) && !injection.Aliases(receivedB), "RawBytes ownership alias");
                row.receiverAChanged = !ReferenceEquals(oldA, receivedA); row.receiverBChanged = !ReferenceEquals(oldB, receivedB);
                if (oldA != null && oldA.Length == length) Check(ReferenceEquals(oldA, receivedA) && ReferenceEquals(oldB, receivedB), "Matching-length receiver storage was not reused");
                for (int j = 0; j < length; j++) Check(receivedA[j] == source[j] && receivedB[j] == source[j], "Received bytes differ");
                if (i == 0) { byte saved = receivedA[0]; receivedA[0] ^= 0xff; Check(receivedB[0] == source[0] && snapshot[7] == source[0], "Fanout mutation leaked"); receivedA[0] = saved; }
                oldA = receivedA; oldB = receivedB;
            }
            else Check(ReferenceEquals(oldA, a.Get<byte[]>("data")) && ReferenceEquals(oldB, b.Get<byte[]>("data")), "No-output attempt changed a receiver array");
            row.receiverLength = a.Get<byte[]>("data").Length;
            Check(a.Get<int>("receivedCount") - beforeA == (emitted ? 1 : 0) && b.Get<int>("receivedCount") - beforeB == (emitted ? 1 : 0), "Receiver callback count differs");
            row.completed = true;
            }
            finally { rows[i] = row; }
            yield return null;
        }
        FlushCase(); VerifySavedCase(trace); trace = null; encoder.Call("_onDisable");
        for (int i = Programs.Count - 1; i >= firstProgram; i--) Programs[i].Dispose();
        for (int i = Owned.Count - 1; i >= firstOwned; i--) { if (Owned[i] is RenderTexture texture) texture.Release(); Object.DestroyImmediate(Owned[i]); Owned.RemoveAt(i); }
    }
    private static void Fill(byte[] value, int id)
    {
        uint state = unchecked((uint)(id + 1) * 0x9e3779b9u) ^ (uint)value.Length;
        for (int i = 0; i < value.Length; i++) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; value[i] = (byte)(state & 255u); }
        for (int i = 0; i < 4; i++) value[i] = (byte)((id >> (8 * i)) & 255);
    }
    private static int U16(byte[] value, int offset) => value[offset] | value[offset + 1] << 8;
    private static uint U32(byte[] value, int offset) => unchecked((uint)(value[offset] | value[offset + 1] << 8 | value[offset + 2] << 16 | value[offset + 3] << 24));
    private static uint Crc(byte[] value, int count)
    {
        uint crc = 0xffffffffu;
        for (int i = 0; i < count; i++) { crc ^= value[i]; for (int bit = 0; bit < 8; bit++) crc = (crc & 1u) == 0u ? crc >> 1 : 0xedb88320u ^ crc >> 1; }
        return crc ^ 0xffffffffu;
    }
    private static void FlushCase()
    {
        CaseBuffer trace = activeBuffer; if (trace == null) return;
        Case item = trace.item; Sample[] rows = trace.rows; byte[][] packets = trace.packets, headers = trace.headers;
        string directory = Path.Combine(Root, "packets", item.name); Directory.CreateDirectory(directory);
        using (var csv = new StreamWriter(Path.Combine(Root, "samples.csv"), true))
        for (int i = trace.flushed; i < trace.captured; i++)
        {
            Sample row = rows[i];
            string packetPath = "", headerPath = "";
            if (row.payloadBytes > 0)
            {
                packetPath = "packets/" + item.name + "/" + row.iteration.ToString("D5") + ".bin"; headerPath = packetPath + ".header";
                using (FileStream file = File.Create(Path.Combine(Root, packetPath))) file.Write(packets[row.iteration], 0, row.payloadBytes);
                File.WriteAllBytes(Path.Combine(Root, headerPath), headers[row.iteration]);
            }
            csv.WriteLine(Row(item.name, row.iteration, row.iteration >= item.warm, row.frame, row.clock, row.length, item.sourceMode, item.unchanged,
                row.sourceStart, row.sourceStop, Ms(row.sourceStop - row.sourceStart), row.encodeStart, row.encodeStop, Ms(row.encodeStop - row.encodeStart), row.decodeStart, row.decodeStop, Ms(row.decodeStop - row.decodeStart), row.payloadBytes,
                row.sourceAllocated, row.sourceChanged, row.snapshotChanged, row.snapshotCapacity, row.length + 7, row.snapshotReportedLength, row.receiverAChanged, row.receiverBChanged, row.receiverLength, row.callbacks, row.applies, row.gpu, packetPath, headerPath, row.completed));
            trace.flushed++;
        }
        activeBuffer = null;
    }
    private static void VerifySavedCase(CaseBuffer trace)
    {
        Case item = trace.item; Sample[] rows = trace.rows;
        foreach (Sample row in rows)
        {
            Check(row.completed, "Successful case contains an incomplete row"); if (row.payloadBytes == 0) continue;
            byte[] packet = trace.packets[row.iteration], header = trace.headers[row.iteration]; int length = row.length;
            Check(U32(header, 52) == Crc(header, 52) && (U16(header, 10) & 1) != 0 && U16(header, 42) == row.payloadBytes && U32(header, 44) == Crc(packet, row.payloadBytes), "Independent header/payload CRC differs");
            Check(U32(header, 0) == ProtocolConstants.Magic && header[4] == ProtocolConstants.VersionMajor && header[5] >= ProtocolConstants.PayloadCrcVersionMinor && U16(header, 8) == 56 && U32(header, 20) == 1u && U32(header, 24) == (uint)(row.iteration + 1), "Protected header identity/version/frame differs");
            Check(packet[0] == 1 && packet[1] == 0 && U16(packet, 2) == 1 && U16(packet, 8) == 1 && packet[10] == NetworkFrameProtocol.MessageTypeVariableState, "Network/message identity differs");
            Check(U16(packet, 14) == length + 9 && U16(packet, 16) == 1 && U32(packet, 18) == DataHash && packet[22] == NetworkFrameProtocol.ValueTypeRawBytes && U16(packet, 23) == length, "Variable body/value bounds differ");
            uint state = unchecked((uint)((item.unchanged ? 0 : row.iteration) + 1) * 0x9e3779b9u) ^ (uint)length; int id = item.unchanged ? 0 : row.iteration;
            for (int j = 0; j < length; j++) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; byte expected = j < 4 ? (byte)((id >> (8 * j)) & 255) : (byte)(state & 255u); Check(packet[25 + j] == expected, "Independent captured field byte differs"); }
        }
        Notes.Add("PASS " + item.name + "; opportunities=" + rows.Length + "; sourceAllocated=" + rows.Count(x => x.sourceAllocated) + "; snapshotReplaced=" + rows.Count(x => x.snapshotChanged) + "; receiverAReplaced=" + rows.Count(x => x.receiverAChanged) + "; receiverBReplaced=" + rows.Count(x => x.receiverBChanged));
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        try { FlushCase(); }
        catch (Exception flushError) { Notes.Add("CaseFlushError=" + flushError); error = error ?? flushError; }
        try
        {
            if (telemetry != null) { telemetry.Write(); telemetry.Dispose(); telemetry = null; }
            if (before != null)
            {
                var after = Sources(); File.WriteAllLines(Path.Combine(Root, "source-after.csv"), new[] { "path,sha256" }.Concat(after.Select(x => Row(x.Key, x.Value))));
                if (after.Count != before.Count || before.Any(x => !after.ContainsKey(x.Key) || after[x.Key] != x.Value)) error = error ?? new InvalidOperationException("Product source changed during measurement");
            }
        }
        catch (Exception cleanup) { error = error ?? cleanup; }
        foreach (var program in Programs.ToArray()) program.Dispose();
        foreach (Object item in Owned) { if (item is RenderTexture texture) texture.Release(); if (item != null) Object.DestroyImmediate(item); } Owned.Clear(); RenderTexture.active = null;
        if (!string.IsNullOrWhiteSpace(Root))
        {
            Directory.CreateDirectory(Root); File.WriteAllLines(Path.Combine(Root, "artifact-hashes.csv"), Artifacts);
            Notes.Add("Assertions=" + assertions); if (error != null) { Notes.Add("FailureCase=" + activeCase + "; Sample=" + activeIteration); Notes.Add(error.ToString()); }
            File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(Notes));
        }
        if (error != null) UnityEngine.Debug.LogException(error); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
