#if UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Data = DecoderSnapshotValidation;
using Object = UnityEngine.Object;

public static class UdonSnapshotValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator work;
    static readonly List<string> Results = new List<string>();

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CompileClient();
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.SnapshotVm", true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    static void CompileClient()
    {
        bool failed = false;
        Application.LogCallback listener = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true;
        };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Full Udon client compilation");
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.SnapshotVm", false)) return;
        SessionState.SetBool("TSMP.SnapshotVm", false);
        EditorApplication.delayCall += () =>
        {
            work = Validate();
            EditorApplication.update += Tick;
        };
    }

    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        Results.Insert(0, error == null ? "PASS" : "FAIL");
        if (error != null) Results.Add(error.ToString());
        File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_SNAPSHOT_RESULTS"), Results);
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;

        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            Code = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(Code);
            Backing = new GameObject(asset.name + " VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Heap references");
            type.GetField("_program", Private).SetValue(Backing, Code);
            type.GetField("_udonVM", Private).SetValue(Backing, vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true);
            type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols())
                events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null)
                foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address))
                        Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
        }

        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM failure: " + name);
        }
        public void Dispose() => Object.DestroyImmediate(Backing.gameObject);
    }

    static IEnumerator Drain(Program decoder)
    {
        double deadline = EditorApplication.timeSinceStartup + 20;
        while (decoder.Get<bool>("readbackInFlight") && EditorApplication.timeSinceStartup < deadline) yield return null;
        Check(!decoder.Get<bool>("readbackInFlight"), "Actual VRC readback callback timeout");
    }

    static void TestAvatarPool()
    {
        using (var sync = new Program("Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetworkVrchatAvatarPoseSync.asset"))
        {
            var avatars = Enumerable.Range(0, 3).Select(i => new GameObject("Pool VM " + i)).ToArray();
            try
            {
                sync.Set("avatarPool", avatars);
                sync.Set("maxPlayers", 3);
                sync.Set("avatarPoseBytes", new byte[] { 4, 2, 0, 0, 0, 0 });
                sync.Call("OnTSMPVariableReceived");
                for (int cycle = 0; cycle < 8; cycle++)
                {
                    sync.Set("_slotPlayerIds", new[] { 1, 2, 3 });
                    sync.Set("_slotLastSeen", new[] { Time.time, Time.time, Time.time });
                    sync.Set("_slotRetireGraceUntil", Time.time + 10);
                    sync.Set("maxPlayers", 1);
                    sync.Call("_postLateUpdate");
                    Check(sync.Get<int>("activeAvatarCount") == 1 && sync.Get<int>("poolSize") == 3, "VM shrink counters");
                    Check(!avatars[1].activeSelf && !avatars[2].activeSelf, "VM overflow deactivation");
                    Check(sync.Get<GameObject[]>("avatarPool").SequenceEqual(avatars), "VM retained objects");
                    sync.Set("maxPlayers", 3);
                    sync.Call("OnTSMPVariableReceived");
                    Check(sync.Get<int[]>("_slotPlayerIds").SequenceEqual(new[] { 1, -1, -1 }), "VM regrowth clears retired IDs");
                    Check(sync.Get<float[]>("_slotLastSeen")[2] == 0, "VM regrowth clears timestamps");
                    Check(sync.Get<int[]>("_recordIndexSlots").All(i => i == -1), "VM invalidates slot cache");
                    sync.Set("avatarPoseBytes", new byte[] { 4, 2, 3, 0, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, 0, 0, 0 });
                    sync.Call("OnTSMPVariableReceived");
                    Check(sync.Get<int>("activeAvatarCount") == 3 && sync.Get<GameObject[]>("avatarPool").SequenceEqual(avatars), "VM slot reuse");
                    sync.Set("avatarPoseBytes", new byte[] { 4, 2, 0, 0, 0, 0 });
                }
                Results.Add("PASS avatar pool VM: eight shrink/regrow cycles, idle resize, inactive retained objects, IDs, timestamps, cache and slot reuse (pool-only fixtures without rigs)");
            }
            finally { foreach (var avatar in avatars) Object.DestroyImmediate(avatar); }
        }
    }

    // First-use shader/driver compilation can exceed the Editor VM's fixed 10s
    // watchdog inside VRCGraphics.Blit. Prepare graphics outside VM execution;
    // preserve that cold-start cost separately from the functional assertions.
    static void WarmMaterials(TSMPCodec[] codecs, RenderTexture source, RenderTexture output)
    {
        double started = EditorApplication.timeSinceStartup;
        RenderTexture previous = RenderTexture.active;
        var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        try
        {
            foreach (TSMPCodec codec in codecs)
            for (int variant = 0; variant < codec.DecodeMaterialCount; variant++)
            foreach (int sample in new[] { 1, 4 })
            {
                Material material = codec.GetDecodeMaterial(variant);
                Check(material != null, "Warmup material missing: " + codec.displayName);
                material.SetFloat("_BlockSize", 8);
                material.SetFloat("_SampleSize", sample);
                material.SetFloat("_StartBlock", codec.payloadStartRow * 80);
                material.SetFloat("_ByteCount", 127);
                material.SetFloat("_ActiveWidthBlocks", 80);
                material.SetFloat("_SourceWidth", source.width);
                material.SetFloat("_SourceHeight", source.height);
                material.SetFloat("_OutputWidth", output.width);
                material.SetFloat("_OutputHeight", output.height);
                material.SetFloat("_FlipY", 1);
                codec.PrepareDecode(source, material);
                Graphics.Blit(source, output, material);
            }
            RenderTexture.active = output;
            pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            Results.Add("PREPARATION graphics material warmup outside VM=" +
                (EditorApplication.timeSinceStartup - started).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "s; not a VM performance measurement");
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixel); }
    }

    static IEnumerator PayloadIntegrity(Program decoder, TSMPCodec[] codecs, Texture2D image, RenderTexture source)
    {
        uint frame = 10000;
        decoder.Set("sampleSize", 4);
        decoder.Set("requirePayloadCrc32", true);
        foreach (var codec in codecs)
        foreach (int mode in new[] { 2, 3 })
        {
            decoder.Set("decodeSafetyMode", mode);
            byte[] packet = Data.NetworkPayload(7);
            Data.Write(codec, image, packet, ++frame, 4, protectPayload: true);
            Graphics.Blit(image, source); decoder.Call("DecodeNow");
            var wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcPresent") && decoder.Get<bool>("lastPayloadCrcVerified"), "VM protected payload rejected: " + codec.displayName);
            uint applied = decoder.Get<uint>("_lastAppliedFrameIndex");
            Data.Write(codec, image, packet, ++frame, 4, protectPayload: true, corruptPayload: true);
            Graphics.Blit(image, source); decoder.Call("DecodeNow");
            wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(decoder.Get<bool>("lastHeaderValid") && !decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcPresent") && !decoder.Get<bool>("lastPayloadCrcVerified"), "VM payload mutation accepted: " + codec.displayName);
            Check(decoder.Get<uint>("_lastAppliedFrameIndex") == applied, "VM CRC failure advanced applied frame");
        }
        using (var state = new Program("Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetworkVrchatAvatarPoseSync.asset"))
        using (var toggle = new Program("Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetworkGameObjectToggle.asset"))
        {
            var target = new GameObject("CRC VM toggle target");
            toggle.Set("targetObject", target);
            // A production Udon int field tests generic field dispatch without a new fixture program.
            // avatarPoseBytes remains null, so no avatar capture or pose application is involved.
            decoder.Set("bindingTargets", new Component[] { state.Backing, toggle.Backing });
            decoder.Set("bindingUdonTargets", new[] { state.Backing, toggle.Backing });
            decoder.Set("bindingNetworkIds", new ushort[] { 1, 2 });
            decoder.Set("bindingVariableHashes", new[] { StableHash.Fnv1A32("receivedValue"), 0u });
            decoder.Set("bindingValueTypes", new[] { (byte)NetworkFrameProtocol.ValueTypeInt32, (byte)NetworkFrameProtocol.ValueTypeUnsupported });
            decoder.Set("bindingFieldNames", new[] { "encodedPoseBytes", string.Empty });
            decoder.Set("bindingDirections", new int[2]);
            decoder.Set("bindingPriorities", new int[2]);
            decoder.Set("decodeSafetyMode", 0);
            decoder.Set("skipDuplicateFrames", true);
            int eventId = 30000;
            foreach (bool prediction in new[] { false, true })
            {
                decoder.Set("usePredictedReadback", prediction);
                decoder.Set("useCombinedByteOutput", prediction);
                byte[] warm = Data.MixedValueRpcPacket(41, ++eventId);
                Data.Write(codecs[0], image, warm, ++frame, 4, protectPayload: true);
                Graphics.Blit(image, source); decoder.Call("DecodeNow");
                var wait = Drain(decoder);
                while (wait.MoveNext()) yield return wait.Current;
                Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcVerified"), "VM CRC prediction warmup");
                state.Set("encodedPoseBytes", -1); target.SetActive(false);
                byte[] packet = Data.MixedValueRpcPacket(42, ++eventId);
                uint retryFrame = ++frame;
                Data.Write(codecs[0], image, packet, retryFrame, 4, protectPayload: true, corruptPayload: true);
                Graphics.Blit(image, source); decoder.Call("DecodeNow");
                wait = Drain(decoder);
                while (wait.MoveNext()) yield return wait.Current;
                Check(!decoder.Get<bool>("lastFrameValid") && state.Get<int>("encodedPoseBytes") == -1 && !target.activeSelf, "VM CRC failure partially applied field/RPC");
                Data.Write(codecs[0], image, packet, retryFrame, 4, protectPayload: true);
                Graphics.Blit(image, source); decoder.Call("DecodeNow");
                wait = Drain(decoder);
                while (wait.MoveNext()) yield return wait.Current;
                Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcVerified") && state.Get<int>("encodedPoseBytes") == 42 && target.activeSelf, "VM CRC failure consumed frame/RPC identity");
            }
            decoder.Set("overlapReadbacks", true);
            foreach (bool badHead in new[] { true, false })
            {
                state.Set("encodedPoseBytes", -1); target.SetActive(false);
                byte[] first = Data.MixedValueRpcPacket(42, ++eventId);
                byte[] second = Data.MixedValueRpcPacket(99, ++eventId, 701);
                Data.Write(codecs[0], image, first, ++frame, 4, protectPayload: true, corruptPayload: badHead);
                Graphics.Blit(image, source); decoder.Call("DecodeNow");
                Data.Write(codecs[0], image, second, ++frame, 4, protectPayload: true, corruptPayload: !badHead);
                Graphics.Blit(image, source); decoder.Call("DecodeNow");
                Check(decoder.Get<int>("pendingDecodeCount") == 2, "VM protected overlap did not use both slots");
                var wait = Drain(decoder);
                while (wait.MoveNext()) yield return wait.Current;
                Check(state.Get<int>("encodedPoseBytes") == (badHead ? 99 : 42) && target.activeSelf, "VM CRC slot ownership or atomic rejection failed");
            }
            Object.DestroyImmediate(target);
        }
        decoder.Set("decodeSafetyMode", 2);
        Data.Write(codecs[0], image, Data.NetworkPayload(0), ++frame, 4);
        Graphics.Blit(image, source); decoder.Call("DecodeNow");
        decoder.Set("requirePayloadCrc32", false);
        var legacyWait = Drain(decoder);
        while (legacyWait.MoveNext()) yield return legacyWait.Current;
        Check(!decoder.Get<bool>("lastFrameValid") && !decoder.Get<bool>("lastPayloadCrcPresent"), "VM strict mode admitted legacy frame");
        decoder.Set("requirePayloadCrc32", false);
        Data.Write(codecs[0], image, Data.NetworkPayload(0), ++frame, 4);
        Graphics.Blit(image, source); decoder.Call("DecodeNow");
        decoder.Set("requirePayloadCrc32", true);
        legacyWait = Drain(decoder);
        while (legacyWait.MoveNext()) yield return legacyWait.Current;
        Check(decoder.Get<bool>("lastFrameValid") && !decoder.Get<bool>("lastPayloadCrcPresent") && !decoder.Get<bool>("lastPayloadCrcVerified"), "VM legacy compatibility lost");
        decoder.Set("requirePayloadCrc32", false);
        decoder.Set("skipDuplicateFrames", false);
        decoder.Set("bindingTargets", new Component[0]);
        decoder.Set("bindingUdonTargets", new UdonBehaviour[0]);
        decoder.Set("bindingNetworkIds", new ushort[0]);
        decoder.Set("bindingVariableHashes", new uint[0]);
        decoder.Set("bindingValueTypes", new byte[0]);
        decoder.Set("bindingFieldNames", new string[0]);
        decoder.Set("bindingDirections", new int[0]);
        decoder.Set("bindingPriorities", new int[0]);
        Results.Add("PASS payload CRC VM: four codecs/readback+parse modes; field+RPC atomic rejection and same-frame/event retry; predicted/fallback/combined and both bad-slot orders; strict missing/legacy compatibility");
    }

    static IEnumerator Validate()
    {
        // Play Mode can switch SDK assets back to editor bytecode. Retrieve client programs explicitly.
        CompileClient();
        Results.Add("PASS Udon client compilation after entering Play Mode");
        TestAvatarPool();
        Results.Add("Full client UdonSharp compile; actual decoder/codec bytecode, VRCGraphics and asynchronous VRC GPU readback callbacks in editor VM");
        Results.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName);
        var names = new[] { "Luma4", "RGB16", "RGB20", "Color256" };
        var owned = new List<Material>();
        var codecs = names.Select(name => Data.InstantiateCodec(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab").GetComponent<TSMPCodec>(), owned)).ToArray();
        var programs = names.Select((name, i) => new Program("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() +
            "/Runtime/Scripts/TSMPCodec" + name + ".asset", codecs[i])).ToArray();
        using (var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset"))
        {
            var a = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var b = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var source = Data.Target(640, 360);
            var output = Data.Target(512, 1);
            decoder.Set<Texture>("sourceTexture", source);
            decoder.Set("payloadByteTexture", output);
            decoder.Set("codecHandlers", programs.Select(p => p.Backing).ToArray());
            decoder.Set("applyEveryFrame", false);
            decoder.Set("skipDuplicateFrames", false);
            decoder.Set("decodeSafetyMode", 2);
            decoder.Set("debugLog", false);
            decoder.Call("_onEnable");
            Data.Write(codecs[0], a, Data.Payload(1), 1, 4);
            Graphics.Blit(a, source);
            WarmMaterials(codecs, source, output);
            uint frame = 0;
            foreach (var codec in codecs)
            foreach (int sample in new[] { 1, 4 })
            foreach (bool changeCodec in new[] { false, true })
            {
                byte[] bytes = Data.Payload((int)++frame);
                Data.Write(codec, a, bytes, frame, sample);
                Data.Write(changeCodec ? codecs[(Array.IndexOf(codecs, codec) + 1) % codecs.Length] : codec,
                    b, Data.Payload(193, changeCodec ? 67 : bytes.Length), frame + 10000, sample);
                Graphics.Blit(a, source);
                decoder.Set("sampleSize", sample);
                decoder.Call("DecodeNow");
                Check(decoder.Get<bool>("readbackInFlight"), "VM header readback not started: " + decoder.Get<string>("lastError"));
                Graphics.Blit(b, source);
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(decoder.Get<bool>("lastFrameValid"), decoder.Get<string>("lastError"));
                Check(decoder.Get<uint>("lastFrameIndex") == frame && decoder.Get<int>("_payloadDataBytes") == bytes.Length && bytes.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(bytes.Length)), "VM frame isolation " + codec.displayName);
            }
            Results.Add("PASS " + frame + " decoder/codec VM changing-source cases, samples 1/4, payload/codec/length changes");
            var integrity = PayloadIntegrity(decoder, codecs, a, source);
            while (integrity.MoveNext()) yield return integrity.Current;
            decoder.Set("decodeSafetyMode", 3);
            decoder.Set("sampleSize", 1);
            foreach (int length in new[] { 200, 0, 400, 0, 1, 400, 0 })
            {
                byte[] bytes = Data.NetworkPayload(length);
                // Capacity reuse belongs to the next write slot, not the last completed frame.
                // UdonSharp represents jagged arrays as object[] in the VM heap.
                byte[] previous = (byte[])decoder.Get<object[]>("_slotPayloads")[decoder.Get<int>("_slotHead")];
                Data.Write(codecs[0], a, bytes, ++frame, 1);
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(decoder.Get<bool>("lastFrameValid"), "VM capacity frame: " + decoder.Get<string>("lastError"));
                Check(decoder.Get<int>("lastNetworkMessageCount") == 1 && decoder.Get<int>("lastPayloadAvailableBytes") == bytes.Length, "VM parsing bounds");
                Check(bytes.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(bytes.Length)), "VM payload bytes");
                if (previous != null && previous.Length >= bytes.Length)
                    Check(ReferenceEquals(previous, decoder.Get<byte[]>("_payloadBytes")), "VM replaced sufficient capacity in the same slot");
            }
            foreach (int length in new[] { 0, 7 })
            {
                Data.Write(codecs[0], a, new byte[length], ++frame, 1);
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(!decoder.Get<bool>("lastFrameValid") && decoder.Get<int>("_decodeStage") == 1 && decoder.Get<string>("lastError") == "Payload is too small for NetworkFrame.", "VM invalid length must not reuse old payload");
            }
            Results.Add("PASS GPU VM payload capacity: seven growing/shrinking NetworkFrames, retained dirty tails, zero/truncated header payload rejection");
            decoder.Set("decodeSafetyMode", 2);
            Data.Write(codecs[0], a, Data.Payload(77), ++frame, 1);
            foreach (bool payload in new[] { false, true })
            foreach (bool objectDisable in new[] { false, true })
            foreach (bool earlyEnable in new[] { false, true })
            {
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                if (payload)
                {
                    while (decoder.Get<bool>("readbackInFlight") && decoder.Get<int>("_decodeStage") == 1) yield return null;
                    Check(decoder.Get<bool>("readbackInFlight"), "VM payload cancellation window");
                }
                var snapshot = decoder.Get<RenderTexture>("_decodeSourceTexture");
                int pending = decoder.Get<int>("pendingDecodeCount");
                if (objectDisable) decoder.Backing.gameObject.SetActive(false); else decoder.Backing.enabled = false;
                Check(decoder.Get<bool>("_decodeSuspended"), "Actual backing disable event not delivered");
                Check(decoder.Get<bool[]>("_slotDiscarded").All(value => value), "VM disable did not discard both slots");
                Check(snapshot != null && snapshot.IsCreated(), "VM released snapshot before pending callback completed");
                uint replacementFrame = 0;
                byte[] replacement = Data.Payload(91);
                if (earlyEnable)
                {
                    if (objectDisable) decoder.Backing.gameObject.SetActive(true); else decoder.Backing.enabled = true;
                    replacementFrame = ++frame;
                    Data.Write(codecs[0], a, replacement, replacementFrame, 1);
                    Graphics.Blit(a, source);
                    decoder.Call("DecodeNow");
                    Check(decoder.Get<int>("pendingDecodeCount") == pending + 1, "VM free slot was not available after re-enable");
                    Check(decoder.Get<RenderTexture>("_decodeSourceTexture") != snapshot, "VM reused cancelled slot snapshot for a new capture");
                }
                else
                {
                    decoder.Call("DecodeNow");
                    Check(decoder.Get<int>("pendingDecodeCount") == pending, "VM accepted a capture while disabled");
                }
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                if (earlyEnable)
                    Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<uint>("lastFrameIndex") == replacementFrame
                        && replacement.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(replacement.Length)), "VM replacement capture did not survive cancellation");
                else
                    Check(!decoder.Get<bool>("lastFrameValid"), "VM cancelled frame applied");
                if (objectDisable) decoder.Backing.gameObject.SetActive(true); else decoder.Backing.enabled = true;
                double cleanupDeadline = EditorApplication.timeSinceStartup + 2;
                while (snapshot != null && EditorApplication.timeSinceStartup < cleanupDeadline) yield return null;
                Check(snapshot == null, "VM snapshot leaked");
                decoder.Call("DecodeNow");
                drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(decoder.Get<bool>("lastFrameValid"), "VM restart failed");
            }
            Results.Add("PASS 8 VM cancellation cases: actual component/GameObject disable during header/payload, callbacks while disabled or after early re-enable, restart and snapshot cleanup");
            decoder.Set("decodeSafetyMode", 0);
            Check(decoder.Get<int>("frameWindowSize") == 256, "VM default frame window");
            var empty = new byte[NetworkFrameProtocol.NetworkHeaderBytes];
            NetworkFrameWriter.BeginNetworkFrame(empty, 0, 1);
            foreach (var item in Data.FrameOrderCases())
            {
                decoder.Set("_hasAppliedFrame", item[6] != 0);
                decoder.Set("_lastAppliedStreamId", item[4]);
                decoder.Set("_lastAppliedFrameIndex", item[0]);
                decoder.Set("frameWindowSize", (int)item[2]);
                decoder.Set("skipDuplicateFrames", item[5] == 0);
                int duplicates = decoder.Get<int>("skippedDuplicateFrameCount");
                int older = decoder.Get<int>("skippedOutOfOrderFrameCount");
                Data.Write(codecs[0], a, empty, item[1], 4);
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                bool accept = item[3] != 0;
                Check(decoder.Get<bool>("lastFrameValid"), "VM window frame validity");
                Check(decoder.Get<uint>("_lastAppliedFrameIndex") == (accept ? item[1] : item[0]), "VM window " + string.Join(",", item));
                Check(decoder.Get<int>("skippedDuplicateFrameCount") == duplicates + (!accept && item[0] == item[1] ? 1 : 0), "VM duplicate counter");
                Check(decoder.Get<int>("skippedOutOfOrderFrameCount") == older + (!accept && item[0] != item[1] ? 1 : 0), "VM older counter");
            }
            Results.Add("PASS 30 compiled Udon frame-window cases including UInt32 wrap, half-range, custom windows, stream switch and disabled filtering");
            decoder.Call("_onDisable");
            RenderTexture.active = null;
            source.Release();
            output.Release();
            foreach (Object item in new Object[] { a, b, source, output }) Object.DestroyImmediate(item);
        }
        foreach (var program in programs) { program.Call("_onDisable"); program.Dispose(); }
        foreach (var codec in codecs) Object.DestroyImmediate(codec.gameObject);
        foreach (var material in owned) Object.DestroyImmediate(material);
    }
}
#endif
