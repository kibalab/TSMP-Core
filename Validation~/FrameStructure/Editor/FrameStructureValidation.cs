#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

public static class FrameStructureValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Root => Environment.GetEnvironmentVariable("TSMP_STRUCTURE_RESULTS");
    static readonly List<string> Results = new List<string>();
    static readonly List<string> Samples = new List<string>();
    static readonly List<string> Timings = new List<string>();
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    static void Finish(Exception error)
    {
        Results.Insert(0, error == null ? "PASS" : "FAIL");
        Results.Add("Assertions=" + checks);
        if (error != null) Results.Add(error.ToString());
        File.WriteAllLines(Path.Combine(Root, "result.txt"), Results);
        File.WriteAllLines(Path.Combine(Root, "timings.csv"), Timings);
        File.WriteAllLines(Path.Combine(Root, "samples.csv"), Samples);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Begin(string mode)
    {
        if (string.IsNullOrWhiteSpace(Root)) throw new InvalidOperationException("TSMP_STRUCTURE_RESULTS required");
        Directory.CreateDirectory(Root);
        Check(!File.Exists(Path.Combine(Root, "result.txt")), "Use fresh results");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RecordEnvironment(mode);
    }
    static void RecordEnvironment(string mode)
    {
        Results.Add("Unity=" + Application.unityVersion + "; Mode=" + mode);
        Results.Add("Protected payload completion and actual state/RPC dispatch. Injected readback bytes; no GPU/raster/transport/client measurement.");
        Samples.Add("mode,bytes,messages,sample,milliseconds");
        Timings.Add("mode,bytes,messages,samples,mean_ms,p95_ms,max_ms");
    }
    // 0 clean, 1 outer trailing, 2 truncated next message, 3 truncated next
    // variable, 4 wrong CRC, 5 truncated RPC argument, 6 RPC trailing,
    // 7 variable trailing, 8 unsupported next message type.
    static byte[] Packet(int kind, int value = 42, int eventId = 601)
    {
        var bytes = new byte[512];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, (uint)eventId), start = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, 0);
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, StableHash.Fnv1A32("receivedValue"), NetworkFrameProtocol.ValueTypeInt32, value);
        if (kind == 3 || kind == 7) { bytes[cursor++] = 1; bytes[cursor++] = 2; bytes[cursor++] = 3; }
        Check(NetworkFrameWriter.EndVariableState(bytes, start, cursor, kind == 3 ? 2 : 1), "State fixture writer failed");
        start = cursor;
        string method = nameof(FrameStructureProbe.ToggleProbe);
        cursor = NetworkFrameWriter.WriteRpcCall(bytes, cursor, 1, 0, StableHash.Fnv1A32(method), new object[] { method, eventId }, out int failed, out int error);
        Check(cursor > start, "RPC fixture writer failed: " + failed + "/" + error);
        if (kind == 5) bytes[start + NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.RpcCallArgumentCountOffset]++;
        if (kind == 6)
        {
            bytes[cursor++] = 9;
            Binary.WriteUInt16LE(bytes, start + NetworkFrameProtocol.MessageBodyLengthOffset,
                (ushort)(cursor - start - NetworkFrameProtocol.MessageHeaderBytes));
        }
        Check(NetworkFrameWriter.EndNetworkFrame(bytes, 0, kind == 2 || kind == 8 ? 3 : 2), "Network fixture writer failed");
        if (kind == 1) bytes[cursor++] = 123;
        if (kind == 2) { bytes[cursor++] = 1; bytes[cursor++] = 2; bytes[cursor++] = 3; }
        if (kind == 8)
        {
            bytes[cursor + NetworkFrameProtocol.MessageTypeOffset] = 255;
            cursor += NetworkFrameProtocol.MessageHeaderBytes;
        }
        Array.Resize(ref bytes, cursor);
        return bytes;
    }
    static byte[] Header(byte[] payload, uint frame, bool badCrc)
    {
        var header = FrameHeader.CreateDefault(); header.Flags = FrameFlags.PayloadCrc32;
        header.StreamId = 77; header.FrameIndex = frame; header.PayloadSize = (ushort)payload.Length;
        header.PayloadCrc32 = Crc32Runtime.Compute(payload, 0, payload.Length, Crc32Runtime.EnsureFastTable(null));
        if (badCrc) header.PayloadCrc32 ^= 1u;
        var bytes = new byte[FrameHeader.Size]; header.WriteTo(bytes, 0);
        Check(FrameHeader.TryRead(bytes, 0, out _), "Header CRC invalid");
        return bytes;
    }
    // Independent bounded parser for the format, not production read helpers.
    static bool Reference(byte[] bytes, int length)
    {
        if (bytes == null || length < 8 || length > bytes.Length || bytes[0] != NetworkFrameProtocol.NetworkVersionMajor) return false;
        Func<int, int> u16 = at => bytes[at] | bytes[at + 1] << 8;
        int messages = u16(NetworkFrameProtocol.NetworkMessageCountOffset), cursor = 8;
        for (int message = 0; message < messages; message++)
        {
            if (cursor > length - NetworkFrameProtocol.MessageHeaderBytes) return false;
            int type = bytes[cursor + NetworkFrameProtocol.MessageTypeOffset];
            int body = cursor + NetworkFrameProtocol.MessageHeaderBytes;
            int end = body + u16(cursor + NetworkFrameProtocol.MessageBodyLengthOffset);
            if (end > length) return false;
            int count, head;
            if (type == NetworkFrameProtocol.MessageTypeVariableState)
            {
                if (body > end - 2) return false;
                count = u16(body); cursor = body + 2; head = NetworkFrameProtocol.VariableValueHeaderBytes;
            }
            else if (type == NetworkFrameProtocol.MessageTypeRpcCall)
            {
                if (body > end - NetworkFrameProtocol.RpcCallBodyHeaderBytes) return false;
                count = bytes[body + NetworkFrameProtocol.RpcCallArgumentCountOffset]; cursor = body + NetworkFrameProtocol.RpcCallBodyHeaderBytes;
                head = NetworkFrameProtocol.RpcArgumentHeaderBytes;
            }
            else return false;
            for (int item = 0; item < count; item++)
            {
                if (cursor > end - head) return false;
                int typeAt = cursor + (type == NetworkFrameProtocol.MessageTypeVariableState ? NetworkFrameProtocol.VariableValueTypeOffset : NetworkFrameProtocol.RpcArgumentTypeOffset);
                int lengthAt = cursor + (type == NetworkFrameProtocol.MessageTypeVariableState ? NetworkFrameProtocol.VariableValueLengthOffset : NetworkFrameProtocol.RpcArgumentLengthOffset);
                // Same value-type contract, independently read framing offsets.
                int valueLength = u16(lengthAt);
                if (!NetworkValueCodec.IsValueLengthValid(bytes[typeAt], valueLength)) return false;
                cursor += head + valueLength;
                if (cursor > end) return false;
            }
            if (cursor != end) return false;
        }
        return cursor == length;
    }
    static List<byte[]> Corpus()
    {
        var corpus = new List<byte[]>();
        for (int kind = 0; kind <= 8; kind++) corpus.Add(Packet(kind));
        var empty = new byte[8]; NetworkFrameWriter.BeginNetworkFrame(empty, 0, 0);
        NetworkFrameWriter.EndNetworkFrame(empty, 0, 0); corpus.Add(empty);
        var excessiveCount = (byte[])empty.Clone();
        Binary.WriteUInt16LE(excessiveCount, NetworkFrameProtocol.NetworkMessageCountOffset, ushort.MaxValue); corpus.Add(excessiveCount);
        var zeroArguments = new byte[64]; int rpcStart = NetworkFrameWriter.BeginNetworkFrame(zeroArguments, 0, 0);
        int rpcEnd = NetworkFrameWriter.WriteRpcCall(zeroArguments, rpcStart, 1, 0, 123u, new object[0], out _, out _);
        Check(rpcEnd > rpcStart && NetworkFrameWriter.EndNetworkFrame(zeroArguments, 0, 1), "Zero argument RPC fixture failed");
        Array.Resize(ref zeroArguments, rpcEnd); corpus.Add(zeroArguments);
        var random = new System.Random(20261031); byte[] clean = Packet(0);
        for (int i = 0; i < 1000; i++)
        {
            byte[] mutation = (byte[])clean.Clone();
            if (i % 3 == 0) Array.Resize(ref mutation, random.Next(clean.Length + 1));
            else mutation[random.Next(mutation.Length)] ^= (byte)(1 << random.Next(8));
            corpus.Add(mutation);
        }
        return corpus;
    }
    static void PureChecks()
    {
        foreach (byte[] bytes in Corpus())
        {
            bool expected = Reference(bytes, bytes.Length);
            bool valid = NetworkFrameReader.TryValidateNetworkFrame(bytes, bytes.Length, out _, out _);
            Check(valid == expected, "Framing/reference mismatch");
        }
        byte[] clean = Packet(0), dirty = new byte[clean.Length + 55]; Array.Copy(clean, dirty, clean.Length);
        Check(NetworkFrameReader.TryValidateNetworkFrame(dirty, clean.Length, out _, out _), "Dirty capacity tail changed logical framing");
        Check(!NetworkFrameReader.TryValidateNetworkFrame(dirty, dirty.Length, out _, out _), "Logical trailing bytes accepted");
        Check(!NetworkFrameReader.TryValidateNetworkFrame(null, 8, out _, out _), "Null accepted");
        Check(!NetworkFrameReader.TryValidateNetworkFrame(clean, -1, out _, out _), "Negative length accepted");
        Check(!NetworkFrameReader.TryValidateNetworkFrame(clean, int.MaxValue, out _, out _), "Overflow length accepted");
        for (int i = 0; i < 10; i++) NetworkFrameReader.TryValidateNetworkFrame(clean, clean.Length, out _, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) NetworkFrameReader.TryValidateNetworkFrame(clean, clean.Length, out _, out _);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, "Preflight allocated warmed managed bytes: " + allocated);
        Results.Add("PASS independent framing corpus=1012 including zero-message/max-count/zero-argument RPC, logical lengths/dirty tails, warmed Native preflight allocations=0 B/1000 calls.");
    }
#if !UDONSHARP
    static void Set(TSMPDecoder decoder, string name, object value) => typeof(TSMPDecoder).GetField(name, Private).SetValue(decoder, value);
    static void Deliver(TSMPDecoder decoder, byte[] payload, uint frame, int safety, bool badCrc)
    {
        Set(decoder, "_payloadBytes", payload); Set(decoder, "_payloadDataBytes", payload.Length); Set(decoder, "_headerBytes", Header(payload, frame, badCrc));
        Set(decoder, "_requestRequirePayloadCrc32", true); Set(decoder, "_requestSafetyMode", safety); Set(decoder, "_requestUseHeaderLayout", false);
        Set(decoder, "_activeSlot", -1); decoder.lastHeaderValid = true; decoder.lastFrameValid = false;
        decoder.lastFrameIndex = frame; decoder.lastStreamId = 77; decoder.lastError = "";
        typeof(TSMPDecoder).GetMethod("CompletePayload", Private).Invoke(decoder, null);
    }
    public static void Run()
    {
        Exception failure = null;
        try
        {
            Begin("Native"); PureChecks();
            foreach (int safety in new[] { 0, 3 }) foreach (int kind in Enumerable.Range(0, 9))
            {
                var owner = new GameObject("Structure regression");
                try
                {
                    var decoder = owner.AddComponent<TSMPDecoder>(); decoder.applyEveryFrame = false; decoder.debugLog = false;
                    var target = owner.AddComponent<FrameStructureProbe>();
                    decoder.bindingTargets = new Component[] { target }; decoder.bindingNetworkIds = new ushort[] { 1 };
                    decoder.bindingVariableHashes = new[] { StableHash.Fnv1A32("receivedValue") };
                    decoder.bindingValueTypes = new byte[] { (byte)NetworkFrameProtocol.ValueTypeInt32 }; decoder.bindingFieldNames = new[] { "receivedValue" }; decoder.bindingDirections = new int[1];
                    Deliver(decoder, Packet(kind), 600, safety, kind == 4);
                    bool clean = kind == 0;
                    Check(decoder.lastFrameValid == clean, "Frame validity differs kind=" + kind + " safety=" + safety + ": " + decoder.lastError);
                    bool applies = clean && safety == 0;
                    Check(target.receivedValue == (applies ? 42 : -7) && target.toggleCalls == (applies ? 1 : 0), "Rejected/parse-only payload had effects");
                    if (!clean && safety == 0)
                    {
                        Check(typeof(TSMPDecoder).GetField("_cachedBindingComponentTargets", Private).GetValue(decoder) == null, "Malformed payload built binding cache");
                        Check(typeof(TSMPDecoder).GetField("_recentRpcEventIds", Private).GetValue(decoder) == null, "Malformed payload changed dedup cache");
                        Deliver(decoder, Packet(0, 43), 601, safety, false);
                        Check(decoder.lastFrameValid && target.receivedValue == 43 && target.toggleCalls == 1 && decoder.skippedDuplicateRpcCount == 0, "Intact retransmission was polluted");
                        Deliver(decoder, Packet(0, 44), 602, safety, false);
                        Check(decoder.lastFrameValid && target.receivedValue == 44 && target.toggleCalls == 1 && decoder.skippedDuplicateRpcCount == 1, "RPC duplicate handling changed");
                    }
                }
                finally { Object.DestroyImmediate(owner); }
            }
            Results.Add("PASS actual Native protected completion, application/parse-only, malformed outer/message/variable/RPC bodies, CRC-negative control, untouched caches, intact retransmission and RPC dedup.");
        }
        catch (Exception error) { failure = error; }
        Finish(failure);
    }
#endif
#if UDONSHARP
    const string ProbePath = "Assets/Validation/FrameStructure/FrameStructureProbe.asset";
    sealed class Program : IDisposable
    {
        readonly IUdonProgram code;
        readonly IUdonVM vm;
        public readonly UdonBehaviour Backing;
        public Program(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path); asset.UpdateProgram(); code = asset.SerializedProgramAsset.RetrieveProgram();
            Check(code != null && code.ByteCode.Length > 0, "Missing client bytecode " + path);
            vm = UdonEditorManager.Instance.ConstructUdonVM(); vm.LoadProgram(code);
            Backing = new GameObject(asset.name + " structure VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { code.SymbolTable, code.Heap }), "Heap unresolved");
            type.GetField("_program", Private).SetValue(Backing, code); type.GetField("_udonVM", Private).SetValue(Backing, vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true); type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in code.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { code.EntryPoints.GetAddressFromSymbol(name) };
            if (path.Contains("TSMPDecoder")) Results.Add("DecoderClientBytecodeBytes=" + code.ByteCode.Length);
        }
        public void Set<T>(string name, T value) => code.Heap.SetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)code.Heap.GetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM failure " + name);
        }
        public double TimedCall(string name)
        {
            long before = Stopwatch.GetTimestamp(); Backing.SendCustomEvent(name); long after = Stopwatch.GetTimestamp();
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM failure " + name);
            return (after - before) * 1000.0 / Stopwatch.Frequency;
        }
        public void Dispose() => Object.DestroyImmediate(Backing.gameObject);
    }
    static void Deliver(Program decoder, byte[] payload, uint frame, int safety, bool badCrc)
    {
        int padded = (payload.Length + 3) / 4 * 4; var raw = new byte[padded]; Array.Copy(payload, raw, payload.Length);
        decoder.Set("_slotHead", 0); decoder.Set("pendingDecodeCount", 1); decoder.Set("_slotStates", new[] { 2, 0 });
        decoder.Set("_slotStages", new[] { 2, 0 }); decoder.Set("_slotReadbackBytes", new[] { raw, (byte[])null });
        decoder.Set("_slotHeaders", new[] { Header(payload, frame, badCrc), (byte[])null });
        decoder.Set("_slotPayloads", new[] { new byte[payload.Length], (byte[])null });
        decoder.Set("_slotPayloadCounts", new[] { payload.Length, 0 }); decoder.Set("_slotReadCounts", new[] { padded / 4, 0 });
        decoder.Set("_slotRequirePayloadCrc32", new[] { true, false }); decoder.Set("_slotSafetyModes", new[] { safety, 0 });
        decoder.Set("_slotHeaderValid", new[] { true, false }); decoder.Set("_slotStreamIds", new uint[] { 77, 0 });
        decoder.Set("_slotFrameIndices", new[] { frame, 0u }); decoder.Set("lastFrameValid", false); decoder.Call("_update");
    }
    public static void RunVm()
    {
        try
        {
            Begin("Compile before entering Play Mode");
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbePath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/FrameStructure/Runtime/FrameStructureProbe.cs");
                AssetDatabase.CreateAsset(asset, ProbePath);
            }
            CompileClient(); AssetDatabase.SaveAssets();
            SessionState.SetBool("TSMP.FrameStructureVm", true); AttachVm(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }
    static void CompileClient()
    {
        bool failed = false; Application.LogCallback listener = (text, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Full Udon client compile failed");
    }
    [InitializeOnLoadMethod] static void AttachVm()
    {
        EditorApplication.playModeStateChanged -= EnteredVm; EditorApplication.playModeStateChanged += EnteredVm;
    }
    static void EnteredVm(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.FrameStructureVm", false)) return;
        SessionState.SetBool("TSMP.FrameStructureVm", false); EditorApplication.delayCall += RunVmAfterPlay;
    }
    static void RunVmAfterPlay()
    {
        Exception failure = null;
        try
        {
            Results.Clear(); Samples.Clear(); Timings.Clear(); checks = 0;
            RecordEnvironment("Compiled Udon client in SDK editor VM, after Play Mode entry");
            CompileClient();
            PureChecks();
            using (var probe = new Program(ProbePath))
            {
                foreach (byte[] bytes in Corpus())
                {
                    probe.Set("input", bytes); probe.Set("inputLength", bytes.Length); probe.Call("ValidateStructure");
                    Check(probe.Get<bool>("valid") == Reference(bytes, bytes.Length), "Udon/reference framing mismatch");
                }
                Measure(probe);
            }
            foreach (int safety in new[] { 0, 3 }) foreach (int kind in Enumerable.Range(0, 9))
            using (var target = new Program(ProbePath)) using (var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset"))
            {
                decoder.Set("applyEveryFrame", false); decoder.Set("debugLog", false); decoder.Set("_crc32Table", Crc32Runtime.EnsureFastTable(null));
                decoder.Set("bindingTargets", new Component[0]); decoder.Set("bindingUdonTargets", new[] { target.Backing });
                decoder.Set("bindingNetworkIds", new ushort[] { 1 }); decoder.Set("bindingVariableHashes", new[] { StableHash.Fnv1A32("receivedValue") });
                decoder.Set("bindingValueTypes", new byte[] { (byte)NetworkFrameProtocol.ValueTypeInt32 });
                decoder.Set("bindingFieldNames", new[] { "receivedValue" }); decoder.Set("bindingDirections", new int[1]);
                Deliver(decoder, Packet(kind), 600, safety, kind == 4);
                bool clean = kind == 0, applies = clean && safety == 0;
                Check(decoder.Get<bool>("lastFrameValid") == clean, "VM frame validity differs kind=" + kind + " safety=" + safety + ": " + decoder.Get<string>("lastError"));
                Check(target.Get<int>("receivedValue") == (applies ? 42 : -7) && target.Get<int>("toggleCalls") == (applies ? 1 : 0), "VM rejected/parse-only payload had effects");
                if (!clean && safety == 0)
                {
                    Check(decoder.Get<UdonBehaviour[]>("_cachedBindingUdonTargets") == null, "VM malformed payload built binding cache");
                    Check(decoder.Get<int[]>("_recentRpcEventIds") == null, "VM malformed payload changed dedup cache");
                    Deliver(decoder, Packet(0, 43), 601, safety, false);
                    Check(decoder.Get<bool>("lastFrameValid") && target.Get<int>("receivedValue") == 43 && target.Get<int>("toggleCalls") == 1 && decoder.Get<int>("skippedDuplicateRpcCount") == 0, "VM intact retransmission polluted");
                    Deliver(decoder, Packet(0, 44), 602, safety, false);
                    Check(decoder.Get<bool>("lastFrameValid") && target.Get<int>("receivedValue") == 44 && target.Get<int>("toggleCalls") == 1 && decoder.Get<int>("skippedDuplicateRpcCount") == 1, "VM RPC dedup changed");
                }
            }
            Results.Add("PASS actual compiled Decoder Update→injected ready payload→CRC→structural preflight→state/RPC, parse-only, cache ownership and intact retransmission. No GPU request or live client.");
        }
        catch (Exception error) { failure = error; }
        Finish(failure);
    }
    static byte[] ProfilePacket(int length, int scalars)
    {
        var bytes = new byte[Math.Max(length, 18 + scalars * 11 + 7)];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, 1), start = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, 0);
        for (int i = 0; i < scalars; i++)
            cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, (uint)i, NetworkFrameProtocol.ValueTypeInt32, i);
        int remaining = length - cursor - NetworkFrameProtocol.VariableValueHeaderBytes;
        Check(remaining >= 0, "Profile payload too short");
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, 999u, NetworkFrameProtocol.ValueTypeRawBytes, new byte[remaining]);
        Check(cursor == length && NetworkFrameWriter.EndVariableState(bytes, start, cursor, scalars + 1) && NetworkFrameWriter.EndNetworkFrame(bytes, 0, 1), "Profile writer failed");
        return bytes;
    }
    static void Measure(Program probe)
    {
        foreach (int length in new[] { 32, 2495, 5375, 8192 })
        foreach (int scalars in length == 5375 ? new[] { 0, 64, 256 } : new[] { 0 })
        {
            byte[] payload = ProfilePacket(length, scalars);
            probe.Set("input", payload); probe.Set("inputLength", payload.Length);
            foreach (string method in new[] { "ValidateStructure", "Noop" })
            {
                for (int i = 0; i < 5; i++) probe.Call(method);
                var times = new List<double>();
                for (int sample = 0; sample < 30; sample++)
                {
                    double ms = probe.TimedCall(method); times.Add(ms);
                    Samples.Add(string.Join(",", method, length, scalars + 1, sample, ms.ToString("F6", CultureInfo.InvariantCulture)));
                }
                if (method == "ValidateStructure") Check(probe.Get<bool>("valid"), "Profile input rejected");
                times.Sort();
                Timings.Add(string.Join(",", method, length, scalars + 1, times.Count, times.Average().ToString("F6", CultureInfo.InvariantCulture), times[28].ToString("F6", CultureInfo.InvariantCulture), times.Last().ToString("F6", CultureInfo.InvariantCulture)));
            }
        }
        Results.Add("Preflight VM microcost:5warm/30 measured,event dispatch included,control recorded not subtracted,input setup+CRC+payloadcopy+application excluded; 5375B with1/65/257entries. Not full decoder or VRChat time.");
    }
#endif
}
#endif
