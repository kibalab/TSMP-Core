#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

public static class CrcPerformanceValidation
{
    const string AssetPath = "Assets/CrcPerformance/CrcPerformanceProbe.asset";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> Summary = new List<string>();
    static readonly List<string> Raw = new List<string>();
    static readonly List<string> Correctness = new List<string>();
    static readonly List<string> Notes = new List<string>();
    static IEnumerator work;
    static string results;
    static VmProbe activeProbe;

    public static void Run()
    {
        results = Environment.GetEnvironmentVariable("TSMP_CRC_RESULTS");
        if (string.IsNullOrEmpty(results)) throw new InvalidOperationException("TSMP_CRC_RESULTS is required.");
        File.WriteAllText(Path.Combine(results, "result.txt"), "RUNNING\n");
        try
        {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/CrcPerformance/Runtime/CrcPerformanceProbe.cs");
            Check(asset.sourceCsScript != null, "CRC probe source was not imported.");
            AssetDatabase.CreateAsset(asset, AssetPath);
        }
        CompileClient();
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.CrcPerformance", true);
        Attach();
        EditorApplication.EnterPlaymode();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(results, "result.txt"), "FAIL\nPreparationFailure\n" + error);
            throw;
        }
    }

    static void CompileClient()
    {
        bool failed = false;
        Application.LogCallback listener = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true;
        };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Udon client compilation failed.");
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        if (!SessionState.GetBool("TSMP.CrcPerformance", false)) return;
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.CrcPerformance", false)) return;
        SessionState.SetBool("TSMP.CrcPerformance", false);
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.delayCall += () =>
        {
            results = Environment.GetEnvironmentVariable("TSMP_CRC_RESULTS");
            Summary.Clear(); Raw.Clear(); Correctness.Clear(); Notes.Clear();
            Summary.Add("bytes,algorithm,samples,mean_ms,p50_ms,p95_ms,min_ms,max_ms,result");
            Raw.Add("bytes,algorithm,sample,order,milliseconds,result");
            Correctness.Add("case,offset,count,bufferBytes,baselineTableEntries,productionTableEntries,reference,native,udonBaseline,udonProduction,status");
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
        activeProbe?.Dispose(); activeProbe = null;
        File.WriteAllLines(Path.Combine(results, "summary.csv"), Summary);
        File.WriteAllLines(Path.Combine(results, "samples.csv"), Raw);
        File.WriteAllLines(Path.Combine(results, "correctness.csv"), Correctness);
        if (error != null) Notes.Add(error.ToString());
        File.WriteAllLines(Path.Combine(results, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(Notes));
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    sealed class VmProbe : IDisposable
    {
        public readonly IUdonProgram Code;
        readonly UdonBehaviour backing;
        readonly FieldInfo errorField = typeof(UdonBehaviour).GetField("_hasError", Private);

        public VmProbe()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetPath);
            Code = asset.SerializedProgramAsset.RetrieveProgram();
            Check(Code != null && Code.ByteCode.Length > 0, "Missing CRC client bytecode.");
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(Code);
            backing = new GameObject("CRC benchmark VM").AddComponent<UdonBehaviour>();
            var type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved CRC VM heap.");
            type.GetField("_program", Private).SetValue(backing, Code);
            type.GetField("_udonVM", Private).SetValue(backing, vm);
            type.GetField("_udonManager", Private).SetValue(backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(backing, true);
            type.GetField("_hasDoneStart", Private).SetValue(backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols())
                events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
        }

        void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public void Input(byte[] bytes, int offset, int count, uint[] baselineTable, uint[] productionTable)
        {
            Set("input", bytes); Set("offset", offset); Set("count", count);
            Set("baselineTable", baselineTable); Set("productionTable", productionTable);
        }
        public uint Result => (uint)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol("result"));
        public double Call(string name)
        {
            long before = Stopwatch.GetTimestamp();
            backing.SendCustomEvent(name);
            long after = Stopwatch.GetTimestamp();
            Check(!(bool)errorField.GetValue(backing), "CRC VM failed in " + name);
            return (after - before) * 1000.0 / Stopwatch.Frequency;
        }
        public void Dispose() { if (backing != null) Object.DestroyImmediate(backing.gameObject); }
    }

    sealed class Case
    {
        public string Name;
        public byte[] Bytes;
        public int Offset;
        public int Count;
        public uint[] Table;
    }

    static IEnumerable<Case> Cases(uint[] table)
    {
        byte[] vector = Encoding.ASCII.GetBytes("123456789");
        yield return new Case { Name = "standard-vector", Bytes = vector, Count = vector.Length, Table = table };
        var offsetVector = Enumerable.Repeat((byte)0xA5, 33).ToArray();
        Array.Copy(vector, 0, offsetVector, 5, vector.Length);
        yield return new Case { Name = "standard-vector-offset-dirty-tail", Bytes = offsetVector, Offset = 5, Count = vector.Length, Table = table };
        yield return new Case { Name = "empty", Bytes = new byte[0], Table = table };
        yield return new Case { Name = "zero-count-at-end", Bytes = vector, Offset = vector.Length, Table = table };
        yield return new Case { Name = "null-input", Bytes = null, Count = 3, Table = table };
        yield return new Case { Name = "negative-offset", Bytes = vector, Offset = -1, Count = 1, Table = table };
        yield return new Case { Name = "negative-count", Bytes = vector, Count = -1, Table = table };
        yield return new Case { Name = "offset-past-end", Bytes = vector, Offset = vector.Length + 1, Count = 1, Table = table };
        yield return new Case { Name = "count-past-end", Bytes = vector, Count = vector.Length + 1, Table = table };
        yield return new Case { Name = "overflow-range", Bytes = vector, Offset = 5, Count = int.MaxValue, Table = table };
        yield return new Case { Name = "null-table", Bytes = vector, Count = vector.Length, Table = null };
        yield return new Case { Name = "short-table", Bytes = vector, Count = vector.Length, Table = new uint[255] };
        var random = new System.Random(31927);
        for (int index = 0; index < 64; index++)
        {
            var bytes = new byte[random.Next(1, 2049)]; random.NextBytes(bytes);
            int offset = random.Next(bytes.Length + 1);
            int count = random.Next(bytes.Length - offset + 1);
            yield return new Case { Name = "random-" + index, Bytes = bytes, Offset = offset, Count = count, Table = table };
        }
        foreach (int length in new[] { 32, 1024, 4121, 6750 })
        {
            var bytes = new byte[length + 38]; random.NextBytes(bytes);
            yield return new Case { Name = "profile-size-" + length, Bytes = bytes, Offset = 7, Count = length, Table = table };
        }
    }

    // Independent bit-at-a-time Editor reference: it does not use the lookup table.
    static uint Reference(byte[] bytes, int offset, int count, uint[] table)
    {
        if (bytes == null || count <= 0 || offset < 0 || offset > bytes.Length || count > bytes.Length - offset
            || table == null || table.Length != 256) return 0;
        uint crc = 0xFFFFFFFFu;
        for (int i = offset; i < offset + count; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }

    static IEnumerator Validate()
    {
        CompileClient();
        activeProbe = new VmProbe();
        using (var sha = SHA256.Create())
            Notes.Add("ClientBytecodeSHA256=" + Hex(sha.ComputeHash(activeProbe.Code.ByteCode)));
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.kibalab.tsmp.core/Runtime/Core/Crc/Crc32Runtime.cs");
        string sourcePath = package != null ? Path.Combine(package.resolvedPath, "Runtime/Core/Crc/Crc32Runtime.cs") : "Packages/com.kibalab.tsmp.core/Runtime/Core/Crc/Crc32Runtime.cs";
        using (var sha = SHA256.Create()) Notes.Add("ProductionSourceSHA256=" + Hex(sha.ComputeHash(File.ReadAllBytes(sourcePath))));
        File.Copy(sourcePath, Path.Combine(results, "Crc32Runtime-used.cs"), true);
        Notes.Add("Unity=" + Application.unityVersion + "; OS=" + SystemInfo.operatingSystem + "; CPU=" + SystemInfo.processorType + "; LogicalCores=" + SystemInfo.processorCount);
        Notes.Add("Revision=" + Environment.GetEnvironmentVariable("TSMP_CRC_REVISION"));
        Notes.Add("Execution=client-target Udon bytecode after Play Mode entry; one VM, shared inputs; one Compute per custom event.");
        Notes.Add("Timing=SendCustomEvent wall time including common Udon dispatch, excluding heap input/result access. Control is recorded without subtraction. No graphics, frame scheduling or end-to-end encoder claim.");
        Notes.Add("Baseline=frozen 256-table byte-loop; production=installed Crc32Runtime.Compute; both compile in the same probe, with separate public lookup-table fields.");
        long tableStarted = Stopwatch.GetTimestamp();
        uint[] table = Crc32Runtime.EnsureTable(null);
        double baselineTableMs = (Stopwatch.GetTimestamp() - tableStarted) * 1000.0 / Stopwatch.Frequency;
        Check(table.Length == 256, "Legacy baseline requires EnsureTable's 256-entry contract.");
        MethodInfo fastFactory = typeof(Crc32Runtime).GetMethod("EnsureFastTable", BindingFlags.Public | BindingFlags.Static);
        tableStarted = Stopwatch.GetTimestamp();
        uint[] productionTable = fastFactory == null ? Crc32Runtime.EnsureTable(null) :
            (uint[])fastFactory.Invoke(null, fastFactory.GetParameters().Length == 0 ? null : new object[] { null });
        double productionTableMs = (Stopwatch.GetTimestamp() - tableStarted) * 1000.0 / Stopwatch.Frequency;
        Check(productionTable != null && productionTable.Length >= 256, "Production table factory returned invalid data.");
        Notes.Add("BaselineTableEntries=" + table.Length + "; BaselineTableBytes=" + table.Length * 4 + "; NativeSetupMs=" + N(baselineTableMs));
        Notes.Add("ProductionTableFactory=" + (fastFactory == null ? "EnsureTable" : "EnsureFastTable") +
            "; ProductionTableEntries=" + productionTable.Length + "; ProductionTableBytes=" + productionTable.Length * 4 +
            "; NativeSetupMs=" + N(productionTableMs) + "; reflected fast-factory invocation included when present; VM table construction is not timed.");
        int correct = 0;
        foreach (Case item in Cases(table))
        {
            uint expected = Reference(item.Bytes, item.Offset, item.Count, item.Table);
            uint[] actualTable = ReferenceEquals(item.Table, table) ? productionTable : item.Table;
            uint native = Crc32Runtime.Compute(item.Bytes, item.Offset, item.Count, actualTable);
            activeProbe.Input(item.Bytes, item.Offset, item.Count, item.Table, actualTable);
            activeProbe.Call("RunBaseline"); uint baseline = activeProbe.Result;
            activeProbe.Call("RunProduction"); uint production = activeProbe.Result;
            bool passed = native == expected && baseline == expected && production == expected;
            Correctness.Add(string.Join(",", item.Name, item.Offset, item.Count, item.Bytes == null ? -1 : item.Bytes.Length,
                item.Table == null ? 0 : item.Table.Length, actualTable == null ? 0 : actualTable.Length,
                U(expected), U(native), U(baseline), U(production), passed ? "PASS" : "FAIL"));
            Check(passed, "CRC output mismatch in " + item.Name);
            if (item.Name == "standard-vector") Check(expected == 0xCBF43926u, "Independent CRC reference does not match standard vector.");
            correct++;
            yield return null;
        }
        Notes.Add("CorrectnessCases=" + correct + "; independent reference, Native, Udon baseline and Udon production agree.");
        int warmup = Setting("TSMP_CRC_WARMUP", 20);
        int samples = Setting("TSMP_CRC_SAMPLES", 60);
        Check(warmup >= 0 && warmup <= 100 && samples >= 20 && samples <= 200, "Invalid CRC warmup/sample settings.");
        Notes.Add("WarmupPerAlgorithm=" + warmup + "; SamplesPerAlgorithm=" + samples + "; order alternates baseline/production each pair.");
        var random = new System.Random(31927);
        foreach (int size in new[] { 32, 1024, 4121, 6750 })
        {
            var bytes = new byte[size + 38]; random.NextBytes(bytes);
            uint expected = Reference(bytes, 7, size, table);
            activeProbe.Input(bytes, 7, size, table, productionTable);
            for (int iteration = 0; iteration < warmup; iteration++)
            {
                activeProbe.Call("RunBaseline"); activeProbe.Call("RunProduction"); activeProbe.Call("RunControl");
                yield return null;
            }
            var baselineTimes = new List<double>();
            var productionTimes = new List<double>();
            var controlTimes = new List<double>();
            for (int sample = 0; sample < samples; sample++)
            {
                foreach (string algorithm in sample % 2 == 0 ? new[] { "Baseline", "Production" } : new[] { "Production", "Baseline" })
                {
                    double elapsed = activeProbe.Call("Run" + algorithm);
                    uint actual = activeProbe.Result;
                    Check(actual == expected, "Measured CRC mismatch at " + size + "/" + algorithm);
                    (algorithm == "Baseline" ? baselineTimes : productionTimes).Add(elapsed);
                    Raw.Add(string.Join(",", size, algorithm, sample, sample % 2 == 0 ? "baseline-first" : "production-first", N(elapsed), U(actual)));
                }
                double control = activeProbe.Call("RunControl");
                Check(activeProbe.Result == (uint)size, "Control event failed.");
                controlTimes.Add(control);
                Raw.Add(string.Join(",", size, "Control", sample, "after-pair", N(control), U((uint)size)));
                yield return null;
            }
            AddSummary(size, "Baseline", baselineTimes, expected);
            AddSummary(size, "Production", productionTimes, expected);
            AddSummary(size, "Control", controlTimes, (uint)size);
            File.WriteAllLines(Path.Combine(results, "summary.csv"), Summary);
            File.WriteAllLines(Path.Combine(results, "samples.csv"), Raw);
            Notes.Add("Measured " + size + " bytes: baseline mean=" + N(baselineTimes.Average()) + "ms; production mean=" + N(productionTimes.Average()) + "ms.");
            yield return null;
        }
    }

    static void AddSummary(int bytes, string algorithm, List<double> values, uint result)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        Summary.Add(string.Join(",", bytes, algorithm, ordered.Length, N(values.Average()),
            N(ordered[(int)Math.Ceiling(ordered.Length * .5) - 1]), N(ordered[(int)Math.Ceiling(ordered.Length * .95) - 1]), N(ordered[0]), N(ordered[ordered.Length - 1]), U(result)));
    }
    static int Setting(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;
    static string N(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
    static string U(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
    static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
