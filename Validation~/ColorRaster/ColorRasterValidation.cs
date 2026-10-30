#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using K13A.TSMP;
using K13A.TSMP.Udon;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

// Native Player validation. Nothing here is an Udon or live-video benchmark.
public sealed class ColorRasterValidation : MonoBehaviour
{
    public TSMPCodec[] prefabs;
    public string buildSourceHashes;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string[] Names = { "Color256", "RGB16", "RGB20" };
    readonly List<Object> owned = new List<Object>();
    readonly List<string> checks = new List<string>();
    readonly List<string> profiles = new List<string>();
    readonly List<string> samples = new List<string>();
    readonly List<string> referenceChecks = new List<string>();
    readonly Dictionary<string, string> baselinePixelHashes = new Dictionary<string, string>(StringComparer.Ordinal);
    TSMPCodec[] codecs;
    string root, phase, reference;
    bool optimized, scopedAllocationValid, frameAllocationValid;
    int frames, assertions;
    ProfilerRecorder frameAllocation;

    IEnumerator Start()
    {
        root = Required("TSMP_COLOR_RASTER_RESULTS");
        phase = Required("TSMP_COLOR_RASTER_PHASE");
        optimized = phase == "optimized";
        Require(optimized || phase == "baseline", "Phase must be baseline or optimized");
        reference = Environment.GetEnvironmentVariable("TSMP_COLOR_RASTER_REFERENCE");
        if (optimized) Require(!string.IsNullOrWhiteSpace(reference) && File.Exists(Path.Combine(reference, "status.txt")), "A completed baseline directory is required");
        frames = int.Parse(Environment.GetEnvironmentVariable("TSMP_COLOR_RASTER_FRAMES") ?? "180", CultureInfo.InvariantCulture);
        Require(frames >= 60 && frames <= 3600, "Measured frames must be 60..3600");
        Directory.CreateDirectory(root);
        Require(!File.Exists(Path.Combine(root, "status.txt")), "Use fresh results");
        Application.runInBackground = true;
        Application.targetFrameRate = 30;
        QualitySettings.vSyncCount = 0;
        File.WriteAllText(Path.Combine(root, "environment.txt"), "phase=" + phase + "\nUnity=" + Application.unityVersion + "\nEditor=" + Application.isEditor +
            "\nGPU=" + SystemInfo.graphicsDeviceName + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nColorSpace=" + QualitySettings.activeColorSpace +
            "\nCPU=" + SystemInfo.processorType + "\nTargetFrameRate=30\nScope=Native Encoder CPU and frame allocations; no Udon/VRChat/video/network latency\n");
        File.WriteAllText(Path.Combine(root, "build-source-hashes.txt"), buildSourceHashes ?? "");
        checks.Add("case,kind,codec,width,height,payloadBytes,pixelSHA256,reused,independent,note");
        profiles.Add("case,codec,width,height,payloadBytes,valueBytes,samples,warmCalls,meanEncodeUs,p50EncodeUs,p95EncodeUs,maxEncodeUs,meanScopedAllocationBytes,meanFrameAllocationBytes,p95FrameAllocationBytes,gc0Collections,elapsedSeconds,observedCallsPerSecond,warmRasterPresent,warmRasterReused");
        samples.Add("case,sample,encodeUs,scopedAllocationBytes,frameAllocationBytes");
        referenceChecks.Add("case,actualFormat,actualRawBytes,actualRawSHA256,baselineOriginalRawSHA256,loadedFormat,loadedRawBytes,loadedRawSHA256,loadedCanonicalRGBA_SHA256,baselinePngSHA256");
        if (optimized)
        {
            foreach (string line in File.ReadLines(Path.Combine(reference, "checks.csv")).Skip(1))
            {
                string[] fields = ReadCsv(line);
                if (fields.Length >= 7 && fields[1] == "direct") baselinePixelHashes.Add(fields[0], fields[6]);
            }
        }
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0)
        {
            bool next; object current = null;
            try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
            catch (Exception error) { Finish(error); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
        Finish(null);
    }

    IEnumerator Run()
    {
        Require(prefabs != null && prefabs.Length == 4, "Expected Color256/RGB16/RGB20/Luma4 prefab order");
        codecs = prefabs.Take(3).Select(CloneCodec).ToArray();
        for (int i = 0; i < codecs.Length; i++)
        {
            MethodInfo method = codecs[i].GetType().GetMethod("TryWriteFrameBuffered");
            Require(method != null, "Buffered API missing");
            Require(optimized ? method.DeclaringType != typeof(TSMPCodec) : method.DeclaringType == typeof(TSMPCodec), "Wrong package phase for " + Names[i]);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        var allocationControl = new byte[8192];
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(allocationControl);
        scopedAllocationValid = delta >= 8192;
        frameAllocation = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 8);
        yield return null;
        allocationControl = new byte[16384];
        GC.KeepAlive(allocationControl);
        yield return null;
        long frameControl = frameAllocation.Valid && frameAllocation.Count > 0 ? frameAllocation.LastValue : -1;
        frameAllocationValid = frameControl >= 16384;
        File.WriteAllText(Path.Combine(root, "allocation-counter.txt"), "ScopedCounterValid=" + scopedAllocationValid + "\n8192-byteControlDelta=" + delta +
            "\nFrameCounterValid=" + frameAllocationValid + "\n16384-byteControlFrame=" + frameControl + "\nUnavailable counters are -1, never zero. Frame counter covers the whole Player frame.\n");
        DirectCases();
        yield return null;
        yield return ProductionCases();
        for (int codec = 0; codec < 3; codec++)
            foreach (int height in new[] { 720, 1440 })
                foreach (int bytes in new[] { 32, 2495, 5375 })
                    yield return Profile(codec, height, bytes);
    }

    void Finish(Exception failure)
    {
        if (frameAllocation.Valid) frameAllocation.Dispose();
        File.WriteAllLines(Path.Combine(root, "checks.csv"), checks);
        File.WriteAllLines(Path.Combine(root, "profiles.csv"), profiles);
        File.WriteAllLines(Path.Combine(root, "samples.csv"), samples);
        File.WriteAllLines(Path.Combine(root, "reference-validation.csv"), referenceChecks);
        foreach (Object value in owned.AsEnumerable().Reverse())
        {
            if (value is RenderTexture target) target.Release();
            if (value != null) Object.DestroyImmediate(value);
        }
        File.WriteAllText(Path.Combine(root, "status.txt"), failure == null ? "PASS\nPhase=" + phase + "\nAssertions=" + assertions +
            "\nNative correctness and measurements completed; this is not live-client or transport certification.\n" : "FAIL\n" + failure);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failure == null ? 0 : 1);
#else
        Application.Quit(failure == null ? 0 : 1);
#endif
    }

    void Require(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
    static string Required(string name) { string value = Environment.GetEnvironmentVariable(name); if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(name + " is required"); return value; }
    static string Csv(object value) => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(Csv));
    static string[] ReadCsv(string line)
    {
        var values = new List<string>(); var value = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { values.Add(value.ToString()); value.Length = 0; }
            else value.Append(character);
        }
        values.Add(value.ToString()); return values.ToArray();
    }
    static byte[] CanonicalRgba(Texture2D texture)
    {
        // LoadImage may choose ARGB32 storage; GetPixels32 returns logical RGBA
        // channels independently of its raw byte ordering. Both use Unity's
        // bottom-to-top pixel order, matching the original baseline raw hash.
        Color32[] colors = texture.GetPixels32(); var result = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            int start = i * 4; result[start] = colors[i].r; result[start + 1] = colors[i].g;
            result[start + 2] = colors[i].b; result[start + 3] = colors[i].a;
        }
        return result;
    }
    static string Digest(byte[] value) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    static T Field<T>(TSMPEncoder encoder, string name) => (T)typeof(TSMPEncoder).GetField(name, Private).GetValue(encoder);
    T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    TSMPCodec CloneCodec(TSMPCodec original)
    {
        TSMPCodec codec = Own(Object.Instantiate(original.gameObject)).GetComponent<TSMPCodec>();
        var materials = new Dictionary<Material, Material>();
        Func<Material, Material> clone = material =>
        {
            if (material == null) return null;
            if (!materials.TryGetValue(material, out Material copy)) materials[material] = copy = Own(new Material(material));
            return copy;
        };
        foreach (FieldInfo field in codec.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] array) field.SetValue(codec, array.Select(clone).ToArray());
        }
        return codec;
    }
    static byte[] Bytes(int count, int seed)
    {
        var value = new byte[count];
        for (int i = 0; i < count; i++) value[i] = (byte)(seed * 29 + i * 73 + (i >> 8));
        return value;
    }
    byte[] Header(TSMPCodec codec, int width, int height, byte[] payload, uint id)
    {
        var header = FrameHeader.CreateDefault();
        header.BlockSize = 8; header.ActiveWidthBlocks = (ushort)(width / 8); header.ActiveHeightBlocks = (ushort)(height / 8);
        header.DecodeSampleSize = 4; header.CodecId = codec.codecId; header.SymbolMode = (byte)codec.SymbolMode;
        header.PayloadSize = (ushort)payload.Length; header.FrameIndex = id; header.TimestampMs = id * 33;
        byte[] options = codec.GetCodecOptionBytes() ?? new byte[0];
        header.CodecOptionLength = (byte)options.Length;
        if (options.Length > 0) header.CodecOption0 = options[0];
        if (options.Length > 1) header.CodecOption1 = options[1];
        if (options.Length > 2) header.CodecOption2 = options[2];
        if (options.Length > 3) header.CodecOption3 = options[3];
        if (options.Length > 4) header.CodecOption4 = options[4];
        var bytes = new byte[FrameHeader.Size]; header.WriteTo(bytes, 0);
        Require(FrameHeaderWriter.TryWritePayloadCrc32(bytes, 0, payload, payload.Length, Crc32Runtime.EnsureTable(null)), "Direct payload CRC stamp");
        return bytes;
    }
    static Texture2D Image(int width, int height) => new Texture2D(width, height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
    void DirectCases()
    {
        string controls = Path.Combine(root, "controls"); Directory.CreateDirectory(controls);
        for (int index = 0; index < 3; index++)
        {
            TSMPCodec codec = codecs[index]; Color32[] buffer = null, independent = null;
            foreach (bool alternate in index == 1 ? new[] { false, true } : new[] { false })
            {
                if (index == 1) { codec.GetType().GetField("rBits").SetValue(codec, alternate ? 3 : 4); codec.GetType().GetField("gBits").SetValue(codec, alternate ? 3 : 4); codec.GetType().GetField("bBits").SetValue(codec, alternate ? 2 : 4); }
                int resolution = 0;
                foreach (int height in new[] { 720, 1440, 720 })
                {
                    int width = height * 16 / 9;
                    var actual = Image(width, height); var fresh = Image(width, height);
                    try
                    {
                        foreach (int count in new[] { 5375, 32, 2495, 0, 1, 7 })
                        {
                            string name = Names[index] + (alternate ? "-332" : "-default") + "-" + resolution + "-" + height + "-" + count;
                            byte[] payload = Bytes(count, count + resolution + 1); byte[] header = Header(codec, width, height, payload, (uint)(count + 1));
                            Color32[] previous = buffer;
                            Require(codec.TryWriteFrameBuffered(actual, 8, header, payload, ref buffer, out string error), name + ": " + error);
                            Require(codec.TryWriteFrame(fresh, 8, header, payload, out error), name + " fresh: " + error);
                            byte[] pixels = actual.GetRawTextureData<byte>().ToArray();
                            Require(pixels.SequenceEqual(fresh.GetRawTextureData<byte>().ToArray()), name + " buffered/fresh pixels differ");
                            bool reused = previous != null && ReferenceEquals(previous, buffer);
                            if (optimized)
                            {
                                Require(buffer != null && buffer.Length == width * height, name + " wrong owned-buffer size");
                                if (previous != null && previous.Length == buffer.Length) Require(reused, name + " warm raster reallocated");
                                Require(codec.TryWriteFrameBuffered(fresh, 8, header, payload, ref independent, out error), name + " independent: " + error);
                                Require(!ReferenceEquals(buffer, independent), name + " separate encoder buffers alias");
                            }
                            string expectedPath = Path.Combine(controls, name + ".png");
                            if (optimized)
                            {
                                var frozen = Image(2, 2);
                                try
                                {
                                    byte[] png = File.ReadAllBytes(Path.Combine(reference, "controls", name + ".png"));
                                    Require(frozen.LoadImage(png), name + " frozen PNG load");
                                    byte[] loadedRaw = frozen.GetRawTextureData<byte>().ToArray(), canonical = CanonicalRgba(frozen);
                                    Require(baselinePixelHashes.TryGetValue(name, out string originalHash), name + " original baseline pixel hash missing");
                                    string actualHash = Digest(pixels);
                                    referenceChecks.Add(Row(name, actual.format, pixels.Length, actualHash, originalHash, frozen.format, loadedRaw.Length,
                                        Digest(loadedRaw), Digest(canonical), Digest(png)));
                                    File.WriteAllLines(Path.Combine(root, "reference-validation.csv"), referenceChecks);
                                    // Save the current image before any comparison failure; an
                                    // original baseline is never regenerated or overwritten.
                                    File.WriteAllBytes(expectedPath, actual.EncodeToPNG());
                                    Require(actualHash == originalHash, name + " differs from independent original baseline raw pixel hash");
                                    Require(pixels.SequenceEqual(canonical), name + " differs from normalized baseline PNG pixels");
                                    Require(header.SequenceEqual(File.ReadAllBytes(Path.Combine(reference, "controls", name + ".header.bin"))) &&
                                        payload.SequenceEqual(File.ReadAllBytes(Path.Combine(reference, "controls", name + ".payload.bin"))), name + " deterministic raw fixture changed");
                                }
                                finally { Object.DestroyImmediate(frozen); }
                            }
                            File.WriteAllBytes(expectedPath, actual.EncodeToPNG());
                            File.WriteAllBytes(Path.Combine(controls, name + ".header.bin"), header);
                            File.WriteAllBytes(Path.Combine(controls, name + ".payload.bin"), payload);
                            checks.Add(Row(name, "direct", Names[index], width, height, count, Digest(pixels), reused, optimized, "Independent baseline control; arbitrary raster payload including empty/tails"));
                        }
                        Color32[] sentinel = new[] { new Color32(7, 11, 13, 17) }; Color32[] original = sentinel;
                        var invalidPayload = Bytes(codec.GetPayloadCapacityBytes(width, height, 8) + 1, 0);
                        foreach (int invalid in new[] { 0, 1, 2, 3, 4 })
                        {
                            byte[] payload = Bytes(32, 1), header = Header(codec, width, height, payload, 1);
                            Require(!codec.TryWriteFrameBuffered(invalid == 0 ? null : actual, invalid == 1 ? 0 : 8, invalid == 2 ? null : header,
                                invalid == 3 ? null : invalid == 4 ? invalidPayload : payload, ref sentinel, out string error), "Invalid args accepted");
                            Require(ReferenceEquals(sentinel, original) && sentinel[0].r == 7 && sentinel[0].a == 17, "Invalid args mutated raster buffer");
                        }
                    }
                    finally { Object.DestroyImmediate(actual); Object.DestroyImmediate(fresh); }
                    resolution++;
                }
            }
            if (index == 1) foreach (string field in new[] { "rBits", "gBits", "bBits" }) codec.GetType().GetField(field).SetValue(codec, 4);
        }
        File.WriteAllLines(Path.Combine(root, "checks.csv"), checks);
    }

    sealed class Sender : IDisposable
    {
        public GameObject root;
        public TSMPEncoder encoder;
        public ColorRasterProbe probe;
        public RenderTexture output;
        public Sender(TSMPCodec codec, int width, int height, int payloadBytes)
        {
            root = new GameObject("Production color sender"); root.SetActive(false);
            probe = root.AddComponent<ColorRasterProbe>(); probe.networkId = 1; probe.packet = Bytes(payloadBytes - 25, 1);
            encoder = root.AddComponent<TSMPEncoder>(); encoder.autoEncode = false; encoder.debugLog = false;
            encoder.networkBehaviours = new TSMPNetworkBehaviour[] { probe }; encoder.selectedCodec = codec;
            encoder.sampleSize = 4; encoder.blockSize = 8; encoder.useGpuLuma4 = false;
            Resize(width, height); root.SetActive(true);
        }
        public void Resize(int width, int height)
        {
            if (output != null) { output.Release(); Object.DestroyImmediate(output); }
            output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
            if (!output.Create()) throw new InvalidOperationException("Sender output creation failed");
            encoder.output = output; encoder.width = width; encoder.height = height;
        }
        public void Dispose() { Object.DestroyImmediate(root); if (output != null) { output.Release(); Object.DestroyImmediate(output); } }
    }

    void CheckProduction(Sender sender, string name)
    {
        uint previousFrame = sender.encoder.frameIndex;
        sender.encoder.EncodeNow();
        Require(sender.encoder.frameIndex == previousFrame + 1 && string.IsNullOrEmpty(sender.encoder.lastError), name + ": " + sender.encoder.lastError);
        Require(sender.encoder.payloadBytes == sender.probe.packet.Length + 25, name + " unexpected NetworkFrame overhead");
        byte[] payload = Field<byte[]>(sender.encoder, "_encodedPayload"), header = Field<byte[]>(sender.encoder, "_header");
        Require(payload.Length == sender.encoder.payloadBytes && payload.Skip(25).SequenceEqual(sender.probe.packet), name + " raw NetworkFrame field changed");
        var fresh = Image(sender.encoder.width, sender.encoder.height);
        try
        {
            Require(sender.encoder.selectedCodec.TryWriteFrame(fresh, 8, header, payload, out string error), name + " fresh: " + error);
            byte[] actual = Field<Texture2D>(sender.encoder, "_stagingTexture").GetRawTextureData<byte>().ToArray();
            Require(actual.SequenceEqual(fresh.GetRawTextureData<byte>().ToArray()), name + " production/fresh pixels differ");
            Require(Binary.ReadUInt32LE(header, FrameHeader.CrcOffset) == Crc32Runtime.Compute(header, 0, 52, Crc32Runtime.EnsureTable(null)), name + " header CRC");
            Require(Binary.ReadUInt32LE(header, FrameHeader.PayloadCrc32Offset) == Crc32Runtime.Compute(payload, 0, payload.Length, Crc32Runtime.EnsureTable(null)), name + " payload CRC");
            checks.Add(Row(name, "production", sender.encoder.selectedCodec.displayName, sender.encoder.width, sender.encoder.height, payload.Length, Digest(actual),
                Field<Color32[]>(sender.encoder, "_rasterPixels") != null, true, "Actual TSMPEncoder.EncodeNow; current header includes runtime timestamp"));
        }
        finally { Object.DestroyImmediate(fresh); }
    }

    IEnumerator ProductionCases()
    {
        var luma = CloneCodec(prefabs[3]);
        var decoderRoot = new GameObject("Strict production decoder");
        var decoder = decoderRoot.AddComponent<TSMPDecoder>();
        decoder.applyEveryFrame = false; decoder.skipDuplicateFrames = false; decoder.decodeSafetyMode = 3; decoder.requirePayloadCrc32 = true;
        decoder.debugLog = false; decoder.codecHandlers = new[] { luma }.Concat(codecs).ToArray(); decoder.blockSize = 8; decoder.sampleSize = 4;
        decoder.payloadByteTexture = new RenderTexture(2048, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        decoder.payloadByteTexture.Create();
        yield return null;
        try
        {
            using (var first = new Sender(codecs[0], 1280, 720, 32))
            using (var second = new Sender(codecs[0], 1280, 720, 32))
            {
                for (int codec = 0; codec < 3; codec++)
                {
                    first.encoder.selectedCodec = codecs[codec]; second.encoder.selectedCodec = codecs[codec];
                    foreach (int height in new[] { 720, 1440, 720 })
                    {
                        first.Resize(height * 16 / 9, height); second.Resize(height * 16 / 9, height);
                        foreach (int bytes in new[] { 5375, 32, 2495 })
                        {
                            first.probe.packet = Bytes(bytes - 25, bytes); second.probe.packet = Bytes(bytes - 25, bytes + 1);
                            string name = Names[codec] + "-production-" + height + "-" + bytes;
                            CheckProduction(first, name); CheckProduction(second, name + "-independent");
                            Color32[] previous = Field<Color32[]>(first.encoder, "_rasterPixels");
                            CheckProduction(first, name + "-repeat");
                            if (optimized)
                            {
                                Require(previous != null && ReferenceEquals(previous, Field<Color32[]>(first.encoder, "_rasterPixels")), name + " production raster was not reused");
                                Require(!ReferenceEquals(previous, Field<Color32[]>(second.encoder, "_rasterPixels")), name + " production encoders share a raster");
                            }
                            decoder.sourceTexture = first.output; decoder.DecodeNow();
                            double deadline = Time.realtimeSinceStartupAsDouble + 30;
                            while (decoder.readbackInFlight && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                            Require(!decoder.readbackInFlight && decoder.lastHeaderValid && decoder.lastFrameValid && decoder.lastPayloadCrcVerified, name + " GPU: " + decoder.lastError);
                            byte[] actual = (byte[])typeof(TSMPDecoder).GetField("_payloadBytes", Private).GetValue(decoder);
                            int count = (int)typeof(TSMPDecoder).GetField("_payloadDataBytes", Private).GetValue(decoder);
                            Require(count == bytes && actual.Take(count).SequenceEqual(Field<byte[]>(first.encoder, "_encodedPayload")), name + " GPU payload differs");
                        }
                        Color32[] beforeRestart = Field<Color32[]>(first.encoder, "_rasterPixels");
                        first.encoder.enabled = false; first.encoder.enabled = true; CheckProduction(first, Names[codec] + "-restart-" + height);
                        if (optimized) Require(!ReferenceEquals(beforeRestart, Field<Color32[]>(first.encoder, "_rasterPixels")), "Disable/re-enable retained a released raster");
                    }
                }
            }
        }
        finally { decoder.payloadByteTexture.Release(); Object.DestroyImmediate(decoder.payloadByteTexture); Object.DestroyImmediate(decoderRoot); }
        File.WriteAllLines(Path.Combine(root, "checks.csv"), checks);
    }

    IEnumerator Profile(int codec, int height, int payloadBytes)
    {
        int width = height * 16 / 9;
        string name = Names[codec] + "-" + height + "-" + payloadBytes;
        using (var sender = new Sender(codecs[codec], width, height, payloadBytes))
        {
            for (int i = 0; i < 20; i++) { Fill(sender.probe.packet, i); sender.encoder.EncodeNow(); Require(string.IsNullOrEmpty(sender.encoder.lastError), name + " warm: " + sender.encoder.lastError); yield return null; }
            Color32[] warm = Field<Color32[]>(sender.encoder, "_rasterPixels");
            if (optimized) Require(warm != null, name + " warm raster missing");
            var micros = new double[frames]; var scoped = new long[frames]; var perFrame = new long[frames];
            int gcStart = GC.CollectionCount(0); double started = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < frames; i++)
            {
                Fill(sender.probe.packet, i + 20);
                long allocated = scopedAllocationValid ? GC.GetAllocatedBytesForCurrentThread() : -1;
                long start = Stopwatch.GetTimestamp(); sender.encoder.EncodeNow(); long end = Stopwatch.GetTimestamp();
                micros[i] = (end - start) * 1000000.0 / Stopwatch.Frequency;
                scoped[i] = scopedAllocationValid ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1;
                Require(string.IsNullOrEmpty(sender.encoder.lastError) && sender.encoder.payloadBytes == payloadBytes, name + " measured encode failed");
                Color32[] currentRaster = Field<Color32[]>(sender.encoder, "_rasterPixels");
                Require(!optimized || ReferenceEquals(warm, currentRaster), name + " measured warm raster changed");
                yield return null;
                perFrame[i] = frameAllocationValid && frameAllocation.Valid && frameAllocation.Count > 0 ? frameAllocation.LastValue : -1;
            }
            double elapsed = Time.realtimeSinceStartupAsDouble - started;
            int collections = GC.CollectionCount(0) - gcStart;
            profiles.Add(Row(name, Names[codec], width, height, payloadBytes, payloadBytes - 25, frames, 20, micros.Average(), Percentile(micros, .5), Percentile(micros, .95), micros.Max(),
                scopedAllocationValid ? scoped.Average() : -1, frameAllocationValid ? perFrame.Average() : -1, frameAllocationValid ? Percentile(perFrame.Select(v => (double)v).ToArray(), .95) : -1,
                collections, elapsed, frames / elapsed, warm != null, warm != null && ReferenceEquals(warm, Field<Color32[]>(sender.encoder, "_rasterPixels"))));
            for (int i = 0; i < frames; i++) samples.Add(Row(name, i, micros[i], scoped[i], perFrame[i]));
            File.WriteAllLines(Path.Combine(root, "profiles.csv"), profiles); File.WriteAllLines(Path.Combine(root, "samples.csv"), samples);
        }
        yield return null;
    }
    static void Fill(byte[] value, int seed) { for (int i = 0; i < value.Length; i++) value[i] = (byte)(seed * 29 + i * 73 + (i >> 8)); }
    static double Percentile(double[] values, double p) { double[] sorted = (double[])values.Clone(); Array.Sort(sorted); return sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))]; }
}
#endif
