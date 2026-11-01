#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharp;
using UdonSharp.Compiler;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
#endif
using Object = UnityEngine.Object;

public static class Float32ContractValidation
{
    private const string Root = "Assets/Validation/Float32Contract/";
    private const string AssetPath = Root + "Float32ContractProbe.asset";
    private const string Session = "TSMP.Float32Contract";
    private const string BinaryPath = "Packages/com.kibalab.tsmp.core/Runtime/Core/Protocol/Binary.cs";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Notes = new List<string>();
    private static readonly List<string> Rows = new List<string>();
    private static int assertions;
    private static string ResultPath => Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
    private static string OutputRoot => Path.GetDirectoryName(Path.GetFullPath(ResultPath));

    public static void RunNative()
    {
        try { Prepare(); Execute(false); Finish(null); }
        catch (Exception error) { Finish(error); }
    }

#if UDONSHARP
    public static void RunVm()
    {
        try
        {
            Prepare();
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Root + "Runtime/Float32ContractProbe.cs");
                Check(asset.sourceCsScript != null, "Missing copied Runtime/Float32ContractProbe.cs");
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            CompileClient(); AssetDatabase.SaveAssets();
            SessionState.SetBool(Session, true); Attach(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }

    private static void CompileClient()
    {
        bool failed = false;
        Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Client compilation failed; preserve compiler log.");
    }

    [InitializeOnLoadMethod]
    private static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Session, false)) return;
        SessionState.SetBool(Session, false);
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.delayCall += () =>
        {
            try { CompileClient(); Execute(true); Finish(null); }
            catch (Exception error) { Finish(error); }
        };
    }
#endif

    private static void Prepare()
    {
        if (string.IsNullOrEmpty(ResultPath)) throw new InvalidOperationException("Set TSMP_VALIDATION_RESULT to a fresh result file.");
        Directory.CreateDirectory(OutputRoot);
        foreach (string path in new[] { ResultPath, Path.Combine(OutputRoot, "vectors.csv"), Path.Combine(OutputRoot, "sources.csv") })
            if (File.Exists(path)) throw new InvalidOperationException("Preserve existing evidence: " + path);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static void Execute(bool vm)
    {
        string before = Hash(File.ReadAllBytes(BinaryPath));
        Notes.Add("Scope=" + (vm ? "Client-target SDK Editor VM" : "Native product helpers") + "; no live VRChat/FPS/allocation claim.");
        Notes.Add("Unity=" + Application.unityVersion + "; WriteNegativeZero=" + (vm ? "NormalizeToPositiveZero" : "PreserveNegativeZero"));
        Notes.Add("ReaderPolicy=Both zeros become positive zero; exponent 255 becomes signed MaxValue; all other wire bits remain exact.");
        var paths = new[] { BinaryPath, Root + "Runtime/Float32ContractProbe.cs", Root + "Editor/Float32ContractValidation.cs", "Packages/manifest.json", "Packages/packages-lock.json" };
        File.WriteAllLines(Path.Combine(OutputRoot, "sources.csv"), new[] { "path,sha256" }.Concat(paths.Where(File.Exists).Select(path => Row(path, Hash(File.ReadAllBytes(path))))));
        Rows.Add("requestedBits,hostFloatBits,heapFloatBits,wireBits,decodedBits,expectedWireBits,expectedReadBits,hostQuietedNaN,heapChangedNaN,writeStatus,writeError");
        using (var endpoint = new Endpoint(vm))
        {
            Scalars(endpoint, vm);
            Wrappers(endpoint, vm);
            Arrays(endpoint, vm);
        }
        Check(Hash(File.ReadAllBytes(BinaryPath)) == before, "Product source changed during contract validation.");
    }

    private static void Scalars(Endpoint endpoint, bool vm)
    {
        int[] input = Corpus();
        float[] values = input.Select(BitConverter.Int32BitsToSingle).ToArray();
        int[] host = values.Select(BitConverter.SingleToInt32Bits).ToArray();
        const int start = 3, stride = 7;
        byte[] readBytes = Filled(start + values.Length * stride + 3);
        for (int i = 0; i < input.Length; i++) PutBits(readBytes, start + i * stride, input[i]);
        byte[] inputCopy = (byte[])readBytes.Clone();
        endpoint.Put("values", values); endpoint.Put("decoded", new float[values.Length]);
        endpoint.Put("readBytes", readBytes);
        endpoint.Put("start", start); endpoint.Put("stride", stride);
        endpoint.Call("ReadScalars");
        float[] decoded = endpoint.Get<float[]>("decoded");
        int hostNaNChanges = 0, heapNaNChanges = 0, writeErrors = 0, writeDifferences = 0;
        Endpoint writer = null;
        try
        {
            for (int i = 0; i < input.Length; i++)
            {
                if (writer == null) writer = new Endpoint(vm);
                writer.Put("scalar", values[i]); writer.Put("start", start); writer.Put("bytes", Filled(10)); writer.Put("returnOffset", -1);
                int heap = BitConverter.SingleToInt32Bits(writer.Get<float>("scalar"));
                bool nan = IsNaN(input[i]);
                Check(nan ? IsNaN(host[i]) && IsNaN(heap) : host[i] == input[i] && heap == input[i], "Host/heap changed finite input " + Hex(input[i]));
                int expectedWire = vm && (heap & 0x7fffffff) == 0 ? 0 : heap;
                int expectedRead = ReadPolicy(input[i]);
                string status = "OK", error = "";
                try { writer.Call("WriteOne"); }
                catch (Exception exception) { status = "ERROR"; error = exception.ToString(); writeErrors++; }
                byte[] written = writer.Get<byte[]>("bytes"), expected = Filled(10);
                int wire = Bits(written, start), read = BitConverter.SingleToInt32Bits(decoded[i]);
                Check(read == expectedRead, "Reader bits differ for " + Hex(input[i]) + ": " + Hex(read) + " != " + Hex(expectedRead));
                if (status == "OK")
                {
                    if (wire != expectedWire) writeDifferences++;
                    PutBits(expected, start, wire);
                    Check(written.SequenceEqual(expected), "Writer changed sentinel bytes.");
                    Check(writer.Get<int>("returnOffset") == start + 4, "Writer return offset differs.");
                    Check(BitConverter.SingleToInt32Bits(writer.Get<float>("scalar")) == heap, "Writer mutated scalar input.");
                }
                if (nan && host[i] != input[i]) hostNaNChanges++;
                if (nan && heap != host[i]) heapNaNChanges++;
                Rows.Add(Row(Hex(input[i]), Hex(host[i]), Hex(heap), Hex(wire), Hex(read), Hex(expectedWire), Hex(expectedRead), nan && host[i] != input[i], nan && heap != host[i], status, error));
                if (status != "OK") { writer.Dispose(); writer = null; } // A failed VM cannot validate the next input.
            }
        }
        finally { if (writer != null) writer.Dispose(); }
        Check(endpoint.Get<byte[]>("readBytes").SequenceEqual(inputCopy), "Reader mutated input bytes.");
        Notes.Add("ScalarVectors=" + input.Length + "; HostNaNBitChanges=" + hostNaNChanges + "; HeapNaNBitChanges=" + heapNaNChanges);
        Notes.Add("ScalarWriteErrors=" + writeErrors + "; ScalarWriteBitDifferences=" + writeDifferences + "; every input is recorded before the final writer gate.");
        Notes.Add("Corpus=Each finite exponent, both signs, five mantissa boundaries; 1024 deterministic xorshift samples; selected zero/infinity/NaN payloads. NaN write expectation uses observed heap bits, not pre-host signalling payloads.");
        Check(writeErrors == 0 && writeDifferences == 0, "Scalar writer contract failed; inspect all preserved vectors.csv rows.");
    }

    private static void Wrappers(Endpoint endpoint, bool vm)
    {
        int[][] groups = {
            new[] { 0, unchecked((int)0x80000000u), 1, unchecked((int)0x807fffffu), 0x00800000, 0x42ffffff, 0x7f7fffff, unchecked((int)0xff7fffffu), 0x3f800001 },
            new[] { 0x7f800000, unchecked((int)0xff800000u), 0x7fc00000, unchecked((int)0xffc00000u), 0x3f7fffff, unchecked((int)0xbf800001u), 0x00000002, unchecked((int)0x80000002u), 0x00800001 }
        };
        int[] positions = { 3, 7, 16, 20, 24, 33, 37, 41, 45 };
        foreach (int[] bits in groups)
        {
            float[] values = bits.Select(BitConverter.Int32BitsToSingle).ToArray();
            endpoint.Put("vector2", new Vector2(values[0], values[1]));
            endpoint.Put("vector3", new Vector3(values[2], values[3], values[4]));
            endpoint.Put("quaternion", new Quaternion(values[5], values[6], values[7], values[8]));
            byte[] readBytes = Filled(55), expected = Filled(55);
            for (int i = 0; i < bits.Length; i++)
            {
                PutBits(readBytes, positions[i], bits[i]);
                int valueBits = BitConverter.SingleToInt32Bits(values[i]);
                PutBits(expected, positions[i], vm && (valueBits & 0x7fffffff) == 0 ? 0 : valueBits);
            }
            byte[] readCopy = (byte[])readBytes.Clone();
            endpoint.Put("bytes", Filled(55)); endpoint.Put("readBytes", readBytes); endpoint.Put("offsets", new int[3]);
            endpoint.Call("WriteVectors"); endpoint.Call("ReadVectors");
            Check(endpoint.Get<byte[]>("bytes").SequenceEqual(expected), "Vector/quaternion writer bits or sentinels differ.");
            Check(endpoint.Get<int[]>("offsets").SequenceEqual(new[] { 11, 28, 49 }), "Vector/quaternion return offsets differ.");
            Vector2 v2 = endpoint.Get<Vector2>("readVector2"); Vector3 v3 = endpoint.Get<Vector3>("readVector3"); Quaternion q = endpoint.Get<Quaternion>("readQuaternion");
            float[] actual = { v2.x, v2.y, v3.x, v3.y, v3.z, q.x, q.y, q.z, q.w };
            for (int i = 0; i < actual.Length; i++) Check(BitConverter.SingleToInt32Bits(actual[i]) == ReadPolicy(bits[i]), "Vector/quaternion reader bits differ.");
            Check(endpoint.Get<byte[]>("readBytes").SequenceEqual(readCopy), "Vector/quaternion reader mutated input bytes.");
        }
        Notes.Add("WrapperCases=2 groups x Vector2/Vector3/Quaternion; distinct unaligned offsets, sentinels, return offsets, special values and input ownership.");
    }

    private static void Arrays(Endpoint endpoint, bool vm)
    {
        int[] bits = { 0, unchecked((int)0x80000000u), 1, unchecked((int)0x807fffffu), 0x42ffffff, unchecked((int)0xc2ffffffu), 0x7f7fffff, unchecked((int)0xff7fffffu), 0x7f800000, unchecked((int)0xff800000u), 0x7fc12345, unchecked((int)0xffc12345u) };
        float[] values = bits.Select(BitConverter.Int32BitsToSingle).ToArray(), reused = new float[bits.Length];
        int[] host = values.Select(BitConverter.SingleToInt32Bits).ToArray();
        byte[] expected = Filled(3 + bits.Length * 4 + 5), readBytes = Filled(expected.Length);
        for (int i = 0; i < bits.Length; i++) { PutBits(expected, 3 + i * 4, vm && (host[i] & 0x7fffffff) == 0 ? 0 : host[i]); PutBits(readBytes, 3 + i * 4, bits[i]); }
        byte[] readCopy = (byte[])readBytes.Clone();
        endpoint.Put("start", 3); endpoint.Put("values", values); endpoint.Put("bytes", Filled(expected.Length)); endpoint.Put("readBytes", readBytes); endpoint.Put("decoded", reused);
        endpoint.Call("WriteArray"); endpoint.Call("ReadArray");
        Check(endpoint.Get<byte[]>("bytes").SequenceEqual(expected), "Float32Array writer bits or sentinels differ.");
        Check(endpoint.Get<int>("returnOffset") == 3 + bits.Length * 4, "Float32Array writer return offset differs.");
        float[] actual = endpoint.Get<float[]>("decoded");
        Check(ReferenceEquals(actual, reused), "Float32Array reader lost its matching-length storage reuse.");
        for (int i = 0; i < bits.Length; i++) Check(BitConverter.SingleToInt32Bits(actual[i]) == ReadPolicy(bits[i]), "Float32Array reader policy differs.");
        Check(endpoint.Get<byte[]>("readBytes").SequenceEqual(readCopy), "Float32Array reader mutated source bytes.");
        Check(endpoint.Get<float[]>("values").Select(BitConverter.SingleToInt32Bits).SequenceEqual(host), "Float32Array writer mutated source values.");
        Notes.Add("Float32Array=12 mixed boundary/negative/NaN values through NetworkValueWriter/NetworkValueReader; unaligned offset/sentinels/return offset/storage reuse/input ownership.");
    }

    private static int[] Corpus()
    {
        var bits = new HashSet<uint> { 0u, 0x80000000u, 0x7f800000u, 0xff800000u, 0x7fc00000u, 0xffc00000u, 0x7f800001u, 0xff800001u, 0x7fffffffu, 0xffffffffu, 0x7fa12345u, 0xffa12345u };
        for (uint exponent = 0; exponent < 255; exponent++)
        foreach (uint mantissa in new[] { 0u, 1u, 0x3fffffu, 0x7ffffeu, 0x7fffffu })
        { uint value = (exponent << 23) | mantissa; bits.Add(value); bits.Add(value | 0x80000000u); }
        uint random = 0x6d2b79f5u;
        for (int i = 0; i < 1024; i++) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; bits.Add(random); }
        return bits.OrderBy(value => value).Select(value => unchecked((int)value)).ToArray();
    }

    private sealed class Endpoint : IDisposable
    {
        private readonly bool isVm;
        private readonly Component target;
#if UDONSHARP
        private readonly IUdonProgram program;
        private readonly IUdonVM machine;
#endif
        public Endpoint(bool vm)
        {
            isVm = vm;
#if UDONSHARP
            if (vm)
            {
                program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetPath).SerializedProgramAsset.RetrieveProgram();
                Check(program != null && program.ByteCode.Length > 0, "Missing compiled client program.");
                machine = UdonEditorManager.Instance.ConstructUdonVM(); Check(machine.LoadProgram(program), "VM load failed.");
                var backing = new GameObject("Float32 contract VM").AddComponent<UdonBehaviour>(); target = backing;
                var type = typeof(UdonBehaviour);
                Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(backing, new object[] { program.SymbolTable, program.Heap }), "Unresolved VM heap.");
                type.GetField("_program", Private).SetValue(backing, program); type.GetField("_udonVM", Private).SetValue(backing, machine);
                type.GetField("_udonManager", Private).SetValue(backing, UdonManager.Instance);
                Notes.Add("ProbeBytecodeBytes=" + program.ByteCode.Length + "; SHA256=" + Hash(program.ByteCode));
                return;
            }
#endif
            target = new GameObject("Float32 contract Native").AddComponent<Float32ContractProbe>();
        }
        public void Put<T>(string name, T value)
        {
#if UDONSHARP
            if (isVm) { program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T)); return; }
#endif
            typeof(Float32ContractProbe).GetField(name).SetValue(target, value);
        }
        public T Get<T>(string name)
        {
#if UDONSHARP
            if (isVm) return (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
#endif
            return (T)typeof(Float32ContractProbe).GetField(name).GetValue(target);
        }
        public void Call(string name)
        {
#if UDONSHARP
            if (isVm)
            {
                var errors = new List<string>();
                Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
                Application.logMessageReceived += listener;
                try { machine.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name)); Check(machine.Interpret() == 0 && errors.Count == 0, "VM failed: " + name + " " + string.Join(" | ", errors)); }
                finally { Application.logMessageReceived -= listener; }
                return;
            }
#endif
            typeof(Float32ContractProbe).GetMethod(name).Invoke(target, null);
        }
        public void Dispose() { Object.DestroyImmediate(target.gameObject); }
    }

    private static int ReadPolicy(int bits) => (bits & 0x7fffffff) == 0 ? 0 : ((bits >> 23) & 255) == 255 ? (bits < 0 ? unchecked((int)0xff7fffffu) : 0x7f7fffff) : bits;
    private static bool IsNaN(int bits) => ((bits >> 23) & 255) == 255 && (bits & 0x7fffff) != 0;
    private static byte[] Filled(int count) => Enumerable.Repeat((byte)0xa5, count).ToArray();
    private static int Bits(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
    private static void PutBits(byte[] bytes, int offset, int bits) { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)((bits >> (8 * i)) & 255); }
    private static string Hex(int bits) => unchecked((uint)bits).ToString("x8", CultureInfo.InvariantCulture);
    private static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static string Row(params object[] values) => string.Join(",", values.Select(value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    private static void Check(bool valid, string message) { assertions++; if (!valid) throw new InvalidOperationException(message); }
    private static void Finish(Exception error)
    {
        if (string.IsNullOrEmpty(ResultPath)) { Debug.LogError(error); EditorApplication.Exit(1); return; }
        if (File.Exists(ResultPath)) { Debug.LogError("Preserving existing result: " + ResultPath + " " + error); EditorApplication.Exit(1); return; }
        Directory.CreateDirectory(OutputRoot);
        if (Rows.Count > 0) File.WriteAllLines(Path.Combine(OutputRoot, "vectors.csv"), Rows);
        Notes.Add("Assertions=" + assertions);
        if (error != null) Notes.Add(error.ToString());
        File.WriteAllLines(ResultPath, new[] { error == null ? "PASS" : "FAIL" }.Concat(Notes));
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
