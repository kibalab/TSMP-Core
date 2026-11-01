#if !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
#endif

public sealed class DecoderSnapshotValidation : MonoBehaviour
{
    public TSMPCodec[] prefabs;
    public int receivedValue;
    public uint lastVariableHash;
    readonly List<string> results = new List<string>();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    uint sequence;

    public void OnTSMPVariableReceived() { }

#if UNITY_EDITOR
    static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runner = new GameObject("Snapshot validation").AddComponent<DecoderSnapshotValidation>();
        runner.prefabs = new[] { "Luma4", "RGB16", "RGB20", "Color256" }.Select(name =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() +
                "/Runtime/Codec_" + name + ".prefab").GetComponent<TSMPCodec>()).ToArray();
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/DecoderSnapshot/Validation.unity");
    }

    public static void Play()
    {
        CreateScene();
        EditorApplication.EnterPlaymode();
    }

    public static void PlayLinear()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        Play();
    }

    public static void Build()
    {
        CreateScene();
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string path = Environment.GetEnvironmentVariable("TSMP_SNAPSHOT_BUILD");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/DecoderSnapshot/Validation.unity" },
            locationPathName = path, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        File.WriteAllText(Path.ChangeExtension(path, ".build-report.txt"), "Result=" + report.summary.result +
            "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\nBackend=Mono\nStripping=Disabled");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
#endif

    IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
        results.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName +
            "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        var work = Validate();
        while (true)
        {
            bool next;
            try { next = work.MoveNext(); }
            catch (Exception error) { Finish("FAIL", error); yield break; }
            if (!next) break;
            yield return work.Current;
        }
        Finish("PASS", null);
    }

    void Finish(string status, Exception error)
    {
        results.Insert(0, status);
        if (error != null) results.Add(error.ToString());
        string text = string.Join("\n", results);
        Debug.Log(text);
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_SNAPSHOT_RESULTS"), text);
#if UNITY_EDITOR
        EditorApplication.Exit(error == null ? 0 : 1);
#else
        Application.Quit(error == null ? 0 : 1);
#endif
    }

    static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    static T Field<T>(TSMPDecoder decoder, string name) => (T)typeof(TSMPDecoder).GetField(name, Private | BindingFlags.Public).GetValue(decoder);

    public static RenderTexture Target(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGB32,
        RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear)
    {
        var texture = new RenderTexture(width, height, 0, format, readWrite) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Check(texture.Create(), "RenderTexture allocation");
        return texture;
    }

    static void Drop(UnityEngine.Object item)
    {
        if (item is RenderTexture texture) texture.Release();
        if (item != null) Destroy(item);
    }

    public static TSMPCodec InstantiateCodec(TSMPCodec prefab, List<Material> owned)
    {
        var codec = Instantiate(prefab.gameObject).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (copies.TryGetValue(original, out Material copy)) return copy;
            copy = new Material(original) { hideFlags = HideFlags.DontSave };
            copies.Add(original, copy);
            owned.Add(copy);
            return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material))
                field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials)
                field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return codec;
    }

    public static void Configure(TSMPCodec codec, int variant)
    {
        if (codec is TSMPCodecRGB16 rgb)
        {
            rgb.rBits = variant < 2 ? 4 : 5;
            rgb.gBits = variant < 2 ? 4 : 6;
            rgb.bBits = 4;
            rgb.localRefine = variant % 2 == 1;
        }
        if (codec is TSMPCodecColor256 color)
        {
            color.robustChannelDecode = variant > 0;
            color.localRefine = variant == 2;
        }
    }

    public static void Write(TSMPCodec codec, Texture2D texture, byte[] payload, uint frame, int sample, bool corruptHeader = false,
        bool protectPayload = false, bool corruptPayload = false)
    {
        var header = FrameHeader.CreateDefault();
        header.BlockSize = 8;
        header.ActiveWidthBlocks = (ushort)(texture.width / 8);
        header.ActiveHeightBlocks = (ushort)(texture.height / 8);
        header.CodecId = codec.codecId;
        header.SymbolMode = (byte)codec.SymbolMode;
        header.PayloadSize = (ushort)payload.Length;
        header.FrameIndex = frame;
        header.DecodeSampleSize = (byte)sample;
        byte[] options = codec.GetCodecOptionBytes() ?? Array.Empty<byte>();
        header.CodecOptionLength = (byte)options.Length;
        if (options.Length > 0) header.CodecOption0 = options[0];
        if (options.Length > 1) header.CodecOption1 = options[1];
        if (options.Length > 2) header.CodecOption2 = options[2];
        if (options.Length > 3) header.CodecOption3 = options[3];
        if (options.Length > 4) header.CodecOption4 = options[4];
        var bytes = new byte[FrameHeader.Size];
        header.WriteTo(bytes, 0);
        if (protectPayload)
            Check(FrameHeaderWriter.TryWritePayloadCrc32(bytes, 0, payload, payload.Length, null), "Protected fixture header");
        if (corruptHeader) bytes[FrameHeader.FrameIndexOffset] ^= 1;
        byte[] encodedPayload = payload;
        if (corruptPayload)
        {
            Check(payload.Length > 0, "Corruption fixture needs payload bytes");
            encodedPayload = (byte[])payload.Clone();
            encodedPayload[encodedPayload.Length - 1] ^= 1;
        }
        Check(codec.TryWriteFrame(texture, 8, bytes, encodedPayload, out string error), error);
    }

    static void Upload(Texture source, RenderTexture destination, bool flip)
    {
        Graphics.Blit(source, destination, new Vector2(1, flip ? 1 : -1), new Vector2(0, flip ? 0 : 1));
    }

    static IEnumerator Drain(TSMPDecoder decoder)
    {
        // First use can synchronously compile a recovery shader variant in the
        // Editor. Keep the correctness deadline separate from performance data.
        float deadline = Time.realtimeSinceStartup + 180;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!decoder.readbackInFlight, "Readback timeout: stage=" + Field<int>(decoder, "_decodeStage") +
            "; frame=" + decoder.lastFrameIndex + "; error=" + decoder.lastError);
    }

    public static byte[] Payload(int seed, int length = 127) => Enumerable.Range(0, length).Select(i => (byte)(seed + i * 73)).ToArray();

    public static byte[] NetworkPayload(int length)
    {
        var bytes = new byte[length + 128];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, 1);
        int start = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, 0);
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, 100, NetworkFrameProtocol.ValueTypeRawBytes, Payload(33, length));
        Check(cursor >= 0 && NetworkFrameWriter.EndVariableState(bytes, start, cursor, 1), "Build capacity test payload");
        NetworkFrameWriter.EndNetworkFrame(bytes, 0, 1);
        Array.Resize(ref bytes, cursor);
        return bytes;
    }

    static void Verify(TSMPDecoder decoder, byte[] expected, uint frame, string label)
    {
        Check(decoder.lastHeaderValid && decoder.lastFrameValid, label + ": " + decoder.lastError);
        Check(decoder.lastFrameIndex == frame, label + ": wrong header frame");
        Check(Field<int>(decoder, "_payloadDataBytes") == expected.Length && expected.SequenceEqual(Field<byte[]>(decoder, "_payloadBytes").Take(expected.Length)), label + ": header/payload generation mismatch");
    }

    IEnumerator Validate()
    {
        var owned = new List<Material>();
        var codecs = prefabs.Select(p => InstantiateCodec(p, owned)).ToArray();
        var decoder = new GameObject("Decoder").AddComponent<TSMPDecoder>();
        decoder.applyEveryFrame = false;
        decoder.skipDuplicateFrames = false;
        decoder.decodeSafetyMode = 2;
        decoder.debugLog = false;
        decoder.codecHandlers = codecs;
        decoder.payloadByteTexture = Target(512, 1);
        var a = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        var b = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        yield return null;
        int cases = 0;
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBFloat })
        {
            var source = Target(640, 360, format);
            decoder.sourceTexture = source;
            foreach (var codec in codecs)
            for (int variant = 0; variant < codec.DecodeMaterialCount; variant++)
            foreach (int sample in new[] { 1, 4 })
            foreach (bool flip in new[] { true, false })
            foreach (int mutation in new[] { 0, 1, 2 })
            {
                Configure(codec, variant);
                byte[] bytes = Payload(19 + cases);
                uint frame = ++sequence;
                Write(codec, a, bytes, frame, sample);
                Write(mutation == 2 ? codecs[(Array.IndexOf(codecs, codec) + 1) % codecs.Length] : codec,
                    b, Payload(163 + cases, mutation == 1 ? 67 : bytes.Length), frame + 10000, sample);
                decoder.flipY = flip;
                decoder.sampleSize = sample;
                Upload(a, source, flip);
                Debug.Log("BEGIN snapshot case=" + cases + "; codec=" + codec.displayName + "; variant=" + variant +
                    "; sample=" + sample + "; flip=" + flip + "; mutation=" + mutation + "; format=" + format);
                var previous = Field<RenderTexture[]>(decoder, "_slotSnapshots")[Field<int>(decoder, "_slotHead")];
                var previousFormat = previous != null ? previous.graphicsFormat : UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                decoder.DecodeNow();
                Check(decoder.readbackInFlight, "Header request not started");
                Upload(b, source, flip);
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                string label = codec.displayName + "/" + variant + "/" + sample + "/" + flip + "/" + mutation + "/" + format;
                Verify(decoder, bytes, frame, label);
                var snapshot = Field<RenderTexture>(decoder, "_decodeSourceTexture");
                Check(snapshot != source && snapshot.IsCreated(), "Snapshot is not owned");
                Check(previous == null || snapshot == previous || snapshot.graphicsFormat != previousFormat, "Repeated allocation at fixed dimensions/format within a slot");
                cases++;
            }
            Drop(source);
        }
        results.Add("PASS " + cases + " changing-source cases: 4 codecs/all variants, sample 1/4, both orientations, same/different payload size and codec switch, ARGB32/Float inputs; allocation reuse");

        var integrity = PayloadIntegrity(decoder, codecs, a);
        while (integrity.MoveNext()) yield return integrity.Current;
        var lengths = PayloadLengths(decoder, codecs[0], a);
        while (lengths.MoveNext()) yield return lengths.Current;
        var lifecycle = Lifecycle(decoder, codecs[0], a, b);
        while (lifecycle.MoveNext()) yield return lifecycle.Current;
        var order = FrameOrder(decoder, codecs[0], a);
        while (order.MoveNext()) yield return order.Current;
        Drop(a);
        Drop(b);
        var precision = Precision();
        while (precision.MoveNext()) yield return precision.Current;
        var timing = Timing(decoder, codecs[0]);
        while (timing.MoveNext()) yield return timing.Current;
        var bytesTarget = decoder.payloadByteTexture;
        Drop(decoder.gameObject);
        yield return null;
        Drop(bytesTarget);
        foreach (var codec in codecs) Drop(codec.gameObject);
        yield return null;
        foreach (var material in owned) Drop(material);
    }

    public static byte[] MixedValueRpcPacket(int value, int eventId, int extraBytes = 0)
    {
        var bytes = new byte[256 + extraBytes];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, (uint)eventId);
        int start = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, 0);
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, StableHash.Fnv1A32(nameof(receivedValue)), NetworkFrameProtocol.ValueTypeInt32, value);
        if (extraBytes > 0)
            cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, 999, NetworkFrameProtocol.ValueTypeRawBytes, Payload(41, extraBytes));
        Check(cursor >= 0 && NetworkFrameWriter.EndVariableState(bytes, start, cursor, extraBytes > 0 ? 2 : 1), "Build protected state fixture");
        string method = nameof(TSMPNetworkGameObjectToggle.ToggleObject);
        cursor = NetworkFrameWriter.WriteRpcCall(bytes, cursor, 2, 0, StableHash.Fnv1A32(method), new object[] { method, eventId }, out int failed, out int error);
        Check(cursor >= 0 && NetworkFrameWriter.EndNetworkFrame(bytes, 0, 2), "Build protected RPC fixture: " + failed + "/" + error);
        Array.Resize(ref bytes, cursor);
        return bytes;
    }

    IEnumerator PayloadIntegrity(TSMPDecoder decoder, TSMPCodec[] codecs, Texture2D image)
    {
        var source = Target(640, 360);
        var toggleOwner = new GameObject("CRC RPC receiver");
        var toggleTarget = new GameObject("CRC toggle target");
        var toggle = toggleOwner.AddComponent<TSMPNetworkGameObjectToggle>();
        toggle.targetObject = toggleTarget;
        decoder.sourceTexture = source;
        decoder.flipY = true;
        decoder.sampleSize = 4;
        decoder.requirePayloadCrc32 = true;
        foreach (var codec in codecs)
        foreach (int mode in new[] { 2, 3 })
        {
            decoder.decodeSafetyMode = mode;
            byte[] packet = NetworkPayload(7); // 32 valid bytes: GPU padding and retained capacity are not data.
            Write(codec, image, packet, ++sequence, 4, protectPayload: true);
            Upload(image, source, true);
            decoder.DecodeNow();
            var wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Verify(decoder, packet, sequence, "Protected " + codec.displayName + "/" + mode);
            Check(decoder.lastPayloadCrcPresent && decoder.lastPayloadCrcVerified, "Protected payload was not verified");
            uint appliedBefore = Field<uint>(decoder, "_lastAppliedFrameIndex");
            Write(codec, image, packet, ++sequence, 4, protectPayload: true, corruptPayload: true);
            Upload(image, source, true);
            decoder.DecodeNow();
            wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(decoder.lastHeaderValid && !decoder.lastFrameValid && decoder.lastPayloadCrcPresent && !decoder.lastPayloadCrcVerified,
                "Damaged payload accepted in " + codec.displayName + "/" + mode);
            Check(Field<uint>(decoder, "_lastAppliedFrameIndex") == appliedBefore, "CRC rejection advanced applied frame");
        }
        decoder.decodeSafetyMode = 0;
        decoder.skipDuplicateFrames = true;
        decoder.bindingTargets = new Component[] { this, toggle };
        decoder.bindingNetworkIds = new ushort[] { 1, 2 };
        decoder.bindingVariableHashes = new[] { StableHash.Fnv1A32(nameof(receivedValue)), 0u };
        decoder.bindingValueTypes = new[] { (byte)NetworkValueType.Int32, (byte)NetworkFrameProtocol.ValueTypeUnsupported };
        decoder.bindingFieldNames = new[] { nameof(receivedValue), string.Empty };
        decoder.bindingDirections = new int[2];
        int eventId = 20000;
        foreach (bool prediction in new[] { false, true })
        {
            decoder.usePredictedReadback = prediction;
            decoder.useCombinedByteOutput = prediction;
            byte[] warm = MixedValueRpcPacket(41, ++eventId);
            Write(codecs[0], image, warm, ++sequence, 4, protectPayload: true);
            Upload(image, source, true); decoder.DecodeNow();
            var wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(decoder.lastFrameValid && decoder.lastPayloadCrcVerified, "Protected prediction warmup");
            receivedValue = -1; toggleTarget.SetActive(false);
            byte[] packet = MixedValueRpcPacket(42, ++eventId);
            uint frame = ++sequence;
            Write(codecs[0], image, packet, frame, 4, protectPayload: true, corruptPayload: true);
            Upload(image, source, true); decoder.DecodeNow();
            wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(!decoder.lastFrameValid && receivedValue == -1 && !toggleTarget.activeSelf, "CRC failure partially applied state or RPC");
            Write(codecs[0], image, packet, frame, 4, protectPayload: true);
            Upload(image, source, true); decoder.DecodeNow();
            wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(decoder.lastFrameValid && decoder.lastPayloadCrcVerified && receivedValue == 42 && toggleTarget.activeSelf,
                "CRC failure consumed frame baseline or RPC dedup entry");
        }
        decoder.overlapReadbacks = true;
        foreach (bool badHead in new[] { true, false })
        {
            receivedValue = -1; toggleTarget.SetActive(false);
            byte[] first = MixedValueRpcPacket(42, ++eventId);
            byte[] second = MixedValueRpcPacket(99, ++eventId, 701);
            Write(codecs[0], image, first, ++sequence, 4, protectPayload: true, corruptPayload: badHead);
            Upload(image, source, true); decoder.DecodeNow();
            Write(codecs[0], image, second, ++sequence, 4, protectPayload: true, corruptPayload: !badHead);
            Upload(image, source, true); decoder.DecodeNow();
            Check(decoder.pendingDecodeCount == 2, "Protected overlap did not use both slots");
            var wait = Drain(decoder);
            while (wait.MoveNext()) yield return wait.Current;
            Check(receivedValue == (badHead ? 99 : 42) && toggleTarget.activeSelf, "CRC slot context leaked or bad slot applied state/RPC");
        }
        decoder.decodeSafetyMode = 2;
        Write(codecs[0], image, NetworkPayload(0), ++sequence, 4);
        Upload(image, source, true); decoder.DecodeNow();
        decoder.requirePayloadCrc32 = false; // Changing policy must not weaken the in-flight request.
        var legacyWait = Drain(decoder);
        while (legacyWait.MoveNext()) yield return legacyWait.Current;
        Check(!decoder.lastFrameValid && !decoder.lastPayloadCrcPresent, "Strict decoder accepted missing CRC");
        decoder.requirePayloadCrc32 = false;
        Write(codecs[0], image, NetworkPayload(0), ++sequence, 4);
        Upload(image, source, true); decoder.DecodeNow();
        decoder.requirePayloadCrc32 = true; // The compatible request retains its captured policy.
        legacyWait = Drain(decoder);
        while (legacyWait.MoveNext()) yield return legacyWait.Current;
        Check(decoder.lastFrameValid && !decoder.lastPayloadCrcPresent && !decoder.lastPayloadCrcVerified, "Legacy compatibility lost");
        decoder.requirePayloadCrc32 = false;
        decoder.skipDuplicateFrames = false;
        decoder.bindingTargets = new Component[0];
        decoder.bindingNetworkIds = new ushort[0];
        decoder.bindingVariableHashes = new uint[0];
        decoder.bindingValueTypes = new byte[0];
        decoder.bindingFieldNames = new string[0];
        decoder.bindingDirections = new int[0];
        Drop(source); Drop(toggleOwner); Drop(toggleTarget);
        results.Add("PASS payload CRC: four codecs in readback/parse modes; state+RPC atomic rejection/retransmit in predicted/fallback paths; both bad-slot orders/different lengths; strict missing and legacy compatibility");
    }

    IEnumerator PayloadLengths(TSMPDecoder decoder, TSMPCodec codec, Texture2D texture)
    {
        var source = Target(640, 360);
        decoder.sourceTexture = source;
        decoder.flipY = true;
        decoder.sampleSize = 1;
        decoder.decodeSafetyMode = 3;
        foreach (int length in new[] { 200, 0, 400, 0, 1, 400, 0 })
        {
            byte[] bytes = NetworkPayload(length);
            byte[] previous = Field<byte[][]>(decoder, "_slotPayloads")[Field<int>(decoder, "_slotHead")];
            Write(codec, texture, bytes, ++sequence, 1);
            Upload(texture, source, true);
            decoder.DecodeNow();
            var drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            Verify(decoder, bytes, sequence, "Payload capacity " + length);
            Check(decoder.lastNetworkMessageCount == 1 && decoder.lastPayloadAvailableBytes == bytes.Length, "Actual network parsing bounds");
            if (previous != null && previous.Length >= bytes.Length) Check(ReferenceEquals(previous, Field<byte[]>(decoder, "_payloadBytes")), "GPU path reallocated sufficient capacity");
        }
        foreach (int length in new[] { 0, 7 })
        {
            Write(codec, texture, new byte[length], ++sequence, 1);
            Upload(texture, source, true);
            decoder.DecodeNow();
            var drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            Check(!decoder.lastFrameValid && decoder.lastError == "Payload is too small for NetworkFrame." && Field<int>(decoder, "_decodeStage") == 1, "Invalid length must not decode retained bytes");
        }
        decoder.decodeSafetyMode = 2;
        results.Add("PASS GPU payload capacity: seven growing/shrinking valid NetworkFrames, dirty retained tails ignored, zero/truncated payload rejected before readback");
        Drop(source);
    }

    IEnumerator Lifecycle(TSMPDecoder decoder, TSMPCodec codec, Texture2D a, Texture2D b)
    {
        decoder.flipY = true;
        decoder.sampleSize = 4;
        byte[] bytes = Payload(77);
        var source = Target(640, 360);
        decoder.sourceTexture = source;
        Write(codec, a, bytes, ++sequence, 4);
        Upload(a, source, true);
        decoder.DecodeNow();
        decoder.sourceTexture = null;
        Drop(source);
        var drain = Drain(decoder);
        while (drain.MoveNext()) yield return drain.Current;
        Verify(decoder, bytes, sequence, "Source removed during readback");

        source = Target(640, 360);
        decoder.sourceTexture = source;
        foreach (bool duringPayload in new[] { false, true })
        foreach (bool objectDisable in new[] { false, true })
        {
            Write(codec, a, bytes, ++sequence, 4);
            Upload(a, source, true);
            decoder.DecodeNow();
            if (duringPayload)
            {
                while (decoder.readbackInFlight && Field<int>(decoder, "_decodeStage") == 1) yield return null;
                Check(decoder.readbackInFlight && Field<int>(decoder, "_decodeStage") == 2, "Payload cancellation window not reached");
            }
            var old = Field<RenderTexture>(decoder, "_decodeSourceTexture");
            if (objectDisable) decoder.gameObject.SetActive(false); else decoder.enabled = false;
            Check(Field<bool[]>(decoder, "_slotDiscarded").All(value => value), "Disable did not discard pending slots");
            Check(old != null && old.IsCreated(), "Pending snapshot was released before readback completed");
            if (objectDisable) decoder.gameObject.SetActive(true); else decoder.enabled = true;
            drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            Check(!decoder.lastFrameValid, "Cancelled callback was applied");
            yield return null;
            Check(old == null, "Snapshot object leaked after disable");
            decoder.DecodeNow();
            drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            Verify(decoder, bytes, sequence, "Re-enabled decoder");
        }

        foreach (int mode in new[] { 1, 2 })
        {
            decoder.decodeSafetyMode = mode;
            decoder.DecodeNow();
            drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            Check(decoder.lastFrameValid, "Safety mode failed");
        }

        decoder.decodeSafetyMode = 2;
        Write(codec, a, bytes, ++sequence, 4, true);
        Write(codec, b, bytes, sequence + 10000, 4);
        Upload(a, source, true);
        decoder.DecodeNow();
        Upload(b, source, true);
        drain = Drain(decoder);
        while (drain.MoveNext()) yield return drain.Current;
        Check(!decoder.lastHeaderValid && !decoder.lastFrameValid && decoder.lastError.StartsWith("Header CRC mismatch"), "Invalid captured header was not rejected");

        decoder.decodeSafetyMode = 0;
        decoder.bindingTargets = new Component[] { this };
        decoder.bindingNetworkIds = new ushort[] { 1 };
        decoder.bindingVariableHashes = new[] { StableHash.Fnv1A32(nameof(receivedValue)) };
        decoder.bindingFieldNames = new[] { nameof(receivedValue) };
        decoder.bindingValueTypes = new[] { (byte)NetworkValueType.Int32 };
        decoder.bindingDirections = new int[1];
        receivedValue = -1;
        bytes = ValuePacket(42);
        Write(codec, a, bytes, ++sequence, 4);
        Write(codec, b, ValuePacket(99), sequence + 10000, 4);
        Upload(a, source, true);
        decoder.DecodeNow();
        Upload(b, source, true);
        drain = Drain(decoder);
        while (drain.MoveNext()) yield return drain.Current;
        Verify(decoder, bytes, sequence, "Variable application");
        Check(receivedValue == 42 && decoder.lastAppliedVariableCount == 1, "Applied later frame instead of captured value");
        decoder.skipDuplicateFrames = true;
        int skipped = decoder.skippedDuplicateFrameCount;
        Upload(a, source, true);
        decoder.DecodeNow();
        drain = Drain(decoder);
        while (drain.MoveNext()) yield return drain.Current;
        Check(decoder.skippedDuplicateFrameCount == skipped + 1, "Duplicate frame policy changed");
        decoder.skipDuplicateFrames = false;
        decoder.decodeSafetyMode = 2;
        Drop(source);
        results.Add("PASS source destruction, disable/re-enable during both stages, GameObject/component cancellation, resource cleanup, duplicate/safety modes, CRC rejection, native TransSync value=42 (later input=99)");
    }

    static byte[] ValuePacket(int value)
    {
        var bytes = new byte[29];
        int message = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, 1);
        int entry = NetworkFrameWriter.BeginVariableState(bytes, message, 1, 1);
        int data = NetworkFrameWriter.BeginVariableValue(bytes, entry, StableHash.Fnv1A32(nameof(receivedValue)), (int)NetworkValueType.Int32);
        Binary.WriteInt32LE(bytes, data, value);
        Check(NetworkFrameWriter.EndVariableValue(bytes, entry, data + 4), "Value packet");
        Check(NetworkFrameWriter.EndVariableState(bytes, message, data + 4, 1), "Message packet");
        Binary.WriteUInt16LE(bytes, NetworkFrameProtocol.NetworkMessageCountOffset, 1);
        return bytes;
    }

    public static uint[][] FrameOrderCases() => new[]
    {
        new uint[] { 100, 100, 256, 0, 0, 0, 1 },
        new uint[] { 100, 101, 256, 1, 0, 0, 1 },
        new uint[] { 101, 100, 256, 0, 0, 0, 1 },
        new uint[] { 255, 0, 256, 0, 0, 0, 1 },
        new uint[] { 256, 0, 256, 1, 0, 0, 1 },
        new uint[] { 257, 0, 256, 1, 0, 0, 1 },
        new uint[] { 256, 1, 256, 0, 0, 0, 1 },
        new uint[] { 511, 0, 512, 0, 0, 0, 1 },
        new uint[] { 512, 0, 512, 1, 0, 0, 1 },
        new uint[] { 1, 0, 1, 1, 0, 0, 1 },
        new uint[] { 0, 0, 1, 0, 0, 0, 1 },
        new uint[] { 1, 0, 0, 1, 0, 0, 1 },
        new uint[] { 0, 0, 0, 0, 0, 0, 1 },
        new uint[] { uint.MaxValue, 0, 256, 1, 0, 0, 1 },
        new uint[] { uint.MaxValue - 1, 1, 256, 1, 0, 0, 1 },
        new uint[] { 0, uint.MaxValue, 256, 0, 0, 0, 1 },
        new uint[] { 1, uint.MaxValue, 256, 0, 0, 0, 1 },
        new uint[] { 0, 2147483647, 256, 1, 0, 0, 1 },
        new uint[] { 0, 2147483648, 256, 0, 0, 0, 1 },
        new uint[] { 2147483648, 0, 256, 1, 0, 0, 1 },
        new uint[] { 2147483649, 1, 256, 0, 0, 0, 1 },
        new uint[] { 100, 99, 256, 1, 1, 0, 1 },
        new uint[] { 100, 100, 256, 1, 1, 0, 1 },
        new uint[] { 255, 0, 256, 1, 0, 1, 1 },
        new uint[] { 100, 100, 256, 1, 0, 1, 1 },
        new uint[] { 100, 99, 256, 1, 0, 0, 0 },
        new uint[] { uint.MaxValue, uint.MaxValue, 256, 0, 0, 0, 1 },
        new uint[] { 0, 1, 256, 1, 0, 0, 1 },
        new uint[] { 2147483646, 0, 2147483647, 0, 0, 0, 1 },
        new uint[] { 2147483647, 0, 2147483647, 1, 0, 0, 1 }
    };

    IEnumerator FrameOrder(TSMPDecoder decoder, TSMPCodec codec, Texture2D texture)
    {
        var window = typeof(TSMPDecoder).GetField("frameWindowSize");
        if (window == null) yield break;
        Check((int)window.GetValue(decoder) == 256, "Default frame window");
        var source = Target(640, 360);
        decoder.sourceTexture = source;
        decoder.decodeSafetyMode = 0;
        foreach (var item in FrameOrderCases())
        {
            typeof(TSMPDecoder).GetField("_hasAppliedFrame", Private).SetValue(decoder, item[6] != 0);
            typeof(TSMPDecoder).GetField("_lastAppliedStreamId", Private).SetValue(decoder, item[4]);
            typeof(TSMPDecoder).GetField("_lastAppliedFrameIndex", Private).SetValue(decoder, item[0]);
            window.SetValue(decoder, (int)item[2]);
            decoder.skipDuplicateFrames = item[5] == 0;
            receivedValue = -1;
            int duplicates = decoder.skippedDuplicateFrameCount;
            int older = Field<int>(decoder, "skippedOutOfOrderFrameCount");
            Write(codec, texture, ValuePacket(42), item[1], 4);
            Upload(texture, source, true);
            decoder.DecodeNow();
            var drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            bool accept = item[3] != 0;
            Check(decoder.lastHeaderValid && decoder.lastFrameValid, "Window header/readback");
            Check(receivedValue == (accept ? 42 : -1), "Window application " + string.Join(",", item));
            Check(Field<uint>(decoder, "_lastAppliedFrameIndex") == (accept ? item[1] : item[0]), "Window anchor");
            Check(decoder.skippedDuplicateFrameCount == duplicates + (!accept && item[0] == item[1] ? 1 : 0), "Duplicate counter");
            Check(Field<int>(decoder, "skippedOutOfOrderFrameCount") == older + (!accept && item[0] != item[1] ? 1 : 0), "Older counter");
        }
        window.SetValue(decoder, 256);
        decoder.skipDuplicateFrames = true;
        typeof(TSMPDecoder).GetField("_lastAppliedFrameIndex", Private).SetValue(decoder, 256u);
        Write(codec, texture, new byte[] { 0 }, 0, 4);
        Upload(texture, source, true);
        decoder.DecodeNow();
        var pending = Drain(decoder);
        while (pending.MoveNext()) yield return pending.Current;
        Check(!decoder.lastFrameValid && Field<uint>(decoder, "_lastAppliedFrameIndex") == 256u, "Invalid zero moved anchor");
        foreach (uint index in new uint[] { 0, 1, 2, 1 })
        {
            receivedValue = -1;
            Write(codec, texture, ValuePacket((int)index), index, 4);
            Upload(texture, source, true);
            decoder.DecodeNow();
            pending = Drain(decoder);
            while (pending.MoveNext()) yield return pending.Current;
            Check(receivedValue == (Field<uint>(decoder, "_lastAppliedFrameIndex") == index ? (int)index : -1), "Post-restart order");
        }
        Check(Field<uint>(decoder, "_lastAppliedFrameIndex") == 2u, "Restart sequence anchor");
        decoder.skipDuplicateFrames = false;
        decoder.decodeSafetyMode = 2;
        Drop(source);
        results.Add("PASS 30 frame-window cases, duplicate/older counters, native variable application, invalid reset payload and 256 -> 0 -> 1 -> 2 -> 1 sequence");
    }

    IEnumerator Precision()
    {
        var capture = typeof(TSMPDecoder).Assembly.GetType("K13A.TSMP.DecoderSnapshotRuntime").GetMethod("Capture");
        var source = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
        var values = Enumerable.Range(0, 64).Select(i => new Color(i * 0.031337f - 0.125f, i * 0.004637f, 0.998765f - i * 0.000123f, 1f)).ToArray();
        source.SetPixels(values);
        source.Apply();
        var parameters = new object[] { source, null, null };
        var snapshot = (RenderTexture)capture.Invoke(null, parameters);
        Check(snapshot != null && snapshot.format == RenderTextureFormat.ARGBFloat && !snapshot.sRGB, "Float32 snapshot format");
        var request = AsyncGPUReadback.Request(snapshot, 0);
        while (!request.done) yield return null;
        Check(!request.hasError, "Float32 readback");
        var actual = request.GetData<Color>();
        for (int i = 0; i < values.Length; i++)
            for (int channel = 0; channel < 4; channel++)
                Check(Mathf.Abs(values[i][channel] - actual[i][channel]) < 0.0000001f, "Snapshot quantization at " + i + "/" + channel);
        Drop(source);
        Drop(snapshot);
        results.Add("PASS Float32 pixel precision (<1e-7), negative/HDR values and linear storage");
    }

    IEnumerator Timing(TSMPDecoder decoder, TSMPCodec codec)
    {
#if UNITY_EDITOR
        UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, true);
#endif
        Profiler.enabled = true;
        foreach (var size in new[] { new Vector2Int(640, 360), new Vector2Int(1920, 1080), new Vector2Int(3840, 2160) })
        {
            var source = Target(size.x, size.y);
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true);
            byte[] bytes = Payload(91);
            Write(codec, texture, bytes, ++sequence, 4);
            Upload(texture, source, true);
            decoder.sourceTexture = source;
            decoder.DecodeNow();
            var drain = Drain(decoder);
            while (drain.MoveNext()) yield return drain.Current;
            Verify(decoder, bytes, sequence, "Resize " + size);
            var snapshot = Field<RenderTexture>(decoder, "_decodeSourceTexture");
            Check(snapshot.width == size.x && snapshot.height == size.y, "Snapshot resize");
            var sampler = CustomSampler.Create("TSMP Snapshot Copy " + size.x, true);
            var recorder = sampler.GetRecorder();
            recorder.enabled = true;
            var commands = new CommandBuffer();
            commands.BeginSample(sampler);
            for (int i = 0; i < 32; i++) commands.Blit(source, snapshot);
            commands.EndSample(sampler);
            var samples = new List<double>();
            for (int frame = 0; frame < 25; frame++)
            {
                Graphics.ExecuteCommandBuffer(commands);
                var request = AsyncGPUReadback.Request(snapshot, 0, 0, 1, 0, 1, 0, 1);
                request.WaitForCompletion();
                Check(!request.hasError, "Timing readback");
                yield return null;
                if (frame >= 10 && recorder.gpuSampleBlockCount == 1 && recorder.gpuElapsedNanoseconds > 0)
                    samples.Add(recorder.gpuElapsedNanoseconds / 1000.0 / 32);
            }
            recorder.enabled = false;
            commands.Dispose();
            samples.Sort();
            results.Add("COPY " + size.x + "x" + size.y + ": VRAM=" + (size.x * (long)size.y * 16) +
                " bytes; median GPU us=" + (samples.Count == 0 ? "unavailable" : samples[samples.Count / 2].ToString("F3")) +
                "; samples=" + samples.Count + "; batch=32; copy-only, not full decode or VRChat timing");
            Drop(source);
            Drop(texture);
        }
        Profiler.enabled = false;
    }
}
#endif
