#if !UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Decodes externally prepared images only. Encoding, streaming and live pacing belong to the caller.
public static class TransportMeasurementValidation
{
    [Serializable] public sealed class Manifest { public Profile[] profiles; }
    [Serializable] public sealed class Profile
    {
        public string name;
        public string framesDirectory;
        public string expectedDirectory;
        public string expectedPayloadDirectory;
        public string fixture;
        public FrameMapping[] frameMappings;
    }
    [Serializable] public sealed class FrameMapping
    {
        public string file;
        public string expectedFile;
        public uint frameIndex;
        public bool frameIndexSpecified;
        public string payloadFile;
        public bool expectedFrameUnknown;
    }
    sealed class FrameCase
    {
        public string file, actualPath, expectedPath, payloadPath;
        public uint frameIndex;
        public bool unexpected, expectedFrameUnknown;
    }

    const string SessionKey = "TSMP.TransportMeasurement";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Regex FrameName = new Regex(@"^frame-(\d{3}|\d{6})\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly List<Object> Owned = new List<Object>();
    static readonly List<string> Rows = new List<string>();
    static readonly List<string> Details = new List<string>();
    static readonly List<string> Summaries = new List<string>();
    static IEnumerator work;
    static string root, manifestPath;
    static bool failed;

    public static void Run()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour))
            throw new InvalidOperationException("TransportMeasurement requires a dedicated SDK-free project.");
        root = RequiredEnvironment("TSMP_TRANSPORT_RESULTS");
        manifestPath = RequiredEnvironment("TSMP_TRANSPORT_MANIFEST");
        Directory.CreateDirectory(root);
        if (File.Exists(Path.Combine(root, "result.txt")) || File.Exists(Path.Combine(root, "frames.csv")))
            throw new InvalidOperationException("Use a new measurement results directory; existing evidence is preserved.");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Transport manifest missing", manifestPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(SessionKey, true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    static string RequiredEnvironment(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(name + " is required.");
        return Path.GetFullPath(value);
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false);
        root = RequiredEnvironment("TSMP_TRANSPORT_RESULTS");
        manifestPath = RequiredEnvironment("TSMP_TRANSPORT_MANIFEST");
        Rows.Clear(); Details.Clear(); Summaries.Clear(); Owned.Clear(); failed = false;
        Rows.Add("profile,file,expectedFile,expectedFrame,expectedFrameUnknown,actualFrame,actualFrameTrusted,width,height,expectedBytes,actualBytes,retainedPayloadDataBytes,actualBytesTrusted,headerValid,frameValid,payloadCrcPresent,payloadCrcVerified,headerCrcExpected,headerCrcActual,payloadCrcExpected,payloadCrcActual,rejectionStage,byteMismatches,status,error");
        Summaries.Add("profile,expectedFrames,observedPngs,exact,rejected,wrong,corrupt,missing,timeouts,inputErrors,pass");
        Details.Add("Decode-only imported PNG measurement: Native TSMP GPU decoder, Luma4, original PNG dimensions, block8, sample4, parse-only, required payload CRC.");
        Details.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        Details.Add("Manifest=" + manifestPath);
        Details.Add("Default mapping uses numeric PNG IDs, not file enumeration order. Explicit frameMappings define the expected output list when supplied.");
        Details.Add("A mapping's nonzero frameIndex overrides the expected PNG ID. For explicit zero override set frameIndexSpecified=true; omitted frameIndex falls back to the expected PNG ID.");
        Details.Add("actualBytes is empty unless current payload readback is established; retainedPayloadDataBytes is internal state and may be stale after a rejected header.");
        Details.Add("Header/payload CRC diagnostics are emitted only when the decoder reached the respective check. No state or RPC is applied.");
        Details.Add("expectedFrameUnknown mappings still undergo strict GPU CRC checks, but even a valid frame is WRONG because independent full-byte comparison is unavailable.");
        Details.Add("This does not measure OBS/Spout, live network delivery, latency, reconnect recovery, Udon or real-client performance.");
        work = Validate();
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }

    static void Flush()
    {
        File.WriteAllLines(Path.Combine(root, "frames.csv"), Rows);
        File.WriteAllLines(Path.Combine(root, "profiles.csv"), Summaries);
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        if (error != null) { failed = true; Details.Add(error.ToString()); }
        foreach (Object item in Owned.AsEnumerable().Reverse())
        {
            if (item is RenderTexture texture) texture.Release();
            if (item != null) Object.DestroyImmediate(item);
        }
        Flush();
        string status = failed ? "FAIL" : "PASS";
        File.WriteAllLines(Path.Combine(root, "result.txt"), new[] { status }.Concat(Details));
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static T Own<T>(T value) where T : Object { Owned.Add(value); return value; }
    static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    static string Csv(object value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    static string Row(params object[] values) { return string.Join(",", values.Select(Csv)); }
    static T Field<T>(TSMPDecoder decoder, string name)
    {
        var field = typeof(TSMPDecoder).GetField(name, Private);
        Check(field != null, "Decoder diagnostic field missing: " + name);
        return (T)field.GetValue(decoder);
    }
    static string Resolve(string directory, string file) { return Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(directory, file)); }
    static string DirectoryPath(string value)
    {
        Check(!string.IsNullOrWhiteSpace(value), "A profile directory is missing.");
        return Resolve(Path.GetDirectoryName(manifestPath), value);
    }

    static Dictionary<uint, string> Images(string directory)
    {
        Check(Directory.Exists(directory), "Image directory missing: " + directory);
        var result = new Dictionary<uint, string>();
        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly).Where(p => string.Equals(Path.GetExtension(p), ".png", StringComparison.OrdinalIgnoreCase)))
        {
            var match = FrameName.Match(Path.GetFileName(path));
            Check(match.Success, "Expected frame-%03d.png or frame-%06d.png, found: " + path);
            uint id = uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            Check(!result.ContainsKey(id), "Duplicate numeric PNG ID: " + id + " in " + directory);
            result.Add(id, path);
        }
        return result;
    }

    static uint ExpectedIndex(FrameMapping map, string expectedPath)
    {
        if (map.expectedFrameUnknown || map.frameIndexSpecified || map.frameIndex != 0) return map.frameIndex;
        var match = FrameName.Match(Path.GetFileName(expectedPath));
        Check(match.Success, "Mapping needs frameIndex or a numeric expected PNG filename: " + expectedPath);
        return uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    static List<FrameCase> Cases(Profile profile, out int observed)
    {
        string actualDirectory = DirectoryPath(profile.framesDirectory);
        string expectedDirectory = DirectoryPath(profile.expectedDirectory);
        var actual = Images(actualDirectory);
        var expected = Images(expectedDirectory);
        observed = actual.Count;
        Check(expected.Count > 0, "Expected image directory is empty: " + expectedDirectory);
        var result = new List<FrameCase>();
        var usedActual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string payloadDirectory = string.IsNullOrWhiteSpace(profile.expectedPayloadDirectory) ? expectedDirectory : DirectoryPath(profile.expectedPayloadDirectory);
        if (profile.frameMappings != null && profile.frameMappings.Length > 0)
        {
            foreach (FrameMapping map in profile.frameMappings)
            {
                Check(map != null && !string.IsNullOrWhiteSpace(map.file) && (map.expectedFrameUnknown || !string.IsNullOrWhiteSpace(map.expectedFile)), "frameMappings require file, expectedFile and frameIndex, or explicit expectedFrameUnknown=true.");
                string actualPath = Resolve(actualDirectory, map.file);
                Check(usedActual.Add(actualPath), "Duplicate mapped output: " + map.file);
                string expectedPath = string.IsNullOrWhiteSpace(map.expectedFile) ? "" : Resolve(expectedDirectory, map.expectedFile);
                result.Add(new FrameCase { file = map.file, actualPath = actualPath, expectedPath = expectedPath, frameIndex = ExpectedIndex(map, expectedPath),
                    expectedFrameUnknown = map.expectedFrameUnknown,
                    payloadPath = map.expectedFrameUnknown ? "" : Resolve(payloadDirectory, string.IsNullOrWhiteSpace(map.payloadFile) ? Path.ChangeExtension(Path.GetFileName(expectedPath), ".bin") : map.payloadFile) });
            }
        }
        else
        {
            foreach (var pair in expected.OrderBy(p => p.Key))
            {
                string actualPath = actual.TryGetValue(pair.Key, out string found) ? found : Path.Combine(actualDirectory, Path.GetFileName(pair.Value));
                usedActual.Add(actualPath);
                result.Add(new FrameCase { file = Path.GetFileName(actualPath), actualPath = actualPath, expectedPath = pair.Value, frameIndex = pair.Key,
                    payloadPath = Path.Combine(payloadDirectory, Path.ChangeExtension(Path.GetFileName(pair.Value), ".bin")) });
            }
        }
        foreach (string path in actual.Values.Where(p => !usedActual.Contains(p)).OrderBy(p => p))
            result.Add(new FrameCase { file = Path.GetFileName(path), actualPath = path, unexpected = true });
        return result;
    }

    static RenderTexture Target(int width, int height)
    {
        var texture = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        texture.filterMode = FilterMode.Point;
        Check(texture.Create(), "RenderTexture creation failed.");
        return texture;
    }

    static TSMPCodec CreateCodec()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        Check(prefab != null, "Luma4 prefab missing.");
        var codec = Own(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        Check(codec != null, "Luma4 codec component missing.");
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (!copies.TryGetValue(original, out Material copy)) copies[original] = copy = Own(new Material(original));
            return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials)
                field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return codec;
    }

    // Explicit compatibility fixture only; arbitrary inputs must supply independent raw .bin files.
    static byte[] FixturePayload(uint frame)
    {
        Check(frame >= 1 && frame <= 12, "video-transport-v1 only defines frames 1 through 12.");
        int length = new[] { 32, 2495, 5375 }[(frame - 1) % 3];
        var bytes = new byte[length];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, frame);
        int messageStart = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, (ushort)frame);
        int overhead = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes
            + NetworkFrameProtocol.VariableStateBodyHeaderBytes + NetworkFrameProtocol.VariableValueHeaderBytes;
        var value = new byte[length - overhead];
        for (int i = 0; i < value.Length; i++) value[i] = (byte)(frame * 29 + i * 73 + (i >> 8));
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, 100, NetworkFrameProtocol.ValueTypeRawBytes, value);
        Check(cursor == length && NetworkFrameWriter.EndVariableState(bytes, messageStart, cursor, 1)
            && NetworkFrameWriter.EndNetworkFrame(bytes, 0, 1), "Compatibility fixture construction failed.");
        return bytes;
    }

    static byte[] ExpectedPayload(Profile profile, FrameCase frame)
    {
        Check(File.Exists(frame.expectedPath), "Expected original PNG missing: " + frame.expectedPath);
        if (File.Exists(frame.payloadPath)) return File.ReadAllBytes(frame.payloadPath);
        Check(profile.fixture == "video-transport-v1", "Expected raw payload missing: " + frame.payloadPath + "; supply .bin files or explicitly select fixture=video-transport-v1.");
        return FixturePayload(frame.frameIndex);
    }

    static string RejectionStage(TSMPDecoder decoder)
    {
        string error = decoder.lastError ?? "";
        if (error.StartsWith("Header CRC mismatch.", StringComparison.Ordinal)) return "HEADER_CRC";
        if (!decoder.lastHeaderValid) return "HEADER_OR_READBACK";
        if (error.StartsWith("Payload CRC mismatch.", StringComparison.Ordinal)) return "PAYLOAD_CRC";
        if (!decoder.lastPayloadCrcVerified) return "PAYLOAD_POLICY_OR_READBACK";
        return "PARSE";
    }

    static void InputRow(Profile profile, FrameCase frame, string status, string error)
    {
        Rows.Add(Row(profile.name, frame.file, frame.expectedPath, frame.unexpected || frame.expectedFrameUnknown ? "" : frame.frameIndex.ToString(CultureInfo.InvariantCulture), frame.expectedFrameUnknown,
            "", false, "", "", "", "", "", false,
            false, false, false, false, "", "", "", "", "INPUT", "", status, error));
    }

    static IEnumerator Validate()
    {
        Application.runInBackground = true;
        string manifestJson = File.ReadAllText(manifestPath);
        File.WriteAllText(Path.Combine(root, "manifest-used.json"), manifestJson);
        Manifest manifest = JsonUtility.FromJson<Manifest>(manifestJson);
        Check(manifest != null && manifest.profiles != null && manifest.profiles.Length > 0, "Manifest profiles are required.");
        Check(manifest.profiles.All(p => p != null && !string.IsNullOrWhiteSpace(p.name)), "Every profile needs a name.");
        Check(manifest.profiles.Select(p => p.name).Distinct(StringComparer.Ordinal).Count() == manifest.profiles.Length, "Profile names must be unique.");
        TSMPCodec codec = CreateCodec();
        var image = Own(new Texture2D(1280, 720, TextureFormat.RGBA32, false, true));
        image.filterMode = FilterMode.Point;
        RenderTexture source = null;
        var decoder = Own(new GameObject("Imported transport measurement decoder")).AddComponent<TSMPDecoder>();
        decoder.applyEveryFrame = false; decoder.skipDuplicateFrames = false; decoder.decodeSafetyMode = 3;
        decoder.requirePayloadCrc32 = true; decoder.debugLog = false;
        decoder.codecHandlers = new[] { codec }; decoder.blockSize = 8; decoder.sampleSize = 4;
        decoder.overlapReadbacks = false; decoder.payloadByteTexture = Target(2048, 1); decoder.sourceTexture = source;
        uint[] crcTable = Crc32Runtime.EnsureTable(null);
        yield return null;
        foreach (Profile profile in manifest.profiles)
        {
            int observed = 0;
            List<FrameCase> frames = null;
            string inputError = null;
            try { frames = Cases(profile, out observed); }
            catch (Exception error) { inputError = error.Message; }
            if (inputError != null)
            {
                failed = true;
                InputRow(profile, new FrameCase { file = "", expectedPath = "" }, "INPUT_ERROR", inputError);
                Summaries.Add(Row(profile.name, 0, observed, 0, 0, 0, 0, 0, 0, 1, false));
                Details.Add("FAIL " + profile.name + ": " + inputError); Flush(); continue;
            }
            int exact = 0, rejected = 0, wrong = 0, corrupt = 0, missing = 0, timeouts = 0, errors = 0;
            foreach (FrameCase frame in frames)
            {
                if (frame.unexpected) { wrong++; InputRow(profile, frame, "WRONG", "Unexpected output PNG without expected mapping."); continue; }
                if (!File.Exists(frame.actualPath)) { missing++; InputRow(profile, frame, "MISSING", "Expected output PNG is missing."); continue; }
                byte[] expected = null;
                inputError = null;
                try
                {
                    if (!frame.expectedFrameUnknown) expected = ExpectedPayload(profile, frame);
                    Check(image.LoadImage(File.ReadAllBytes(frame.actualPath)), "PNG loading failed: " + frame.actualPath);
                    Check(image.width > 0 && image.height > 0 && image.width % 8 == 0 && image.height % 8 == 0,
                        "PNG dimensions must be positive multiples of block8: " + image.width + "x" + image.height + ": " + frame.actualPath);
                }
                catch (Exception error) { inputError = error.Message; }
                if (inputError != null) { errors++; InputRow(profile, frame, "INPUT_ERROR", inputError); continue; }
                image.filterMode = FilterMode.Point;
                if (source == null || source.width != image.width || source.height != image.height)
                {
                    if (source != null) source.Release();
                    source = Target(image.width, image.height);
                    decoder.sourceTexture = source;
                }
                Graphics.Blit(image, source);
                decoder.lastHeaderValid = false; decoder.lastFrameValid = false;
                decoder.lastPayloadCrcPresent = false; decoder.lastPayloadCrcVerified = false;
                decoder.DecodeNow();
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (decoder.readbackInFlight && EditorApplication.timeSinceStartup < deadline) yield return null;
                if (decoder.readbackInFlight)
                {
                    timeouts++; failed = true;
                    InputRow(profile, frame, "TIMEOUT", "GPU readback exceeded 30 seconds; remaining cases are not run.");
                    Summaries.Add(Row(profile.name, frames.Count(f => !f.unexpected), observed, exact, rejected, wrong, corrupt, missing, timeouts, errors, false));
                    Flush();
                    throw new TimeoutException("GPU timeout; profile=" + profile.name + "; file=" + frame.file);
                }
                byte[] actual = Field<byte[]>(decoder, "_payloadBytes");
                int retainedLength = Field<int>(decoder, "_payloadDataBytes");
                bool valid = decoder.lastHeaderValid && decoder.lastFrameValid && decoder.lastPayloadCrcPresent && decoder.lastPayloadCrcVerified;
                bool payloadCurrent = decoder.lastHeaderValid && (decoder.lastPayloadCrcVerified || (decoder.lastError ?? "").StartsWith("Payload CRC mismatch.", StringComparison.Ordinal));
                bool expectedFrame = !frame.expectedFrameUnknown && decoder.lastFrameIndex == frame.frameIndex;
                int mismatches = valid && expected != null ? Math.Abs(expected.Length - retainedLength) : -1;
                if (valid && expected != null)
                {
                    Check(actual != null && retainedLength >= 0 && retainedLength <= actual.Length, "Validated payload exceeds diagnostic buffer.");
                    for (int i = 0; i < Math.Min(expected.Length, retainedLength); i++) if (actual[i] != expected[i]) mismatches++;
                }
                string status = !valid ? "REJECTED" : !expectedFrame ? "WRONG" : mismatches == 0 ? "EXACT" : "CORRUPT";
                if (status == "EXACT") exact++; else if (status == "REJECTED") rejected++; else if (status == "WRONG") wrong++; else corrupt++;
                byte[] header = Field<byte[]>(decoder, "_headerBytes");
                bool headerCheckReached = decoder.lastHeaderValid || (decoder.lastError ?? "").StartsWith("Header CRC mismatch.", StringComparison.Ordinal);
                string headerExpected = "", headerActual = "", payloadExpected = "", payloadActual = "";
                if (headerCheckReached && header != null && header.Length >= FrameHeader.Size)
                {
                    headerExpected = Binary.ReadUInt32LE(header, FrameHeader.CrcOffset).ToString("X8");
                    headerActual = Crc32Runtime.Compute(header, 0, FrameHeader.BytesBeforeCrc, crcTable).ToString("X8");
                    if (decoder.lastPayloadCrcPresent) payloadExpected = Binary.ReadUInt32LE(header, FrameHeader.PayloadCrc32Offset).ToString("X8");
                }
                if (payloadCurrent && actual != null && retainedLength >= 0 && retainedLength <= actual.Length)
                    payloadActual = Crc32Runtime.Compute(actual, 0, retainedLength, crcTable).ToString("X8");
                string actualFrame = headerCheckReached && header != null && header.Length >= FrameHeader.Size ? Binary.ReadUInt32LE(header, FrameHeader.FrameIndexOffset).ToString(CultureInfo.InvariantCulture) : "";
                string diagnosticError = valid && frame.expectedFrameUnknown ? "Expected original frame is unknown; independent payload comparison is unavailable." : decoder.lastError;
                Rows.Add(Row(profile.name, frame.file, frame.expectedPath, frame.expectedFrameUnknown ? "" : frame.frameIndex.ToString(CultureInfo.InvariantCulture), frame.expectedFrameUnknown,
                    actualFrame, decoder.lastHeaderValid, image.width, image.height, expected == null ? "" : expected.Length.ToString(CultureInfo.InvariantCulture),
                    payloadCurrent ? retainedLength.ToString(CultureInfo.InvariantCulture) : "", retainedLength, payloadCurrent, decoder.lastHeaderValid, decoder.lastFrameValid,
                    decoder.lastPayloadCrcPresent, decoder.lastPayloadCrcVerified, headerExpected, headerActual, payloadExpected, payloadActual, valid ? "" : RejectionStage(decoder),
                    valid && expected != null ? mismatches.ToString(CultureInfo.InvariantCulture) : "", status, diagnosticError));
            }
            bool pass = rejected + wrong + corrupt + missing + timeouts + errors == 0 && exact > 0;
            failed |= !pass;
            Summaries.Add(Row(profile.name, frames.Count(f => !f.unexpected), observed, exact, rejected, wrong, corrupt, missing, timeouts, errors, pass));
            Details.Add((pass ? "PASS " : "FAIL ") + profile.name + ": exact=" + exact + "; rejected=" + rejected + "; wrong=" + wrong + "; corrupt=" + corrupt + "; missing=" + missing + "; inputErrors=" + errors);
            Flush();
        }
    }
}
#endif
