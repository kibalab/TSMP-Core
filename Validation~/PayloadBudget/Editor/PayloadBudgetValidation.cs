using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

public static class PayloadBudgetValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<string> Results = new List<string>();
    private static int assertions;
    private static bool failed;
    private static uint Hash(string field) => TransSyncMetadata.GetOrCreate(null, typeof(PayloadBudgetProbe)).Fields.Single(f => f.FieldInfo.Name == field).VariableHash;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Test("Budget normalization preserves capacity and protocol limits", Normalization);
#if !UDONSHARP
        Test("Zero target is byte-identical to frozen prior frame builder for 64 inputs", BaselineParity);
        Test("Native output budget, large-field deferral, fairness and latest value", NativeScheduling);
        Test("Native queued RPC survives budget reduction and resumes without consumed repeats", NativeRpc);
        Test("Native failed manual write rolls back offset and count", NativeManual);
#endif
        Finish();
        EditorApplication.Exit(failed ? 1 : 0);
    }
    private static void Normalization()
    {
        Check(NetworkPayloadBuffer.ResolveWriteLimit(0, 2048) == 2048, "Zero changed capacity");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(-1, 2048) == 2048, "Negative target differs across paths");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(1, 2048) == 8, "Tiny target did not include network header");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(29, 2048) == 29, "Target was ignored");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(4096, 512) == 512, "Target exceeded raster");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(int.MaxValue, int.MaxValue) == 65535, "Protocol limit exceeded");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(1, 0) == 0, "Empty capacity grew");
        Check(NetworkPayloadBuffer.ResolveWriteLimit(0, -1) == 0, "Negative capacity remained");
    }

    private static Dictionary<uint, byte[]> ReadValues(byte[] payload, int length)
    {
        Check(payload != null && length >= 8 && length <= payload.Length, "Invalid packet length");
        var result = new Dictionary<uint, byte[]>();
        int offset = 8;
        int messages = Binary.ReadUInt16LE(payload, 2);
        for (int m = 0; m < messages; m++)
        {
            int end = offset + 8 + Binary.ReadUInt16LE(payload, offset + 6);
            Check(end <= length && end > offset, "Message outside packet");
            if (payload[offset + 2] == NetworkFrameProtocol.MessageTypeVariableState)
            {
                int count = Binary.ReadUInt16LE(payload, offset + 8);
                int cursor = offset + 10;
                Check(count > 0, "Empty VariableState");
                for (int f = 0; f < count; f++)
                {
                    int bytes = Binary.ReadUInt16LE(payload, cursor + 5);
                    Check(cursor + 7 + bytes <= end, "Field outside message");
                    result.Add(Binary.ReadUInt32LE(payload, cursor), payload.Skip(cursor + 7).Take(bytes).ToArray());
                    cursor += 7 + bytes;
                }
                Check(cursor == end, "Stale bytes in message");
            }
            offset = end;
        }
        Check(offset == length, "Stale bytes in packet");
        return result;
    }
#if !UDONSHARP
    private sealed class Sender : IDisposable
    {
        private readonly GameObject owner = new GameObject("Budget encoder");
        private readonly GameObject source = new GameObject("Budget source");
        private readonly GameObject codec;
        private readonly RenderTexture output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
        public readonly TSMPEncoder Encoder;
        public readonly PayloadBudgetProbe Probe;
        public Sender()
        {
            codec = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
            Encoder = owner.AddComponent<TSMPEncoder>();
            Encoder.autoEncode = false;
            Encoder.debugLog = false;
            Encoder.useGpuLuma4 = false;
            Encoder.transSyncRefreshInterval = 0;
            Encoder.transRpcRepeatFrames = 2;
            Encoder.output = output;
            Encoder.selectedCodec = codec.GetComponent<TSMPCodec>();
            Probe = source.AddComponent<PayloadBudgetProbe>();
            Probe.networkId = 1;
            Probe.transRpcEncoder = Encoder;
            Encoder.networkBehaviours = new TSMPNetworkBehaviour[] { Probe };
            output.Create();
            Check(Encoder.targetPayloadBytes == 0, "New scenes changed default policy");
        }
        public List<EncoderNativeFrameBuilder.QueuedRpc> Queue => (List<EncoderNativeFrameBuilder.QueuedRpc>)typeof(TSMPEncoder).GetField("_queuedRpcs", Private).GetValue(Encoder);
        public Dictionary<uint, byte[]> Send(bool mustOutput = true)
        {
            uint before = Encoder.frameIndex;
            Encoder.EncodeNow();
            if (!mustOutput)
            {
                Check(Encoder.frameIndex == before, "Rejected/empty frame advanced sequence");
                return new Dictionary<uint, byte[]>();
            }
            Check(Encoder.frameIndex == before + 1 && string.IsNullOrEmpty(Encoder.lastError), "Encode failed: " + Encoder.lastError);
            Check(Encoder.payloadBytes <= Encoder.effectivePayloadBudgetBytes, "Target not enforced");
            var bytes = (byte[])typeof(TSMPEncoder).GetField("_encodedPayload", Private).GetValue(Encoder);
            return ReadValues(bytes, Encoder.payloadBytes);
        }
        public void Dispose()
        {
            Object.DestroyImmediate(owner); Object.DestroyImmediate(source); Object.DestroyImmediate(codec);
            output.Release(); Object.DestroyImmediate(output);
        }
    }
    private static void NativeScheduling()
    {
        using (var s = new Sender())
        {
            s.Encoder.maxPayloadBytes = 32;
            s.Encoder.targetPayloadBytes = 29;
            uint first = s.Send().Keys.Single();
            Check(s.Encoder.payloadBytes == 29 && s.Encoder.autoVariableCount == 1 && s.Encoder.deferredVariableCount == 2, "Unexpected budget output");
            Check(s.Encoder.maxPayloadBytes > 29 && s.Encoder.targetPayloadBytes == 29, "Allocation changed transmission target");
            Check(s.Probe.bigCommits == 0, "Deferred large field marked sent");
            uint second = s.Send().Keys.Single();
            Check(first != second && s.Send().Keys.Single() == first, "Equal-priority field starved");
            s.Probe.big[0] = 73;
            s.Encoder.targetPayloadBytes = 0;
            var values = s.Send();
            Check(values.Count == 3 && values[Hash("big")][0] == 73, "Latest deferred value lost");
            Check(s.Probe.bigCommits == 1 && s.Probe.committedFirstByte == 73, "Large field not committed after output");
            Check(s.Encoder.payloadBytes == 147, "Unexpected unlimited fixture size");
            s.Probe.changeBudgetOnCapture = true;
            s.Encoder.targetPayloadBytes = 29;
            Check(s.Send().Count == 1 && s.Encoder.effectivePayloadBudgetBytes == 29 && s.Encoder.targetPayloadBytes == 0,
                "Capture-time budget change affected the in-flight frame");
            s.Encoder.targetPayloadBytes = 1;
            s.Send(false);
            Check(s.Encoder.effectivePayloadBudgetBytes == 8, "Tiny target normalization differs in encoder");
            s.Encoder.targetPayloadBytes = 65535;
            Check(s.Send().Count == 2 && s.Encoder.effectivePayloadBudgetBytes == s.Encoder.usablePayloadBytes, "Raster capacity not enforced");
            Results.Add("MEASURED Native fixture target29=29B/1field; unlimited=147B/3fields; fairness=alternating; deferred latest value=73");
        }
    }
    private static void NativeRpc()
    {
        using (var s = new Sender())
        {
            s.Encoder.targetPayloadBytes = 256;
            Check(s.Encoder.QueueTransRpc(1, 99, "BudgetEvent", 123), "Queue failed");
            s.Encoder.targetPayloadBytes = 29;
            Check(!s.Encoder.QueueTransRpc(1, 99, "BudgetEvent", 124), "Oversized new RPC accepted");
            for (int i = 0; i < 3; i++)
            {
                Check(s.Send().Count == 1, "Deferred RPC blocked eligible state");
                Check(s.Encoder.deferredRpcCount == 1 && s.Encoder.rpcMessageCount == 0, "RPC deferral missing");
                Check(s.Queue.Count == 1 && s.Queue[0].RepeatsRemaining == 2, "Deferred repeat consumed");
            }
            s.Encoder.targetPayloadBytes = 256;
            s.Send();
            Check(s.Encoder.rpcMessageCount == 1 && s.Queue.Count == 1 && s.Queue[0].RepeatsRemaining == 1, "RPC did not resume");
            s.Send();
            Check(s.Encoder.rpcMessageCount == 1 && s.Queue.Count == 0, "RPC repeat budget changed");
        }
    }
    private static void NativeManual()
    {
        using (var s = new Sender())
        {
            s.Probe.bigEnabled = s.Probe.aEnabled = s.Probe.bEnabled = false;
            s.Probe.manualWrite = true;
            s.Encoder.targetPayloadBytes = 29;
            s.Send(false);
            Check(!s.Probe.manualWritten && s.Encoder.payloadBytes == 0, "Oversized manual write escaped budget");
            Check((int)typeof(TSMPEncoder).GetField("_payloadOffset", Private).GetValue(s.Encoder) == 8, "Failed manual write advanced offset");
            s.Encoder.targetPayloadBytes = 256;
            var values = s.Send();
            Check(s.Probe.manualWritten && values.Count == 1 && values[1234u].SequenceEqual(s.Probe.big), "Failed manual write poisoned retry");
        }
    }
    private static void BaselineParity()
    {
        using (var s = new Sender())
        {
            byte[] actual = null, actualCopy = null, baseline = null, baselineCopy = null;
            int ao = 0, ast = -1, ac = 0, bo = 0, bst = -1, bc = 0;
            var actualState = new EncoderNativeSendState();
            var baselineState = new EncoderNativeSendState();
            var cache = new Dictionary<Type, TransSyncMetadata.Cache>();
            var sources = new List<TSMPNetworkBehaviour> { s.Probe };
            var rpcs = new List<EncoderNativeFrameBuilder.QueuedRpc>();
            var baselineRpcs = new List<PayloadBudgetBaselineFrameBuilder.QueuedRpc>();
            for (uint frame = 0; frame < 64; frame++)
            {
                s.Probe.a = (int)frame * 71;
                s.Probe.big = Enumerable.Range(0, (int)(frame % 4) * 127).Select(i => (byte)(i * 17 + frame)).ToArray();
                rpcs.Clear(); baselineRpcs.Clear();
                if (frame % 3 == 0)
                {
                    var args = new object[] { "예산😀", (int)frame };
                    rpcs.Add(new EncoderNativeFrameBuilder.QueuedRpc { NetworkId = 1, RpcHash = 123, Arguments = args, RepeatsRemaining = 1 });
                    baselineRpcs.Add(new PayloadBudgetBaselineFrameBuilder.QueuedRpc { NetworkId = 1, RpcHash = 123, Arguments = args, RepeatsRemaining = 1 });
                }
                Check(EncoderNativeFrameBuilder.BuildNetworkPayload(sources, rpcs, cache, ref actual, ref actualCopy, ref ao, ref ast, ref ac, frame,
                    NetworkPayloadBuffer.ResolveWriteLimit(0, 2048), out int am, out int av, out int ar, out int ab, out string ae, actualState, frame, 0), ae);
                Check(PayloadBudgetBaselineFrameBuilder.BuildNetworkPayload(sources, baselineRpcs, cache, ref baseline, ref baselineCopy, ref bo, ref bst, ref bc, frame,
                    2048, out int bm, out int bv, out int br, out int bb, out string be, baselineState, frame, 0), be);
                Check(ab == bb && am == bm && av == bv && ar == br && actualCopy.SequenceEqual(baselineCopy), "Default bytes differ at frame " + frame);
                actualState.Commit(actual, frame); baselineState.Commit(baseline, frame);
            }
        }
    }
#endif

#if UDONSHARP
    public static void RunVm()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const string path = "Assets/Validation/PayloadBudget/PayloadBudgetProbe.asset";
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/PayloadBudget/Runtime/PayloadBudgetProbe.cs");
            AssetDatabase.CreateAsset(asset, path);
        }
        bool compileError = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) compileError = true;
        };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= callback; }
        if (compileError)
        {
            failed = true; Results.Add("FAIL Fresh Udon client compilation"); Finish(); EditorApplication.Exit(1); return;
        }
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.PayloadBudget.Vm", true);
        AttachVm();
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void AttachVm()
    {
        if (!SessionState.GetBool("TSMP.PayloadBudget.Vm", false)) return;
        EditorApplication.playModeStateChanged -= VmEnteredPlay;
        EditorApplication.playModeStateChanged += VmEnteredPlay;
    }
    private static void VmEnteredPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool("TSMP.PayloadBudget.Vm", false);
        EditorApplication.playModeStateChanged -= VmEnteredPlay;
        EditorApplication.delayCall += () =>
        {
            Test("Budget normalization in SDK assembly", Normalization);
            Test("Actual Udon Encoder budget, fairness, RPC deferral, recovery and manual offsets", VerifyVm);
            Finish(); EditorApplication.Exit(failed ? 1 : 0);
        };
    }
    private sealed class VmProgram : IDisposable
    {
        private readonly IUdonProgram program;
        private readonly IUdonVM vm;
        public readonly UdonBehaviour Backing;
        public VmProgram(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            asset.UpdateProgram();
            program = asset.GetRealProgram();
            Check(program != null && program.ByteCode.Length > 0, "Missing bytecode: " + path);
            vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            Backing = new GameObject(asset.name + " budget VM").AddComponent<UdonBehaviour>();
            Check((bool)typeof(UdonBehaviour).GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { program.SymbolTable, program.Heap }), "Heap unresolved");
            typeof(UdonBehaviour).GetField("_program", Private).SetValue(Backing, program);
            typeof(UdonBehaviour).GetField("_udonVM", Private).SetValue(Backing, vm);
            typeof(UdonBehaviour).GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            if (path.Contains("TSMPEncoder.asset")) Results.Add("EncoderBytecodeBytes=" + program.ByteCode.Length);
        }
        public void Set<T>(string name, T value) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name));
            Check(vm.Interpret() == 0, "Udon event failed: " + name);
        }
        public void Dispose() => Object.DestroyImmediate(Backing.gameObject);
    }
    private static void ConfigureVm(VmProgram encoder, VmProgram source, string[] fields)
    {
        var metadata = TransSyncMetadata.GetOrCreate(null, typeof(PayloadBudgetProbe)).Fields;
        encoder.Set("bindingTargets", new Component[0]);
        encoder.Set("bindingUdonTargets", fields.Select(unused => source.Backing).ToArray());
        encoder.Set("bindingNetworkIds", fields.Select(unused => (ushort)1).ToArray());
        encoder.Set("bindingVariableHashes", fields.Select(Hash).ToArray());
        encoder.Set("bindingFieldNames", fields);
        encoder.Set("bindingValueTypes", fields.Select(name => (byte)metadata.Single(f => f.FieldInfo.Name == name).ValueType).ToArray());
        encoder.Set("bindingDirections", new int[fields.Length]);
        encoder.Set("bindingPriorities", fields.Select(name => metadata.Single(f => f.FieldInfo.Name == name).Sync.Priority).ToArray());
        encoder.Set("bindingSendOnChange", fields.Select(name => metadata.Single(f => f.FieldInfo.Name == name).Sync.SendOnChange).ToArray());
        encoder.Set("bindingMinSendIntervals", new float[fields.Length]);
        encoder.Set("bindingSentEvents", fields.Select(name => metadata.Single(f => f.FieldInfo.Name == name).Sync.SentEvent).ToArray());
    }
    private static Dictionary<uint, byte[]> SendVm(VmProgram encoder, bool mustOutput = true)
    {
        encoder.Call("ClearFrame");
        uint before = encoder.Get<uint>("frameIndex");
        encoder.Call("EncodeNow");
        if (!mustOutput)
        {
            Check(encoder.Get<uint>("frameIndex") == before, "Empty frame advanced");
            return new Dictionary<uint, byte[]>();
        }
        Check(encoder.Get<uint>("frameIndex") == before + 1 && string.IsNullOrEmpty(encoder.Get<string>("lastError")), "Udon encode failed: " + encoder.Get<string>("lastError"));
        int bytes = encoder.Get<int>("payloadBytes");
        Check(bytes <= encoder.Get<int>("effectivePayloadBudgetBytes"), "Udon target exceeded");
        return ReadValues(encoder.Get<byte[]>("_payloadBytes"), bytes);
    }
    private static void VerifyVm()
    {
        using (var source = new VmProgram("Assets/Validation/PayloadBudget/PayloadBudgetProbe.asset"))
        using (var encoder = new VmProgram("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset"))
        {
            var output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
            output.Create();
            try
            {
                Check(encoder.Get<int>("targetPayloadBytes") == 0, "Udon default policy changed");
                encoder.Set("output", output);
                encoder.Set("autoEncode", false);
                encoder.Set("clearAfterEncode", false);
                encoder.Set("debugLog", false);
                encoder.Set("useGpuLuma4", false);
                encoder.Set("useBlockSymbolTexture", false);
                encoder.Set("transSyncRefreshInterval", 0f);
                encoder.Set("transRpcRepeatFrames", 2);
                encoder.Set("codecId", 0);
                encoder.Set("maxPayloadBytes", 32);
                encoder.Call("_onEnable");
                ConfigureVm(encoder, source, new[] { "big", "a", "b" });
                source.Set("transRpcEncoder", encoder.Backing);
                encoder.Set("targetPayloadBytes", 29);
                uint first = SendVm(encoder).Keys.Single();
                Check(encoder.Get<int>("payloadBytes") == 29 && encoder.Get<int>("deferredVariableCount") == 2, "Udon budget scheduler mismatch");
                Check(encoder.Get<int>("maxPayloadBytes") > 29 && encoder.Get<int>("targetPayloadBytes") == 29, "Udon allocation changed target");
                uint second = SendVm(encoder).Keys.Single();
                Check(first != second && SendVm(encoder).Keys.Single() == first, "Udon fairness failed");
                Check(source.Get<int>("bigCommits") == 0, "Udon deferred field committed");
                source.Get<byte[]>("big")[0] = 73;
                encoder.Set("targetPayloadBytes", 0);
                var values = SendVm(encoder);
                Check(values.Count == 3 && values[Hash("big")][0] == 73 && source.Get<int>("bigCommits") == 1, "Udon latest deferred value lost");
                Check(encoder.Get<int>("payloadBytes") == 147, "Unexpected Udon unlimited fixture size");
                source.Set("changeBudgetOnCapture", true);
                encoder.Set("targetPayloadBytes", 29);
                Check(SendVm(encoder).Count == 1 && encoder.Get<int>("effectivePayloadBudgetBytes") == 29 && encoder.Get<int>("targetPayloadBytes") == 0,
                    "Udon capture-time budget change affected the in-flight frame");
                encoder.Set("targetPayloadBytes", 1);
                SendVm(encoder, false);
                Check(encoder.Get<int>("effectivePayloadBudgetBytes") == 8, "Udon tiny target differs");
                encoder.Set("targetPayloadBytes", 256);
                source.Call("QueueEvent");
                Check(source.Get<bool>("queueAccepted"), "Udon queue failed");
                encoder.Set("targetPayloadBytes", 29);
                source.Call("QueueEvent");
                Check(!source.Get<bool>("queueAccepted"), "Oversized Udon event queued");
                for (int i = 0; i < 3; i++)
                {
                    Check(SendVm(encoder).Count == 1, "Deferred Udon RPC blocked state");
                    Check(encoder.Get<int>("deferredRpcCount") == 1 && encoder.Get<int>("rpcMessageCount") == 0, "Udon RPC deferral missing");
                    Check(encoder.Get<int>("_pendingRpcCount") == 1 && encoder.Get<int[]>("_pendingRpcRepeatsRemaining")[0] == 2, "Udon deferred repeat consumed");
                }
                encoder.Set("targetPayloadBytes", 256);
                SendVm(encoder);
                Check(encoder.Get<int>("rpcMessageCount") == 1 && encoder.Get<int[]>("_pendingRpcRepeatsRemaining")[0] == 1, "Udon RPC did not resume");
                SendVm(encoder);
                Check(encoder.Get<int>("_pendingRpcCount") == 0, "Udon repeat did not finish");
                encoder.Set("targetPayloadBytes", 29);
                source.Call("WriteManualRpc");
                Check(source.Get<bool>("manualRpcBegan") && !source.Get<bool>("manualRpcWritten"), "Udon manual budget not enforced");
                Check(encoder.Get<int>("_payloadOffset") == 21 && encoder.Get<int>("_currentRpcArgumentCount") == 0, "Udon failed argument advanced offsets/count");
                encoder.Call("CancelCurrentMessage");
                Check(encoder.Get<int>("_payloadOffset") == 8 && encoder.Get<int>("_currentMessageStartOffset") == -1, "Udon cancel did not roll back");
                encoder.Set("targetPayloadBytes", 256);
                Check(SendVm(encoder).Count == 2, "Udon manual failure poisoned later state");
                Results.Add("MEASURED Udon fixture target29=29B/1field; unlimited=147B/3fields; fairness=alternating; pending RPC survived3limited outputs then2repeats");
            }
            finally { output.Release(); Object.DestroyImmediate(output); }
        }
    }
#endif

    private static void Test(string name, Action action)
    {
        try { action(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Finish()
    {
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), (failed ? "FAIL" : "PASS") +
            "\nUnity=" + Application.unityVersion + "\nAssertions=" + assertions + "\n" + string.Join("\n", Results));
    }
}
