#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// A finite source schedule is replayed by media ordinal, not by host wall time.
// Production Encoder/Decoder methods and codec materials remain unmodified.
public sealed class BudgetDeliveryValidation : MonoBehaviour
{
    [Serializable] public sealed class Plan { public Profile[] profiles; }
    [Serializable] public sealed class Profile
    {
        public string name, fixture, frames, codec;
        public int target;
        public bool burstHold;
    }
    const int Frames = 120, Width = 1280, Height = 720;
    const string Session = "TSMP.BudgetDelivery";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static string Root => Environment.GetEnvironmentVariable("TSMP_BUDGET_DELIVERY_RESULTS");
    static string Phase => Environment.GetEnvironmentVariable("TSMP_BUDGET_DELIVERY_PHASE");
    readonly List<Object> owned = new List<Object>();
    readonly List<string> details = new List<string>();
    readonly List<string> sources = new List<string> { "path,sha256" };
    uint[] referenceCrc;
    int checks;
    static string Csv(params object[] values) => string.Join(",", values.Select(v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static string Digest(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    static T Field<T>(object item, string name) => (T)item.GetType().GetField(name, Flags).GetValue(item);
    void Drop(Object item)
    {
        if (item is RenderTexture rt) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); }
        owned.Remove(item); if (item != null) Object.DestroyImmediate(item);
    }
    public static void Run()
    {
        if (string.IsNullOrWhiteSpace(Root) || (Phase != "Generate" && Phase != "Replay")) throw new InvalidOperationException("Results and Generate/Replay phase required");
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour)) throw new InvalidOperationException("Dedicated SDK-free Native project required");
        Directory.CreateDirectory(Root);
        if (File.Exists(Path.Combine(Root, "result.txt"))) throw new InvalidOperationException("Use fresh phase results");
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Session, true); Attach(); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered;
    }
    static void Entered(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Session, false))
        { SessionState.SetBool(Session, false); new GameObject("Budget delivery validation").AddComponent<BudgetDeliveryValidation>(); }
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
        referenceCrc = Crc32Runtime.EnsureFastTable(null);
        Own(new GameObject("Budget delivery frame pump")).AddComponent<Camera>().cullingMask = 0;
        details.Add("Actual Native production Encoder/raster/GPU Decoder/state/RPC; Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; color=" + QualitySettings.activeColorSpace);
        details.Add("120 scheduled media frames at30fps; host execution is offline and not a live delivery or VRChat FPS measurement.");
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.luma4", "com.kibalab.tsmp.codec.color256" })
            foreach (string file in Directory.GetFiles("Packages/" + package, "*", SearchOption.AllDirectories).Where(p => new[] { ".cs", ".shader", ".cginc", ".mat", ".prefab" }.Contains(Path.GetExtension(p))))
                sources.Add(Csv(Path.GetFullPath(file), Digest(File.ReadAllBytes(file))));
        foreach (string file in Directory.GetFiles("Assets/Validation/BudgetDelivery", "*.cs", SearchOption.AllDirectories))
            sources.Add(Csv(Path.GetFullPath(file), Digest(File.ReadAllBytes(file))));
        File.WriteAllLines(Path.Combine(Root, "source-hashes-before.csv"), sources);
        var stack = new Stack<IEnumerator>(); stack.Push(Phase == "Generate" ? Generate() : Replay());
        while (stack.Count > 0)
        {
            var next = stack.Peek(); bool moved;
            try { moved = next.MoveNext(); }
            catch (Exception error) { Finish(error); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next.Current is IEnumerator child) stack.Push(child); else yield return next.Current;
        }
        Finish(null);
    }
    void Finish(Exception error)
    {
        if (error != null) details.Add(error.ToString());
        details.Add("Assertions=" + checks);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(details));
        var after = new List<string> { "path,sha256" };
        foreach (string row in sources.Skip(1))
        {
            string path = row.Split(new[] { "\",\"" }, StringSplitOptions.None)[0].Trim('"');
            after.Add(Csv(path, Digest(File.ReadAllBytes(path))));
        }
        File.WriteAllLines(Path.Combine(Root, "source-hashes-after.csv"), after);
        foreach (Object item in owned.AsEnumerable().Reverse().ToArray()) Drop(item);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    TSMPCodec Codec(string name)
    {
        string prefabPath = "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath); Check(prefab != null, "Missing codec " + prefabPath);
        var instance = Own(Object.Instantiate(prefab)); var codec = instance.GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (!copies.TryGetValue(original, out Material copy)) { copy = Own(new Material(original) { hideFlags = HideFlags.DontSave }); copies.Add(original, copy); }
            return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials) field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return codec;
    }
    RenderTexture Target(int width, int height)
    {
        var texture = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
        Check(texture.Create(), "RenderTexture creation failed"); return texture;
    }
    static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    IEnumerator Generate()
    {
        var metadata = TransSyncMetadata.GetOrCreate(null, typeof(BudgetDeliveryProbe));
        File.WriteAllLines(Path.Combine(Root, "fields.csv"), new[] { "field,hash,bytes,period,priority" }.Concat(new[] { "critical", "medium", "bulk" }.Select((name, i) => Csv(name, metadata.Fields.Single(f => f.FieldInfo.Name == name).VariableHash, new[] { 64, 512, 2048 }[i], new[] { 1, 3, 6 }[i], new[] { 100, 50, 0 }[i]))));
        var manifest = new Plan { profiles = new Profile[6] }; int profileIndex = 0;
        foreach (string name in new[] { "Luma4", "Color256" }) foreach (int target in new[] { 0, 1024, 2304 })
        {
            string label = name.ToLowerInvariant() + "-t" + target, directory = Path.Combine(Root, label);
            foreach (string sub in new[] { "input", "header", "payload" }) Directory.CreateDirectory(Path.Combine(directory, sub));
            TSMPCodec codec = Codec(name);
            var owner = Own(new GameObject(label + " sender")); owner.SetActive(false);
            var sender = owner.AddComponent<BudgetDeliveryProbe>(); sender.networkId = 1;
            var encoder = owner.AddComponent<TSMPEncoder>(); encoder.autoEncode = false; encoder.debugLog = false; encoder.useGpuLuma4 = true;
            encoder.width = Width; encoder.height = Height; encoder.blockSize = 8; encoder.sampleSize = 4; encoder.frameRate = 30;
            encoder.frameIndex = 1; encoder.streamId = 20261031; encoder.targetPayloadBytes = target; encoder.transSyncRefreshInterval = 0;
            encoder.transRpcRepeatFrames = 4; encoder.selectedCodec = codec; encoder.networkBehaviours = new TSMPNetworkBehaviour[] { sender };
            encoder.output = Target(Width, Height); owner.SetActive(true);
            var png = Own(new Texture2D(Width, Height, TextureFormat.RGBA32, false, true));
            var rows = new List<string> { "ordinal,criticalGeneration,mediumGeneration,bulkGeneration,bytes,fields,rpcs,queue,deferredFields,deferredRpcs,encodeMs,warm,gpuLuma4,gpuSymbolWidth,gpuSymbolHeight,gpuUploadBytes,payloadSha256,headerSha256,pngSha256" };
            for (int ordinal = 1; ordinal <= Frames; ordinal++)
            {
                sender.critical = BudgetDeliveryProbe.Value(0, ordinal, 64);
                int medium = BudgetDeliveryProbe.Generation(ordinal, 3), bulk = BudgetDeliveryProbe.Generation(ordinal, 6);
                sender.medium = BudgetDeliveryProbe.Value(1, medium, 512); sender.bulk = BudgetDeliveryProbe.Value(2, bulk, 2048);
                if ((ordinal - 1) % 15 == 0) Check(encoder.QueueTransRpc(1, StableHash.Fnv1A32(nameof(BudgetDeliveryProbe.ReceiveBudgetEvent)), nameof(BudgetDeliveryProbe.ReceiveBudgetEvent), ordinal), "RPC enqueue failed");
                uint before = encoder.frameIndex; long start = Stopwatch.GetTimestamp(); encoder.EncodeNow(); double milliseconds = Elapsed(start);
                Check(encoder.frameIndex == before + 1 && encoder.payloadBytes > 0 && string.IsNullOrEmpty(encoder.lastError), "Production encode failed: " + encoder.lastError);
                Check(target == 0 || encoder.payloadBytes <= target, "Transmission target escaped");
                byte[] header = (byte[])Field<byte[]>(encoder, "_header").Clone(), payload = Field<byte[]>(encoder, "_encodedPayload").Take(encoder.payloadBytes).ToArray();
                Check(FrameHeader.TryRead(header, 0, out FrameHeader parsed) && parsed.FrameIndex == ordinal && parsed.PayloadSize == payload.Length, "Source header invalid");
                Check(parsed.PayloadCrc32 == Crc32Runtime.Compute(payload, 0, payload.Length, referenceCrc), "Source payload CRC invalid");
                string stem = "frame-" + ordinal.ToString("000000");
                File.WriteAllBytes(Path.Combine(directory, "header", stem + ".bin"), header); File.WriteAllBytes(Path.Combine(directory, "payload", stem + ".bin"), payload);
                RenderTexture previous = RenderTexture.active; RenderTexture.active = encoder.output;
                png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); png.Apply(false, false); RenderTexture.active = previous;
                byte[] image = png.EncodeToPNG(); File.WriteAllBytes(Path.Combine(directory, "input", stem + ".png"), image);
                var symbols = Field<RenderTexture>(encoder, "_gpuSymbols"); var upload = Field<byte[]>(encoder, "_gpuUploadBytes");
                rows.Add(Csv(ordinal, ordinal, medium, bulk, payload.Length, encoder.autoVariableCount, encoder.rpcMessageCount, encoder.queuedRpcCount, encoder.deferredVariableCount, encoder.deferredRpcCount, milliseconds.ToString("F6", CultureInfo.InvariantCulture), ordinal <= 10,
                    encoder.lastFrameUsedGpuLuma4, symbols != null ? symbols.width : 0, symbols != null ? symbols.height : 0, upload != null ? upload.Length : 0, Digest(payload), Digest(header), Digest(image)));
                if (ordinal % 30 == 0) File.WriteAllLines(Path.Combine(directory, "source-frames.csv"), rows);
                yield return null;
            }
            File.WriteAllLines(Path.Combine(directory, "source-frames.csv"), rows);
            manifest.profiles[profileIndex++] = new Profile { name = label, fixture = directory, frames = Path.Combine(directory, "input"), codec = name, target = target };
            File.WriteAllText(Path.Combine(Root, "fixtures.json"), JsonUtility.ToJson(manifest, true));
            details.Add("PASS generated " + label + ":120 frames");
            Drop(png); Drop(encoder.output); Drop(owner); Drop(codec.gameObject);
        }
    }
    IEnumerator Replay()
    {
        string planPath = Environment.GetEnvironmentVariable("TSMP_BUDGET_DELIVERY_PLAN");
        string json = File.ReadAllText(planPath); File.WriteAllText(Path.Combine(Root, "plan-used.json"), json);
        var plan = JsonUtility.FromJson<Plan>(json); Check(plan != null && plan.profiles != null && plan.profiles.Length > 0, "Replay profiles missing");
        var metadata = TransSyncMetadata.GetOrCreate(null, typeof(BudgetDeliveryProbe));
        string[] fields = { "critical", "medium", "bulk" };
        foreach (Profile profile in plan.profiles)
        {
            var codec = Codec(profile.codec); var luma = profile.codec == "Luma4" ? codec : Codec("Luma4");
            var owner = Own(new GameObject(profile.name + " receiver")); owner.SetActive(false);
            var receiver = owner.AddComponent<BudgetDeliveryProbe>(); receiver.networkId = 1;
            var decoder = owner.AddComponent<TSMPDecoder>(); receiver.decoder = decoder;
            decoder.applyEveryFrame = false; decoder.debugLog = false; decoder.requirePayloadCrc32 = true; decoder.skipDuplicateFrames = true;
            decoder.maxCrcRecoveryAttempts = 2; decoder.recoverHeaderCrc = false; decoder.decodeSafetyMode = 0;
            decoder.usePredictedReadback = true; decoder.useCombinedByteOutput = true; decoder.overlapReadbacks = true; decoder.flipY = true;
            decoder.codecHandlers = profile.codec == "Luma4" ? new[] { codec } : new[] { luma, codec };
            decoder.sourceTexture = Target(Width, Height); decoder.payloadByteTexture = Target(2048, 1);
            decoder.bindingTargets = fields.Select(f => (Component)receiver).ToArray(); decoder.bindingNetworkIds = new ushort[] { 1, 1, 1 };
            decoder.bindingVariableHashes = fields.Select(f => metadata.Fields.Single(v => v.FieldInfo.Name == f).VariableHash).ToArray();
            decoder.bindingValueTypes = new byte[] { NetworkFrameProtocol.ValueTypeRawBytes, NetworkFrameProtocol.ValueTypeRawBytes, NetworkFrameProtocol.ValueTypeRawBytes };
            decoder.bindingFieldNames = fields; decoder.bindingDirections = new int[3]; owner.SetActive(true);
            var image = Own(new Texture2D(Width, Height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point });
            var rows = new List<string> { "ordinal,sourceOrdinal,status,frameIndex,bytes,submitMs,completionWallMs,warm,headerValid,payloadVerified,frameValid,headerAttempts,payloadAttempts,recoveredPayloads,duplicateSkips,criticalGeneration,mediumGeneration,bulkGeneration,eventsAdded,inputSha256,decodedSha256,error" };
            var eventRows = new List<string> { "eventId,deliveryOrdinal" };
            string bins = Path.Combine(Root, profile.name + "-accepted"); Directory.CreateDirectory(bins);
            for (int ordinal = 1; ordinal <= Frames; ordinal++)
            {
                int sourceOrdinal = profile.burstHold && ordinal >= 45 && ordinal <= 56 ? 44 : ordinal;
                string stem = "frame-" + sourceOrdinal.ToString("000000"), path = Path.Combine(profile.frames, stem + ".png");
                byte[] inputBytes = File.ReadAllBytes(path);
                Check(image.LoadImage(inputBytes), "Cannot read input " + path);
                string inputSha = Digest(inputBytes);
                Graphics.Blit(image, (RenderTexture)decoder.sourceTexture);
                decoder.lastFrameValid = decoder.lastHeaderValid = decoder.lastPayloadCrcVerified = false;
                int duplicateBefore = decoder.skippedDuplicateFrameCount, eventBefore = receiver.events.Count, recoveredBefore = decoder.recoveredPayloadCount;
                int[] before = new byte[][] { receiver.critical, receiver.medium, receiver.bulk }.Select(v => v == null ? 0 : (int)Binary.ReadUInt32LE(v, 0)).ToArray();
                long start = Stopwatch.GetTimestamp(); decoder.DecodeNow(); double submit = Elapsed(start);
                double deadline = EditorApplication.timeSinceStartup + 120;
                while (decoder.readbackInFlight && EditorApplication.timeSinceStartup < deadline) yield return null;
                double wall = Elapsed(start); Check(!decoder.readbackInFlight, "GPU readback timeout: " + profile.name + "/" + ordinal);
                int duplicates = decoder.skippedDuplicateFrameCount - duplicateBefore;
                string status = duplicates > 0 ? "DUPLICATE" : decoder.lastFrameValid && decoder.lastHeaderValid && decoder.lastPayloadCrcVerified ? "EXACT" : "REJECTED";
                int bytes = 0; string decodedSha = "";
                if (status == "EXACT")
                {
                    byte[] expected = File.ReadAllBytes(Path.Combine(profile.fixture, "payload", stem + ".bin"));
                    bytes = Field<int>(decoder, "_payloadDataBytes");
                    byte[] actual = Field<byte[]>(decoder, "_payloadBytes").Take(bytes).ToArray();
                    Check(decoder.lastFrameIndex == sourceOrdinal && bytes == expected.Length && actual.SequenceEqual(expected), "Accepted wrong source bytes: " + profile.name + "/" + ordinal);
                    decodedSha = Digest(actual); File.WriteAllBytes(Path.Combine(bins, "frame-" + ordinal.ToString("000000") + ".bin"), actual);
                }
                int[] generations = new byte[][] { receiver.critical, receiver.medium, receiver.bulk }.Select((value, index) => ValidateValue(value, index, sourceOrdinal)).ToArray();
                if (status != "EXACT") Check(before.SequenceEqual(generations) && eventBefore == receiver.events.Count, "Rejected/duplicate frame changed state or RPC");
                foreach (int eventId in receiver.events.Skip(eventBefore))
                {
                    Check(eventId >= 1 && eventId <= sourceOrdinal && (eventId - 1) % 15 == 0, "Wrong event applied");
                    Check(receiver.events.Count(e => e == eventId) == 1, "Duplicate event applied");
                    eventRows.Add(Csv(eventId, ordinal));
                }
                rows.Add(Csv(ordinal, sourceOrdinal, status, decoder.lastFrameIndex, bytes, submit.ToString("F6", CultureInfo.InvariantCulture), wall.ToString("F6", CultureInfo.InvariantCulture), ordinal <= 10, decoder.lastHeaderValid, decoder.lastPayloadCrcVerified, decoder.lastFrameValid,
                    Field<int>(decoder, "_headerRecoveryAttempt"), Field<int>(decoder, "_payloadRecoveryAttempt"), decoder.recoveredPayloadCount - recoveredBefore, duplicates,
                    generations[0], generations[1], generations[2], receiver.events.Count - eventBefore, inputSha, decodedSha, decoder.lastError));
                if (ordinal % 30 == 0) { File.WriteAllLines(Path.Combine(Root, profile.name + ".csv"), rows); File.WriteAllLines(Path.Combine(Root, profile.name + "-events.csv"), eventRows); }
                yield return null;
            }
            File.WriteAllLines(Path.Combine(Root, profile.name + ".csv"), rows); File.WriteAllLines(Path.Combine(Root, profile.name + "-events.csv"), eventRows);
            details.Add("PASS replay " + profile.name + ":120 observations; events=" + receiver.events.Count);
            Drop(image); Drop(decoder.sourceTexture); Drop(decoder.payloadByteTexture); Drop(owner); Drop(codec.gameObject); if (luma != codec) Drop(luma.gameObject);
        }
    }
    int ValidateValue(byte[] value, int index, int sourceOrdinal)
    {
        if (value == null) return 0;
        int generation = (int)Binary.ReadUInt32LE(value, 0), period = new[] { 1, 3, 6 }[index];
        Check(generation >= 1 && generation <= sourceOrdinal && (generation - 1) % period == 0, "Invalid applied generation");
        Check(value.SequenceEqual(BudgetDeliveryProbe.Value(index, generation, new[] { 64, 512, 2048 }[index])), "Wrong field bytes applied");
        return generation;
    }
}
#endif
