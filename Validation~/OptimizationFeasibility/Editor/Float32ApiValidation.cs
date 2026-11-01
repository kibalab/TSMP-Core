#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// API inventory and isolated client-VM feasibility; product sources remain unchanged.
public static class Float32ApiValidation
{
    private const string ProbeRoot = "Assets/Validation/Float32Api";
    private const string ProbeAsset = ProbeRoot + "/Float32ApiProbe.asset";
    private const string ProbeSession = "TSMP.Float32ApiProbe";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Notes = new List<string>();
    private static readonly List<string> Vectors = new List<string>();
    private static int assertions, currentWriteErrors, currentWriteDifferences, currentReadErrors, currentReadDifferences;
    private static int hostNaNChanges, heapNaNChanges, rawNaNWriteChanges, rawNaNReadChanges, bitcastNaNWriteChanges;
    private static string ResultPath => Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
    private static string OutputRoot => Path.GetDirectoryName(Path.GetFullPath(ResultPath));

    [Serializable]
    private sealed class Inventory
    {
        public string scope;
        public string unity;
        public int totalNodeCount;
        public string[] bitConverterNodes;
        public string[] singleToInt32Bits;
        public string[] int32BitsToSingle;
        public string[] doubleToInt64Bits;
        public string[] int64BitsToDouble;
        public string[] singleGetBytes;
        public string[] toSingle;
    }

    public static void RunInventory()
    {
        string resultPath = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        if (string.IsNullOrEmpty(resultPath))
            throw new InvalidOperationException("TSMP_VALIDATION_RESULT must name a fresh output file.");
        if (File.Exists(resultPath) || File.Exists(Path.ChangeExtension(resultPath, ".json")))
            throw new InvalidOperationException("Preserve the existing inventory; use a fresh output file.");
        try
        {
            string[] nodes = UdonEditorManager.Instance.GetNodeDefinitions()
                .Select(node => node.fullName).Where(name => !string.IsNullOrEmpty(name))
                .Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();
            string[] converter = nodes.Where(name => name.Contains("SystemBitConverter")).ToArray();
            var inventory = new Inventory
            {
                scope = "Installed SDK node exposure only. Presence does not prove UdonSharp compilation, VM execution, performance or allocation behavior.",
                unity = Application.unityVersion,
                totalNodeCount = nodes.Length,
                bitConverterNodes = converter,
                singleToInt32Bits = converter.Where(name => name.Contains("SingleToInt32Bits")).ToArray(),
                int32BitsToSingle = converter.Where(name => name.Contains("Int32BitsToSingle")).ToArray(),
                doubleToInt64Bits = converter.Where(name => name.Contains("DoubleToInt64Bits")).ToArray(),
                int64BitsToDouble = converter.Where(name => name.Contains("Int64BitsToDouble")).ToArray(),
                singleGetBytes = converter.Where(name => name.Contains("GetBytes") && name.Contains("__SystemSingle__")).ToArray(),
                toSingle = converter.Where(name => name.Contains("ToSingle")).ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath)));
            File.WriteAllText(Path.ChangeExtension(resultPath, ".json"), JsonUtility.ToJson(inventory, true) + "\n");
            File.WriteAllLines(resultPath, new[]
            {
                "PASS", "Scope=" + inventory.scope, "Unity=" + inventory.unity,
                "TotalNodeCount=" + inventory.totalNodeCount,
                "BitConverterNodeCount=" + converter.Length,
                "SingleToInt32Bits=" + inventory.singleToInt32Bits.Length,
                "Int32BitsToSingle=" + inventory.int32BitsToSingle.Length,
                "DoubleToInt64Bits=" + inventory.doubleToInt64Bits.Length,
                "Int64BitsToDouble=" + inventory.int64BitsToDouble.Length,
                "SingleGetBytes=" + inventory.singleGetBytes.Length,
                "ToSingle=" + inventory.toSingle.Length
            });
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath)));
            File.WriteAllText(resultPath, "FAIL\n" + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    public static void RunProbe()
    {
        if (string.IsNullOrEmpty(ResultPath) || File.Exists(ResultPath))
        {
            Debug.LogError("Use TSMP_VALIDATION_RESULT with a fresh output file; existing evidence is preserved.");
            EditorApplication.Exit(1); return;
        }
        try
        {
            Directory.CreateDirectory(OutputRoot);
            foreach (string file in new[] { "vectors.csv", "timings.csv", "summary.csv", "sources.csv" })
                if (File.Exists(Path.Combine(OutputRoot, file))) throw new InvalidOperationException("Preserve existing " + file);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbeAsset);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProbeRoot + "/Runtime/Float32ApiProbe.cs");
                Require(asset.sourceCsScript != null, "Missing copied Runtime/Float32ApiProbe.cs");
                AssetDatabase.CreateAsset(asset, ProbeAsset);
            }
            CompileClient(); AssetDatabase.SaveAssets();
            SessionState.SetBool(ProbeSession, true); AttachProbe(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { FinishProbe(error); }
    }

    private static void CompileClient()
    {
        bool failed = false;
        Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Require(!failed, "Client compilation failed; preserve the compiler log.");
    }

    [InitializeOnLoadMethod]
    private static void AttachProbe()
    {
        EditorApplication.playModeStateChanged -= ProbeEntered;
        EditorApplication.playModeStateChanged += ProbeEntered;
    }

    private static void ProbeEntered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(ProbeSession, false)) return;
        SessionState.SetBool(ProbeSession, false);
        EditorApplication.playModeStateChanged -= ProbeEntered;
        EditorApplication.delayCall += ExecuteProbe;
    }

    private static void ExecuteProbe()
    {
        try
        {
            CompileClient();
            string binaryPath = "Packages/com.kibalab.tsmp.core/Runtime/Core/Protocol/Binary.cs";
            string binaryBefore = Hash(File.ReadAllBytes(binaryPath));
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbeAsset);
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            Notes.Add("Scope=Actual client-target SDK Editor VM API/helper feasibility; no product substitution, full Encoder/FPS, live VRChat or measured allocation claim.");
            Notes.Add("Unity=" + Application.unityVersion + "; PlayMode=" + Application.isPlaying + "; CPU=" + SystemInfo.processorType);
            Notes.Add("ProbeBytecodeBytes=" + program.ByteCode.Length + "; SHA256=" + Hash(program.ByteCode));
            Notes.Add("StopwatchFrequency=" + Stopwatch.Frequency);
            Vectors.Add("inputBits,inputClass,inputValue,hostFloatBits,heapFloatBits,heapFloatBitsAfterRaw,rawApiWriteBits,rawApiReadBits,rawWriteExactInput,rawReadExactInput,rawWriteExactHeap,currentWriteStatus,currentWriterHeapBits,currentWriteBits,bitcastWriteBits,bitcastWriteExactInputPolicy,writeMatchesCurrent,currentReadStatus,currentReadBits,bitcastReadBits,readMatchesCurrent,currentWriteError,currentReadError");
            foreach (int bits in VectorBits()) Characterize(bits);
            File.WriteAllLines(Path.Combine(OutputRoot, "vectors.csv"), Vectors);
            Benchmark();
            Require(Hash(File.ReadAllBytes(binaryPath)) == binaryBefore, "Product Binary.cs changed during probe.");
            var sourcePaths = new[] { binaryPath, ProbeRoot + "/Runtime/Float32ApiProbe.cs", ProbeRoot + "/Editor/Float32ApiValidation.cs" };
            File.WriteAllLines(Path.Combine(OutputRoot, "sources.csv"), new[] { "path,sha256" }.Concat(sourcePaths.Select(path => Row(path, Hash(File.ReadAllBytes(path))))));
            Notes.Add("CurrentWriteVmErrors=" + currentWriteErrors + "; CurrentWriteDifferences=" + currentWriteDifferences);
            Notes.Add("CurrentReadVmErrors=" + currentReadErrors + "; CurrentReadDifferences=" + currentReadDifferences);
            Notes.Add("NaNBitChanges=Host:" + hostNaNChanges + "; HeapVsHost:" + heapNaNChanges + "; RawWriteVsInput:" + rawNaNWriteChanges + "; RawReadVsInput:" + rawNaNReadChanges + "; BitcastWriteVsInput:" + bitcastNaNWriteChanges);
            Notes.Add("Raw API exact bits are required for finite values, both zeros and infinities. NaN classification is required, while NaN sign/payload changes at observed host/heap/API boundaries are recorded rather than hidden or treated as an unavailable API.");
            Notes.Add("Characterization differences/errors are preserved current-product observations, not bitcast API failures or approval to change wire semantics.");
            FinishProbe(null);
        }
        catch (Exception error) { FinishProbe(error); }
    }

    private sealed class ProbeVm : IDisposable
    {
        private readonly IUdonProgram program;
        private readonly IUdonVM vm;
        private readonly UdonBehaviour backing;
        public ProbeVm()
        {
            program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbeAsset).SerializedProgramAsset.RetrieveProgram();
            Require(program != null && program.ByteCode.Length > 0, "Missing serialized client probe.");
            vm = UdonEditorManager.Instance.ConstructUdonVM(); Require(vm.LoadProgram(program), "VM load failed.");
            backing = new GameObject("Float32 isolated client VM").AddComponent<UdonBehaviour>();
            var type = typeof(UdonBehaviour);
            Require((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(backing, new object[] { program.SymbolTable, program.Heap }), "Unresolved heap.");
            type.GetField("_program", Private).SetValue(backing, program); type.GetField("_udonVM", Private).SetValue(backing, vm);
            type.GetField("_udonManager", Private).SetValue(backing, UdonManager.Instance);
        }
        public void Put<T>(string name, T value) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
        public uint Address(string name) => program.EntryPoints.GetAddressFromSymbol(name);
        public uint Call(uint address, out long elapsed)
        {
            vm.SetProgramCounter(address); long start = Stopwatch.GetTimestamp(); uint result = vm.Interpret();
            elapsed = Stopwatch.GetTimestamp() - start; return result;
        }
        public void Call(string name) { long ignored; Require(Call(Address(name), out ignored) == 0, "VM failed: " + name); }
        public string Attempt(string name, out string error)
        {
            var errors = new List<string>();
            Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += listener;
            try { long ignored; uint status = Call(Address(name), out ignored); error = string.Join(" | ", errors); return status == 0 && errors.Count == 0 ? "OK" : "VM=" + status; }
            catch (Exception exception) { error = exception.ToString() + " | " + string.Join(" | ", errors); return "Exception"; }
            finally { Application.logMessageReceived -= listener; }
        }
        public void Dispose() { Object.DestroyImmediate(backing.gameObject); }
    }

    private static IEnumerable<int> VectorBits()
    {
        var values = new HashSet<uint> { 0u, 0x80000000u, 1u, 2u, 0x007fffffu, 0x00800000u, 0x00800001u,
            0x80000001u, 0x807fffffu, 0x80800000u, 0x3f000000u, 0xbf000000u, 0x3f800000u, 0xbf800000u,
            0x40490fdbu, 0xc0490fdbu, 0x7f7ffffeu, 0x7f7fffffu, 0xff7fffffu, 0x7f800000u, 0xff800000u,
            0x7fc00000u, 0xffc00000u, 0x7f800001u, 0xff800001u, 0x7fffffffu, 0xffffffffu };
        foreach (int exponent in new[] { -126, -125, -64, -32, -16, -1, 0, 1, 7, 15, 23, 24, 30, 63, 126, 127 })
        {
            uint center = (uint)(exponent + 127) << 23;
            foreach (uint bits in new[] { center - 1, center, center + 1 }) { values.Add(bits); values.Add(bits | 0x80000000u); }
        }
        return values.OrderBy(bits => bits).Select(bits => unchecked((int)bits));
    }

    private static void Characterize(int bits)
    {
        float value = BitConverter.Int32BitsToSingle(bits);
        int hostBits = BitConverter.SingleToInt32Bits(value);
        bool nan = IsNaNBits(bits);
        byte[] source = new byte[4];
        for (int i = 0; i < source.Length; i++) source[i] = (byte)((bits >> (i * 8)) & 255);
        string writeStatus, readStatus, writeError, readError;
        int currentWrite, currentRead, currentWriterHeapBits, fastWrite, fastRead, rawWrite, rawRead, heapBits, heapAfterRaw;
        using (var probe = new ProbeVm())
        {
            probe.Put("scalarValue", value); probe.Put("inputBits", bits);
            heapBits = BitConverter.SingleToInt32Bits(probe.Get<float>("scalarValue"));
            probe.Call("RawApi");
            rawWrite = probe.Get<int>("rawBitsResult"); rawRead = BitConverter.SingleToInt32Bits(probe.Get<float>("rawFloatResult"));
            heapAfterRaw = BitConverter.SingleToInt32Bits(probe.Get<float>("scalarValue"));
            Require(nan ? IsNaNBits(hostBits) && IsNaNBits(heapBits) && IsNaNBits(heapAfterRaw) && IsNaNBits(rawWrite) && IsNaNBits(rawRead)
                : hostBits == bits && heapBits == bits && heapAfterRaw == bits && rawWrite == bits && rawRead == bits,
                "Raw API lost value/classification for " + Hex(bits));
            probe.Call("BitcastScalarWrite"); fastWrite = Binary.ReadInt32LE(probe.Get<byte[]>("scalarBytes"), 0);
            int expectedWrite = value == 0f ? 0 : bits;
            Require(nan ? IsNaNBits(fastWrite) : fastWrite == expectedWrite, "Policy bitcast writer lost value/classification for " + Hex(bits));
            probe.Put("scalarBytes", (byte[])source.Clone()); probe.Call("BitcastScalarRead");
            fastRead = BitConverter.SingleToInt32Bits(probe.Get<float>("scalarResult"));
            float expectedRead = ((bits >> 23) & 255) == 255 ? (bits < 0 ? -float.MaxValue : float.MaxValue) : value == 0f ? 0f : value;
            Require(fastRead == BitConverter.SingleToInt32Bits(expectedRead), "Policy bitcast reader differs for " + Hex(bits));
        }
        using (var probe = new ProbeVm())
        {
            probe.Put("scalarValue", value); probe.Put("scalarBytes", new byte[] { 165, 165, 165, 165 });
            currentWriterHeapBits = BitConverter.SingleToInt32Bits(probe.Get<float>("scalarValue"));
            writeStatus = probe.Attempt("CurrentScalarWrite", out writeError); currentWrite = Binary.ReadInt32LE(probe.Get<byte[]>("scalarBytes"), 0);
        }
        using (var probe = new ProbeVm())
        {
            probe.Put("scalarBytes", (byte[])source.Clone()); readStatus = probe.Attempt("CurrentScalarRead", out readError);
            currentRead = BitConverter.SingleToInt32Bits(probe.Get<float>("scalarResult"));
        }
        if (writeStatus != "OK") currentWriteErrors++; else if (currentWrite != fastWrite) currentWriteDifferences++;
        if (readStatus != "OK") currentReadErrors++; else if (currentRead != fastRead) currentReadDifferences++;
        if (nan)
        {
            if (hostBits != bits) hostNaNChanges++;
            if (heapBits != hostBits) heapNaNChanges++;
            if (rawWrite != bits) rawNaNWriteChanges++;
            if (rawRead != bits) rawNaNReadChanges++;
            if (fastWrite != bits) bitcastNaNWriteChanges++;
        }
        string inputClass = nan ? "NaN" : ((bits >> 23) & 255) == 255 ? "Infinity" : (bits & 0x7fffffff) == 0 ? "Zero" : ((bits >> 23) & 255) == 0 ? "Subnormal" : "Finite";
        Vectors.Add(Row(Hex(bits), inputClass, value.ToString("R", CultureInfo.InvariantCulture), Hex(hostBits), Hex(heapBits), Hex(heapAfterRaw),
            Hex(rawWrite), Hex(rawRead), rawWrite == bits, rawRead == bits, rawWrite == heapBits, writeStatus, Hex(currentWriterHeapBits),
            Hex(currentWrite), Hex(fastWrite), fastWrite == (value == 0f ? 0 : bits), writeStatus == "OK" && currentWrite == fastWrite, readStatus, Hex(currentRead), Hex(fastRead),
            readStatus == "OK" && currentRead == fastRead, writeError, readError));
    }

    private static bool IsNaNBits(int bits) => ((bits >> 23) & 255) == 255 && (bits & 0x7fffff) != 0;

    private struct TimingSample { public int round, order, variant; public long ticks; }
    private static void Benchmark()
    {
        string[] names = { "CurrentBatchWrite", "BitcastBatchWrite", "CurrentBatchRead", "BitcastBatchRead" };
        var values = Enumerable.Range(0, 128).Select(i => (i % 2 == 0 ? 1f : -1f) * (1.25f + i * 0.5f)).ToArray();
        var expected = new byte[512]; for (int i = 0; i < values.Length; i++) Array.Copy(BitConverter.GetBytes(values[i]), 0, expected, i * 4, 4);
        var samples = new TimingSample[320]; int at = 0;
        using (var probe = new ProbeVm())
        {
            probe.Put("values", values); probe.Put("bytes", (byte[])expected.Clone()); probe.Put("decoded", new float[128]);
            uint[] addresses = names.Select(probe.Address).ToArray();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            for (int round = -10; round < 80; round++)
            for (int order = 0; order < names.Length; order++)
            {
                int variant = (round & 1) == 0 ? order : names.Length - 1 - order;
                long ticks; Require(probe.Call(addresses[variant], out ticks) == 0, "Normal finite benchmark VM failure: " + names[variant]);
                if (variant < 2) Require(probe.Get<byte[]>("bytes").SequenceEqual(expected), "Normal finite writer benchmark differs: " + names[variant]);
                else
                {
                    var decoded = probe.Get<float[]>("decoded");
                    for (int i = 0; i < values.Length; i++) Require(BitConverter.SingleToInt32Bits(decoded[i]) == BitConverter.SingleToInt32Bits(values[i]), "Normal finite reader benchmark differs.");
                }
                if (round >= 0) samples[at++] = new TimingSample { round = round, order = order, variant = variant, ticks = ticks };
            }
        }
        File.WriteAllLines(Path.Combine(OutputRoot, "timings.csv"), new[] { "round,order,event,batchSize,elapsedTicks,elapsedUs" }.Concat(samples.Select(sample => Row(sample.round, sample.order, names[sample.variant], 128, sample.ticks, (sample.ticks * 1000000.0 / Stopwatch.Frequency).ToString("R", CultureInfo.InvariantCulture)))));
        var summaries = new List<string> { "event,batchSize,samples,meanUs,p95Us,p99Us,maxUs" };
        for (int i = 0; i < names.Length; i++)
        {
            var times = samples.Where(sample => sample.variant == i).Select(sample => sample.ticks * 1000000.0 / Stopwatch.Frequency).OrderBy(value => value).ToArray();
            summaries.Add(Row(names[i], 128, times.Length, times.Average(), times[(int)Math.Ceiling(times.Length * .95) - 1], times[(int)Math.Ceiling(times.Length * .99) - 1], times.Last()));
        }
        File.WriteAllLines(Path.Combine(OutputRoot, "summary.csv"), summaries);
        Notes.Add("Microbenchmark=128 ordinary finite values/event; 10 warm + 80 measured rounds; forward/reverse event order alternates. Symbol lookup/SetPC, input setup and output validation are outside timed Interpret. No control subtraction or allocations measured.");
    }

    private static void Require(bool valid, string message) { assertions++; if (!valid) throw new InvalidOperationException(message); }
    private static string Hex(int value) => unchecked((uint)value).ToString("x8", CultureInfo.InvariantCulture);
    private static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static string Row(params object[] values) => string.Join(",", values.Select(value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    private static void FinishProbe(Exception error)
    {
        if (Vectors.Count > 0) File.WriteAllLines(Path.Combine(OutputRoot, "vectors.csv"), Vectors);
        Notes.Add("Vectors=" + Math.Max(0, Vectors.Count - 1) + "; Assertions=" + assertions);
        if (error != null) Notes.Add(error.ToString());
        File.WriteAllLines(ResultPath, new[] { error == null ? "PASS" : "FAIL" }.Concat(Notes));
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
