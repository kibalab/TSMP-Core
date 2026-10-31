using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;

public static class SparseRecoveryVmValidation
{
    static int assertions;
    static void Require(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    static void Set(IUdonProgram program, string name, object value, Type type) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, type);
    static T Get<T>(IUdonProgram program, string name) => (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
    static uint Crc(byte[] data, uint[] table) => Crc32Runtime.Compute(data, 0, data.Length, table);
    public static void Run() { RunValidation(false); }
    public static void RunBudgetSuccessOnly() { RunValidation(true); }
    public static void InspectDecoderHeap()
    {
        string root = Environment.GetEnvironmentVariable("TSMP_SPARSE_VM_RESULTS");
        Directory.CreateDirectory(root);
        var lines = new List<string>();
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset");
            Require(asset != null, "Decoder program asset missing");
            asset.UpdateProgram();
            IUdonProgram program = asset.GetRealProgram();
            Require(program != null && program.ByteCode.Length > 0, "Compiled Decoder missing");
            Require(Get<bool>(program, "recoverSparsePayload"), "Sparse default must be true");
            Require(Get<int>(program, "maxCrcRecoveryAttempts") == 2, "Retry default must be two");
            Require(!Get<bool>(program, "recoverHeaderCrc"), "Header recovery default must be false");
            foreach (string field in new[] { "_slotSparseCounts", "_slotRecoverSparsePayload", "_sparsePositions", "_sparseModeOne", "_sparseCrcs" })
            {
                Array value = Get<Array>(program, field);
                int expected = field == "_sparseCrcs" ? 6 : field == "_sparsePositions" || field == "_sparseModeOne" ? 16 : 2;
                Require(value != null && value.Length == expected, "Wrong heap array length: " + field);
                lines.Add(field + ".Length=" + value.Length);
            }
            Require(Get<byte[]>(program, "_sparseNormalZero") == null && Get<byte[]>(program, "_sparseNormalOne") == null, "Baseline buffers allocated before failure");
            lines.Insert(0, "PASS"); lines.Add("recoverSparsePayload=true; maxCrcRecoveryAttempts=2; recoverHeaderCrc=false");
            lines.Add("BytecodeBytes=" + program.ByteCode.Length); lines.Add("Assertions=" + assertions);
            File.WriteAllLines(Path.Combine(root, "result.txt"), lines);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "result.txt"), "FAIL\n" + error); throw;
        }
    }
    static void RunValidation(bool budgetSuccessOnly)
    {
        string root = Environment.GetEnvironmentVariable("TSMP_SPARSE_VM_RESULTS");
        Directory.CreateDirectory(root);
        var lines = new List<string>();
        try
        {
            const string assetPath = "Assets/Validation/SparseRecovery/SparseRecoveryVmProbe.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/SparseRecovery/Udon/SparseRecoveryVmProbe.cs");
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            bool errors = false;
            Application.LogCallback callback = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors = true; };
            Application.logMessageReceived += callback;
            try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
            finally { Application.logMessageReceived -= callback; }
            Require(!errors, "Udon client compilation errors");
            IUdonProgram program = asset.GetRealProgram();
            Require(program != null && program.ByteCode.Length > 0, "No Udon bytecode");
            IUdonVM vm = UdonEditorManager.Instance.ConstructUdonVM(); vm.LoadProgram(program);
            uint[] table = Crc32Runtime.EnsureFastTable(null);
            var random = new System.Random(202610310);
            var times = new List<string> { "case,path,length,sample,warm,hypotheses,elapsedMs,success" };
            for (int trial = 0; trial < (budgetSuccessOnly ? 70 : 175); trial++)
            {
                int profile = trial / 35, sample = trial % 35;
                int length = budgetSuccessOnly ? (profile == 0 ? 5375 : 8192) : profile == 0 ? 32 : profile == 1 ? 2495 : profile == 2 ? 5375 : 8192;
                byte[] expected = new byte[length]; random.NextBytes(expected);
                byte[] normal = (byte[])expected.Clone(), one = (byte[])expected.Clone(), current = (byte[])expected.Clone();
                bool worst = budgetSuccessOnly || profile == 4;
                int varying = worst ? 8 : 2;
                for (int p = 0; p < varying; p++)
                {
                    int offset = worst ? p : p * 7;
                    normal[offset] ^= (byte)(budgetSuccessOnly ? (p == 0 ? 1 : 0) : p == 0 && !worst ? 0 : 1);
                    one[offset] ^= (byte)(budgetSuccessOnly ? (p == 0 ? 0 : 2) : p == 1 && !worst ? 0 : 2);
                    current[offset] ^= 3;
                }
                int[] positions = new int[16]; byte[] modeOne = new byte[16];
                int differing = SparsePayloadRecovery.Collect(normal, one, length, positions, modeOne, 8, 0, true);
                differing = SparsePayloadRecovery.Collect(normal, current, length, positions, modeOne, 8, differing, false);
                uint normalCrc = Crc(normal, table), oneCrc = Crc(one, table), currentCrc = Crc(current, table), expectedCrc = worst && !budgetSuccessOnly ? 0u : Crc(expected, table);
                byte[] reference = (byte[])current.Clone();
                bool native = SparsePayloadRecovery.TryRecover(normal, reference, length, positions, modeOne, 8, differing,
                    normalCrc, oneCrc, currentCrc, expectedCrc, table, new byte[24], new uint[24], new byte[8], out int nativeHypotheses);
                foreach (string path in budgetSuccessOnly ? new[] { "CaptureRecoverAndVerify" } : new[] { "Recover", "CaptureAndRecover", "CaptureRecoverAndVerify" })
                {
                Set(program, "normal", normal, typeof(byte[])); Set(program, "current", (byte[])current.Clone(), typeof(byte[]));
                Set(program, "normalCopy", new byte[length], typeof(byte[])); Set(program, "observedOne", one, typeof(byte[]));
                Set(program, "modeOne", modeOne, typeof(byte[])); Set(program, "positions", positions, typeof(int[]));
                Set(program, "positionOffset", 8, typeof(int)); Set(program, "differing", differing, typeof(int)); Set(program, "count", length, typeof(int));
                Set(program, "normalCrc", normalCrc, typeof(uint)); Set(program, "modeOneCrc", oneCrc, typeof(uint));
                Set(program, "currentCrc", currentCrc, typeof(uint)); Set(program, "expectedCrc", expectedCrc, typeof(uint));
                Set(program, "table", table, typeof(uint[])); Set(program, "values", new byte[24], typeof(byte[]));
                Set(program, "effects", new uint[24], typeof(uint[])); Set(program, "winner", new byte[8], typeof(byte[]));
                vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(path));
                var watch = Stopwatch.StartNew(); uint status = vm.Interpret(); watch.Stop();
                Require(status == 0, "Udon VM status=" + status);
                Require(Get<bool>(program, "recovered") == native, "Udon recovery decision differs");
                Require(Get<int>(program, "hypotheses") == nativeHypotheses && nativeHypotheses <= 384, "Udon candidate budget differs");
                Require(Get<byte[]>(program, "current").SequenceEqual(reference), "Udon candidate bytes differ");
                if (budgetSuccessOnly) Require(native && nativeHypotheses == 384, "Saturated successful fixture missing");
                times.Add((budgetSuccessOnly ? "budget-success" : worst ? "worst" : "sparse-success") + "," + path + "," + length + "," + sample + "," + (sample < 5) + "," + nativeHypotheses + "," + watch.Elapsed.TotalMilliseconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "," + native);
                }
            }
            File.WriteAllLines(Path.Combine(root, "timings.csv"), times);
            lines.Add("PASS"); lines.Add("Assertions=" + assertions); lines.Add("BytecodeBytes=" + program.ByteCode.Length);
            lines.Add("Automatic Udon client bytecode in editor VM. Recover excludes caller scans/copy; CaptureAndRecover includes both scans and bulk copy; CaptureRecoverAndVerify also includes the final full CRC on a recovered payload. All exclude allocation/GPU/readback; not actual VRChat FPS. Five warm and thirty measured samples per case/path.");
            File.WriteAllLines(Path.Combine(root, "result.txt"), lines); AssetDatabase.SaveAssets();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "result.txt"), "FAIL\nAssertions=" + assertions + "\n" + error); throw;
        }
    }
}
