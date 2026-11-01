#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Production mode-zero controls followed by forced, same-snapshot diagnostic
// mode 0/1/2 draws. Diagnostic candidate failures are recorded, never accepted.
public sealed class ModeCoverageValidation : MonoBehaviour
{
    const string SessionKey = "TSMP.ModeCoverage";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Root => Environment.GetEnvironmentVariable("TSMP_ROBUSTNESS_RESULTS");
    readonly List<Object> owned = new List<Object>();
    readonly List<Material> materials = new List<Material>();
    readonly List<string> details = new List<string>(), rows = new List<string>(), hashes = new List<string>(), controlRows = new List<string>();
    int assertions, controls, diagnosticFailures, aliasControls;
    string currentDirectory;
    uint sequence = 600000;
    sealed class Configuration { public string name; public TSMPCodec codec; public bool alias; }

    public static void Run() { PlayerSettings.colorSpace = ColorSpace.Gamma; Begin(); }
    public static void RunLinear() { PlayerSettings.colorSpace = ColorSpace.Linear; Begin(); }
    static void Begin()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour)) throw new InvalidOperationException("Use a dedicated SDK-free project.");
        if (string.IsNullOrWhiteSpace(Root)) throw new InvalidOperationException("TSMP_ROBUSTNESS_RESULTS required.");
        Directory.CreateDirectory(Root); if (File.Exists(Path.Combine(Root, "result.txt"))) throw new InvalidOperationException("Use fresh results.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SessionState.SetBool(SessionKey, true); Attach(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered;
    }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false); new GameObject("Recovery mode coverage").AddComponent<ModeCoverageValidation>();
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
        Own(new GameObject("Mode coverage frame pump")).AddComponent<Camera>().cullingMask = 0;
        details.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        details.Add("Actual package normal decoder first, then private RunByteDecodeTo on its retained validated-context snapshot. Forced candidates are diagnostic and never applied.");
        details.Add("Variable RGB16 8/8/4 is explicitly a palette-alias diagnostic: 256 channel levels map into 209 integer Color32 values. It is not claimed to support exact arbitrary bytes.");
        rows.Add("case,configuration,sourceFormat,sourceGraphicsFormat,sample,flip,payloadBytes,stage,mode,productionExact,productionCrc,byteMismatches,bitMismatches,crcValid,status,lut,sourceSha256,actualSha256,referenceSha256");
        hashes.Add("path,sha256");
        controlRows.Add("case,configuration,sourceFormat,sample,flip,payloadBytes,headerValid,frameValid,payloadCrcVerified,productionExact,headerByteMismatches,payloadByteMismatches,headerAttempts,payloadAttempts,actualHeaderSha256,actualPayloadSha256,error");
        var stack = new Stack<IEnumerator>(); stack.Push(Validate());
        while (stack.Count > 0)
        {
            IEnumerator next = stack.Peek(); bool moved;
            try { moved = next.MoveNext(); }
            catch (Exception error) { Finish(error); yield break; }
            if (!moved) { stack.Pop(); continue; } if (next.Current is IEnumerator nested) { stack.Push(nested); continue; } yield return next.Current;
        }
        Finish(null);
    }
    T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    void Check(bool condition, string error) { assertions++; if (!condition) throw new InvalidOperationException(error); }
    static T Field<T>(TSMPDecoder decoder, string name) => (T)typeof(TSMPDecoder).GetField(name, Private | BindingFlags.Public).GetValue(decoder);
    static void Set(TSMPDecoder decoder, string name, int value) => typeof(TSMPDecoder).GetField(name, Private).SetValue(decoder, value);
    static string Sha(byte[] bytes)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static string C(object value) => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(C));
    static int Differences(byte[] a, byte[] b, out int bits)
    {
        int count = Math.Abs(a.Length - b.Length); bits = count * 8;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            int delta = a[i] ^ b[i]; if (delta != 0) count++; for (; delta != 0; delta >>= 1) bits += delta & 1;
        }
        return count;
    }
    void Flush()
    {
        File.WriteAllLines(Path.Combine(Root, "modes.csv"), rows); File.WriteAllLines(Path.Combine(Root, "source-hashes.csv"), hashes);
        File.WriteAllLines(Path.Combine(Root, "controls.csv"), controlRows);
    }
    void Finish(Exception error)
    {
        if (error != null) details.Add(error.ToString());
        details.Add("Production controls=" + controls + "; alias-setting rejected controls=" + aliasControls + "; forced-candidate mismatches=" + diagnosticFailures + "; assertions=" + assertions);
        details.Add("PASS means required clean non-alias production controls and mode-zero parity hold, no wrong CRC-valid candidate is accepted, and all candidates were measured. It does not mean every forced recovery candidate was exact.");
        Flush(); File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(details));
        foreach (Object item in owned.AsEnumerable().Reverse()) { if (item is RenderTexture rt) rt.Release(); if (item != null) Object.DestroyImmediate(item); }
        foreach (Material material in materials) if (material != null) Object.DestroyImmediate(material);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    TSMPCodec Codec(string name)
    {
        string path = "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Check(prefab != null, "Missing " + path);
        TSMPCodec codec = DecoderSnapshotValidation.InstantiateCodec(prefab.GetComponent<TSMPCodec>(), materials); Own(codec.gameObject); return codec;
    }
    IEnumerator Drain(TSMPDecoder decoder)
    {
        // Correctness only: first use can synchronously compile a new shader
        // while the main thread is inside the header callback. No runtime
        // timeout or performance measurement is changed by this allowance.
        float started = Time.realtimeSinceStartup, deadline = started + 180;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < deadline) yield return null;
        if (decoder.readbackInFlight)
        {
            yield return TimeoutDiagnostics(decoder, Time.realtimeSinceStartup - started);
            Check(false, "Mode coverage readback exceeded 180 seconds; see timeout diagnostics. stage=" + Field<int>(decoder, "_decodeStage") + "; pending=" + decoder.pendingDecodeCount);
        }
    }
    IEnumerator TimeoutDiagnostics(TSMPDecoder decoder, float elapsed)
    {
        string directory = currentDirectory ?? Root; Directory.CreateDirectory(directory);
        var messages = new List<string>
        {
            "ElapsedSeconds=" + elapsed.ToString("R", CultureInfo.InvariantCulture), "DeadlineSeconds=180", "Stage=" + Field<int>(decoder, "_decodeStage"),
            "Pending=" + decoder.pendingDecodeCount, "SlotHead=" + Field<int>(decoder, "_slotHead"), "ActiveSlot=" + Field<int>(decoder, "_activeSlot"),
            "SlotStates=" + string.Join(",", Field<int[]>(decoder, "_slotStates")), "SlotStages=" + string.Join(",", Field<int[]>(decoder, "_slotStages")),
            "SlotDiscarded=" + string.Join(",", Field<bool[]>(decoder, "_slotDiscarded")), "ReadbackInFlight=" + decoder.readbackInFlight,
            "HeaderValid=" + decoder.lastHeaderValid, "FrameValid=" + decoder.lastFrameValid, "PayloadCrcVerified=" + decoder.lastPayloadCrcVerified,
            "RequestedByteCount=" + decoder.lastRequestedByteCount, "PayloadDataBytes=" + Field<int>(decoder, "_payloadDataBytes"),
            "Error=" + decoder.lastError, "CPU buffers below may contain prior/pending data and are not claimed current payload results."
        };
        foreach (string field in new[] { "_headerBytes", "_payloadBytes", "_readbackBytes" })
        {
            byte[] buffer = Field<byte[]>(decoder, field);
            if (buffer != null) File.WriteAllBytes(Path.Combine(directory, "timeout-cpu" + field + ".bin"), buffer);
            messages.Add(field + "Capacity=" + (buffer == null ? -1 : buffer.Length));
        }
        File.WriteAllLines(Path.Combine(directory, "timeout.txt"), messages);
        var requests = new List<AsyncGPUReadbackRequest>(); var labels = new List<string>(); var seen = new HashSet<int>();
        var targets = new Dictionary<string, RenderTexture>
        {
            { "source", decoder.sourceTexture as RenderTexture }, { "snapshot", Field<RenderTexture>(decoder, "_decodeSourceTexture") },
            { "header-target", Field<RenderTexture>(decoder, "_headerByteTexture") }, { "payload-target", Field<RenderTexture>(decoder, "_requestByteTexture") },
            { "combined-target", Field<RenderTexture>(decoder, "_combinedByteTexture") }
        };
        foreach (var pair in targets)
        {
            RenderTexture target = pair.Value;
            if (target == null || !target.IsCreated() || !seen.Add(target.GetInstanceID())) continue;
            messages.Add(pair.Key + "=" + target.width + "x" + target.height + "; format=" + target.graphicsFormat);
            labels.Add(pair.Key); requests.Add(AsyncGPUReadback.Request(target, 0));
        }
        // Bounded asynchronous best effort; never WaitForCompletion on a
        // possibly stalled GPU merely to obtain diagnostic pixels.
        float diagnosticDeadline = Time.realtimeSinceStartup + 5;
        var collected = new bool[requests.Count];
        Action<int> collect = index =>
        {
            AsyncGPUReadbackRequest request = requests[index];
            if (!request.done) return;
            collected[index] = true;
            if (!request.hasError)
            {
                byte[] actual = request.GetData<byte>().ToArray(); File.WriteAllBytes(Path.Combine(directory, "timeout-" + labels[index] + ".native.bin"), actual);
                messages.Add(labels[index] + "Readback=SUCCESS; bytes=" + actual.Length + "; sha256=" + Sha(actual));
            }
            else messages.Add(labels[index] + "Readback=ERROR");
        };
        while (collected.Any(value => !value) && Time.realtimeSinceStartup < diagnosticDeadline)
        {
            for (int index = 0; index < requests.Count; index++) if (!collected[index]) collect(index);
            if (collected.Any(value => !value)) yield return null;
        }
        for (int index = 0; index < requests.Count; index++) if (!collected[index]) messages.Add(labels[index] + "Readback=UNAVAILABLE_WITHIN_5_SECONDS");
        messages.Add("ReadbackInFlightAfterDiagnostic=" + decoder.readbackInFlight); messages.Add("PendingAfterDiagnostic=" + decoder.pendingDecodeCount);
        File.WriteAllLines(Path.Combine(directory, "timeout.txt"), messages);
    }
    byte[] ExpectedHeader(TSMPCodec codec, Texture2D image, byte[] payload, uint frame, int sample)
    {
        FrameHeader header = FrameHeader.CreateDefault(); header.BlockSize = 8; header.ActiveWidthBlocks = (ushort)(image.width / 8); header.ActiveHeightBlocks = (ushort)(image.height / 8);
        header.CodecId = codec.codecId; header.SymbolMode = (byte)codec.SymbolMode; header.PayloadSize = (ushort)payload.Length; header.FrameIndex = frame; header.DecodeSampleSize = (byte)sample;
        byte[] options = codec.GetCodecOptionBytes() ?? new byte[0]; header.CodecOptionLength = (byte)options.Length;
        if (options.Length > 0) header.CodecOption0 = options[0]; if (options.Length > 1) header.CodecOption1 = options[1]; if (options.Length > 2) header.CodecOption2 = options[2];
        if (options.Length > 3) header.CodecOption3 = options[3]; if (options.Length > 4) header.CodecOption4 = options[4];
        byte[] bytes = new byte[FrameHeader.Size]; header.WriteTo(bytes, 0);
        Check(FrameHeaderWriter.TryWritePayloadCrc32(bytes, 0, payload, payload.Length, null), "Independent header CRC build failed."); return bytes;
    }
    byte[] Read(RenderTexture target, int count, bool nativeFormat = false, string rawPath = null, Action beforePaddingFailure = null)
    {
        AsyncGPUReadbackRequest request = nativeFormat ? AsyncGPUReadback.Request(target, 0) : AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
        request.WaitForCompletion(); Check(!request.hasError, "Mode coverage GPU readback failed."); byte[] all = request.GetData<byte>().ToArray();
        if (!string.IsNullOrWhiteSpace(rawPath)) File.WriteAllBytes(rawPath, all);
        if (!nativeFormat)
        {
            bool paddingValid = all.Skip(count).All(value => value == 0);
            if (!paddingValid && beforePaddingFailure != null) beforePaddingFailure();
            Check(paddingValid, "Mode coverage byte-target padding is not zero.");
        }
        return nativeFormat ? all : all.Take(count).ToArray();
    }
    void PreserveNative(string directory, byte[] raster, RenderTexture source, RenderTexture snapshot, RenderTextureFormat format)
    {
        File.WriteAllBytes(Path.Combine(directory, "raster.rgba32.bin"), raster);
        if (source != null && source.IsCreated()) File.WriteAllBytes(Path.Combine(directory, "source.native.bin"), Read(source, 0, true));
        if (snapshot != null && snapshot.IsCreated()) File.WriteAllBytes(Path.Combine(directory, "snapshot.native.bin"), Read(snapshot, 0, true));
        File.WriteAllText(Path.Combine(directory, "source-format.txt"), "RenderTexture=" + format + "\nGraphicsFormat=" + (source == null ? "missing" : source.graphicsFormat.ToString()) +
            "\nSnapshotGraphicsFormat=" + (snapshot == null ? "missing" : snapshot.graphicsFormat.ToString()) + "\nWidth=640\nHeight=360\nColorSpace=" + QualitySettings.activeColorSpace +
            "\nNo float normalization, re-quantization or post-comparison rounding was applied.\n");
    }
    IEnumerator Validate()
    {
        foreach (string file in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) == "ModeCoverageValidation.cs" || Path.GetFileName(path) == "DecoderSnapshotValidation.cs"))
            hashes.Add(Row(Path.GetFullPath(file), Sha(File.ReadAllBytes(file))));
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.luma4", "com.kibalab.tsmp.codec.rgb16", "com.kibalab.tsmp.codec.color256" })
            foreach (string file in Directory.GetFiles("Packages/" + package + "/Runtime", "*", SearchOption.AllDirectories).Where(path => path.EndsWith(".cs") || path.EndsWith(".shader") || path.EndsWith(".cginc"))) hashes.Add(Row(Path.GetFullPath(file), Sha(File.ReadAllBytes(file))));
        TSMPCodec luma = Codec("Luma4"), color = Codec("Color256"); var configurations = new List<Configuration>
        {
            new Configuration { name = "Luma4", codec = luma }, new Configuration { name = "Color256RobustRefine", codec = color }
        };
        foreach (int[] bits in new[] { new[] { 1, 1, 1 }, new[] { 3, 3, 2 }, new[] { 5, 6, 4 }, new[] { 8, 8, 4 } })
        {
            var codec = (TSMPCodecRGB16)Codec("RGB16"); codec.rBits = bits[0]; codec.gBits = bits[1]; codec.bBits = bits[2]; codec.localRefine = true; codec.refineRadius = 2;
            configurations.Add(new Configuration { name = "RGB16_" + string.Join("_", bits), codec = codec, alias = bits[0] == 8 });
        }
        foreach (var option in new[] { new { bits = new[] { 1, 1, 1 }, refine = false }, new { bits = new[] { 1, 2, 2 }, refine = true }, new { bits = new[] { 1, 2, 2 }, refine = false } })
        {
            var codec = (TSMPCodecRGB16)Codec("RGB16"); codec.rBits = option.bits[0]; codec.gBits = option.bits[1]; codec.bBits = option.bits[2]; codec.localRefine = option.refine; codec.refineRadius = 2;
            configurations.Add(new Configuration { name = "RGB16_" + string.Join("_", option.bits) + (option.refine ? "_Refine" : "_Direct"), codec = codec });
        }
        ((TSMPCodecColor256)color).robustChannelDecode = true; ((TSMPCodecColor256)color).localRefine = true;
        TSMPDecoder decoder = Own(new GameObject("Actual package mode decoder")).AddComponent<TSMPDecoder>(); decoder.applyEveryFrame = false; decoder.skipDuplicateFrames = false;
        decoder.requirePayloadCrc32 = true; decoder.decodeSafetyMode = 3; decoder.overlapReadbacks = false; decoder.debugLog = false; decoder.blockSize = 8;
        decoder.maxCrcRecoveryAttempts = 0; decoder.payloadByteTexture = Own(DecoderSnapshotValidation.Target(2048, 1));
        Texture2D image = Own(new Texture2D(640, 360, TextureFormat.RGBA32, false, true)); image.filterMode = FilterMode.Point;
        RenderTexture headerOutput = Own(DecoderSnapshotValidation.Target(14, 1)), payloadOutput = Own(DecoderSnapshotValidation.Target(2048, 1));
        MethodInfo run = typeof(TSMPDecoder).GetMethod("RunByteDecodeTo", Private); Check(run != null, "Production shader path missing."); uint[] crc = Crc32Runtime.EnsureFastTable(null);
        yield return null;
        foreach (Configuration configuration in configurations)
        foreach (RenderTextureFormat format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBFloat })
        {
            RenderTexture source = Own(DecoderSnapshotValidation.Target(640, 360, format)); decoder.sourceTexture = source;
            decoder.codecHandlers = configuration.codec == luma ? new[] { luma } : new[] { luma, configuration.codec };
            foreach (int sample in new[] { 1, 4 })
            foreach (bool flip in new[] { true, false })
            foreach (int count in new[] { 32, 1023 })
            {
                Check(configuration.codec.GetPayloadCapacityBytes(640, 360, 8) >= count, "Fixture exceeds real codec capacity.");
                byte[] payload = DecoderSnapshotValidation.NetworkPayload(count - 25); Check(payload.Length == count, "Wrong fixture byte count."); uint frame = ++sequence;
                byte[] header = ExpectedHeader(configuration.codec, image, payload, frame, sample);
                DecoderSnapshotValidation.Write(configuration.codec, image, payload, frame, sample, protectPayload: true);
                byte[] raster = image.GetRawTextureData<byte>().ToArray(); string rasterSha = Sha(raster);
                string id = frame.ToString(CultureInfo.InvariantCulture), directory = Path.Combine(Root, id); Directory.CreateDirectory(directory);
                currentDirectory = directory;
                File.WriteAllBytes(Path.Combine(directory, "header.bin"), header); File.WriteAllBytes(Path.Combine(directory, "payload.bin"), payload);
                File.WriteAllBytes(Path.Combine(directory, "raster.rgba32.bin"), raster);
                File.WriteAllBytes(Path.Combine(directory, "raster.png"), image.EncodeToPNG());
                decoder.flipY = flip; decoder.sampleSize = sample; decoder.ResetDecodeDiagnostics();
                Graphics.Blit(image, source, new Vector2(1, flip ? 1 : -1), new Vector2(0, flip ? 0 : 1)); decoder.DecodeNow(); yield return Drain(decoder);
                byte[] actualHeader = (Field<byte[]>(decoder, "_headerBytes") ?? new byte[0]).Take(header.Length).ToArray();
                byte[] productionBuffer = Field<byte[]>(decoder, "_payloadBytes") ?? new byte[0];
                int productionCount = Mathf.Clamp(Field<int>(decoder, "_payloadDataBytes"), 0, productionBuffer.Length);
                byte[] productionPayload = productionBuffer.Take(productionCount).ToArray();
                bool productionExact = payload.SequenceEqual(productionPayload) && decoder.lastFrameValid && decoder.lastPayloadCrcVerified;
                RenderTexture snapshot = Field<RenderTexture>(decoder, "_decodeSourceTexture");
                File.WriteAllBytes(Path.Combine(directory, "production-header.bin"), actualHeader); File.WriteAllBytes(Path.Combine(directory, "production-payload.bin"), productionPayload);
                controlRows.Add(Row(id, configuration.name, format, sample, flip, count, decoder.lastHeaderValid, decoder.lastFrameValid, decoder.lastPayloadCrcVerified, productionExact,
                    Differences(actualHeader, header, out _), Differences(productionPayload, payload, out _), decoder.headerRecoveryAttemptCount, decoder.payloadRecoveryAttemptCount, Sha(actualHeader), Sha(productionPayload), decoder.lastError));
                controls++;
                bool requiredFailure = (!configuration.alias && !productionExact) || !decoder.lastHeaderValid || decoder.lastFrameIndex != frame || !header.SequenceEqual(actualHeader)
                    || decoder.headerRecoveryAttemptCount != 0 || decoder.payloadRecoveryAttemptCount != 0 || snapshot == null || !snapshot.IsCreated();
                if (requiredFailure || !productionExact) PreserveNative(directory, raster, source, snapshot, format);
                Check(decoder.headerRecoveryAttemptCount == 0 && decoder.payloadRecoveryAttemptCount == 0, "Clean max-zero production performed recovery.");
                Check(decoder.lastHeaderValid && decoder.lastFrameIndex == frame, "Clean production header rejected or wrong identity.");
                Check(header.SequenceEqual(actualHeader), "Production header differs from independent bytes.");
                if (!configuration.alias) Check(productionExact, "Clean non-alias production rejected: " + configuration.name + "/" + format + "/" + sample + "/" + flip + "/" + count + ": " + decoder.lastError);
                else if (!productionExact) { aliasControls++; Check(!decoder.lastFrameValid && !decoder.lastPayloadCrcVerified, "Aliased payload was incorrectly accepted."); }
                Check(snapshot != null && snapshot.IsCreated(), "Retained actual snapshot missing.");
                bool preserveSource = !productionExact;
                foreach (bool isHeader in new[] { true, false })
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    Set(decoder, "_decodeStage", isHeader ? 1 : 2); Set(decoder, "_headerRecoveryAttempt", isHeader ? mode : 0); Set(decoder, "_payloadRecoveryAttempt", isHeader ? 0 : mode);
                    RenderTexture output = isHeader ? headerOutput : payloadOutput;
                    int start = isHeader ? 2 * (640 / 8) : Field<int>(decoder, "_payloadStartBlock"), symbolMode = isHeader ? 0 : Field<int>(decoder, "_payloadSymbolMode");
                    Check((bool)run.Invoke(decoder, new object[] { start, isHeader ? header.Length : payload.Length, symbolMode, output, false }), "Production forced draw unavailable.");
                    string outputName = (isHeader ? "header" : "payload") + "-mode" + mode;
                    byte[] actual = Read(output, isHeader ? header.Length : payload.Length, false, Path.Combine(directory, outputName + ".target-rgba32.bin"), () => PreserveNative(directory, raster, source, snapshot, format)), expected = isHeader ? header : payload;
                    int mismatches = Differences(actual, expected, out int bitMismatches);
                    bool valid = isHeader ? FrameHeaderReader.TryValidate(actual, 0, crc, out _, out _, out _, out _, out _, out _)
                        : FrameHeaderReader.TryValidatePayload(header, 0, actual, actual.Length, true, crc, out _, out _, out _, out _);
                    File.WriteAllBytes(Path.Combine(directory, outputName + ".bin"), actual);
                    bool normalMatches = actual.SequenceEqual(isHeader ? header : productionPayload);
                    if (mismatches != 0 || (mode == 0 && !normalMatches)) PreserveNative(directory, raster, source, snapshot, format);
                    Material material = (isHeader ? luma : configuration.codec).selectedDecodeMaterial;
                    rows.Add(Row(id, configuration.name, format, source.graphicsFormat, sample, flip, count, isHeader ? "Header" : "Payload", mode,
                        productionExact, decoder.lastPayloadCrcVerified, mismatches, bitMismatches, valid, mismatches == 0 ? "EXACT" : valid ? "CRC_ACCEPTED_WRONG" : "REJECTED_DIAGNOSTIC", material.IsKeywordEnabled("TSMP_CALIBRATION_LUT"), rasterSha, Sha(actual), Sha(expected)));
                    Check(mismatches == 0 || !valid, "Wrong forced candidate passed CRC.");
                    if (mode == 0) Check(normalMatches, "Forced mode-zero differs from actual normal output.");
                    if (mismatches != 0) { diagnosticFailures++; preserveSource = true; }
                    if (!isHeader && !material.HasProperty("_RecoveryMode")) Check(normalMatches, "Unsupported direct mode changed ordinary bytes.");
                    if (configuration.name == "Luma4" && sample == 1) Check(!material.IsKeywordEnabled("TSMP_CALIBRATION_LUT"), "Luma4 sample-one unexpectedly used LUT.");
                }
                if (preserveSource)
                {
                    PreserveNative(directory, raster, source, snapshot, format);
                }
                Flush(); yield return null;
            }
        }
        details.Add("PASS retained same-snapshot forced mode-zero parity and strict CRC comparison; all mode1/2 candidates recorded independently of normal production acceptance.");
    }
}
#endif
