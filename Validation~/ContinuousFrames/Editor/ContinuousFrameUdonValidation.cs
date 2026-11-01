#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

public static class ContinuousFrameUdonValidation
{
    const string ProbePath = "Assets/ContinuousFrames/ContinuousFrameProbe.asset";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static bool clientReady;

    public static void Run()
    {
        clientReady = false;
        ContinuousFrameValidation.CreateScene();
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbePath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/ContinuousFrames/ContinuousFrameProbe.cs");
            AssetDatabase.CreateAsset(asset, ProbePath);
        }
        bool failed = false;
        Application.LogCallback listener = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) failed = true;
        };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        if (failed) throw new InvalidOperationException("Full client UdonSharp compilation failed");
        ValidateMaterialPreparation();
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.ContinuousUdon", true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    static void ValidateMaterialPreparation()
    {
        var root = new GameObject("Decoder preparation validation");
        try
        {
            var decoder = (K13A.TSMP.Udon.TSMPDecoder)UdonSharpUndo.AddComponent(root, typeof(K13A.TSMP.Udon.TSMPDecoder));
            decoder.readbackPackMaterial = null;
            K13A.TSMP.Editor.SetupPreparation.PrepareAll();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(decoder);
            if (decoder.readbackPackMaterial == null || backing == null ||
                !backing.publicVariables.TryGetVariableValue("readbackPackMaterial", out Material serialized) ||
                serialized != decoder.readbackPackMaterial)
                throw new InvalidOperationException("Decoder packing material was not automatically serialized to Udon");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_RESULTS"), "preparation.txt"),
                "PASS missing decoder packing material automatically assigned and serialized to backing UdonBehaviour");
            var encoder=(TSMPEncoder)UdonSharpUndo.AddComponent(root,typeof(TSMPEncoder));
            encoder.luma4EncodeMaterial=null;
            K13A.TSMP.Editor.SetupPreparation.PrepareAll();
            backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(encoder);
            if(encoder.luma4EncodeMaterial==null || backing==null ||
                !backing.publicVariables.TryGetVariableValue("luma4EncodeMaterial",out serialized) || serialized!=encoder.luma4EncodeMaterial)
                throw new InvalidOperationException("Encoder GPU material was not automatically serialized to Udon");
            File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_RESULTS"),"preparation.txt"),
                "\nPASS missing encoder GPU material automatically assigned and serialized to backing UdonBehaviour");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        if (SessionState.GetBool("TSMP.ContinuousUdon", false))
            ContinuousFrameValidation.UdonFactory = CreateLoopback;
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.ContinuousUdon", false)) return;
        SessionState.SetBool("TSMP.ContinuousUdon", false);
        ContinuousFrameValidation.UdonFactory = CreateLoopback;
    }

    static ContinuousFrameValidation.ILoopback CreateLoopback(ContinuousFrameValidation runner, ContinuousFrameValidation.Case test)
    {
        if (!clientReady)
        {
            bool failed = false;
            Application.LogCallback listener = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true;
            };
            Application.logMessageReceived += listener;
            try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
            finally { Application.logMessageReceived -= listener; }
            if (failed) throw new InvalidOperationException("Post-Play client compilation failed.");
            clientReady = true;
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_RESULTS"), "bytecode-target.txt"),
                "Client compilation after Play Mode entry, before retrieving VM programs.\n");
        }
        return new Loopback(runner, test);
    }

    static void WarmGraphics(TSMPCodec codec, RenderTexture source, RenderTexture output)
    {
        double started = EditorApplication.timeSinceStartup;
        RenderTexture previous = RenderTexture.active;
        var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        try
        {
            for (int variant = 0; variant < codec.DecodeMaterialCount; variant++)
            foreach (int sample in new[] { 1, 4 })
            {
                var material = codec.GetDecodeMaterial(variant);
                if (material == null) throw new InvalidOperationException("Missing preparation material.");
                material.SetFloat("_BlockSize", 8); material.SetFloat("_SampleSize", sample);
                material.SetFloat("_StartBlock", codec.GetPayloadStartRow(source.width, 8) * (source.width / 8));
                material.SetFloat("_ByteCount", 127); material.SetFloat("_ActiveWidthBlocks", source.width / 8);
                material.SetFloat("_SourceWidth", source.width); material.SetFloat("_SourceHeight", source.height);
                material.SetFloat("_OutputWidth", output.width); material.SetFloat("_OutputHeight", output.height);
                material.SetFloat("_FlipY", 1);
                codec.PrepareDecode(source, material);
                Graphics.Blit(source, output, material);
            }
            RenderTexture.active = output;
            pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_RESULTS"), "graphics-preparation.txt"),
                codec.displayName + " " + (EditorApplication.timeSinceStartup - started).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "s outside VM/profile timing\n");
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixel); }
    }

    sealed class Program : IDisposable
    {
        readonly IUdonProgram code;
        public readonly UdonBehaviour Backing;

        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            code = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(code);
            Backing = new GameObject(asset.name + " continuous VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            if (!(bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { code.SymbolTable, code.Heap }))
                throw new InvalidOperationException("Unresolved Udon heap");
            type.GetField("_program", Private).SetValue(Backing, code);
            bool profiling = (Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_FILTER") ?? "").Contains("profile-");
            type.GetField("_udonVM", Private).SetValue(Backing, profiling ? new TimedVm(vm, code, asset.name) : vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true);
            type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in code.EntryPoints.GetExportedSymbols())
                events[name] = new List<uint> { code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec == null) return;
            foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address))
                    code.Heap.SetHeapVariable(address, field.GetValue(codec), code.Heap.GetHeapVariableType(address));
        }

        public void Set<T>(string name, T value) => code.Heap.SetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)code.Heap.GetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name));
        public object GetOptional(string name) => code.SymbolTable.TryGetAddressFromSymbol(name, out uint address) ? code.Heap.GetHeapVariable(address) : null;
        public void SetAny(string name, object value)
        {
            uint address = code.SymbolTable.GetAddressFromSymbol(name);
            code.Heap.SetHeapVariable(address, value, code.Heap.GetHeapVariableType(address));
        }
        public void SetOptional<T>(string name, T value)
        {
            if (code.SymbolTable.TryGetAddressFromSymbol(name, out uint address)) code.Heap.SetHeapVariable(address, value, typeof(T));
        }
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            if ((bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing))
                throw new InvalidOperationException("Udon VM failed: " + name);
        }
        public void Dispose() => Object.DestroyImmediate(Backing.gameObject);
    }

    sealed class TimedVm : IUdonVM
    {
        readonly IUdonVM inner;
        readonly Dictionary<uint, string> labels = new Dictionary<uint, string>();

        public TimedVm(IUdonVM vm, IUdonProgram program, string owner)
        {
            inner = vm;
            foreach (string name in program.EntryPoints.GetExportedSymbols())
                labels[program.EntryPoints.GetAddressFromSymbol(name)] = owner + "." + name;
        }

        public bool DebugLogging { get => inner.DebugLogging; set => inner.DebugLogging = value; }
        public bool LoadProgram(IUdonProgram program) => inner.LoadProgram(program);
        public IUdonProgram RetrieveProgram() => inner.RetrieveProgram();
        public void SetProgramCounter(uint counter) => inner.SetProgramCounter(counter);
        public uint GetProgramCounter() => inner.GetProgramCounter();
        public IUdonHeap InspectHeap() => inner.InspectHeap();
        public uint Interpret()
        {
            string label;
            if (!labels.TryGetValue(inner.GetProgramCounter(), out label)) label = "VM.other";
            using (ResourceProfile.Time(label)) return inner.Interpret();
        }
    }

    sealed class Loopback : ContinuousFrameValidation.ILoopback
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<Program> programs = new List<Program>();
        readonly Program encoder;
        readonly Program decoder;
        readonly Program sender;
        readonly Program receiver;
        readonly ContinuousFrameValidation runner;
        readonly Dictionary<int, Program> codecs = new Dictionary<int, Program>();
        readonly RenderTexture source;

        public Loopback(ContinuousFrameValidation runner, ContinuousFrameValidation.Case test)
        {
            this.runner = runner;
            string[] names = { "Luma4", "RGB16", "RGB20", "Color256" };
            TSMPCodec native = runner.CloneCodec(test.Codec, owned);
            var codec = Add("Packages/com.kibalab.tsmp.codec." + names[test.Codec].ToLowerInvariant() + "/Runtime/Scripts/TSMPCodec" + names[test.Codec] + ".asset", native);
            Program luma = test.Codec == 0 ? codec : Add("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", runner.CloneCodec(0, owned));
            codecs[0] = luma;
            codecs[test.Codec] = codec;
            sender = Add(ProbePath);
            receiver = Add(ProbePath);
            sender.Set("networkId", (ushort)1);
            receiver.Set("networkId", (ushort)1);
            receiver.Set("appliedIds", new int[ContinuousFrameValidation.Capacity]);
            receiver.Set("appliedTimes", new float[ContinuousFrameValidation.Capacity]);

            source = ContinuousFrameValidation.Texture(test.Width, test.Height);
            var bytes = ContinuousFrameValidation.Texture(512, Math.Max(1, (test.Bytes + 64 + 2047) / 2048));
            owned.Add(source);
            owned.Add(bytes);
            WarmGraphics(native, source, bytes);
            if (test.Codec != 0) WarmGraphics(runner.CloneCodec(0, owned), source, bytes);
            var expand = new Material(runner.expandMaterial);
            owned.Add(expand);
            encoder = Add("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
            encoder.Set("output", source);
            encoder.Set("autoEncode", false);
            encoder.Set("sampleSize", test.Sample);
            encoder.Set("frameIndex", 1u);
            encoder.Set("blockExpandMaterial", expand);
            encoder.Set("luma4EncodeMaterial", Resources.Load<Material>("TSMPEncodeLuma4"));
            encoder.Set("useGpuLuma4", Environment.GetEnvironmentVariable("TSMP_DISABLE_GPU_LUMA4") != "1");
            encoder.Set("selectedCodecUdonTarget", codec.Backing);
            encoder.Set("debugLog", false);
            Bind(encoder, sender);
            sender.Set("transRpcEncoder", encoder.Backing);
            encoder.Set("bindingSendOnChange", new[] { false });
            encoder.Set("bindingMinSendIntervals", new[] { 0f });
            encoder.Call("_onEnable");

            decoder = Add("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset");
            decoder.Set<Texture>("sourceTexture", source);
            decoder.Set("payloadByteTexture", bytes);
            decoder.Set("readbackPackMaterial", Resources.Load<Material>("TSMPReadbackPack"));
            decoder.Set("usePredictedReadback", Environment.GetEnvironmentVariable("TSMP_DISABLE_PREDICTION") != "1");
            decoder.Set("useCombinedByteOutput", Environment.GetEnvironmentVariable("TSMP_DISABLE_COMBINED_OUTPUT") != "1");
            decoder.Set("overlapReadbacks", Environment.GetEnvironmentVariable("TSMP_SINGLE_SLOT") != "1");
            decoder.SetOptional("retryAfterReadback", Environment.GetEnvironmentVariable("TSMP_DISABLE_READBACK_RETRY") != "1");
            decoder.Set("codecHandlers", test.Codec == 0 ? new[] { luma.Backing } : new[] { luma.Backing, codec.Backing });
            decoder.Set("applyEveryFrame", false);
            decoder.Set("flipY", true);
            decoder.Set("sampleSize", test.Sample);
            decoder.Set("debugLog", true);
            Bind(decoder, receiver);
            decoder.Call("_onEnable");
        }

        Program Add(string path, TSMPCodec codec = null)
        {
            var program = new Program(path, codec);
            programs.Add(program);
            return program;
        }
        static void Bind(Program owner, Program target)
        {
            var field = TransSyncMetadata.GetOrCreate(null, typeof(ContinuousFrameProbe)).Fields.Single();
            owner.Set("bindingTargets", new Component[0]);
            owner.Set("bindingUdonTargets", new[] { target.Backing });
            owner.Set("bindingNetworkIds", new ushort[] { 1 });
            owner.Set("bindingVariableHashes", new[] { field.VariableHash });
            owner.Set("bindingValueTypes", new[] { (byte)field.ValueType });
            owner.Set("bindingFieldNames", new[] { nameof(ContinuousFrameProbe.packet) });
            owner.Set("bindingDirections", new[] { 0 });
            owner.Set("bindingPriorities", new[] { 0 });
        }
        public bool Busy => decoder.Get<bool>("readbackInFlight");
        public int PayloadBytes => Binary.ReadUInt16LE(encoder.Get<byte[]>("_headerBytes"), FrameHeader.PayloadSizeOffset);
        public int AppliedCount => receiver.Get<int>("appliedCount");
        public int CorruptCount => receiver.Get<int>("corruptCount");
        public int[] AppliedIds => receiver.Get<int[]>("appliedIds");
        public float[] AppliedTimes => receiver.Get<float[]>("appliedTimes");
        public string Error => encoder.Get<string>("lastError");
        public string DecoderError => decoder.Get<bool>("lastFrameValid") ? null : decoder.Get<string>("lastError");
        public string Diagnostics => "header=" + decoder.Get<bool>("lastHeaderValid") + ", valid=" + decoder.Get<bool>("lastFrameValid") + ", messages=" + decoder.Get<int>("lastNetworkMessageCount") + ", applied=" + decoder.Get<int>("lastAppliedVariableCount") + ", error=" + decoder.Get<string>("lastError");
        public void Publish(byte[] packet)
        {
            sender.Set("packet", packet);
            uint before = encoder.Get<uint>("frameIndex");
            encoder.Call("EncodeNow");
            if (encoder.Get<uint>("frameIndex") != before + 1)
                throw new InvalidOperationException("Encoder output failure: " + Error);
        }
        public void Decode() => decoder.Call("DecodeNow");
        public void SetAutomatic(bool enabled) => decoder.Set("applyEveryFrame", enabled);
        public void TickAutomatic() => decoder.Call("_update");
        public Dictionary<string, Action> ProfileHelpers(int size, int width, int height)
        {
            receiver.Set("profilePixels", new Color32[(size + 3) / 4]);
            receiver.Set("profileBuffer", new byte[size]);
            receiver.Set("profileValue", new byte[size]);
            receiver.Set("profileCrcTable", Crc32Runtime.EnsureTable(null));
            receiver.Set("profileWidthBlocks", width / 8);
            receiver.Set("profileHeightBlocks", height / 8);
            receiver.Set("profileBasePixels", new Color32[width / 8 * (height / 8)]);
            receiver.Set("profileFramePixels", new Color32[width / 8 * (height / 8)]);
            receiver.Set("profileColors", SymbolCodec.CreateLuma4Colors());
            return new Dictionary<string, Action>
            {
                { "CopyPixels", () => receiver.Call("ProfileCopyPixels") }, { "CopyRawBytes", () => receiver.Call("ProfileCopyRawBytes") },
                { "WriteRawBytes", () => receiver.Call("ProfileWriteRawBytes") }, { "HeaderCrc", () => receiver.Call("ProfileHeaderCrc") },
                { "Control", () => receiver.Call("ProfileControl") }, { "BasePixelCopy", () => receiver.Call("ProfileBasePixelCopy") },
                { "LumaPayload", () => receiver.Call("ProfileLumaPayload") }
            };
        }
        public void Stop() => decoder.Call("_onDisable");
        public void Resume() => decoder.Call("_onEnable");
        public object ReadDecoder(string name) => decoder.GetOptional(name);
        public object ReadEncoder(string name) => encoder.GetOptional(name);
        public void SetGpuEncoding(bool enabled) => encoder.Set("useGpuLuma4",enabled);
        public void RestartEncoder() { encoder.Call("_onDisable"); encoder.Call("_onEnable"); }
        public void WriteDecoder(string name, object value) => decoder.SetAny(name, value);
        public RenderTexture Output => source;
        public byte[] ReceivedPacket => receiver.Get<byte[]>("packet");
        public void SetSample(int sample) => encoder.Set("sampleSize", sample);
        public int RpcCount => receiver.Get<int>("rpcCount");
        public void QueueRpc() => sender.Call("QueueProbeRpc");
        public void SelectCodec(int index)
        {
            if (!codecs.TryGetValue(index, out var codec))
            {
                string name = new[] { "Luma4", "RGB16", "RGB20", "Color256" }[index];
                codecs[index] = codec = Add("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Scripts/TSMPCodec" + name + ".asset", runner.CloneCodec(index, owned));
            }
            encoder.Set("selectedCodecUdonTarget", codec.Backing);
            decoder.Set("codecHandlers", codecs.Values.Select(c => c.Backing).ToArray());
        }
        public void Dispose()
        {
            RenderTexture.active = null;
            foreach (var program in programs) program.Dispose();
            foreach (var item in owned)
            {
                if (item is RenderTexture texture) texture.Release();
                if (item != null) Object.Destroy(item);
            }
        }
    }
}
#endif
