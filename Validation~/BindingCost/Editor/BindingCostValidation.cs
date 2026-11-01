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
using K13A.TSMP.Udon;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

public static class BindingCostValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string ProbePath = "Assets/Validation/BindingCost/BindingCostProbe.asset";
    const string RecorderPath = "Assets/Validation/BindingCost/BindingCostRecorder.asset";
    const string Session = "TSMP.BindingCost";
    static string Root => Environment.GetEnvironmentVariable("TSMP_BINDING_COST_RESULTS");
    static readonly List<Object> owned = new List<Object>();
    static readonly List<Program> programs = new List<Program>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> samples = new List<string> { "case,sample,measured,frame,clock,startTicks,endTicks,encodeMs,fields,targets,expectedFields,encodedFields,payloadBytes,beforeCallbacks,usedGpu,packet,header" };
    static readonly List<string> helper = new List<string> { "targets,sample,method,startTicks,endTicks,milliseconds" };
    static readonly List<string> semantics = new List<string> { "case,round,startTicks,endTicks,encodeMs,expectedOrder,actualOrder,expectedFields,encodedFields,actualCallbacks" };
    static readonly List<string> hashes = new List<string> { "kind,path,bytes,sha256" };
    static Dictionary<string, string> before;
    static IEnumerator work;
    static int checks, lastFrame = -1;
    [Serializable] public sealed class Plan { public Case[] cases; }
    [Serializable] public sealed class Case { public string name, regime; public int fields, targets, warm, samples; }
    static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    static string Q(object value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(Q));
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
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
                foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories).Where(path => new[] { ".cs", ".shader", ".cginc", ".meta", ".asmdef", ".mat", ".prefab" }.Contains(Path.GetExtension(path))).OrderBy(path => path))
                    result[Path.GetFullPath(path)] = Hash(File.ReadAllBytes(path));
            }
        }
        return result;
    }
    static void Compile()
    {
        bool failed = false; Application.LogCallback log = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Assert || kind == LogType.Exception) failed = true; };
        Application.logMessageReceived += log;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= log; }
        Check(!failed, "Client compilation failed");
    }
    static void Asset(string assetPath, string script)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) return;
        var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>(); asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(script);
        Check(asset.sourceCsScript != null, "Missing script " + script); AssetDatabase.CreateAsset(asset, assetPath);
    }
    public static void Run()
    {
        try
        {
            Check(!string.IsNullOrWhiteSpace(Root), "Results required"); Directory.CreateDirectory(Root);
            Check(!File.Exists(Path.Combine(Root, "result.txt")), "Fresh results required");
            File.WriteAllText(Path.Combine(Root, "result.txt"), "RUNNING");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Asset(ProbePath, "Assets/Validation/BindingCost/Runtime/BindingCostProbe.cs");
            Asset(RecorderPath, "Assets/Validation/BindingCost/Runtime/BindingCostRecorder.cs");
            Compile(); AssetDatabase.SaveAssets(); SessionState.SetBool(Session, true); Attach(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }
    [InitializeOnLoadMethod] static void Attach() { EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered; }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Session, false)) return;
        SessionState.SetBool(Session, false); work = Validate(); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
        try { if (work.MoveNext()) return; } catch (Exception error) { Finish(error); return; } Finish(null);
    }
    static void Dump()
    {
        File.WriteAllLines(Path.Combine(Root, "samples.csv"), samples); File.WriteAllLines(Path.Combine(Root, "helper.csv"), helper);
        File.WriteAllLines(Path.Combine(Root, "semantics.csv"), semantics);
        File.WriteAllLines(Path.Combine(Root, "artifact-hashes.csv"), hashes);
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        foreach (var p in programs.ToArray()) p.Dispose(); programs.Clear(); RenderTexture.active = null;
        foreach (Object item in owned.AsEnumerable().Reverse()) { if (item is RenderTexture texture) texture.Release(); if (item != null) Object.DestroyImmediate(item); } owned.Clear();
        if (before != null)
        {
            var after = Sources(); File.WriteAllLines(Path.Combine(Root, "source-after.csv"), new[] { "path,sha256" }.Concat(after.Select(pair => Row(pair.Key, pair.Value))));
            if (after.Count != before.Count || before.Any(pair => !after.ContainsKey(pair.Key) || pair.Value != after[pair.Key])) error = error ?? new InvalidOperationException("Product source changed during run");
        }
        if (error != null) notes.Add(error.ToString()); notes.Add("Assertions=" + checks); Dump();
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(notes)); EditorApplication.Exit(error == null ? 0 : 1);
    }
    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;
        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path); Check(asset != null, "Missing program " + path);
            Code = asset.SerializedProgramAsset.RetrieveProgram(); Check(Code != null && Code.ByteCode.Length > 0, "Missing client bytecode " + path);
            if (!hashes.Any(line => line.Contains(Q(path)))) hashes.Add(Row("client-bytecode", path, Code.ByteCode.Length, Hash(Code.ByteCode)));
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(Code), "VM load failed");
            Backing = new GameObject(asset.name + " binding validation VM").AddComponent<UdonBehaviour>();
            Type t = typeof(UdonBehaviour);
            Check((bool)t.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved VM references");
            t.GetField("_program", Private).SetValue(Backing, Code); t.GetField("_udonVM", Private).SetValue(Backing, vm);
            t.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance); t.GetField("_isReady", Private).SetValue(Backing, true); t.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var table = (Dictionary<string, List<uint>>)t.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols()) table[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null) foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address)) Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
            programs.Add(this);
        }
        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name) { Backing.SendCustomEvent(name); Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM failed " + name); }
        public void Timed(string name, out long start, out long stop) { start = Stopwatch.GetTimestamp(); Backing.SendCustomEvent(name); stop = Stopwatch.GetTimestamp(); Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM failed " + name); }
        public void Dispose() { programs.Remove(this); if (Backing != null) Object.DestroyImmediate(Backing.gameObject); }
    }
    static Program Codec()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        Check(prefab != null, "Luma4 prefab missing"); var native = Keep(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (copies.TryGetValue(original, out Material existing)) return existing;
            var copy = Keep(new Material(original)); copies.Add(original, copy); return copy;
        };
        foreach (var field in native.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(native, clone((Material)field.GetValue(native)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(native) is Material[] mats) field.SetValue(native, mats.Select(clone).ToArray());
        }
        return new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", native);
    }
    static Program Encoder(Program codec)
    {
        var e = new Program("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
        var output = Keep(new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }); Check(output.Create(), "Output allocation failed");
        e.Set("output", output); e.Set("autoEncode", false); e.Set("debugLog", false); e.Set("sampleSize", 4); e.Set("frameIndex", 1u);
        e.Set("selectedCodecUdonTarget", codec.Backing); e.Set("useGpuLuma4", true); e.Set("luma4EncodeMaterial", Keep(new Material(Resources.Load<Material>("TSMPEncodeLuma4"))));
        e.Set("blockExpandMaterial", Keep(new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"))));
        e.Set("transSyncRefreshInterval", 0f); e.Set("targetPayloadBytes", 0); e.Call("_onEnable"); return e;
    }
    static string Field(int index, int targetCount) => "f" + (targetCount == 1 ? index : 0).ToString("D3");
    static void Bind(Program owner, Program[] targets, int count, bool encoder = true)
    {
        owner.Set("bindingTargets", new Component[0]); owner.Set("bindingUdonTargets", Enumerable.Range(0, count).Select(i => targets[i % targets.Length].Backing).ToArray());
        owner.Set("bindingNetworkIds", Enumerable.Range(0, count).Select(i => (ushort)(i % targets.Length + 1)).ToArray());
        owner.Set("bindingVariableHashes", Enumerable.Range(0, count).Select(i => StableHash.Fnv1A32(Field(i, targets.Length))).ToArray());
        owner.Set("bindingValueTypes", Enumerable.Repeat((byte)NetworkFrameProtocol.ValueTypeFloat32, count).ToArray());
        owner.Set("bindingFieldNames", Enumerable.Range(0, count).Select(i => Field(i, targets.Length)).ToArray());
        owner.Set("bindingDirections", new int[count]); owner.Set("bindingPriorities", new int[count]);
        if (encoder)
        {
            owner.Set("bindingSendOnChange", Enumerable.Repeat(true, count).ToArray()); owner.Set("bindingMinSendIntervals", new float[count]);
        }
    }
    static int VerifyPacket(byte[] packet, float[] expected, int targetCount, int expectedCount)
    {
        Func<int, int> u16 = at => packet[at] | packet[at + 1] << 8;
        Check(packet.Length >= 8, "Short packet"); int messages = u16(2), cursor = 8, found = 0; var seen = new HashSet<string>();
        for (int message = 0; message < messages; message++)
        {
            Check(cursor + 10 <= packet.Length, "Message bounds"); int network = u16(cursor), bodyEnd = cursor + 8 + u16(cursor + 6);
            Check(packet[cursor + 2] == NetworkFrameProtocol.MessageTypeVariableState && bodyEnd <= packet.Length, "Message contract");
            int count = u16(cursor + 8); cursor += 10;
            for (int field = 0; field < count; field++)
            {
                Check(cursor + 11 <= bodyEnd && packet[cursor + 4] == NetworkFrameProtocol.ValueTypeFloat32 && u16(cursor + 5) == 4, "Float entry contract");
                uint hash = BitConverter.ToUInt32(packet, cursor); int index = targetCount == 1 ? Enumerable.Range(0, expected.Length).Single(i => StableHash.Fnv1A32(Field(i, targetCount)) == hash) : network - 1;
                Check(index >= 0 && index < expected.Length && network == index % targetCount + 1 && hash == StableHash.Fnv1A32(Field(index, targetCount)), "Unexpected entry identity");
                Check(seen.Add(network + ":" + hash), "Duplicate entry"); Check(BitConverter.ToSingle(packet, cursor + 7) == expected[index], "Entry value mismatch"); cursor += 11; found++;
            }
            Check(cursor == bodyEnd, "Message trailing bytes");
        }
        Check(cursor == packet.Length && found == expectedCount, "Packet field count/tail"); return found;
    }
    static void Deliver(Program decoder, byte[] packet, byte[] header, uint frame)
    {
        int padded = (packet.Length + 3) / 4 * 4; var raw = new byte[padded]; Array.Copy(packet, raw, packet.Length);
        decoder.Set("_slotHead", 0); decoder.Set("pendingDecodeCount", 1); decoder.Set("_slotStates", new[] { 2, 0 }); decoder.Set("_slotStages", new[] { 2, 0 });
        decoder.Set("_slotReadbackBytes", new[] { raw, (byte[])null }); decoder.Set("_slotHeaders", new[] { header, (byte[])null });
        decoder.Set("_slotPayloads", new[] { new byte[packet.Length], (byte[])null }); decoder.Set("_slotPayloadCounts", new[] { packet.Length, 0 }); decoder.Set("_slotReadCounts", new[] { padded / 4, 0 });
        decoder.Set("_slotRequirePayloadCrc32", new[] { true, false }); decoder.Set("_slotSafetyModes", new[] { 0, 0 }); decoder.Set("_slotHeaderValid", new[] { true, false });
        decoder.Set("_slotStreamIds", new uint[] { 1, 0 }); decoder.Set("_slotFrameIndices", new[] { frame, 0u }); decoder.Set("lastFrameValid", false); decoder.Call("_update");
        Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcVerified"), "Protected Decoder completion failed: " + decoder.Get<string>("lastError"));
    }
    static IEnumerator Validate()
    {
        Compile(); Application.runInBackground = true; Application.targetFrameRate = 90; QualitySettings.vSyncCount = 0;
        notes.Add("Unity=" + Application.unityVersion + "; CPU=" + SystemInfo.processorType + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; Color=" + QualitySettings.activeColorSpace + "; StopwatchFrequency=" + Stopwatch.Frequency);
        notes.Add("Post-Play client compilation; full actual EncodeNow elapsed, nested VM work included once, one opportunity per observed Unity frame. GPU Encoder output; protected byte injection for Decoder application, not receiver GPU timing, source Hz or live VRChat FPS.");
        before = Sources(); File.WriteAllLines(Path.Combine(Root, "source-before.csv"), new[] { "path,sha256" }.Concat(before.Select(pair => Row(pair.Key, pair.Value))));
        byte[] plan = File.ReadAllBytes(Environment.GetEnvironmentVariable("TSMP_BINDING_COST_PLAN")); File.WriteAllBytes(Path.Combine(Root, "plan-used.json"), plan); hashes.Add(Row("plan", "plan-used.json", plan.Length, Hash(plan)));
        var parsed = JsonUtility.FromJson<Plan>(System.Text.Encoding.UTF8.GetString(plan).TrimStart('\uFEFF')); Check(parsed.cases != null && parsed.cases.Length == 12, "Frozen matrix expected");
        if (Environment.GetEnvironmentVariable("TSMP_BINDING_COST_SEMANTICS_ONLY") != "1")
            foreach (Case item in parsed.cases) { var run = Measure(item); while (run.MoveNext()) yield return run.Current; Dump(); GC.Collect(); yield return null; }
        else notes.Add("Scope=SemanticsOnly; no performance matrix sampled");
        var semantics = SemanticCases(); while (semantics.MoveNext()) yield return semantics.Current; Dump();
        var edges = SemanticEdges(); while (edges.MoveNext()) yield return edges.Current; Dump();
    }
    static IEnumerator Measure(Case item)
    {
        int firstProgram = programs.Count; var codec = Codec(); var e = Encoder(codec);
        var senders = Enumerable.Range(0, item.targets).Select(i => new Program(ProbePath)).ToArray();
        var receivers = Enumerable.Range(0, item.targets).Select(i => new Program(ProbePath)).ToArray();
        Bind(e, senders, item.fields); var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset");
        decoder.Set("applyEveryFrame", false); decoder.Set("debugLog", false); decoder.Set("_crc32Table", Crc32Runtime.EnsureFastTable(null)); Bind(decoder, receivers, item.fields, false);
        float[] expected = new float[item.fields]; int count = item.warm + item.samples; string directory = Path.Combine(Root, "packets", item.name); Directory.CreateDirectory(directory);
        for (int iteration = 0; iteration < count; iteration++)
        {
            for (int i = 0; i < item.fields; i++)
                if (iteration == 0 || item.regime == "changing" || item.regime == "quarter-changing" && i % 4 == iteration % 4)
                { expected[i] = iteration * 1024 + i + 1; senders[i % item.targets].Set(Field(i, item.targets), expected[i]); }
            int[] previous = senders.Select(s => s.Get<int>("beforeCount")).ToArray(); uint sequence = e.Get<uint>("frameIndex");
            double clock = Time.realtimeSinceStartupAsDouble; e.Timed("EncodeNow", out long start, out long stop);
            Check(string.IsNullOrEmpty(e.Get<string>("lastError")), "Encode failed " + item.name + ": " + e.Get<string>("lastError"));
            int emitted = iteration == 0 || item.regime == "changing" ? item.fields : item.regime == "unchanged" ? 0 : item.fields / 4;
            Check(e.Get<uint>("frameIndex") == sequence + (emitted > 0 ? 1u : 0u), "Unexpected publication");
            Check(e.Get<int>("autoVariableCount") == emitted, "Encoder field diagnostic mismatch");
            int[] callbackDeltas = senders.Select((s, i) => s.Get<int>("beforeCount") - previous[i]).ToArray();
            Check(callbackDeltas.All(n => n == 1), "BeforeEncode not exactly once per target");
            string packetPath = "", headerPath = ""; bool gpu = false; int payloadCount = 0;
            if (emitted > 0)
            {
                byte[] header = e.Get<byte[]>("_headerBytes").ToArray();
                payloadCount = header[42] | header[43] << 8; // successful ClearFrame resets the public count, not the protected header
                byte[] packet = e.Get<byte[]>("_payloadBytes").Take(payloadCount).ToArray();
                VerifyPacket(packet, expected, item.targets, emitted); gpu = e.Get<bool>("lastFrameUsedGpuLuma4"); Check(gpu, "GPU Luma4 path not used");
                packetPath = "packets/" + item.name + "/" + iteration.ToString("D3") + ".bin"; headerPath = packetPath + ".header";
                File.WriteAllBytes(Path.Combine(Root, packetPath), packet); File.WriteAllBytes(Path.Combine(Root, headerPath), header);
                Deliver(decoder, packet, header, sequence);
                Check(decoder.Get<int>("lastAppliedVariableCount") == emitted, "Decoder count mismatch");
                for (int i = 0; i < item.fields; i++) Check(receivers[i % item.targets].Get<float>(Field(i, item.targets)) == expected[i], "Receiver value mismatch " + i);
            }
            samples.Add(Row(item.name, iteration, iteration >= item.warm, Time.frameCount, clock, start, stop, Ms(stop - start), item.fields, item.targets, emitted, e.Get<int>("autoVariableCount"), payloadCount, callbackDeltas.Sum(), gpu, packetPath, headerPath));
            yield return null;
        }
        if (item.regime == "changing")
        {
            using (var scan = new Program(ProbePath))
            {
                scan.Set("scanTargets", senders.Select(s => s.Backing).ToArray());
                foreach (string method in new[] { "ScanBeforeEncode", "DirectBeforeEncode" })
                {
                    for (int i = 0; i < 5; i++) scan.Call(method);
                    for (int i = 0; i < 32; i++)
                    {
                        scan.Timed(method, out long start, out long stop); helper.Add(Row(item.targets, i, method, start, stop, Ms(stop - start))); yield return null;
                    }
                }
            }
        }
        foreach (var p in programs.Skip(firstProgram).ToArray()) p.Dispose();
    }
    static IEnumerator SemanticCases()
    {
        int first = programs.Count; var codec = Codec(); var e = Encoder(codec); var targets = Enumerable.Range(0, 4).Select(i => new Program(ProbePath)).ToArray();
        var recorder = new Program(RecorderPath); recorder.Set("visits", new int[4096]);
        for (int i = 0; i < targets.Length; i++) { targets[i].Set("targetId", i); targets[i].Set("recorder", recorder.Backing); }
        // Repeated targets, rotating first-seen order, same-length target replacement,
        // direction changes, and sendMode set by BeforeEncode are checked outside timing.
        Bind(e, targets.Take(3).ToArray(), 12);
        e.Set("bindingFieldNames", Enumerable.Range(0, 12).Select(i => "f" + (i / 3).ToString("D3")).ToArray());
        e.Set("bindingVariableHashes", Enumerable.Range(0, 12).Select(i => StableHash.Fnv1A32("f" + (i / 3).ToString("D3"))).ToArray());
        var refs = e.Get<UdonBehaviour[]>("bindingUdonTargets");
        for (int round = 0; round < 9; round++)
        {
            if (round == 3) for (int i = 0; i < refs.Length; i++) if (refs[i] == targets[1].Backing) refs[i] = targets[3].Backing;
            if (round == 5) e.Set("bindingDirections", Enumerable.Range(0, 12).Select(i => i % 3 == 0 ? (int)NetworkSyncDirection.ReceiveOnly : 0).ToArray());
            if (round == 7) e.Set("bindingDirections", new int[12]);
            var directions = e.Get<int[]>("bindingDirections"); int rotation = e.Get<int[]>("_sendOrder") == null ? 0 : e.Get<int>("_sendRotation");
            if (round == 3) rotation = 0; // same-length target substitution rebuilds send identity
            var expectedOrder = new List<int>();
            for (int step = 0; step < refs.Length; step++)
            {
                int index = (step + rotation % refs.Length) % refs.Length; if (directions[index] != 0) continue;
                int target = Array.FindIndex(targets, t => t.Backing == refs[index]); if (!expectedOrder.Contains(target)) expectedOrder.Add(target);
            }
            recorder.Set("count", 0); int[] old = targets.Select(t => t.Get<int>("beforeCount")).ToArray();
            foreach (var t in targets) { t.Set("setModeInBefore", true); t.Set("modeInBefore", round % 2 == 0 ? (int)SendMode.Always : (int)SendMode.OnChange); }
            e.Call("EncodeNow"); Check(string.IsNullOrEmpty(e.Get<string>("lastError")), "Semantic encode failure");
            int expectedFields = new[] { 12, 0, 12, 12, 12, 0, 8, 4, 12 }[round];
            Check(e.Get<int>("autoVariableCount") == expectedFields, "BeforeEncode sendMode/direction effect differs round=" + round);
            int[] order = recorder.Get<int[]>("visits").Take(recorder.Get<int>("count")).ToArray(); Check(order.SequenceEqual(expectedOrder), "BeforeEncode first-seen order differs round=" + round);
            for (int i = 0; i < targets.Length; i++) Check(targets[i].Get<int>("beforeCount") == old[i] + (expectedOrder.Contains(i) ? 1 : 0), "Callback once/cache stale round=" + round);
            notes.Add("SemanticRound=" + round + "; Order=" + string.Join("/", order) + "; AppliedSendMode=" + (round % 2 == 0 ? (int)SendMode.Always : (int)SendMode.OnChange) + "; EncodedFields=" + e.Get<int>("autoVariableCount"));
            yield return null;
        }
        foreach (var p in programs.Skip(first).ToArray()) p.Dispose(); notes.Add("PASS callback once/order, same-length in-place target substitution, direction changes and callback-observed sendMode changes.");
    }
    static void CustomBind(Program encoder, Program[] targets, int[] targetIndices, string[] names, int[] priorities, float[] intervals = null)
    {
        int count = names.Length;
        encoder.Set("bindingTargets", new Component[0]); encoder.Set("bindingUdonTargets", targetIndices.Select(i => targets[i].Backing).ToArray());
        encoder.Set("bindingNetworkIds", targetIndices.Select(i => (ushort)(i + 1)).ToArray()); encoder.Set("bindingFieldNames", names);
        encoder.Set("bindingVariableHashes", names.Select(StableHash.Fnv1A32).ToArray()); encoder.Set("bindingValueTypes", Enumerable.Repeat((byte)NetworkFrameProtocol.ValueTypeFloat32, count).ToArray());
        encoder.Set("bindingDirections", new int[count]); encoder.Set("bindingPriorities", priorities); encoder.Set("bindingSendOnChange", Enumerable.Repeat(true, count).ToArray());
        encoder.Set("bindingMinSendIntervals", intervals ?? new float[count]);
    }
    static void SemanticCall(string name, int round, Program e, Program[] targets, Program recorder, int[] order, int fields)
    {
        recorder.Set("count", 0); int[] old = targets.Select(t => t.Get<int>("beforeCount")).ToArray();
        e.Timed("EncodeNow", out long start, out long stop);
        Check(string.IsNullOrEmpty(e.Get<string>("lastError")), "Semantic encode failed " + name + ": " + e.Get<string>("lastError"));
        int[] actual = recorder.Get<int[]>("visits").Take(recorder.Get<int>("count")).ToArray();
        Check(actual.SequenceEqual(order), "Semantic callback order differs " + name + "/" + round);
        Check(e.Get<int>("autoVariableCount") == fields, "Semantic field/sendMode effect differs " + name + "/" + round);
        int callbackCount = 0;
        for (int i = 0; i < targets.Length; i++) { int delta = targets[i].Get<int>("beforeCount") - old[i]; Check(delta == (order.Contains(i) ? 1 : 0), "Semantic callback count differs " + name); callbackCount += delta; }
        semantics.Add(Row(name, round, start, stop, Ms(stop - start), string.Join("/", order), string.Join("/", actual), fields, e.Get<int>("autoVariableCount"), callbackCount));
    }
    static IEnumerator SemanticEdges()
    {
        foreach (string name in new[] { "mixed-priority-rotation", "undue-or-receiveonly-first-field", "cross-callback-sendmode", "callback-target-substitution", "callback-direction-mutation" })
        {
            int first = programs.Count; var codec = Codec(); var e = Encoder(codec); var targets = Enumerable.Range(0, 3).Select(i => new Program(ProbePath)).ToArray();
            var recorder = new Program(RecorderPath); recorder.Set("visits", new int[4096]);
            for (int i = 0; i < targets.Length; i++) { targets[i].Set("targetId", i); targets[i].Set("recorder", recorder.Backing); targets[i].Set("sendMode", (int)SendMode.Always); }
            if (name == "mixed-priority-rotation")
            {
                int[] refs = { 0, 1, 0, 2, 1, 2 };
                CustomBind(e, targets, refs, new[] { "f000", "f000", "f001", "f000", "f001", "f001" }, new[] { 10, 10, 10, 0, 0, 0 });
                for (int round = 0; round < 6; round++)
                {
                    var order = new List<int>();
                    foreach (int begin in new[] { 0, 3 }) for (int step = 0; step < 3; step++) { int target = refs[begin + (step + round % 3) % 3]; if (!order.Contains(target)) order.Add(target); }
                    SemanticCall(name, round, e, targets, recorder, order.ToArray(), 6); yield return null;
                }
            }
            else if (name == "undue-or-receiveonly-first-field")
            {
                CustomBind(e, targets, new[] { 0, 0, 1 }, new[] { "f000", "f001", "f000" }, new[] { 10, 0, 5 }, new[] { 1000f, 0f, 0f });
                SemanticCall(name, 0, e, targets, recorder, new[] { 0, 1 }, 3); yield return null;
                SemanticCall(name, 1, e, targets, recorder, new[] { 1, 0 }, 2); yield return null;
                e.Get<int[]>("bindingDirections")[0] = (int)NetworkSyncDirection.ReceiveOnly;
                SemanticCall(name, 2, e, targets, recorder, new[] { 1, 0 }, 2); yield return null;
                e.Get<int[]>("bindingDirections")[0] = 0;
                SemanticCall(name, 3, e, targets, recorder, new[] { 0, 1 }, 3); yield return null;
            }
            else if (name == "cross-callback-sendmode")
            {
                CustomBind(e, targets, new[] { 0, 1, 0, 1 }, new[] { "f000", "f000", "f001", "f001" }, new[] { 30, 20, 10, 0 });
                SemanticCall(name, 0, e, targets, recorder, new[] { 0, 1 }, 4); yield return null;
                targets[1].Set("sendMode", (int)SendMode.OnChange);
                targets[0].Set("setOtherMode", true); targets[0].Set("otherTarget", targets[1].Backing); targets[0].Set("otherMode", (int)SendMode.Always);
                targets[1].Set("setOtherMode", true); targets[1].Set("otherTarget", targets[0].Backing); targets[1].Set("otherMode", (int)SendMode.OnChange);
                SemanticCall(name, 1, e, targets, recorder, new[] { 0, 1 }, 4); yield return null;
                targets[0].Set("setOtherMode", false); targets[1].Set("setOtherMode", false);
                SemanticCall(name, 2, e, targets, recorder, new[] { 0, 1 }, 2); yield return null;
            }
            else if (name == "callback-target-substitution")
            {
                CustomBind(e, targets, new[] { 0, 1, 0, 1 }, new[] { "f000", "f000", "f001", "f001" }, new[] { 30, 20, 10, 0 });
                SemanticCall(name, 0, e, targets, recorder, new[] { 0, 1 }, 4); yield return null;
                targets[0].Set("bindingEncoder", e.Backing); targets[0].Set("mutateBindingTarget", true); targets[0].Set("mutationIndex", 1); targets[0].Set("replacementTarget", targets[2].Backing);
                SemanticCall(name, 1, e, targets, recorder, new[] { 0, 1 }, 4); yield return null;
                SemanticCall(name, 2, e, targets, recorder, new[] { 0, 2, 1 }, 4); yield return null;
            }
            else
            {
                CustomBind(e, targets, new[] { 0, 1, 0 }, new[] { "f000", "f000", "f001" }, new[] { 30, 20, 10 });
                SemanticCall(name, 0, e, targets, recorder, new[] { 0, 1 }, 3); yield return null;
                targets[0].Set("bindingEncoder", e.Backing); targets[0].Set("mutateBindingDirection", true); targets[0].Set("mutationIndex", 1); targets[0].Set("directionValue", (int)NetworkSyncDirection.ReceiveOnly);
                SemanticCall(name, 1, e, targets, recorder, new[] { 0 }, 2); yield return null;
                e.Get<int[]>("bindingDirections")[1] = 0;
                SemanticCall(name, 2, e, targets, recorder, new[] { 0, 1 }, 3); yield return null;
            }
            notes.Add("PASS semantic edge " + name); foreach (var p in programs.Skip(first).ToArray()) p.Dispose();
        }
    }
}
#endif
