#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharp;
using UdonSharp.Compiler;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
#endif
using Object = UnityEngine.Object;

public static class BudgetRecoveryValidation
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static string Root => Environment.GetEnvironmentVariable("TSMP_BUDGET_RECOVERY_RESULTS");
    static readonly List<string> Report = new List<string>();
    static readonly List<string> Trace = new List<string> { "case,frame,time,stream,payload_bytes,messages,dropped,held,high,medium,slow,rpc_calls,source_frame,sent_field_mask,rpc_written,queue_count,head_repeats,packet,header" };
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    static void Begin()
    {
        if (string.IsNullOrWhiteSpace(Root) || File.Exists(Path.Combine(Root, "result.txt"))) throw new InvalidOperationException("Fresh results required");
        Directory.CreateDirectory(Root);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Report.Add("Unity=" + Application.unityVersion);
    }
    static void Finish(Exception error)
    {
        File.WriteAllLines(Path.Combine(Root, "trace.csv"), Trace);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL", "Assertions=" + checks }.Concat(Report).Concat(error == null ? new string[0] : new[] { error.ToString() }));
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static object Get(object instance, string name) => instance.GetType().GetField(name, Flags).GetValue(instance);
    static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Flags).SetValue(instance, value);
    static uint Hash(Type type, string name) => TransSyncMetadata.GetOrCreate(null, type).Fields.Single(f => f.FieldInfo.Name == name).VariableHash;
    static Dictionary<uint, byte[]> Values(byte[] payload, int length)
    {
        var result = new Dictionary<uint, byte[]>();
        int offset = 8, messages = payload[2] | payload[3] << 8;
        for (int message = 0; message < messages; message++)
        {
            Check(offset + 8 <= length, "Message header bounds");
            int end = offset + 8 + (payload[offset + 6] | payload[offset + 7] << 8);
            Check(end <= length, "Message body bounds");
            if (payload[offset + 2] == NetworkFrameProtocol.MessageTypeVariableState)
            {
                int count = payload[offset + 8] | payload[offset + 9] << 8, cursor = offset + 10;
                for (int field = 0; field < count; field++)
                {
                    Check(cursor + 7 <= end, "Entry header bounds");
                    int bytes = payload[cursor + 5] | payload[cursor + 6] << 8;
                    Check(cursor + 7 + bytes <= end, "Entry body bounds");
                    uint hash = (uint)(payload[cursor] | payload[cursor + 1] << 8 | payload[cursor + 2] << 16 | payload[cursor + 3] << 24);
                    result.Add(hash, payload.Skip(cursor + 7).Take(bytes).ToArray()); cursor += 7 + bytes;
                }
                Check(cursor == end, "Variable tail");
            }
            offset = end;
        }
        Check(offset == length, "Packet tail"); return result;
    }
#if !UDONSHARP
    sealed class Native : IDisposable
    {
        readonly GameObject owner = new GameObject("Budget diagnostic encoder"), source = new GameObject("Budget diagnostic source");
        readonly GameObject codec;
        readonly RenderTexture output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
        public readonly TSMPEncoder Encoder;
        public readonly BudgetRecoveryProbe Probe;
        public Native()
        {
            codec = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
            Encoder = owner.AddComponent<TSMPEncoder>(); Encoder.autoEncode = false; Encoder.debugLog = false; Encoder.useGpuLuma4 = false;
            Encoder.transSyncRefreshInterval = 0; Encoder.transRpcRepeatFrames = 2; Encoder.output = output; Encoder.selectedCodec = codec.GetComponent<TSMPCodec>();
            Probe = source.AddComponent<BudgetRecoveryProbe>(); Probe.networkId = 1; Probe.transRpcEncoder = Encoder;
            Encoder.networkBehaviours = new TSMPNetworkBehaviour[] { Probe }; output.Create();
        }
        public Dictionary<uint, byte[]> Send(bool outputExpected = true)
        {
            uint before = Encoder.frameIndex; Encoder.EncodeNow();
            Check(Encoder.frameIndex == before + (outputExpected ? 1u : 0u), "Unexpected frame sequence: " + Encoder.lastError);
            Check(string.IsNullOrEmpty(Encoder.lastError), "Encode error " + Encoder.lastError);
            return outputExpected ? Values((byte[])Get(Encoder, "_encodedPayload"), Encoder.payloadBytes) : new Dictionary<uint, byte[]>();
        }
        public void Dispose() { Object.DestroyImmediate(owner); Object.DestroyImmediate(source); Object.DestroyImmediate(codec); output.Release(); Object.DestroyImmediate(output); }
    }
    static void Diagnostics()
    {
        using (var n = new Native())
        {
            var e = n.Encoder; var p = n.Probe; e.targetPayloadBytes = 29;
            uint first = n.Send().Keys.Single();
            Check(e.oversizedVariableCount == 1 && e.deferredVariableCount == 2, "Oversized versus deferred counts");
            Check(e.lastDeferredVariableFitsBudget && e.lastDeferredVariableRequiredBytes == 29 && e.lastDeferredVariableName != "big", "Normal peer deferral mislabeled oversized");
            Check(first != n.Send().Keys.Single() && first == n.Send().Keys.Single(), "Equal-priority rotation");
            p.aEnabled = p.bEnabled = false; n.Send(false);
            Check(e.lastDeferredVariableName == "big" && e.lastDeferredVariableNetworkId == 1 && e.lastDeferredVariableRequiredBytes == 125 && !e.lastDeferredVariableFitsBudget, "Oversized field identity/required bytes");
            p.big = new byte[] { 7, 8, 9, 10 }; Check(n.Send()[Hash(typeof(BudgetRecoveryProbe), "big")].SequenceEqual(p.big), "Dynamic shrink latest bytes");
            Check(e.deferredVariableCount == 0 && e.lastDeferredVariableRequiredBytes == 0 && e.lastDeferredVariableName == "", "Stale field diagnostics");
            p.big = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray(); n.Send(false);
            Check(e.oversizedVariableCount == 1 && e.lastDeferredVariableRequiredBytes == 125, "Dynamic grow diagnostic");
            e.targetPayloadBytes = 125; Check(n.Send().Single().Value.SequenceEqual(p.big), "Budget expansion did not send full latest field");
            Check(e.oversizedVariableCount == 0, "Oversized count persisted after expansion");
            p.bigEnabled = false; p.aEnabled = p.bEnabled = true; e.targetPayloadBytes = 256;
            Check(e.QueueTransRpc(1, 99u, "ReceiveEvent", 123), "Queue head RPC"); e.targetPayloadBytes = 29;
            var queue = (List<EncoderNativeFrameBuilder.QueuedRpc>)Get(e, "_queuedRpcs");
            for (int i = 0; i < 3; i++)
            {
                Check(n.Send().Count == 1, "Blocked RPC starved small state");
                Check(e.deferredRpcCount == 1 && e.blockedRpcNetworkId == 1 && e.blockedRpcHash == 99 && e.blockedRpcRequiredBytes == 43, "Blocked RPC diagnostics");
                Check(queue.Count == 1 && queue[0].RepeatsRemaining == 2, "Blocked repeat consumed");
            }
            e.targetPayloadBytes = 256; n.Send(); Check(queue[0].RepeatsRemaining == 1 && e.deferredRpcCount == 0 && e.blockedRpcRequiredBytes == 0, "RPC resume/reset");
            n.Send(); Check(queue.Count == 0, "RPC queue did not drain");
            e.targetPayloadBytes = 8; n.Send(false); Check(e.deferredVariableCount > 0, "Reset setup missing");
            var saved = e.output; e.output = null; e.EncodeNow();
            Check(e.deferredVariableCount == 0 && e.deferredRpcCount == 0 && e.oversizedVariableCount == 0 && e.lastDeferredVariableName == "" && e.blockedRpcRequiredBytes == 0, "Early return left stale diagnostics"); e.output = saved;
        }
        Report.Add("Native actual EncodeNow: dynamic grow/shrink, fit-alone classification, equal-priority rotation, blocked FIFO RPC repeat retention and diagnostic reset PASS.");
    }
    static readonly uint[] Crc = Crc32Runtime.EnsureFastTable(null);
    static byte[] Header(byte[] payload, uint stream, uint frame)
    {
        var h = FrameHeader.CreateDefault(); h.Flags = FrameFlags.PayloadCrc32; h.StreamId = stream; h.FrameIndex = frame; h.PayloadSize = (ushort)payload.Length;
        h.PayloadCrc32 = Crc32Runtime.Compute(payload, 0, payload.Length, Crc); var header = new byte[FrameHeader.Size]; h.WriteTo(header, 0);
        return header;
    }
    static void Deliver(TSMPDecoder decoder, byte[] payload, uint stream, uint frame)
    {
        byte[] header = Header(payload, stream, frame);
        Set(decoder, "_payloadBytes", payload); Set(decoder, "_payloadDataBytes", payload.Length); Set(decoder, "_headerBytes", header);
        Set(decoder, "_requestRequirePayloadCrc32", true); Set(decoder, "_requestSafetyMode", 0); Set(decoder, "_requestUseHeaderLayout", false); Set(decoder, "_activeSlot", -1);
        decoder.lastHeaderValid = true; decoder.lastFrameValid = false; decoder.lastFrameIndex = frame; decoder.lastStreamId = stream; decoder.lastError = "";
        typeof(TSMPDecoder).GetMethod("CompletePayload", Flags).Invoke(decoder, null);
        Check(decoder.lastFrameValid && decoder.lastPayloadCrcVerified, "Protected delivery failed " + decoder.lastError);
    }
    static byte[] Build(RefreshRecoveryProbe source, EncoderNativeSendState state, Dictionary<Type, TransSyncMetadata.Cache> cache, List<EncoderNativeFrameBuilder.QueuedRpc> queue, double now, uint frame, float refresh, out int messages, out int rpcs)
    {
        byte[] payload = new byte[4096], encoded = null; int offset = 0, start = -1, variables = 0;
        Check(EncoderNativeFrameBuilder.BuildNetworkPayload(new List<TSMPNetworkBehaviour> { source }, queue, cache, ref payload, ref encoded,
            ref offset, ref start, ref variables, frame, 4096, out messages, out _, out rpcs, out int bytes, out string error, state, now, refresh, null, true), "Scheduler build failed " + error);
        if (messages > 0) { state.Commit(payload, now); if (rpcs > 0) EncoderNativeFrameBuilder.AdvanceQueuedRpcs(queue); }
        return encoded.Take(bytes).ToArray();
    }
    static void Refresh(string name, int dropFrames, bool held, bool restart)
    {
        var owner = new GameObject(name); var sender = owner.AddComponent<RefreshRecoveryProbe>(); var receiver = owner.AddComponent<RefreshRecoveryProbe>();
        var decoder = owner.AddComponent<TSMPDecoder>(); decoder.applyEveryFrame = false; decoder.debugLog = false;
        sender.networkId = receiver.networkId = 1;
        string[] fields = { "high", "medium", "slow" };
        decoder.bindingTargets = fields.Select(_ => (Component)receiver).ToArray(); decoder.bindingNetworkIds = new ushort[] { 1, 1, 1 };
        decoder.bindingVariableHashes = fields.Select(f => Hash(typeof(RefreshRecoveryProbe), f)).ToArray();
        decoder.bindingValueTypes = fields.Select(_ => (byte)NetworkFrameProtocol.ValueTypeRawBytes).ToArray(); decoder.bindingFieldNames = fields; decoder.bindingDirections = new int[3];
        var state = new EncoderNativeSendState(); var cache = new Dictionary<Type, TransSyncMetadata.Cache>();
        var queue = new List<EncoderNativeFrameBuilder.QueuedRpc>(); byte[] lastVisible = null; uint lastVisibleFrame = 0, lastVisibleStream = 101;
        int[] emitted = new int[3]; int empty = 0;
        double[] restored = { -1, -1, -1 }; int[] restoredFrame = { -1, -1, -1 }; uint stream = 101;
        try
        {
            for (int frame = 0; frame <= 240; frame++)
            {
                double now = frame / 30.0;
                if (frame == 15) foreach (string field in fields) Set(sender, field, Enumerable.Repeat((byte)42, 32).ToArray());
                if (frame == 15) queue.Add(new EncoderNativeFrameBuilder.QueuedRpc { NetworkId = 1, RpcHash = StableHash.Fnv1A32("ReceiveEvent"), Arguments = new object[] { "ReceiveEvent", 501 }, RepeatsRemaining = 4 });
                if (restart && frame == 120)
                {
                    Check(receiver.calls == (dropFrames >= 4 ? 0 : 1), "Old stream event baseline before restart");
                    state.Reset(); stream = 102;
                    foreach (string field in fields) Set(sender, field, Enumerable.Repeat((byte)77, 32).ToArray());
                    queue.Add(new EncoderNativeFrameBuilder.QueuedRpc { NetworkId = 1, RpcHash = StableHash.Fnv1A32("ReceiveEvent"), Arguments = new object[] { "ReceiveEvent", 501 }, RepeatsRemaining = 4 });
                }
                uint sourceFrame = (uint)(restart && frame >= 120 ? frame - 120 : frame);
                byte[] packet = Build(sender, state, cache, queue, now, sourceFrame, 1f, out int messages, out int rpcs);
                var sent = Values(packet, packet.Length); if (messages == 0) empty++;
                int mask = 0;
                for (int f = 0; f < fields.Length; f++) if (sent.ContainsKey(Hash(typeof(RefreshRecoveryProbe), fields[f]))) { emitted[f]++; mask |= 1 << f; }
                string packetPath = "", headerPath = "";
                if (messages > 0)
                {
                    string directory = Path.Combine("packets", name); Directory.CreateDirectory(Path.Combine(Root, directory));
                    packetPath = Path.Combine(directory, frame.ToString("D3") + ".bin"); headerPath = Path.Combine(directory, frame.ToString("D3") + ".header.bin");
                    File.WriteAllBytes(Path.Combine(Root, packetPath), packet); File.WriteAllBytes(Path.Combine(Root, headerPath), Header(packet, stream, sourceFrame));
                }
                if (frame == 1) Check(messages == 0, "Unchanged fields sent before refresh/interval");
                bool dropped = frame >= 15 && frame < 15 + dropFrames, hold = dropped && held;
                if (messages > 0 && !dropped) { Deliver(decoder, packet, stream, sourceFrame); lastVisible = packet; lastVisibleFrame = sourceFrame; lastVisibleStream = stream; }
                else if (hold && lastVisible != null) Deliver(decoder, lastVisible, lastVisibleStream, lastVisibleFrame);
                for (int f = 0; f < fields.Length; f++)
                {
                    byte[] value = (byte[])Get(receiver, fields[f]);
                    Check(value.Length == 32 && value.All(b => b == value[0]), "Partial/corrupt state applied");
                    Check(value[0] == 0 || value[0] == 42 || (restart && value[0] == 77), "Unexpected state applied");
                    if (value[0] == 42 && restored[f] < 0) { restored[f] = now; restoredFrame[f] = frame; }
                }
                Trace.Add(string.Join(",", name, frame, now.ToString("R", CultureInfo.InvariantCulture), stream, packet.Length, messages, dropped ? 1 : 0, hold ? 1 : 0, receiver.high[0], receiver.medium[0], receiver.slow[0], receiver.calls, sourceFrame, mask, rpcs, queue.Count, queue.Count > 0 ? queue[0].RepeatsRemaining : 0, packetPath, headerPath));
            }
            Check(restored.All(t => t >= 0), "Unchanged state never recovered");
            double[] expected = dropFrames < 45 ? new[] { 1.5, 1.5, 1.5 } : new[] { 2.5, 2.5, 3.0 };
            Check(restored.SequenceEqual(expected), "Unexpected first recovery times; scheduler cache/refresh/minimum interval mismatch");
            if (!restart) Check(emitted.SequenceEqual(new[] { 9, 9, 6 }), "Unexpected refresh emission cadence " + string.Join("/", emitted));
            Check(empty > 200, "Unchanged send-on-change fields emitted continuously");
            double end = (15 + dropFrames) / 30.0;
            Check(restored[0] <= end + 1.0 + 1.0 / 30.0 && restored[1] <= end + 1.0 + 1.0 / 30.0 && restored[2] <= end + 1.5 + 1.0 / 30.0, "Refresh eligible recovery bound exceeded");
            Check(receiver.calls == (dropFrames >= 4 ? 0 : 1) + (restart ? 1 : 0), "Finite repeats/dedup expectation failed: " + receiver.calls);
            if (restart) Check(fields.All(f => ((byte[])Get(receiver, f)).All(b => b == 77)), "Restart/latest unchanged state missing");
            Report.Add(name + ": lostFrames=" + dropFrames + "; restoredHigh/Medium/Slow=" + string.Join("/", restored.Select(t => t.ToString("F6", CultureInfo.InvariantCulture))) + "s; emitted=" + string.Join("/", emitted) + "; emptyFrames=" + empty + "; events=" + receiver.calls + "; expectedMissingEvent=" + (dropFrames >= 4 ? 1 : 0));
        }
        finally { Object.DestroyImmediate(owner); }
    }
    public static void Run()
    {
        Exception error = null;
        try
        {
            Begin(); var owner = new GameObject("Default refresh check"); var encoder = owner.AddComponent<TSMPEncoder>(); encoder.autoEncode = false;
            Check(encoder.transSyncRefreshInterval == 1f && encoder.transRpcRepeatFrames == 4, "Production defaults changed"); Object.DestroyImmediate(owner);
            Diagnostics(); Refresh("drop-0.1", 3, false, false); Refresh("drop-0.4", 12, false, false); Refresh("drop-1.5", 45, false, false);
            Refresh("hold-0.4", 12, true, false); Refresh("hold-1.5-restart", 45, true, true); Refresh("drop-0.1-restart-dedup", 3, false, true);
            Report.Add("Refresh uses monotonic simulated30Hz time passed to unchanged production scheduler/builder, default1s refresh, actual protectedCRC Decoder state/RPC application via injectedbytes. No GPU/video/network/live-client claim. Header stream/frame reader and duplicate suppression are bypassed; RPC stream dedup uses lastStreamId.");
        }
        catch (Exception failure) { error = failure; }
        Finish(error);
    }
#endif
#if UDONSHARP
    public static void RunVm()
    {
        Begin(); const string path = "Assets/Validation/BudgetRecovery/BudgetRecoveryProbe.asset";
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
        if (asset == null) { asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>(); asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/BudgetRecovery/Runtime/BudgetRecoveryProbe.cs"); AssetDatabase.CreateAsset(asset, path); }
        CompileClient();
        AssetDatabase.SaveAssets(); SessionState.SetBool("TSMP.BudgetRecovery", true); Attach(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Attach() { EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered; }
    static void CompileClient()
    {
        bool compileError = false; Application.LogCallback callback = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) compileError = true; };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); } finally { Application.logMessageReceived -= callback; }
        Check(!compileError, "Fresh client compile failed");
    }
    static string Digest(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.BudgetRecovery", false)) return;
        SessionState.SetBool("TSMP.BudgetRecovery", false); EditorApplication.delayCall += () => { Exception error = null; try { VerifyVm(); } catch (Exception failure) { error = failure; } Finish(error); };
    }
    sealed class Vm : IDisposable
    {
        readonly IUdonProgram program; readonly IUdonVM vm; public readonly UdonBehaviour Backing;
        public Vm(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path); asset.UpdateProgram(); program = asset.SerializedProgramAsset.RetrieveProgram(); Check(program != null && program.ByteCode.Length > 0, "Missing client bytecode");
            vm = UdonEditorManager.Instance.ConstructUdonVM(); vm.LoadProgram(program); Backing = new GameObject("Budget recovery VM").AddComponent<UdonBehaviour>();
            Check((bool)typeof(UdonBehaviour).GetMethod("ResolveUdonHeapReferences", Flags).Invoke(Backing, new object[] { program.SymbolTable, program.Heap }), "Heap unresolved");
            Set(Backing, "_program", program); Set(Backing, "_udonVM", vm); Set(Backing, "_udonManager", UdonManager.Instance);
            Set(Backing, "_isReady", true); Set(Backing, "_hasDoneStart", true);
            var events = (Dictionary<string, List<uint>>)Get(Backing, "_eventTable");
            foreach (string name in program.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { program.EntryPoints.GetAddressFromSymbol(name) };
            Report.Add("ClientVM=" + path + "; BytecodeBytes=" + program.ByteCode.Length + "; SHA256=" + Digest(program.ByteCode));
        }
        public void Put<T>(string name, T value) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Read<T>(string name) => (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name) { Backing.SendCustomEvent(name); Check(!(bool)Get(Backing, "_hasError"), "Udon event failed " + name); }
        public void Dispose() { Object.DestroyImmediate(Backing.gameObject); }
    }
    static Dictionary<uint, byte[]> SendVm(Vm e, bool output = true)
    {
        e.Call("ClearFrame"); uint before = e.Read<uint>("frameIndex"); e.Call("EncodeNow");
        Check(e.Read<uint>("frameIndex") == before + (output ? 1u : 0u), "VM outputsequence " + e.Read<string>("lastError"));
        Check(string.IsNullOrEmpty(e.Read<string>("lastError")), "VM encode error");
        return output ? Values(e.Read<byte[]>("_payloadBytes"), e.Read<int>("payloadBytes")) : new Dictionary<uint, byte[]>();
    }
    static void VerifyVm()
    {
        CompileClient();
        var programs = new List<string> { "path,target,playmode,source_sha256,bytecode_bytes,bytecode_sha256" };
        foreach (var asset in UdonSharpProgramAsset.GetAllUdonSharpPrograms().Where(a => AssetDatabase.GetAssetPath(a).Contains("com.kibalab.tsmp")).OrderBy(AssetDatabase.GetAssetPath))
        {
            var program = asset.SerializedProgramAsset.RetrieveProgram(); Check(program != null && program.ByteCode.Length > 0, "Missing product client program");
            string path = AssetDatabase.GetAssetPath(asset), source = AssetDatabase.GetAssetPath(asset.sourceCsScript);
            programs.Add(path + ",client," + Application.isPlaying + "," + Digest(File.ReadAllBytes(source)) + "," + program.ByteCode.Length + "," + Digest(program.ByteCode));
        }
        Check(programs.Count >= 17 && Application.isPlaying, "Expected at least16product client programs after Play entry");
        File.WriteAllLines(Path.Combine(Root, "client-programs.csv"), programs);
        using (var source = new Vm("Assets/Validation/BudgetRecovery/BudgetRecoveryProbe.asset"))
        using (var e = new Vm("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset"))
        {
            var output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32); output.Create();
            try
            {
                e.Put("output", output); e.Put("autoEncode", false); e.Put("debugLog", false); e.Put("useGpuLuma4", false); e.Put("useBlockSymbolTexture", false); e.Put("clearAfterEncode", false); e.Put("transSyncRefreshInterval", 0f); e.Put("transRpcRepeatFrames", 2); e.Call("_onEnable");
                string[] fields = { "big", "a", "b" }; var metadata = TransSyncMetadata.GetOrCreate(null, typeof(BudgetRecoveryProbe)).Fields;
                e.Put("bindingTargets", new Component[0]); e.Put("bindingUdonTargets", fields.Select(_ => source.Backing).ToArray()); e.Put("bindingNetworkIds", new ushort[] { 1, 1, 1 });
                e.Put("bindingVariableHashes", fields.Select(f => Hash(typeof(BudgetRecoveryProbe), f)).ToArray()); e.Put("bindingFieldNames", fields); e.Put("bindingValueTypes", fields.Select(f => (byte)metadata.Single(m => m.FieldInfo.Name == f).ValueType).ToArray());
                e.Put("bindingDirections", new int[3]); e.Put("bindingPriorities", new[] { 100, 0, 0 }); e.Put("bindingSendOnChange", new[] { true, false, false }); e.Put("bindingMinSendIntervals", new float[3]);
                source.Put("transRpcEncoder", e.Backing); e.Put("targetPayloadBytes", 29); uint first = SendVm(e).Keys.Single();
                Check(e.Read<int>("oversizedVariableCount") == 1 && e.Read<int>("deferredVariableCount") == 2, "VM oversized counts");
                Check(e.Read<bool>("lastDeferredVariableFitsBudget") && e.Read<int>("lastDeferredVariableRequiredBytes") == 29 && e.Read<string>("lastDeferredVariableName") != "big", "VM normal peer mislabeled");
                Check(first != SendVm(e).Keys.Single() && first == SendVm(e).Keys.Single(), "VM peer rotation");
                // Native EnabledBy metadata is baked into bindings by the normal binding builder. Remove peers from this test's explicit table instead.
                e.Put("bindingFieldNames", new[] { "big" }); e.Put("bindingVariableHashes", new[] { Hash(typeof(BudgetRecoveryProbe), "big") }); e.Put("bindingValueTypes", new[] { (byte)NetworkFrameProtocol.ValueTypeRawBytes }); e.Put("bindingUdonTargets", new[] { source.Backing }); e.Put("bindingNetworkIds", new ushort[] { 1 });
                SendVm(e, false); Check(e.Read<string>("lastDeferredVariableName") == "big" && e.Read<int>("lastDeferredVariableRequiredBytes") == 125 && !e.Read<bool>("lastDeferredVariableFitsBudget"), "VM oversized identity");
                source.Put("big", new byte[] { 7, 8, 9, 10 }); Check(SendVm(e).Single().Value.SequenceEqual(new byte[] { 7, 8, 9, 10 }), "VM dynamic shrink"); Check(e.Read<int>("deferredVariableCount") == 0 && e.Read<string>("lastDeferredVariableName") == "", "VM stale field");
                source.Put("big", Enumerable.Range(0, 100).Select(i => (byte)i).ToArray()); SendVm(e, false); Check(e.Read<int>("oversizedVariableCount") == 1, "VM dynamic grow");
                e.Put("targetPayloadBytes", 125); Check(SendVm(e).Single().Value.SequenceEqual(Enumerable.Range(0, 100).Select(i => (byte)i)), "VM budget expand");
                e.Put("targetPayloadBytes", 256); source.Call("QueueEvent"); Check(source.Read<bool>("queueAccepted"), "VM queue head"); e.Put("targetPayloadBytes", 29);
                for (int i = 0; i < 3; i++) { SendVm(e, false); Check(e.Read<int>("deferredRpcCount") == 1 && e.Read<int>("blockedRpcRequiredBytes") == 43 && e.Read<int>("blockedRpcNetworkId") == 1 && e.Read<uint>("blockedRpcHash") == 99u, "VM blocked head diagnostic"); Check(e.Read<int[]>("_pendingRpcRepeatsRemaining")[0] == 2, "VM blocked repeat consumed"); }
                e.Put("targetPayloadBytes", 256); SendVm(e); Check(e.Read<int[]>("_pendingRpcRepeatsRemaining")[0] == 1 && e.Read<int>("blockedRpcRequiredBytes") == 0, "VM RPC resume/reset"); SendVm(e); Check(e.Read<int>("_pendingRpcCount") == 0, "VM repeats drain");
                e.Put("targetPayloadBytes", 8); source.Put("big", new byte[101]); SendVm(e, false); e.Put<RenderTexture>("output", null); e.Call("EncodeNow");
                Check(e.Read<int>("deferredVariableCount") == 0 && e.Read<int>("deferredRpcCount") == 0 && e.Read<string>("lastDeferredVariableName") == "" && e.Read<int>("blockedRpcRequiredBytes") == 0, "VM stale diagnostics on early return");
                Report.Add("Actual freshly compiled client Encoder VM: diagnostic parity, dynamic arrays, rotation, retained FIFO repeats, budget expansion, reset PASS. Refresh simulation is separately Native-only.");
            }
            finally { output.Release(); Object.DestroyImmediate(output); }
        }
    }
#endif
}
#endif
