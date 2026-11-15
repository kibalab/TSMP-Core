#if !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-10000)]
public sealed class ContinuousFrameValidation : MonoBehaviour
{
    public TSMPCodec[] codecPrefabs;
    public Material expandMaterial;
    public static Func<ContinuousFrameValidation, Case, ILoopback> UdonFactory;
    public const int Capacity = 32768;
    public const string CsvHeader = "case,path,codec,width,height,valueBytes,payloadBytes,sample,sendTarget,loopTarget,seconds,loopHz,published,txHz,appliedInWindow,applyHz,receivedAfterDrain,missPercent,schedulerMisses,captureCount,busyPercent,encodeP95ms,latencyP50ms,latencyP95ms,latencyP99ms,latencyMaxMs,snapshotP95ms,applyGapP95ms,applyGapMaxMs,latencyFirstThirdMs,latencyLastThirdMs,sourceAgeP95ms,decoderErrorObservations,corrupt,outOfOrder,predictedReadbacks,predictionFallbacks,publicationMode";

    public sealed class Case
    {
        public string Name;
        public int Codec;
        public int Width = 640;
        public int Height = 360;
        public int Bytes = 32;
        public int Sample = 1;
        public int SendHz = 60;
        public int LoopHz = 60;
        public double Seconds = 12;
        public bool Baseline;
        public bool PublishEveryFrame;
    }

    public interface ILoopback : IDisposable
    {
        bool Busy { get; }
        int PayloadBytes { get; }
        int AppliedCount { get; }
        int CorruptCount { get; }
        int[] AppliedIds { get; }
        float[] AppliedTimes { get; }
        string Error { get; }
        string DecoderError { get; }
        string Diagnostics { get; }
        void Publish(byte[] packet);
        void Decode();
        void Stop();
        object ReadDecoder(string name);
        object ReadEncoder(string name);
        void SetGpuEncoding(bool enabled);
        void RestartEncoder();
        void WriteDecoder(string name, object value);
        void SelectCodec(int index);
        void SetSample(int sample);
        void Resume();
        void QueueRpc();
        int RpcCount { get; }
        RenderTexture Output { get; }
        byte[] ReceivedPacket { get; }
        int ReadbackCallbacks { get; }
        void SetAutomatic(bool enabled);
        void TickAutomatic();
        Dictionary<string, Action> ProfileHelpers(int size, int width, int height);
    }

    readonly List<string> rows = new List<string>();
    readonly double[] published = new double[Capacity];
    readonly double[] captureTimes = new double[Capacity];
    readonly double[] sent = new double[Capacity];
    string resultRoot;
    string failure;
    Action scheduledStep;
    Action<string> scheduledObservation;

    void Update()
    {
        scheduledStep?.Invoke();
        scheduledObservation?.Invoke("Update");
    }

    void LateUpdate()
    {
        scheduledObservation?.Invoke("LateUpdate");
    }

#if UNITY_EDITOR
    public static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runner = new GameObject("Continuous frame validation").AddComponent<ContinuousFrameValidation>();
        runner.codecPrefabs = new[] { "Luma4", "RGB16", "RGB20", "Color256" }.Select(name =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() +
                "/Runtime/Codec_" + name + ".prefab").GetComponent<TSMPCodec>()).ToArray();
        runner.expandMaterial = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat");
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/ContinuousFrames/Validation.unity");
    }

    public static void Play()
    {
        CreateScene();
        EditorApplication.EnterPlaymode();
    }

    public static void Build()
    {
        CreateScene();
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string path = Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_BUILD");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/ContinuousFrames/Validation.unity" }, locationPathName = path,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        File.WriteAllText(Path.ChangeExtension(path, ".build-report.txt"), "Result=" + report.summary.result +
            "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\nBackend=Mono\nStripping=Disabled");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
#endif

    IEnumerator Start()
    {
        resultRoot = Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_RESULTS");
        Directory.CreateDirectory(resultRoot);
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.logMessageReceived += OnLog;
        File.WriteAllText(Path.Combine(resultRoot, "environment.txt"), "Unity=" + Application.unityVersion +
            "\nGPU=" + SystemInfo.graphicsDeviceName + "\nAPI=" + SystemInfo.graphicsDeviceType +
            "\nColorSpace=" + QualitySettings.activeColorSpace + "\nEditor=" + Application.isEditor +
            "\nPath=" + (UdonFactory == null ? "Native" : "Compiled Udon VM") +
            "\nManaged source=" + Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_REVISION") +
            "\nAutomatic scheduling=" + Environment.GetEnvironmentVariable("TSMP_AUTOMATIC_SCHEDULING") +
            "\nCombined output disabled=" + Environment.GetEnvironmentVariable("TSMP_DISABLE_COMBINED_OUTPUT") +
            "\nSingle slot=" + Environment.GetEnvironmentVariable("TSMP_SINGLE_SLOT") +
            "\nRetry disabled=" + Environment.GetEnvironmentVariable("TSMP_DISABLE_READBACK_RETRY"));
        rows.Add(CsvHeader);
        var work = Run();
        while (true)
        {
            bool next;
            try
            {
                if (failure != null) throw new InvalidOperationException(failure);
                next = work.MoveNext();
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(resultRoot, "status.txt"), "FAIL\n" + error);
                Finish(1);
                yield break;
            }
            if (!next) break;
            yield return work.Current;
        }
        File.WriteAllText(Path.Combine(resultRoot, "status.txt"), "PASS: measurements completed; this does not assert lossless delivery.");
        Finish(0);
    }

    void OnLog(string text, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) failure = text + "\n" + stack;
    }

    void Finish(int code)
    {
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    static IEnumerable<Case> Cases()
    {
        yield return new Case { Name = "profile-small30", SendHz = 30, LoopHz = 30 };
        yield return new Case { Name = "profile-small60" };
        yield return new Case { Name = "profile-large30", Width = 1280, Height = 720, Bytes = 4096, SendHz = 30, LoopHz = 30 };
        yield return new Case { Name = "profile-large60", Width = 1280, Height = 720, Bytes = 4096 };
        yield return new Case { Name = "profile-1080-small60", Width = 1920, Height = 1080 };
        yield return new Case { Name = "profile-4k-small30", Width = 3840, Height = 2160, SendHz = 30, LoopHz = 30 };
        yield return new Case { Name = "profile-luma4-sample4", Width = 1280, Height = 720, Bytes = 4096, Sample = 4 };
        yield return new Case { Name = "prediction-regression", Width = 1280, Height = 720, Bytes = 4096 };
        yield return new Case { Name = "prediction-shrink", Width = 1280, Height = 720, Bytes = 4096, Sample = 4 };
        yield return new Case { Name = "overlap-regression", Width = 1280, Height = 720, Bytes = 4096 };
        yield return new Case { Name = "sender-only-60", Baseline = true };
        foreach (int loop in new[] { 60, 90, 120, 144, 180, 240, -1 })
            yield return new Case { Name = "small-60-at-" + loop, LoopHz = loop };
        foreach (int send in new[] { 15, 30 })
            yield return new Case { Name = "small-" + send + "-at-60", SendHz = send };
        foreach (int send in new[] { 30, 90, 120 })
            yield return new Case { Name = "small-" + send + "-at-120", LoopHz = 120, SendHz = send };
        yield return new Case { Name = "large-60-at-60", Bytes = 1024 };
        yield return new Case { Name = "large-60-at-120", Bytes = 1024, LoopHz = 120 };
        for (int codec = 0; codec < 4; codec++)
            yield return new Case { Name = "codec-" + codec + "-sample4", Codec = codec, Bytes = 1024, Sample = 4, LoopHz = 120 };
        yield return new Case { Name = "hd-small", Width = 1280, Height = 720, LoopHz = 120 };
        yield return new Case { Name = "hd-large", Width = 1280, Height = 720, Bytes = 4096, LoopHz = 120 };
        yield return new Case { Name = "sustained-60", Seconds = 60 };
        yield return new Case { Name = "sustained-60-at-120", Seconds = 60, LoopHz = 120 };
        yield return new Case { Name = "small-30-at-30", SendHz = 30, LoopHz = 30 };
        yield return new Case { Name = "small-60-at-30", SendHz = 60, LoopHz = 30 };
        yield return new Case { Name = "hd-large-every-frame-at-30", Width = 1280, Height = 720, Bytes = 4096, SendHz = 30, LoopHz = 30, PublishEveryFrame = true };
        yield return new Case { Name = "sustained-every-frame-at-30", Seconds = 60, SendHz = 30, LoopHz = 30, PublishEveryFrame = true };
    }

    IEnumerator Run()
    {
        string filter = Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_FILTER");
        foreach (Case test in Cases())
        {
            if (test.Name.StartsWith("profile-") && string.IsNullOrEmpty(filter)) continue;
            if (test.Name == "prediction-shrink" && string.IsNullOrEmpty(filter)) continue;
            if (!string.IsNullOrEmpty(filter) && !filter.Split(',').Contains(test.Name)) continue;
            Application.targetFrameRate = test.LoopHz;
            using (ILoopback loop = UdonFactory != null ? UdonFactory(this, test) : CreateNative(test))
            {
                yield return null;
                var run = test.Name.StartsWith("profile-") ? Profile(test, loop) :
                    test.Name == "prediction-regression" ? Regression(loop) :
                    test.Name == "prediction-shrink" ? PredictionShrink(loop) :
                    test.Name == "overlap-regression" ? OverlapRegression(loop) : Measure(test, loop);
                while (run.MoveNext()) yield return run.Current;
            }
            yield return null;
            GC.Collect();
        }
    }

    IEnumerator Profile(Case test, ILoopback loop)
    {
        var profile = ResourceProfile.Measure(test, loop, step => scheduledStep = step, resultRoot);
        while (profile.MoveNext()) yield return profile.Current;
    }

    IEnumerator Measure(Case test, ILoopback loop)
    {
        Array.Clear(published, 0, published.Length);
        Array.Clear(sent, 0, sent.Length);
        Array.Clear(captureTimes, 0, captureTimes.Length);
        var packet = new byte[test.Bytes];
        for (int i = 0; i < packet.Length; i++) packet[i] = (byte)(i * 37);
        SetPacketId(packet, 1);
        sent[1] = Time.realtimeSinceStartupAsDouble;
        loop.Publish(packet);
        published[1] = Time.realtimeSinceStartupAsDouble;
        if (!test.Baseline)
        {
            captureTimes[1] = Time.realtimeSinceStartupAsDouble;
            loop.Decode();
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (loop.Busy && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (loop.Busy || loop.AppliedCount != 1 || loop.AppliedIds[0] != 1)
                throw new InvalidOperationException("Preflight failed: " + loop.Diagnostics);
        }
        double begin = Time.realtimeSinceStartupAsDouble + 2;
        double end = begin + test.Seconds;
        double due = Time.realtimeSinceStartupAsDouble;
        double interval = 1.0 / test.SendHz;
        int id = 1, sends = 0, updates = 0, busy = 0, captures = 0, missed = 0, decodeErrors = 0;
        var encodes = new List<double>();
        var ages = new List<double>();
        int latestApplied = 0;
        bool automatic = Environment.GetEnvironmentVariable("TSMP_AUTOMATIC_SCHEDULING") == "1" && !test.Baseline;
        var timing = new List<string> { "phase,frame,time,captures,captureFrame,captureTime,completionFrame,completionTime,retries,busy,publishedId,appliedCount" };
        bool instrumented = loop.ReadDecoder("decodeCaptureCount") != null;
        int observedCaptures = instrumented ? (int)loop.ReadDecoder("decodeCaptureCount") : 0;
        var idleTimes = new List<double>();
        Action<string> observe = phase =>
        {
            int total = (int)loop.ReadDecoder("decodeCaptureCount");
            float captured = (float)loop.ReadDecoder("lastDecodeCaptureTime");
            float completed = (float)loop.ReadDecoder("lastReadbackCompletionTime");
            if (total != observedCaptures)
            {
                int capturedId = id;
                while (capturedId > 1 && published[capturedId] > captured + .0001) capturedId--;
                if (captureTimes[capturedId] == 0) captureTimes[capturedId] = captured;
                if (captured >= begin && captured < end)
                {
                    captures += total - observedCaptures;
                    if (completed > 0 && captured >= completed) idleTimes.Add((captured - completed) * 1000);
                }
                observedCaptures = total;
            }
            timing.Add(string.Join(",", phase, Time.frameCount, N(Time.realtimeSinceStartupAsDouble), total,
                loop.ReadDecoder("lastDecodeCaptureFrame"), N(captured), loop.ReadDecoder("lastReadbackCompletionFrame"), N(completed),
                loop.ReadDecoder("automaticReadbackRetryCount"), loop.Busy, id, loop.AppliedCount));
        };
        Action step = () =>
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool measured = now >= begin && now < end;
            if (measured) updates++;
            if (test.PublishEveryFrame || now >= due)
            {
                if (!test.PublishEveryFrame)
                {
                    int elapsedSlots = Math.Max(1, (int)Math.Floor((now - due) / interval) + 1);
                    if (measured) missed += elapsedSlots - 1;
                    due += elapsedSlots * interval;
                }
                id++;
                if (id >= Capacity) throw new InvalidOperationException("Measurement capacity exceeded");
                SetPacketId(packet, id);
                sent[id] = Time.realtimeSinceStartupAsDouble;
                loop.Publish(packet);
                published[id] = Time.realtimeSinceStartupAsDouble;
                if (sent[id] >= begin && sent[id] < end)
                {
                    sends++;
                    encodes.Add((published[id] - sent[id]) * 1000);
                }
            }

            if (!test.Baseline)
            {
                if (measured && !string.IsNullOrEmpty(loop.DecoderError)) decodeErrors++;
                bool atCapacity = loop.Busy;
                object pending = loop.ReadDecoder("pendingDecodeCount");
                if (pending != null)
                    atCapacity = (int)pending >= (Environment.GetEnvironmentVariable("TSMP_SINGLE_SLOT") == "1" ? 1 : 2);
                if (atCapacity)
                {
                    if (measured) busy++;
                }
                else if (!automatic || !instrumented)
                {
                    if (captureTimes[id] == 0) captureTimes[id] = Time.realtimeSinceStartupAsDouble;
                    if (measured) captures++;
                }
                if (automatic) loop.TickAutomatic();
                else loop.Decode();
                int count = loop.AppliedCount;
                if (count > 0) latestApplied = loop.AppliedIds[count - 1];
                if (measured && latestApplied > 0) ages.Add((Time.realtimeSinceStartupAsDouble - sent[latestApplied]) * 1000);
            }
        };
        if (automatic)
        {
            loop.SetAutomatic(true);
            scheduledStep = step;
            if (instrumented) scheduledObservation = observe;
        }
        while (Time.realtimeSinceStartupAsDouble < end + 1)
        {
            if (!automatic) step();
            yield return null;
        }

        scheduledStep = null;
        scheduledObservation = null;
        loop.SetAutomatic(false);

        double stop = Time.realtimeSinceStartupAsDouble;
        while (loop.Busy && Time.realtimeSinceStartupAsDouble < stop + 5) yield return null;
        if (loop.Busy) throw new InvalidOperationException("Readback did not complete after sender stopped");
        if (!string.IsNullOrEmpty(loop.Error)) throw new InvalidOperationException(loop.Error);

        var latencies = new List<double>();
        var snapshotLatency = new List<double>();
        var gaps = new List<double>();
        var first = new List<double>();
        var last = new List<double>();
        int received = 0, inWindow = 0, outOfOrder = 0, previous = 0;
        double previousTime = 0;
        var unique = new HashSet<int>();
        var trace = new List<string> { "id,sentSeconds,publishedSeconds,capturedSeconds,appliedSeconds,latencyMs,snapshotMs,inMeasuredWindow" };
        int countFinal = loop.AppliedCount;
        int[] ids = loop.AppliedIds;
        float[] times = loop.AppliedTimes;
        for (int i = 0; i < countFinal; i++)
        {
            int applied = ids[i];
            if (applied <= previous || applied > id) outOfOrder++;
            previous = applied;
            if (applied <= 0 || applied >= Capacity || sent[applied] <= 0) continue;
            double stamp = times[i];
            double delay = (stamp - sent[applied]) * 1000;
            bool included = sent[applied] >= begin && sent[applied] < end;
            trace.Add(string.Join(",", applied, N(sent[applied]), N(published[applied]), N(captureTimes[applied]), N(stamp), N(delay), N((stamp - captureTimes[applied]) * 1000), included));
            if (!included) continue;
            if (!unique.Add(applied)) throw new InvalidOperationException("Duplicate actual application");
            received++;
            if (stamp < end) inWindow++;
            latencies.Add(delay);
            if (captureTimes[applied] > 0) snapshotLatency.Add((stamp - captureTimes[applied]) * 1000);
            if (previousTime > 0) gaps.Add((stamp - previousTime) * 1000);
            previousTime = stamp;
            if (sent[applied] < begin + test.Seconds / 3) first.Add(delay);
            if (sent[applied] >= end - test.Seconds / 3) last.Add(delay);
        }
        if (!test.Baseline && (received == 0 || outOfOrder != 0 || loop.CorruptCount != 0))
            throw new InvalidOperationException("Invalid application results: received=" + received + ", corrupt=" + loop.CorruptCount + ", order=" + outOfOrder + "; " + loop.Diagnostics);
        string row = string.Join(",", test.Name, UdonFactory == null ? "Native" : "UdonVM", codecPrefabs[test.Codec].name,
            test.Width, test.Height, test.Bytes, loop.PayloadBytes, test.Sample, test.SendHz, test.LoopHz, N(test.Seconds),
            N(updates / test.Seconds), sends, N(sends / test.Seconds), inWindow, N(inWindow / test.Seconds), received,
            N(sends == 0 ? 0 : 100.0 * (sends - received) / sends), missed, captures, N(updates == 0 ? 0 : 100.0 * busy / updates),
            N(P(encodes, .95)), N(P(latencies, .5)), N(P(latencies, .95)), N(P(latencies, .99)), N(P(latencies, 1)),
            N(P(snapshotLatency, .95)), N(P(gaps, .95)), N(P(gaps, 1)), N(Average(first)), N(Average(last)), N(P(ages, .95)),
            decodeErrors, loop.CorruptCount, outOfOrder, loop.ReadDecoder("predictedReadbackCount"), loop.ReadDecoder("predictionFallbackCount"),
            test.PublishEveryFrame ? "every-update" : "wall-clock");
        rows.Add(row);
        File.WriteAllLines(Path.Combine(resultRoot, "summary.csv"), rows);
        File.WriteAllLines(Path.Combine(resultRoot, test.Name + "-applications.csv"), trace);
        var publications = new List<string> { "id,sentSeconds,publishedSeconds,capturedSeconds,inMeasuredWindow" };
        for (int i = 1; i <= id; i++)
            publications.Add(string.Join(",", i, N(sent[i]), N(published[i]), N(captureTimes[i]), sent[i] >= begin && sent[i] < end));
        File.WriteAllLines(Path.Combine(resultRoot, test.Name + "-publications.csv"), publications);
        if (automatic && instrumented)
        {
            File.WriteAllLines(Path.Combine(resultRoot, test.Name + "-timing.csv"), timing);
            File.WriteAllText(Path.Combine(resultRoot, test.Name + "-scheduling.txt"),
                "Automatic retries=" + loop.ReadDecoder("automaticReadbackRetryCount") + "\nObserved idle p50 ms=" + N(P(idleTimes, .5)) +
                "\nObserved idle p95 ms=" + N(P(idleTimes, .95)) + "\nObserved idle samples=" + idleTimes.Count);
        }
        File.WriteAllText(Path.Combine(resultRoot, "progress.txt"), row);
        Debug.Log("CONTINUOUS " + row);
        loop.Stop();
    }

    static double Average(List<double> values) => values.Count == 0 ? 0 : values.Average();

    IEnumerator ShrinkCapture(ILoopback loop, List<string> trace, string label, int codec, byte[] expected,
        int predicted, int fallback, int callbacks, int rpcDelta = 0, bool reject = false, Action afterCapture = null, bool dirtyTail = false)
    {
        int beforeApplied = loop.AppliedCount, beforeRpc = loop.RpcCount, beforeCallbacks = loop.ReadbackCallbacks;
        uint appliedAnchor = (uint)loop.ReadDecoder("_lastAppliedFrameIndex");
        int beforePredicted = (int)loop.ReadDecoder("predictedReadbackCount"), beforeFallback = (int)loop.ReadDecoder("predictionFallbackCount");
        int payload = loop.PayloadBytes;
        byte[] header = (byte[])loop.ReadEncoder(UdonFactory == null ? "_header" : "_headerBytes");
        uint frame = Binary.ReadUInt32LE(header, FrameHeader.FrameIndexOffset);
        double started = Time.realtimeSinceStartupAsDouble;
        loop.Decode(); afterCapture?.Invoke();
        var wait = Drain(loop); while (wait.MoveNext()) yield return wait.Current;
        int actualPredicted = (int)loop.ReadDecoder("predictedReadbackCount") - beforePredicted;
        int actualFallback = (int)loop.ReadDecoder("predictionFallbackCount") - beforeFallback;
        int actualCallbacks = beforeCallbacks < 0 ? -1 : loop.ReadbackCallbacks - beforeCallbacks;
        bool crc = (bool)loop.ReadDecoder("lastPayloadCrcVerified");
        if (loop.AppliedCount != beforeApplied + (reject ? 0 : 1) || loop.RpcCount != beforeRpc + rpcDelta || loop.CorruptCount != 0 ||
            (!reject && ((uint)loop.ReadDecoder("lastFrameIndex") != frame || !(bool)loop.ReadDecoder("lastFrameValid") || !crc || !expected.SequenceEqual(loop.ReceivedPacket) ||
                (int)loop.ReadDecoder("_payloadDataBytes") != payload)) ||
            (reject && ((bool)loop.ReadDecoder("lastFrameValid") || crc || (uint)loop.ReadDecoder("_lastAppliedFrameIndex") != appliedAnchor)))
            throw new InvalidOperationException("Shrink state/CRC/atomicity failure " + label + ": " + loop.Diagnostics);
        if ((predicted >= 0 && actualPredicted != predicted) || (fallback >= 0 && actualFallback != fallback) ||
            (callbacks >= 0 && actualCallbacks >= 0 && actualCallbacks != callbacks))
            throw new InvalidOperationException("Shrink request path failure " + label + ": predicted=" + actualPredicted + ", fallback=" + actualFallback + ", callbacks=" + actualCallbacks);
        bool hasDirtyTail = false;
        if (dirtyTail)
        {
            byte[] retained = (byte[])loop.ReadDecoder("_payloadBytes");
            for (int i = payload; i < Math.Min(retained.Length, payload + 64); i++) hasDirtyTail |= retained[i] != 0;
            if (!hasDirtyTail) throw new InvalidOperationException("Shrink test did not retain a dirty payload tail: " + label);
        }
        trace.Add(string.Join(",", label, codec, payload, actualCallbacks, actualPredicted, actualFallback,
            loop.AppliedCount - beforeApplied, loop.RpcCount - beforeRpc, crc, N((Time.realtimeSinceStartupAsDouble - started) * 1000), hasDirtyTail));
    }

    IEnumerator PredictionShrink(ILoopback loop)
    {
        // Callback counts are observed by the SDK VM wrapper. Native private
        // callbacks are unavailable (-1), so Native checks public path counters.
        bool candidate = Environment.GetEnvironmentVariable("TSMP_EXPECT_SHRINK_PREDICTION") == "1";
        var trace = new List<string> { "case,codec,payloadBytes,completedReadbacks,predictedDelta,fallbackDelta,appliedDelta,rpcDelta,crcVerified,wallMs,dirtyTailPresent" };
        int id = 0;
        Func<int, byte[]> packet = size => { var value = Enumerable.Range(0, size).Select(i => (byte)(i * 37)).ToArray(); SetPacketId(value, ++id); return value; };
        loop.SetAutomatic(false); loop.WriteDecoder("debugLog", false); loop.WriteDecoder("requirePayloadCrc32", true);
        loop.WriteDecoder("recoverHeaderCrc", false); loop.WriteDecoder("maxCrcRecoveryAttempts", 0);
        loop.WriteDecoder("usePredictedReadback", true); loop.WriteDecoder("useCombinedByteOutput", true); loop.WriteDecoder("overlapReadbacks", true);
        foreach (int codec in new[] { 0, 1, 2, 3 })
        foreach (int size in new[] { 32, 33, 34, 35 })
        {
            loop.SelectCodec(codec); loop.SetSample(4);
            for (int pass = 0; pass < 2; pass++)
            {
                byte[] warm = packet(4096); loop.Publish(warm);
                var w = ShrinkCapture(loop, trace, "warm-" + codec + "-" + size + "-" + pass, codec, warm, -1, -1, -1);
                while (w.MoveNext()) yield return w.Current;
            }
            byte[] small = packet(size); loop.Publish(small);
            bool reuse = candidate && codec == 0;
            var run = ShrinkCapture(loop, trace, "shrink-" + size, codec, small, reuse ? 1 : 0, reuse ? 0 : 1, reuse ? 1 : 2,
                afterCapture: () => loop.Publish(packet(64)), dirtyTail: true);
            while (run.MoveNext()) yield return run.Current;
        }
        loop.SelectCodec(0); loop.SetSample(4);
        for (int pass = 0; pass < 2; pass++)
        {
            byte[] warm = packet(32); loop.Publish(warm);
            var w = ShrinkCapture(loop, trace, "grow-warm-" + pass, 0, warm, -1, -1, -1); while (w.MoveNext()) yield return w.Current;
        }
        byte[] growth = packet(33); loop.Publish(growth);
        var growthRun = ShrinkCapture(loop, trace, "growth-with-RGBA-padding", 0, growth, 0, 1, 2);
        while (growthRun.MoveNext()) yield return growthRun.Current;
        byte[] optionWarm = packet(512); loop.Publish(optionWarm);
        var warmRun = ShrinkCapture(loop, trace, "option-warm", 0, optionWarm, -1, -1, -1); while (warmRun.MoveNext()) yield return warmRun.Current;
        loop.SetSample(1); byte[] option = packet(32); loop.Publish(option);
        var optionRun = ShrinkCapture(loop, trace, "sample-mismatch", 0, option, 0, 1, 2); while (optionRun.MoveNext()) yield return optionRun.Current;
        loop.Output.Release(); loop.Output.width = 1920; loop.Output.height = 1080;
        if (!loop.Output.Create()) throw new InvalidOperationException("Resize control texture creation failed");
        byte[] resized = packet(33); loop.Publish(resized);
        var resizeRun = ShrinkCapture(loop, trace, "source-dimensions-mismatch", 0, resized, 0, 0, 2); while (resizeRun.MoveNext()) yield return resizeRun.Current;
        loop.Output.Release(); loop.Output.width = 1280; loop.Output.height = 720; loop.Output.Create(); loop.SetSample(4);

        foreach (bool damageHeader in new[] { true, false })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                byte[] warm = packet(4096); loop.Publish(warm);
                var w = ShrinkCapture(loop, trace, "crc-warm-" + damageHeader + "-" + pass, 0, warm, -1, -1, -1); while (w.MoveNext()) yield return w.Current;
            }
            loop.QueueRpc(); byte[] small = packet(32); loop.Publish(small);
            var image = new Texture2D(loop.Output.width, loop.Output.height, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active; RenderTexture.active = loop.Output;
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); RenderTexture.active = previous;
            Color32[] intact = image.GetPixels32();
            int valueByte = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.VariableStateBodyHeaderBytes + NetworkFrameProtocol.VariableValueHeaderBytes + 20;
            int block = (damageHeader ? 2 : Luma4Raster.PayloadStartRow) * (image.width / 8) + (damageHeader ? FrameHeader.CrcOffset : valueByte) * 2;
            int x0 = block % (image.width / 8) * 8, y0 = block / (image.width / 8) * 8;
            for (int y = y0; y < y0 + 8; y++) for (int x = x0; x < x0 + 8; x++)
            { Color c = image.GetPixel(x, image.height - 1 - y); image.SetPixel(x, image.height - 1 - y, c.r > .5f ? Color.black : Color.white); }
            image.Apply(false, false); Graphics.Blit(image, loop.Output);
            var bad = ShrinkCapture(loop, trace, damageHeader ? "bad-header" : "bad-prefix-payload", 0, small, -1, -1, damageHeader || candidate ? 1 : 2, reject: true);
            while (bad.MoveNext()) yield return bad.Current;
            image.SetPixels32(intact); image.Apply(false, false); Graphics.Blit(image, loop.Output); Destroy(image);
            var retry = ShrinkCapture(loop, trace, "intact-same-frame-retry-" + damageHeader, 0, small, 0, 0, 2, rpcDelta: 1);
            while (retry.MoveNext()) yield return retry.Current;
            // Consume remaining sender repetitions before queuing the next RPC.
            // These images are deliberately not decoded; the intact retry above
            // is the only application of this protected RPC in this scenario.
            for (int repeat = 0; repeat < 16 && (int)loop.ReadEncoder("queuedRpcCount") > 0; repeat++) loop.Publish(packet(32));
            if ((int)loop.ReadEncoder("queuedRpcCount") > 0) throw new InvalidOperationException("Shrink RPC repetition cleanup failed");
        }

        foreach (bool growFirst in new[] { false, true })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                byte[] warm = packet(512); loop.Publish(warm);
                var w = ShrinkCapture(loop, trace, "overlap-warm-" + growFirst + "-" + pass, 0, warm, -1, -1, -1); while (w.MoveNext()) yield return w.Current;
            }
            int beforeApplied = loop.AppliedCount, beforeRpc = loop.RpcCount, beforeCallbacks = loop.ReadbackCallbacks;
            int beforePredicted = (int)loop.ReadDecoder("predictedReadbackCount"), beforeFallback = (int)loop.ReadDecoder("predictionFallbackCount");
            byte[] first = packet(growFirst ? 4096 : 32); loop.Publish(first); loop.Decode();
            byte[] second = packet(growFirst ? 32 : 4096); loop.Publish(second); loop.Decode();
            if ((int)loop.ReadDecoder("pendingDecodeCount") != 2) throw new InvalidOperationException("Shrink/grow did not occupy both slots");
            loop.Publish(packet(64)); var wait = Drain(loop); while (wait.MoveNext()) yield return wait.Current;
            int callbacks = beforeCallbacks < 0 ? -1 : loop.ReadbackCallbacks - beforeCallbacks;
            int predicted = (int)loop.ReadDecoder("predictedReadbackCount") - beforePredicted, fallback = (int)loop.ReadDecoder("predictionFallbackCount") - beforeFallback;
            int firstId = first[0] | first[1] << 8 | first[2] << 16, secondId = second[0] | second[1] << 8 | second[2] << 16;
            if (loop.AppliedCount != beforeApplied + 2 || loop.AppliedIds[beforeApplied] != firstId || loop.AppliedIds[beforeApplied + 1] != secondId ||
                !second.SequenceEqual(loop.ReceivedPacket) || loop.RpcCount != beforeRpc || loop.CorruptCount != 0 ||
                !(bool)loop.ReadDecoder("lastPayloadCrcVerified") || predicted != (candidate ? 1 : 0) || fallback != (candidate ? 1 : 2) ||
                (callbacks >= 0 && callbacks != (candidate ? 3 : 4))) throw new InvalidOperationException("Shrink/grow slot isolation failure: " + loop.Diagnostics);
            trace.Add(string.Join(",", "overlap-grow-first-" + growFirst, 0, -1, callbacks, predicted, fallback, 2, 0, true, -1, false));
        }
        File.WriteAllLines(Path.Combine(resultRoot, "prediction-shrink.csv"), trace);
        File.WriteAllText(Path.Combine(resultRoot, "prediction-shrink.txt"), "PASS\nExpected shrink reuse=" + candidate +
            "\nLuma4 plain shrink, RGBA remainders, retained dirty tails, growth, strict other codecs, metadata, CRC/RPC retry and both shrink/grow slot orders." +
            "\ncompletedReadbacks=-1 means Native private callback count unavailable; SDK VM counts actual completed callback events. wallMs includes frame scheduling and is not GPU execution time.\n");
        loop.Stop();
    }

    IEnumerator Drain(ILoopback loop)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 10;
        while (loop.Busy && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        if (loop.Busy) throw new InvalidOperationException("Regression readback timeout");
    }

    static void CheckEncoderPayloadCrc(ILoopback loop)
    {
        byte[] header = (byte[])loop.ReadEncoder(UdonFactory == null ? "_header" : "_headerBytes");
        byte[] payload = (byte[])loop.ReadEncoder(UdonFactory == null ? "_encodedPayload" : "_payloadBytes");
        if (!FrameHeaderReader.TryValidatePayload(header, 0, payload, loop.PayloadBytes, true, null,
            out bool present, out uint expected, out uint actual, out string error) || !present || expected != actual)
            throw new InvalidOperationException("Production encoder did not emit a valid payload CRC: " + error);
    }

    IEnumerator Regression(ILoopback loop)
    {
        var results = new List<string>();
        int id = 0;
        Func<int, byte[]> packet = size =>
        {
            var bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 37)).ToArray();
            SetPacketId(bytes, ++id);
            return bytes;
        };
        foreach (int codec in new[] { 0, 1, 2, 3, 0 })
        {
            loop.SelectCodec(codec);
            foreach (int size in new[] { 32, 32, 33, 1024, 32, 4096, 32 })
            {
                int count = loop.AppliedCount;
                byte[] expected = packet(size);
                loop.Publish(expected);
                CheckEncoderPayloadCrc(loop);
                loop.Decode();
                loop.Publish(packet(size));
                var wait = Drain(loop);
                while (wait.MoveNext()) yield return wait.Current;
                if (loop.AppliedCount != count + 1 || loop.AppliedIds[count] != id - 1 || loop.CorruptCount != 0 || !expected.SequenceEqual(loop.ReceivedPacket))
                    throw new InvalidOperationException("Prediction source isolation failed: codec=" + codec + ", size=" + size + "; " + loop.Diagnostics);
            }
        }
        results.Add("PASS codec switches, growth/shrink, multi-row readback, source overwritten during prediction/fallback");
        foreach (int sample in new[] { 4, 1, 4, 1 })
        {
            loop.SetSample(sample);
            int count = loop.AppliedCount;
            loop.Publish(packet(32));
            loop.Decode();
            var wait = Drain(loop);
            while (wait.MoveNext()) yield return wait.Current;
            if (loop.AppliedCount != count + 1 || loop.AppliedIds[count] != id)
                throw new InvalidOperationException("Header option fallback failed: " + loop.Diagnostics);
        }
        results.Add("PASS header sample-size changes");

        foreach(bool gpu in new[] { false,true })
        {
            loop.SetGpuEncoding(gpu);
            loop.RestartEncoder();
            if(loop.ReadEncoder("_gpuUpload")!=null || loop.ReadEncoder("_gpuSymbols")!=null)
                throw new InvalidOperationException("Encoder retained GPU textures after disable");
            int count=loop.AppliedCount;
            byte[] expected=packet(32);
            loop.Publish(expected);
            if((bool)loop.ReadEncoder("lastFrameUsedGpuLuma4")!=gpu)
                throw new InvalidOperationException("Encoder did not use expected CPU/GPU path");
            loop.Decode();
            var wait=Drain(loop);
            while(wait.MoveNext()) yield return wait.Current;
            if(loop.AppliedCount!=count+1 || !expected.SequenceEqual(loop.ReceivedPacket))
                throw new InvalidOperationException("CPU/GPU switch or encoder restart changed payload");
        }
        results.Add("PASS encoder CPU/GPU switch, disable releases GPU resources, re-enable recovers");

        // Damage a state value beyond the probe's marker bytes; preserve its header
        // and the following RPC. Nothing may apply before the whole payload CRC passes.
        int beforePayloadCrc = loop.AppliedCount;
        int beforePayloadRpc = loop.RpcCount;
        loop.QueueRpc();
        byte[] crcPacket = packet(32);
        loop.Publish(crcPacket);
        CheckEncoderPayloadCrc(loop);
        RenderTexture crcPrevious = RenderTexture.active;
        var crcImage = new Texture2D(loop.Output.width, loop.Output.height, TextureFormat.RGBA32, false, true);
        RenderTexture.active = loop.Output;
        crcImage.ReadPixels(new Rect(0, 0, crcImage.width, crcImage.height), 0, 0);
        Color32[] intact = crcImage.GetPixels32();
        int payloadValueByte = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes
            + NetworkFrameProtocol.VariableStateBodyHeaderBytes + NetworkFrameProtocol.VariableValueHeaderBytes + 20;
        int payloadBlock = Luma4Raster.PayloadStartRow * (crcImage.width / 8) + payloadValueByte * 2;
        int crcX = payloadBlock % (crcImage.width / 8) * 8;
        int crcY = payloadBlock / (crcImage.width / 8) * 8;
        for (int y = crcY; y < crcY + 8; y++)
        for (int x = crcX; x < crcX + 8; x++)
        {
            Color c = crcImage.GetPixel(x, crcImage.height - 1 - y);
            crcImage.SetPixel(x, crcImage.height - 1 - y, c.r > .5f ? Color.black : Color.white);
        }
        crcImage.Apply(false, false);
        Graphics.Blit(crcImage, loop.Output);
        RenderTexture.active = crcPrevious;
        bool crcDebug = (bool)loop.ReadDecoder("debugLog");
        loop.WriteDecoder("debugLog", false);
        loop.Decode();
        var payloadWait = Drain(loop);
        while (payloadWait.MoveNext()) yield return payloadWait.Current;
        if (loop.AppliedCount != beforePayloadCrc || loop.RpcCount != beforePayloadRpc
            || !(bool)loop.ReadDecoder("lastHeaderValid") || (bool)loop.ReadDecoder("lastFrameValid")
            || !(bool)loop.ReadDecoder("lastPayloadCrcPresent") || (bool)loop.ReadDecoder("lastPayloadCrcVerified"))
            throw new InvalidOperationException("Damaged production payload applied state/RPC or escaped CRC rejection: " + loop.Diagnostics);
        crcImage.SetPixels32(intact); crcImage.Apply(false, false);
        Graphics.Blit(crcImage, loop.Output); Destroy(crcImage);
        loop.Decode();
        payloadWait = Drain(loop);
        while (payloadWait.MoveNext()) yield return payloadWait.Current;
        loop.WriteDecoder("debugLog", crcDebug);
        if (loop.AppliedCount != beforePayloadCrc + 1 || loop.RpcCount != beforePayloadRpc + 1
            || !(bool)loop.ReadDecoder("lastPayloadCrcVerified") || !crcPacket.SequenceEqual(loop.ReceivedPacket))
            throw new InvalidOperationException("CRC failure poisoned same-frame/event retry: " + loop.Diagnostics);
        results.Add("PASS production header payload CRC, pixel-damaged state+RPC atomic rejection and intact same-frame/event retry");

        int beforeCrc = loop.AppliedCount;
        loop.Publish(packet(32));
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = loop.Output;
        var damaged = new Texture2D(loop.Output.width, loop.Output.height, TextureFormat.RGBA32, false, true);
        damaged.ReadPixels(new Rect(0, 0, damaged.width, damaged.height), 0, 0);
        int block = 2 * (damaged.width / 8) + FrameHeader.CrcOffset * 2;
        int x0 = (block % (damaged.width / 8)) * 8;
        int y0 = (block / (damaged.width / 8)) * 8;
        for (int y = y0; y < y0 + 8; y++)
        for (int x = x0; x < x0 + 8; x++)
        {
            Color c = damaged.GetPixel(x, damaged.height - 1 - y);
            damaged.SetPixel(x, damaged.height - 1 - y, c.r > .5f ? Color.black : Color.white);
        }
        damaged.Apply(false, false);
        Graphics.Blit(damaged, loop.Output);
        RenderTexture.active = previous;
        Destroy(damaged);
        loop.Decode();
        var crcWait = Drain(loop);
        while (crcWait.MoveNext()) yield return crcWait.Current;
        if (loop.AppliedCount != beforeCrc || !((string)loop.ReadDecoder("lastError")).Contains("Header CRC mismatch"))
            throw new InvalidOperationException("CRC rejection failed: " + loop.Diagnostics);
        results.Add("PASS corrupt current header discarded without applying speculative payload");

        for (int pass = 0; pass < 2; pass++)
        {
            loop.Publish(packet(32));
            loop.Decode();
            var wait = Drain(loop);
            while (wait.MoveNext()) yield return wait.Current;
        }
        int beforeDisable = loop.AppliedCount;
        loop.Publish(packet(32));
        loop.Decode();
        loop.Stop();
        loop.Resume();
        var disableWait = Drain(loop);
        while (disableWait.MoveNext()) yield return disableWait.Current;
        if (loop.AppliedCount != beforeDisable) throw new InvalidOperationException("Disabled pending frame applied");
        loop.Publish(packet(32));
        loop.Decode();
        var restartWait = Drain(loop);
        while (restartWait.MoveNext()) yield return restartWait.Current;
        if (loop.AppliedCount != beforeDisable + 1) throw new InvalidOperationException("Resume failed: " + loop.Diagnostics);
        results.Add("PASS disable/enable discards pending combined readback and recovers");

        int expectedRpcs = loop.RpcCount;
        for (int eventIndex = 0; eventIndex < 12; eventIndex++)
        {
            loop.QueueRpc();
            expectedRpcs++;
            for (int repeat = 0; repeat < 4; repeat++)
            {
                loop.Publish(packet(32));
                if (repeat == 0 || repeat == 2) continue;
                loop.Decode();
                var wait = Drain(loop);
                while (wait.MoveNext()) yield return wait.Current;
            }
            if (loop.RpcCount != expectedRpcs) throw new InvalidOperationException("RPC replay/loss: " + loop.RpcCount + " != " + expectedRpcs);
        }
        results.Add("PASS 12 RPC events: two of four copies deliberately skipped; each executed once");
        var otherCase = new Case { Codec = 1, Bytes = 1024 };
        using (ILoopback other = UdonFactory != null ? UdonFactory(this, otherCase) : CreateNative(otherCase))
        {
            for (int pass = 0; pass < 8; pass++)
            {
                byte[] firstPacket = packet(32);
                byte[] secondPacket = packet(1024);
                int firstCount = loop.AppliedCount;
                int secondCount = other.AppliedCount;
                loop.Publish(firstPacket);
                other.Publish(secondPacket);
                loop.Decode();
                other.Decode();
                var firstWait = Drain(loop);
                while (firstWait.MoveNext()) yield return firstWait.Current;
                var secondWait = Drain(other);
                while (secondWait.MoveNext()) yield return secondWait.Current;
                if (loop.AppliedCount != firstCount + 1 || other.AppliedCount != secondCount + 1 ||
                    !loop.ReceivedPacket.SequenceEqual(firstPacket) || !other.ReceivedPacket.SequenceEqual(secondPacket))
                    throw new InvalidOperationException("Concurrent decoder material isolation failed");
            }
            other.Stop();
        }
        results.Add("PASS two concurrent decoders with different codecs, image sizes and payload lengths");
        if ((int)loop.ReadDecoder("predictedReadbackCount") == 0 || (int)loop.ReadDecoder("predictionFallbackCount") == 0)
            throw new InvalidOperationException("Prediction and fallback paths must both run");
        results.Add("Predicted=" + loop.ReadDecoder("predictedReadbackCount") + "; Fallback=" + loop.ReadDecoder("predictionFallbackCount"));
        results.Add("Combined byte outputs=" + loop.ReadDecoder("combinedByteOutputCount"));
        File.WriteAllLines(Path.Combine(resultRoot, "regression.txt"), results);
        loop.Stop();
    }
    IEnumerator OverlapRegression(ILoopback loop)
    {
        loop.SetAutomatic(false);
        loop.WriteDecoder("overlapReadbacks", true);
        var results = new List<string>();
        int id = 0;
        Func<int, byte[]> packet = size =>
        {
            var value = Enumerable.Range(0, size).Select(i => (byte)(i * 37)).ToArray();
            SetPacketId(value, ++id);
            return value;
        };
        foreach (int codec in new[] { 0, 1, 2, 3, 0 })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                loop.Publish(packet(32));
                loop.Decode();
                var warm = Drain(loop);
                while (warm.MoveNext()) yield return warm.Current;
            }
            int before = loop.AppliedCount;
            int skipped = (int)loop.ReadDecoder("skippedBusyDecodeCount");
            byte[] first = packet(33);
            loop.Publish(first);
            loop.Decode();
            loop.SelectCodec(codec);
            loop.SetSample(codec % 2 == 0 ? 1 : 4);
            byte[] second = packet(4096);
            loop.Publish(second);
            loop.Decode();
            loop.Publish(packet(32));
            loop.Decode();
            if ((int)loop.ReadDecoder("pendingDecodeCount") != 2 || (int)loop.ReadDecoder("skippedBusyDecodeCount") != skipped + 1)
                throw new InvalidOperationException("Two-slot capacity limit failed");
            var wait = Drain(loop);
            while (wait.MoveNext()) yield return wait.Current;
            if (loop.AppliedCount != before + 2 || loop.AppliedIds[before] != id - 2 || loop.AppliedIds[before + 1] != id - 1 ||
                !second.SequenceEqual(loop.ReceivedPacket) || loop.CorruptCount != 0)
                throw new InvalidOperationException("Slot source/configuration isolation failed: " + loop.Diagnostics);
        }
        results.Add("PASS two simultaneous captures, third rejected at capacity, ordered application across codec/sample/length changes and source overwrite");

        for (int scenario = 0; scenario < 3; scenario++)
        {
            int before = loop.AppliedCount;
            bool previousDebugLog = (bool)loop.ReadDecoder("debugLog");
            loop.Publish(packet(32));
            loop.Decode();
            byte[] second = packet(32);
            loop.Publish(second);
            loop.Decode();
            loop.WriteDecoder("_processingReadbacks", true);
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            int[] states = (int[])loop.ReadDecoder("_slotStates");
            while ((states[0] != 2 || states[1] != 2) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (states[0] != 2 || states[1] != 2) throw new InvalidOperationException("Both real readbacks did not finish");
            int head = (int)loop.ReadDecoder("_slotHead");
            if (scenario == 0)
            {
                states[head] = 1;
                loop.WriteDecoder("_processingReadbacks", false);
                loop.TickAutomatic();
                yield return null;
                if (loop.AppliedCount != before) throw new InvalidOperationException("Younger ready frame overtook older pending frame");
                states[head] = 2;
            }
            else
            {
                if (scenario == 1)
                    ((bool[])loop.ReadDecoder("_slotDiscarded"))[head] = true;
                else
                {
                    loop.WriteDecoder("debugLog", false);
                    ((string[])loop.ReadDecoder("_slotErrors"))[head] = "Injected completed-slot error for regression validation.";
                }
                loop.WriteDecoder("_processingReadbacks", false);
            }
            loop.TickAutomatic();
            var wait = Drain(loop);
            while (wait.MoveNext()) yield return wait.Current;
            loop.WriteDecoder("debugLog", previousDebugLog);
            int expected = scenario == 0 ? 2 : 1;
            if (loop.AppliedCount != before + expected || !second.SequenceEqual(loop.ReceivedPacket))
                throw new InvalidOperationException("Ordered readiness/discard drain failed: " + loop.Diagnostics);
        }
        results.Add("PASS controlled readiness reordering after real GPU completion; younger result held until head ready; discarded/failed head does not stall younger result");

        int beforeDisable = loop.AppliedCount;
        loop.Publish(packet(32));
        loop.Decode();
        loop.Publish(packet(32));
        loop.Decode();
        loop.Stop();
        loop.Resume();
        var disableWait = Drain(loop);
        while (disableWait.MoveNext()) yield return disableWait.Current;
        if (loop.AppliedCount != beforeDisable) throw new InvalidOperationException("Cancelled slot applied after re-enable");
        loop.Publish(packet(32));
        loop.Decode();
        var resumeWait = Drain(loop);
        while (resumeWait.MoveNext()) yield return resumeWait.Current;
        if (loop.AppliedCount != beforeDisable + 1) throw new InvalidOperationException("Two-slot cancellation failed to recover");
        results.Add("PASS disable/re-enable cancels both pending slots and recovers");

        int expectedRpcs = loop.RpcCount;
        for (int eventIndex = 0; eventIndex < 8; eventIndex++)
        {
            loop.QueueRpc();
            expectedRpcs++;
            for (int pair = 0; pair < 2; pair++)
            {
                loop.Publish(packet(32));
                loop.Decode();
                loop.Publish(packet(32));
                loop.Decode();
                var wait = Drain(loop);
                while (wait.MoveNext()) yield return wait.Current;
            }
            if (loop.RpcCount != expectedRpcs) throw new InvalidOperationException("Overlapping RPC replay/loss");
        }
        results.Add("PASS eight RPC events with overlapping repeated copies applied exactly once");
        File.WriteAllLines(Path.Combine(resultRoot, "overlap-regression.txt"), results);
        loop.Stop();
    }

    static void SetPacketId(byte[] packet, int id)
    {
        packet[0] = (byte)(id & 255);
        packet[1] = (byte)((id >> 8) & 255);
        packet[2] = (byte)((id >> 16) & 255);
        for (int i = 0; i < 3; i++) packet[packet.Length - 4 + i] = (byte)(255 - packet[i]);
    }
    static double P(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
    }
    static string N(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    public static RenderTexture Texture(int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!texture.Create()) throw new InvalidOperationException("RenderTexture allocation failed");
        return texture;
    }

    public TSMPCodec CloneCodec(int index, List<Object> owned)
    {
        TSMPCodec codec = Instantiate(codecPrefabs[index].gameObject).GetComponent<TSMPCodec>();
        owned.Add(codec.gameObject);
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> copy = material =>
        {
            if (material == null) return null;
            if (!copies.TryGetValue(material, out Material clone))
            {
                clone = new Material(material);
                copies.Add(material, clone);
                owned.Add(clone);
            }
            return clone;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, copy((Material)field.GetValue(codec)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] array)
                field.SetValue(codec, array.Select(copy).ToArray());
        }
        return codec;
    }

    static ILoopback CreateNative(Case test)
    {
#if !UDONSHARP
        return new NativeLoopback(FindObjectOfType<ContinuousFrameValidation>(), test);
#else
        throw new InvalidOperationException("SDK tests must install the compiled Udon adapter");
#endif
    }

#if !UDONSHARP
    sealed class NativeLoopback : ILoopback
    {
        readonly List<Object> owned = new List<Object>();
        readonly TSMPEncoder encoder;
        readonly TSMPDecoder decoder;
        readonly ContinuousFrameProbe sender;
        readonly ContinuousFrameProbe receiver;
        readonly ContinuousFrameValidation runner;
        readonly Dictionary<int, TSMPCodec> codecs = new Dictionary<int, TSMPCodec>();
        public NativeLoopback(ContinuousFrameValidation runner, Case test)
        {
            this.runner = runner;
            var root = new GameObject("Continuous native loopback");
            root.SetActive(false);
            owned.Add(root);
            sender = root.AddComponent<ContinuousFrameProbe>();
            receiver = root.AddComponent<ContinuousFrameProbe>();
            sender.networkId = receiver.networkId = 1;
            receiver.appliedIds = new int[Capacity];
            receiver.appliedTimes = new float[Capacity];
            TSMPCodec codec = runner.CloneCodec(test.Codec, owned);
            TSMPCodec luma = test.Codec == 0 ? codec : runner.CloneCodec(0, owned);
            codecs[0] = luma;
            codecs[test.Codec] = codec;
            encoder = root.AddComponent<TSMPEncoder>();
            encoder.autoEncode = false;
            encoder.useGpuLuma4 = Environment.GetEnvironmentVariable("TSMP_DISABLE_GPU_LUMA4") != "1";
            encoder.output = Texture(test.Width, test.Height);
            owned.Add(encoder.output);
            encoder.sampleSize = test.Sample;
            encoder.frameIndex = 1;
            encoder.selectedCodec = codec;
            encoder.networkBehaviours = new TSMPNetworkBehaviour[] { sender };
            encoder.debugLog = false;
            decoder = root.AddComponent<TSMPDecoder>();
            decoder.usePredictedReadback = Environment.GetEnvironmentVariable("TSMP_DISABLE_PREDICTION") != "1";
            decoder.useCombinedByteOutput = Environment.GetEnvironmentVariable("TSMP_DISABLE_COMBINED_OUTPUT") != "1";
            decoder.overlapReadbacks = Environment.GetEnvironmentVariable("TSMP_SINGLE_SLOT") != "1";
            typeof(TSMPDecoder).GetField("retryAfterReadback")?.SetValue(decoder, Environment.GetEnvironmentVariable("TSMP_DISABLE_READBACK_RETRY") != "1");
            decoder.applyEveryFrame = false;
            decoder.sourceTexture = encoder.output;
            decoder.payloadByteTexture = Texture(512, Math.Max(1, (test.Bytes + 64 + 2047) / 2048));
            owned.Add(decoder.payloadByteTexture);
            decoder.codecHandlers = test.Codec == 0 ? new[] { luma } : new[] { luma, codec };
            decoder.flipY = true;
            decoder.sampleSize = test.Sample;
            decoder.debugLog = true;
            var field = TransSyncMetadata.GetOrCreate(null, typeof(ContinuousFrameProbe)).Fields.Single();
            decoder.bindingTargets = new Component[] { receiver };
            decoder.bindingNetworkIds = new ushort[] { 1 };
            decoder.bindingVariableHashes = new[] { field.VariableHash };
            decoder.bindingValueTypes = new[] { (byte)field.ValueType };
            decoder.bindingFieldNames = new[] { nameof(ContinuousFrameProbe.packet) };
            decoder.bindingDirections = new[] { 0 };
            root.SetActive(true);
        }
        public bool Busy => decoder.readbackInFlight;
        public int PayloadBytes => encoder.payloadBytes;
        public int AppliedCount => receiver.appliedCount;
        public int CorruptCount => receiver.corruptCount;
        public int[] AppliedIds => receiver.appliedIds;
        public float[] AppliedTimes => receiver.appliedTimes;
        public string Error => encoder.lastError;
        public string DecoderError => decoder.lastFrameValid ? null : decoder.lastError;
        public string Diagnostics => "header=" + decoder.lastHeaderValid + ", valid=" + decoder.lastFrameValid + ", frame=" + decoder.lastFrameIndex + ", messages=" + decoder.lastNetworkMessageCount + ", applied=" + decoder.lastAppliedVariableCount + ", error=" + decoder.lastError + ", receiverId=" + receiver.networkId + ", senderId=" + sender.networkId;
        public void Publish(byte[] packet)
        {
            sender.packet = packet;
            uint before = encoder.frameIndex;
            encoder.EncodeNow();
            if (encoder.frameIndex != before + 1) throw new InvalidOperationException("Encoder output failure: " + encoder.lastError);
        }
        public void Decode() => decoder.DecodeNow();
        public void SetAutomatic(bool enabled) => decoder.applyEveryFrame = enabled;
        public void TickAutomatic() { }
        public Dictionary<string, Action> ProfileHelpers(int size, int width, int height)
        {
            receiver.profilePixels = new Color32[(size + 3) / 4];
            receiver.profileBuffer = new byte[size];
            receiver.profileValue = new byte[size];
            receiver.profileCrcTable = Crc32Runtime.EnsureTable(null);
            receiver.profileWidthBlocks = width / 8;
            receiver.profileHeightBlocks = height / 8;
            receiver.profileBasePixels = new Color32[width / 8 * (height / 8)];
            receiver.profileFramePixels = new Color32[receiver.profileBasePixels.Length];
            receiver.profileColors = SymbolCodec.CreateLuma4Colors();
            return new Dictionary<string, Action>
            {
                { "CopyPixels", receiver.ProfileCopyPixels }, { "CopyRawBytes", receiver.ProfileCopyRawBytes },
                { "WriteRawBytes", receiver.ProfileWriteRawBytes }, { "HeaderCrc", receiver.ProfileHeaderCrc },
                { "Control", receiver.ProfileControl }, { "BasePixelCopy", receiver.ProfileBasePixelCopy },
                { "LumaPayload", receiver.ProfileLumaPayload }
            };
        }
        public void Stop() => decoder.enabled = false;
        public void Resume() => decoder.enabled = true;
        public object ReadDecoder(string name) => typeof(TSMPDecoder).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(decoder);
        public object ReadEncoder(string name) => typeof(TSMPEncoder).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(encoder);
        public void SetGpuEncoding(bool enabled) => encoder.useGpuLuma4=enabled;
        public void RestartEncoder() { encoder.enabled=false; encoder.enabled=true; }
        public void WriteDecoder(string name, object value) => typeof(TSMPDecoder).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(decoder, value);
        public RenderTexture Output => encoder.output;
        public byte[] ReceivedPacket => receiver.packet;
        public int ReadbackCallbacks => -1;
        public void SetSample(int sample) => encoder.sampleSize = sample;
        public int RpcCount => receiver.rpcCount;
        public void QueueRpc()
        {
            if (!encoder.QueueTransRpc(1, StableHash.Fnv1A32(nameof(ContinuousFrameProbe.ReceiveProbeRpc)), nameof(ContinuousFrameProbe.ReceiveProbeRpc)))
                throw new InvalidOperationException("RPC enqueue failed");
        }
        public void SelectCodec(int index)
        {
            if (!codecs.TryGetValue(index, out var codec)) codecs[index] = codec = runner.CloneCodec(index, owned);
            encoder.selectedCodec = codec;
            decoder.codecHandlers = codecs.Values.ToArray();
        }
        public void Dispose()
        {
            RenderTexture.active = null;
            foreach (var item in owned)
            {
                if (item is RenderTexture texture) texture.Release();
                if (item != null) Destroy(item);
            }
        }
    }
#endif
}
#endif
