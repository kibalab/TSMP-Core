#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Focused request/snapshot/CRC safety checks for bounded recovery. Uses the
// existing Snapshot fixture writers, not alternate production decode methods.
public sealed class CoreRecoveryValidation : MonoBehaviour
{
    [Serializable] public sealed class SavedCases { public SavedCase[] cases; }
    [Serializable] public sealed class SavedCase { public string name, input, header, payload, codecPrefabPath, stage; }
    const string SessionKey = "TSMP.CoreRecovery";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Root => Environment.GetEnvironmentVariable("TSMP_ROBUSTNESS_RESULTS");
    readonly List<Object> owned = new List<Object>();
    readonly List<Material> materials = new List<Material>();
    readonly List<string> details = new List<string>();
    readonly List<string> sourceHashes = new List<string> { "path,sha256" };
    readonly HashSet<string> hashed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public int receivedValue;
    uint sequence = 300000;
    int eventId = 1000000, checks;

    public void OnTSMPVariableReceived() { }
    public static void Run()
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
        SessionState.SetBool(SessionKey, false); new GameObject("Core recovery safety").AddComponent<CoreRecoveryValidation>();
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
        details.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
        details.Add("Actual Native TSMPDecoder, protected production rasters, bounded CRC retry and atomic state/RPC boundary. No VRChat/Udon/client/service run.");
        foreach (string file in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) == "CoreRecoveryValidation.cs" || Path.GetFileName(path) == "DecoderSnapshotValidation.cs")) Hash(file);
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.luma4", "com.kibalab.tsmp.codec.rgb16", "com.kibalab.tsmp.codec.color256", "com.kibalab.tsmp.codec.rgb20" })
            foreach (string file in Directory.GetFiles("Packages/" + package + "/Runtime", "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs") || path.EndsWith(".shader") || path.EndsWith(".cginc"))) Hash(file);
        Own(new GameObject("Recovery frame pump")).AddComponent<Camera>().cullingMask = 0;
        var stack = new Stack<IEnumerator>(); stack.Push(Validate());
        while (stack.Count > 0)
        {
            IEnumerator next = stack.Peek(); bool moved;
            try { moved = next.MoveNext(); }
            catch (Exception error) { Finish(error); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next.Current is IEnumerator nested) { stack.Push(nested); continue; } yield return next.Current;
        }
        Finish(null);
    }
    T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    static T Field<T>(TSMPDecoder decoder, string name) => (T)typeof(TSMPDecoder).GetField(name, Private | BindingFlags.Public).GetValue(decoder);
    static void Set(TSMPDecoder decoder, string name, int value) => typeof(TSMPDecoder).GetField(name).SetValue(decoder, value);
    static void SetBool(TSMPDecoder decoder, string name, bool value) => typeof(TSMPDecoder).GetField(name).SetValue(decoder, value);
    static int Attempts(TSMPDecoder decoder, bool header) => Field<int>(decoder, header ? "headerRecoveryAttemptCount" : "payloadRecoveryAttemptCount");
    void Hash(string path)
    {
        string full = Path.GetFullPath(path); if (!hashed.Add(full)) return;
        using (var digest = System.Security.Cryptography.SHA256.Create())
        {
            string hash = BitConverter.ToString(digest.ComputeHash(File.ReadAllBytes(full))).Replace("-", "").ToLowerInvariant();
            sourceHashes.Add("\"" + full.Replace("\"", "\"\"") + "\",\"" + hash + "\"");
        }
    }
    void Finish(Exception error)
    {
        if (error != null) details.Add(error.ToString()); details.Add("Assertions=" + checks);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(details));
        File.WriteAllLines(Path.Combine(Root, "source-hashes.csv"), sourceHashes);
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
        float deadline = Time.realtimeSinceStartup + 30;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!decoder.readbackInFlight, "Recovery readback timeout " + decoder.lastError);
    }
    void Upload(Texture image, RenderTexture source) { Graphics.Blit(image, source); }
    byte[] Write(TSMPCodec codec, Texture2D image, int value, bool badHeader = false, bool badPayload = false, int extra = 0, uint frame = 0)
    {
        byte[] payload = DecoderSnapshotValidation.MixedValueRpcPacket(value, ++eventId, extra);
        DecoderSnapshotValidation.Write(codec, image, payload, frame == 0 ? ++sequence : frame, 4, badHeader, true, badPayload); return payload;
    }
    void Verify(TSMPDecoder decoder, byte[] expected, uint frame)
    {
        Check(decoder.lastHeaderValid && decoder.lastFrameValid && decoder.lastPayloadCrcVerified, "Valid retry rejected: " + decoder.lastError);
        Check(decoder.lastFrameIndex == frame && Field<int>(decoder, "_payloadDataBytes") == expected.Length, "Wrong retry identity/length.");
        Check(expected.SequenceEqual(Field<byte[]>(decoder, "_payloadBytes").Take(expected.Length)), "Wrong protected retry bytes.");
    }
    IEnumerator Validate()
    {
        Check(typeof(TSMPDecoder).GetField("maxCrcRecoveryAttempts") != null, "Recovery API missing.");
        TSMPCodec[] codecs = new[] { "Luma4", "RGB16", "Color256", "RGB20" }.Select(Codec).ToArray();
        TSMPDecoder decoder = Own(new GameObject("Bounded recovery decoder")).AddComponent<TSMPDecoder>();
        Check(!Field<bool>(decoder, "recoverHeaderCrc"), "Experimental header recovery must default off.");
        decoder.applyEveryFrame = false; decoder.skipDuplicateFrames = true; decoder.requirePayloadCrc32 = true; decoder.decodeSafetyMode = 0;
        decoder.debugLog = false; decoder.overlapReadbacks = false; decoder.codecHandlers = codecs; decoder.flipY = true; decoder.blockSize = 8; decoder.sampleSize = 4;
        decoder.payloadByteTexture = Own(DecoderSnapshotValidation.Target(2048, 1));
        RenderTexture source = Own(DecoderSnapshotValidation.Target(640, 360)); decoder.sourceTexture = source;
        Texture2D image = Own(new Texture2D(640, 360, TextureFormat.RGBA32, false, true)); image.filterMode = FilterMode.Point;
        GameObject toggleOwner = Own(new GameObject("Recovery RPC receiver")), target = Own(new GameObject("Recovery RPC target"));
        TSMPNetworkGameObjectToggle toggle = toggleOwner.AddComponent<TSMPNetworkGameObjectToggle>(); toggle.targetObject = target;
        decoder.bindingTargets = new Component[] { this, toggle }; decoder.bindingNetworkIds = new ushort[] { 1, 2 };
        decoder.bindingVariableHashes = new[] { StableHash.Fnv1A32(nameof(receivedValue)), 0u };
        decoder.bindingValueTypes = new[] { (byte)NetworkValueType.Int32, (byte)NetworkFrameProtocol.ValueTypeUnsupported };
        decoder.bindingFieldNames = new[] { nameof(receivedValue), string.Empty }; decoder.bindingDirections = new int[2];
        yield return null;

        Set(decoder, "maxCrcRecoveryAttempts", 2);
        foreach (bool badHeader in new[] { true, false })
        {
            decoder.ResetDecodeDiagnostics(); receivedValue = -1; target.SetActive(false);
            Write(codecs[0], image, 52, badHeader, !badHeader); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder);
            Check(Attempts(decoder, true) == 0 && Attempts(decoder, false) == (badHeader ? 0 : 2), "Default-off header policy changed payload recovery budget.");
            Check(!decoder.lastFrameValid && receivedValue == -1 && !target.activeSelf, "Default policy applied corrupt state/RPC.");
        }
        foreach (bool enabledAtRequest in new[] { false, true })
        {
            decoder.ResetDecodeDiagnostics(); SetBool(decoder, "recoverHeaderCrc", enabledAtRequest);
            receivedValue = -1; target.SetActive(false); Write(codecs[0], image, 52, true); Upload(image, source); decoder.DecodeNow();
            SetBool(decoder, "recoverHeaderCrc", !enabledAtRequest); yield return Drain(decoder);
            Check(Attempts(decoder, true) == (enabledAtRequest ? 2 : 0), "In-flight header recovery option changed.");
            Check(!decoder.lastFrameValid && receivedValue == -1 && !target.activeSelf, "Captured header option applied corrupt data.");
        }
        // Each pending slot captures its own experimental header option.
        decoder.overlapReadbacks = true;
        foreach (bool firstEnabled in new[] { false, true })
        {
            decoder.ResetDecodeDiagnostics(); receivedValue = -1; target.SetActive(false);
            SetBool(decoder, "recoverHeaderCrc", firstEnabled); Write(codecs[0], image, 52, true); Upload(image, source); decoder.DecodeNow();
            SetBool(decoder, "recoverHeaderCrc", !firstEnabled); Write(codecs[0], image, 53, true); Upload(image, source); decoder.DecodeNow();
            SetBool(decoder, "recoverHeaderCrc", false); Check(decoder.pendingDecodeCount == 2, "Header-option capture did not queue two slots.");
            yield return Drain(decoder); Check(Attempts(decoder, true) == 2 && Attempts(decoder, false) == 0, "Header option leaked between pending slots.");
            Check(receivedValue == -1 && !target.activeSelf, "Mixed header options applied corrupt state/RPC.");
        }
        decoder.overlapReadbacks = false; SetBool(decoder, "recoverHeaderCrc", true);
        details.Add("PASS default-off header recovery/payload budget, mutable header-option capture in both directions and per-slot option ownership.");

        foreach (TSMPCodec codec in codecs)
        foreach (bool prediction in new[] { false, true })
        foreach (int budget in new[] { 0, 1, 2 })
        foreach (bool badHeader in new[] { true, false })
        {
            decoder.ResetDecodeDiagnostics();
            decoder.usePredictedReadback = prediction; decoder.useCombinedByteOutput = prediction; Set(decoder, "maxCrcRecoveryAttempts", budget);
            byte[] warm = Write(codec, image, 11); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder); Verify(decoder, warm, sequence);
            Check(Attempts(decoder, true) == 0 && Attempts(decoder, false) == 0, "Clean frame performed recovery.");
            receivedValue = -1; target.SetActive(false); uint frame = ++sequence;
            byte[] bad = Write(codec, image, 42, badHeader, !badHeader, frame: frame); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder);
            Check(!decoder.lastFrameValid && receivedValue == -1 && !target.activeSelf, "Corrupt bytes applied state/RPC.");
            Material selected = (badHeader ? codecs[0] : codec).selectedDecodeMaterial;
            int supportedBudget = selected != null && selected.HasProperty("_RecoveryMode") ? budget : 0;
            Check(Attempts(decoder, badHeader) == supportedBudget && Attempts(decoder, !badHeader) == 0, "Wrong bounded stage attempts: " + codec.displayName + "/" + badHeader + "/" + budget + "; h=" + Attempts(decoder, true) + "; p=" + Attempts(decoder, false));
            Check(Field<uint>(decoder, "_lastAppliedFrameIndex") == frame - 1, "Rejected retry advanced applied baseline.");
            // Same source frame, intact retransmission; corruption must not consume RPC/frame dedup.
            DecoderSnapshotValidation.Write(codec, image, bad, frame, 4, protectPayload: true); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder);
            Verify(decoder, bad, frame); Check(receivedValue == 42 && target.activeSelf, "Intact retransmission did not apply after rejection.");
        }
        details.Add("PASS all four codecs, prediction/combined on/off, both CRC stages, budgets0/1/2: bounded rejection, no clean retries, state/RPC atomicity and same-frame intact retransmission.");

        // Request policy is captured when queued, not read from mutable Inspector state.
        foreach (bool badHeader in new[] { true, false })
        foreach (int budget in new[] { 0, 2 })
        {
            decoder.ResetDecodeDiagnostics();
            Set(decoder, "maxCrcRecoveryAttempts", budget); receivedValue = -1; target.SetActive(false);
            Write(codecs[0], image, 52, badHeader, !badHeader); Upload(image, source); decoder.DecodeNow(); Set(decoder, "maxCrcRecoveryAttempts", 2 - budget);
            yield return Drain(decoder); Check(Attempts(decoder, badHeader) == budget, "In-flight request recovery policy changed.");
            Check(receivedValue == -1 && !target.activeSelf && !decoder.lastFrameValid, "Policy capture applied corrupt frame.");
        }
        details.Add("PASS request recovery budgets captured across mutable settings changes.");

        foreach (int configuredBudget in new[] { -3, 7 })
        {
            decoder.ResetDecodeDiagnostics(); Set(decoder, "maxCrcRecoveryAttempts", configuredBudget);
            receivedValue = -1; target.SetActive(false); Write(codecs[0], image, 52, true); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder);
            Check(Attempts(decoder, true) == (configuredBudget < 0 ? 0 : 2), "Recovery budget runtime clamp failed.");
            Check(!decoder.lastFrameValid && receivedValue == -1 && !target.activeSelf, "Out-of-range budget applied corrupt data.");
        }
        foreach (bool strict in new[] { true, false })
        {
            decoder.ResetDecodeDiagnostics(); Set(decoder, "maxCrcRecoveryAttempts", 2); decoder.requirePayloadCrc32 = strict;
            receivedValue = -1; target.SetActive(false); byte[] legacy = DecoderSnapshotValidation.MixedValueRpcPacket(57, ++eventId);
            DecoderSnapshotValidation.Write(codecs[0], image, legacy, ++sequence, 4); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder);
            Check(Attempts(decoder, true) == 0 && Attempts(decoder, false) == 0, "Missing payload CRC triggered recovery.");
            Check(decoder.lastFrameValid == !strict && !decoder.lastPayloadCrcPresent && !decoder.lastPayloadCrcVerified, "Legacy CRC policy changed.");
            Check(receivedValue == (strict ? -1 : 57) && target.activeSelf == !strict, "Legacy policy state/RPC application changed.");
        }
        decoder.requirePayloadCrc32 = true;
        details.Add("PASS runtime budget clamps and strict/compatible legacy frames without payload CRC: no recovery attempts.");

        // Both snapshot slots retain independent bytes, budgets and error/retry state.
        Set(decoder, "maxCrcRecoveryAttempts", 2); decoder.overlapReadbacks = true;
        foreach (bool prediction in new[] { false, true })
        foreach (bool badHead in new[] { false, true })
        foreach (bool badHeader in new[] { false, true })
        {
            decoder.ResetDecodeDiagnostics();
            decoder.usePredictedReadback = prediction; decoder.useCombinedByteOutput = prediction; receivedValue = -1; target.SetActive(false);
            Write(codecs[0], image, 42, badHead && badHeader, badHead && !badHeader); Upload(image, source); decoder.DecodeNow();
            Write(codecs[0], image, 99, !badHead && badHeader, !badHead && !badHeader, 701); Upload(image, source); decoder.DecodeNow();
            Check(decoder.pendingDecodeCount == 2, "Overlap did not capture both slots.");
            Write(codecs[0], image, 777); Upload(image, source); // Mutate live source while both slots are pending.
            yield return Drain(decoder); Check(receivedValue == (badHead ? 99 : 42), "FIFO/source/retry context leaked.");
            Check(target.activeSelf && decoder.pendingDecodeCount == 0, "Good slot RPC was lost or pending slot leaked.");
        }
        details.Add("PASS both bad-slot orders, header/payload failures, prediction/combined on/off, different lengths and live-source mutation.");

        decoder.overlapReadbacks = false; decoder.usePredictedReadback = false; decoder.useCombinedByteOutput = false;
        foreach (bool badHeader in new[] { false, true })
        foreach (bool objectDisable in new[] { false, true })
        {
            decoder.ResetDecodeDiagnostics();
            receivedValue = -1; target.SetActive(false); Write(codecs[0], image, 42, badHeader, !badHeader); Upload(image, source); decoder.DecodeNow();
            float deadline = Time.realtimeSinceStartup + 30;
            while (decoder.readbackInFlight && Attempts(decoder, badHeader) == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(decoder.readbackInFlight && Attempts(decoder, badHeader) > 0, "Retry cancellation window not observed.");
            if (objectDisable) decoder.gameObject.SetActive(false); else decoder.enabled = false;
            Check(Field<bool[]>(decoder, "_slotDiscarded").All(value => value), "Disable did not discard retry slots.");
            if (objectDisable) decoder.gameObject.SetActive(true); else decoder.enabled = true;
            yield return Drain(decoder); Check(receivedValue == -1 && !target.activeSelf && !decoder.lastFrameValid, "Cancelled retry applied data/RPC.");
            byte[] good = Write(codecs[0], image, 68); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder); Verify(decoder, good, sequence);
            Check(receivedValue == 68, "Re-enabled decoder failed after retry cancellation.");
        }
        details.Add("PASS component/GameObject disable/re-enable during header/payload recovery.");
        yield return Saved(decoder, codecs);
    }
    IEnumerator Saved(TSMPDecoder decoder, TSMPCodec[] codecs)
    {
        string path = Environment.GetEnvironmentVariable("TSMP_RECOVERY_CASES");
        if (string.IsNullOrWhiteSpace(path)) { details.Add("Saved real-recovery fixtures: NOT_REQUESTED; synthetic safety does not certify compressed recovery."); yield break; }
        string json = File.ReadAllText(path); Hash(path); File.WriteAllText(Path.Combine(Root, "saved-cases-used.json"), json);
        SavedCases saved = JsonUtility.FromJson<SavedCases>(json); Check(saved != null && saved.cases != null && saved.cases.Length > 0, "Saved recovery cases required.");
        decoder.decodeSafetyMode = 3; decoder.skipDuplicateFrames = false; decoder.usePredictedReadback = true; decoder.useCombinedByteOutput = true;
        foreach (SavedCase item in saved.cases)
        {
            Check(item.stage == "Header" || item.stage == "Payload", "Saved stage must be Header or Payload.");
            Hash(item.payload); Hash(item.header); Hash(item.input);
            byte[] payload = File.ReadAllBytes(item.payload), header = File.ReadAllBytes(item.header); Check(FrameHeader.TryRead(header, 0, out FrameHeader info), "Saved header invalid.");
            Texture2D image = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true)); Check(image.LoadImage(File.ReadAllBytes(item.input)), "Saved input cannot load."); image.filterMode = FilterMode.Point;
            RenderTexture source = Own(DecoderSnapshotValidation.Target(image.width, image.height)); decoder.sourceTexture = source;
            SetBool(decoder, "recoverHeaderCrc", item.stage == "Header");
            if (!codecs.Any(value => value.codecId == info.CodecId)) throw new InvalidOperationException("Saved codec handler missing.");
            Set(decoder, "maxCrcRecoveryAttempts", 0); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder); Check(!decoder.lastFrameValid, "Saved baseline unexpectedly exact: " + item.name);
            if (item.stage == "Header") Check(!decoder.lastHeaderValid && (decoder.lastError ?? "").StartsWith("Header CRC mismatch.", StringComparison.Ordinal), "Saved baseline did not fail at header CRC: " + item.name);
            else Check(decoder.lastHeaderValid && decoder.lastPayloadCrcPresent && !decoder.lastPayloadCrcVerified && (decoder.lastError ?? "").StartsWith("Payload CRC mismatch.", StringComparison.Ordinal), "Saved baseline did not fail at payload CRC: " + item.name);
            int before = Field<int>(decoder, item.stage == "Header" ? "recoveredHeaderCount" : "recoveredPayloadCount");
            int headerAttemptsBefore = Attempts(decoder, true);
            Set(decoder, "maxCrcRecoveryAttempts", 2); Upload(image, source); decoder.DecodeNow(); yield return Drain(decoder); Verify(decoder, payload, info.FrameIndex);
            Check(header.SequenceEqual(Field<byte[]>(decoder, "_headerBytes").Take(FrameHeader.Size)), "Saved recovered header differs from independent source.");
            Check(Field<int>(decoder, item.stage == "Header" ? "recoveredHeaderCount" : "recoveredPayloadCount") == before + 1, "Saved successful recovery counter missing.");
            if (item.stage == "Payload") Check(Attempts(decoder, true) == headerAttemptsBefore, "Default-off actual payload recovery retried the header.");
            details.Add("PASS saved actual compressed recovery: " + item.name + "; stage=" + item.stage);
        }
    }
}
#endif
