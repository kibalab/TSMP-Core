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
using K13A.TSMP;
using K13A.TSMP.Udon;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

// Editor validation only. Product component bytecode performs capture and receive;
// the host changes the source Transforms, checks results and archives raw bytes.
public static class PackedPoseValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Session = "TSMP.PackedPose";
    static string Root => Environment.GetEnvironmentVariable("TSMP_PACKED_POSE_RESULTS");
    static readonly List<Object> owned = new List<Object>();
    static readonly List<Program> programs = new List<Program>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> artifacts = new List<string> { "path,bytes,sha256" };
    static readonly List<string> bindings = new List<string> { "case,index,kind,networkId,variableHash,fieldName,bodyBytes,boneIds,boneParentIndices" };
    static readonly HashSet<string> artifactPaths = new HashSet<string>();
    static Dictionary<string, string> before;
    static IEnumerator work;
    static CaseBuffer active;
    static int assertions, lastFrame = -1, activeIteration = -1;
    static string activeCase;
    [Serializable] public sealed class Plan { public Case[] cases; }
    [Serializable] public sealed class Case { public string name; public int transforms, humanoids, warm, samples; public bool unchanged; }
    struct Sample
    {
        public int iteration, inputId, frame, payloadBytes, expectedFields, appliedFields, sourceArrayReplacements, receiverArrayReplacements;
        public double clock;
        public long sourceStart, sourceStop, encodeStart, encodeStop, decodeStart, decodeStop;
        public uint encodedFrame;
        public bool gpu, completed;
    }
    sealed class CaseBuffer
    {
        public Case item; public Sample[] rows; public byte[][] packets, headers; public int captured, flushed;
    }
    static void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
    static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
    static string Row(params object[] values) => string.Join(",", values.Select(v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    static void Compile()
    {
        bool failed = false;
        Application.LogCallback listener = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) failed = true; };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Client compilation failed");
    }
    public static void Run()
    {
        try
        {
            Check(!string.IsNullOrWhiteSpace(Root), "Set TSMP_PACKED_POSE_RESULTS"); Directory.CreateDirectory(Root);
            Check(!File.Exists(Path.Combine(Root, "result.txt")) && !File.Exists(Path.Combine(Root, "samples.csv")), "Preserve existing results");
            File.WriteAllText(Path.Combine(Root, "result.txt"), "RUNNING");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Compile(); AssetDatabase.SaveAssets(); SessionState.SetBool(Session, true); Attach(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }
    [InitializeOnLoadMethod] static void Attach() { EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered; }
    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Session, false)) return;
        SessionState.SetBool(Session, false); work = Validate(); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
        try { if (work.MoveNext()) return; } catch (Exception error) { Finish(error); return; } Finish(null);
    }
    static Dictionary<string, string> Sources()
    {
        var result = new Dictionary<string, string>();
        foreach (string package in new[] { "com.kibalab.tsmp.core", "com.kibalab.tsmp.codec.luma4" })
        {
            string path = "Packages/" + package; var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            string directory = info == null ? path : info.resolvedPath;
            foreach (string scope in new[] { "Runtime", "Editor", "Shaders" })
            {
                string folder = Path.Combine(directory, scope); if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    if (new[] { ".cs", ".shader", ".cginc", ".meta", ".asmdef", ".mat", ".prefab" }.Contains(Path.GetExtension(file))) result[Path.GetFullPath(file)] = Hash(File.ReadAllBytes(file));
            }
        }
        return result;
    }
    static IEnumerator Validate()
    {
        Compile(); Application.runInBackground = true; Application.targetFrameRate = 90; QualitySettings.vSyncCount = 0;
        notes.Add("Unity=" + Application.unityVersion + "; CPU=" + SystemInfo.processorType + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; StopwatchFrequency=" + Stopwatch.Frequency);
        notes.Add("Scope=Actual client Encoder, shipped Transform/Humanoid BeforeEncode and receive bytecode, generated real 17-bone Animator rigs, actual GPU Luma4 publication and protected completed-byte Decoder injection. Not VRChat player API capture, receiver GPU timing, live client/FPS, production pacing or an allocation-free claim.");
        notes.Add("Capture=EncodeNow invokes TSMPBeforeEncode once through the product dispatch. Host source preparation changes only Transform/bone values. No extra capture call or timing subtraction is used.");
        notes.Add("Configuration=Default RotOnly Transform packing without Rigidbody; default Humanoid settings with generated 17-bone rigs that contain no finger Transforms. Default discrete receive. Refresh=0 isolates unchanged capture/comparison and suppresses periodic output; not the production default one-second refresh workload.");
        notes.Add("Timings=One actual EncodeNow opportunity per observed Unity frame; source setup and result/raw checks outside VM timers share the process. Decoder time includes component application, excludes receiver GPU/capture/transport. Files/CSV formatted after each bounded case.");
        before = Sources(); File.WriteAllLines(Path.Combine(Root, "source-before.csv"), new[] { "path,sha256" }.Concat(before.Select(x => Row(x.Key, x.Value))));
        byte[] planBytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("TSMP_PACKED_POSE_PLAN")); File.WriteAllBytes(Path.Combine(Root, "plan-used.json"), planBytes);
        var plan = JsonUtility.FromJson<Plan>(System.Text.Encoding.UTF8.GetString(planBytes).TrimStart('\uFEFF'));
        Check(plan != null && plan.cases != null && plan.cases.Length > 0 && plan.cases.Length <= 8, "Use a bounded explicit plan");
        var names = new HashSet<string>();
        foreach (Case item in plan.cases)
        {
            Check(!string.IsNullOrEmpty(item.name) && item.name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && names.Add(item.name), "Invalid or duplicate case name");
            Check(item.transforms >= 0 && item.transforms <= 8 && item.humanoids >= 0 && item.humanoids <= 4 && item.transforms + item.humanoids > 0, "Pose count outside bounded workload");
            Check(item.warm >= 1 && item.samples >= 1 && item.warm + item.samples <= 20000, "Invalid sample count");
        }
        File.WriteAllText(Path.Combine(Root, "samples.csv"), "case,sample,measured,inputId,frame,clock,transforms,humanoids,unchanged,sourceStartTicks,sourceEndTicks,sourceMs,encodeStartTicks,encodeEndTicks,encodeMs,decodeStartTicks,decodeEndTicks,decodeMs,encodedFrame,payloadBytes,expectedFields,appliedFields,sourceArrayReplacements,receiverArrayReplacements,usedGpu,packet,header,completed\n");
        foreach (Case item in plan.cases)
        {
            IEnumerator measure = Measure(item); while (measure.MoveNext()) yield return measure.Current;
            measure = null; GC.Collect(); yield return null;
        }
        RebindControl();
    }
    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code; public readonly UdonBehaviour Backing;
        static readonly FieldInfo HasError = typeof(UdonBehaviour).GetField("_hasError", Private);
        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path); Check(asset != null, "Missing program " + path);
            Code = asset.SerializedProgramAsset.RetrieveProgram(); Check(Code != null && Code.ByteCode.Length > 0, "Missing client bytecode " + path);
            if (artifactPaths.Add(path)) artifacts.Add(Row(path, Code.ByteCode.Length, Hash(Code.ByteCode)));
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(Code), "VM load failed");
            Backing = new GameObject("Packed pose " + asset.name).AddComponent<UdonBehaviour>(); Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved VM references");
            type.GetField("_program", Private).SetValue(Backing, Code); type.GetField("_udonVM", Private).SetValue(Backing, vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance); type.GetField("_isReady", Private).SetValue(Backing, true); type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null) foreach (FieldInfo field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address)) Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
            programs.Add(this);
        }
        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Timed(string name, out long start, out long stop)
        {
            start = Stopwatch.GetTimestamp(); try { Backing.SendCustomEvent(name); } finally { stop = Stopwatch.GetTimestamp(); }
            assertions++; if ((bool)HasError.GetValue(Backing)) throw new InvalidOperationException("VM event failed: " + name);
        }
        public void Call(string name) { Timed(name, out long ignoredStart, out long ignoredStop); }
        public void Dispose() { programs.Remove(this); if (Backing != null) Object.DestroyImmediate(Backing.gameObject); }
    }
    static Program Codec()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        Check(prefab != null, "Luma4 prefab missing"); var codec = Keep(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = material => { if (material == null) return null; if (copies.TryGetValue(material, out Material found)) return found; var copy = Keep(new Material(material)); copies.Add(material, copy); return copy; };
        foreach (FieldInfo field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials) field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", codec);
    }
    static Program Encoder(Program codec)
    {
        var encoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
        var output = Keep(new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }); Check(output.Create(), "Output allocation failed");
        encoder.Set("output", output); encoder.Set("autoEncode", false); encoder.Set("debugLog", false); encoder.Set("sampleSize", 4); encoder.Set("frameIndex", 1u);
        encoder.Set("selectedCodecUdonTarget", codec.Backing); encoder.Set("useGpuLuma4", true); encoder.Set("luma4EncodeMaterial", Keep(new Material(Resources.Load<Material>("TSMPEncodeLuma4"))));
        encoder.Set("blockExpandMaterial", Keep(new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"))));
        encoder.Set("transSyncRefreshInterval", 0f); encoder.Set("targetPayloadBytes", 0); encoder.Call("_onEnable"); return encoder;
    }
    sealed class Rig
    {
        public Animator animator; public Transform[] bones; public int[] ids;
    }
    static Rig Humanoid(string name)
    {
        var root = Keep(new GameObject(name)); var bones = new Dictionary<HumanBodyBones, Transform>();
        AddBone(bones, HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
        AddBone(bones, HumanBodyBones.Spine, bones[HumanBodyBones.Hips], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.Chest, bones[HumanBodyBones.Spine], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.Neck, bones[HumanBodyBones.Chest], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.Head, bones[HumanBodyBones.Neck], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.LeftUpperArm, bones[HumanBodyBones.Chest], new Vector3(-.2f, .1f, 0));
        AddBone(bones, HumanBodyBones.LeftLowerArm, bones[HumanBodyBones.LeftUpperArm], new Vector3(-.3f, 0, 0));
        AddBone(bones, HumanBodyBones.LeftHand, bones[HumanBodyBones.LeftLowerArm], new Vector3(-.25f, 0, 0));
        AddBone(bones, HumanBodyBones.RightUpperArm, bones[HumanBodyBones.Chest], new Vector3(.2f, .1f, 0));
        AddBone(bones, HumanBodyBones.RightLowerArm, bones[HumanBodyBones.RightUpperArm], new Vector3(.3f, 0, 0));
        AddBone(bones, HumanBodyBones.RightHand, bones[HumanBodyBones.RightLowerArm], new Vector3(.25f, 0, 0));
        AddBone(bones, HumanBodyBones.LeftUpperLeg, bones[HumanBodyBones.Hips], new Vector3(-.1f, -.1f, 0));
        AddBone(bones, HumanBodyBones.LeftLowerLeg, bones[HumanBodyBones.LeftUpperLeg], new Vector3(0, -.4f, 0));
        AddBone(bones, HumanBodyBones.LeftFoot, bones[HumanBodyBones.LeftLowerLeg], new Vector3(0, -.4f, .05f));
        AddBone(bones, HumanBodyBones.RightUpperLeg, bones[HumanBodyBones.Hips], new Vector3(.1f, -.1f, 0));
        AddBone(bones, HumanBodyBones.RightLowerLeg, bones[HumanBodyBones.RightUpperLeg], new Vector3(0, -.4f, 0));
        AddBone(bones, HumanBodyBones.RightFoot, bones[HumanBodyBones.RightLowerLeg], new Vector3(0, -.4f, .05f));
        var human = bones.Select(b => new HumanBone { boneName = b.Value.name, humanName = HumanTrait.BoneName[(int)b.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        var skeleton = root.GetComponentsInChildren<Transform>().Select(b => new SkeletonBone { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = b.localScale }).ToArray();
        var avatar = Keep(AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton, armStretch = .05f, legStretch = .05f, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f }));
        Check(avatar.isValid && avatar.isHuman, "Generated Avatar is not humanoid");
        var animator = root.AddComponent<Animator>(); animator.avatar = avatar; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        int[] ids = bones.Keys.Select(id => (int)id).ToArray();
        return new Rig { animator = animator, ids = ids, bones = ids.Select(id => animator.GetBoneTransform((HumanBodyBones)id)).ToArray() };
    }
    static void AddBone(Dictionary<HumanBodyBones, Transform> bones, HumanBodyBones id, Transform parent, Vector3 position)
    {
        var bone = new GameObject(id.ToString()).transform; bone.SetParent(parent, false); bone.localPosition = position; bones.Add(id, bone);
    }
    sealed class Pair
    {
        public Program sender, receiver; public Transform source, destination; public Rig sentRig, receivedRig;
        public string kind, field; public ushort id; public uint hash; public int bodyBytes;
        public byte[] oldSource, oldReceiver; public bool packetSeen; public string[] boneFailureMessages;
    }
    static Pair MakePair(string kind, int index)
    {
        bool humanoid = kind == "humanoid";
        string path = "Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetwork" + (humanoid ? "HumanoidPoseSync" : "TransformSync") + ".asset";
        var pair = new Pair { sender = new Program(path), receiver = new Program(path), kind = kind, field = humanoid ? "poseBytes" : "packedBytes", id = (ushort)(index + 1), hash = StableHash.Fnv1A32(humanoid ? "humanoid.pose" : "transform.packed"), bodyBytes = humanoid ? 123 : 31 };
        pair.sender.Set("networkId", pair.id); pair.receiver.Set("networkId", pair.id);
        pair.receiver.Set("receiveInterpolation", (int)ReceiveInterpolationMode.Discrete);
        if (humanoid)
        {
            pair.sentRig = Humanoid("Sent Humanoid " + index); pair.receivedRig = Humanoid("Received Humanoid " + index);
            pair.sender.Set("animator", pair.sentRig.animator); pair.receiver.Set("animator", pair.receivedRig.animator);
            pair.sender.Call("ResolveBones"); pair.receiver.Call("ResolveBones");
            Check(pair.sender.Get<int>("validBoneCount") == 17 && pair.receiver.Get<int>("validBoneCount") == 17, "Generated fixture must resolve all 17 real humanoid bones");
            pair.boneFailureMessages = Enumerable.Range(0, 17).Select(i => "Humanoid world rotation differs for bone " + i).ToArray();
        }
        else
        {
            pair.source = Keep(new GameObject("Sent Transform " + index)).transform; pair.destination = Keep(new GameObject("Received Transform " + index)).transform;
            pair.sender.Set("target", pair.source); pair.receiver.Set("target", pair.destination);
        }
        return pair;
    }
    static void Bind(Program owner, Pair[] pairs, bool encoder)
    {
        int count = pairs.Length;
        owner.Set("bindingTargets", new Component[0]); owner.Set("bindingUdonTargets", pairs.Select(p => encoder ? p.sender.Backing : p.receiver.Backing).ToArray());
        owner.Set("bindingNetworkIds", pairs.Select(p => p.id).ToArray()); owner.Set("bindingVariableHashes", pairs.Select(p => p.hash).ToArray());
        owner.Set("bindingValueTypes", Enumerable.Repeat((byte)NetworkFrameProtocol.ValueTypeRawBytes, count).ToArray()); owner.Set("bindingFieldNames", pairs.Select(p => p.field).ToArray());
        owner.Set("bindingDirections", new int[count]); owner.Set("bindingPriorities", new int[count]);
        if (encoder) { owner.Set("bindingSendOnChange", Enumerable.Repeat(true, count).ToArray()); owner.Set("bindingMinSendIntervals", new float[count]); }
    }
    sealed class Injection
    {
        readonly byte[] raw; readonly int[] states = new int[2], stages = new int[2], counts = new int[2], readCounts = new int[2], safety = new int[2];
        readonly uint[] frames = new uint[2], streams = { 1u, 0u }; readonly bool[] valid = { true, false }, require = { true, false };
        readonly byte[][] readbacks, payloads, headers;
        public Injection(int maximum)
        {
            raw = new byte[(maximum + 3) / 4 * 4]; readbacks = new[] { raw, (byte[])null }; payloads = new[] { new byte[maximum], (byte[])null }; headers = new[] { new byte[56], (byte[])null };
        }
        public void Prepare(Program decoder, byte[] packet, int length, byte[] header, uint frame)
        {
            int padded = (length + 3) / 4 * 4; Array.Copy(packet, raw, length); Array.Clear(raw, length, padded - length); Array.Copy(header, headers[0], 56);
            states[0] = 2; stages[0] = 2; counts[0] = length; readCounts[0] = padded / 4; frames[0] = frame;
            decoder.Set("_slotHead", 0); decoder.Set("pendingDecodeCount", 1); decoder.Set("_slotStates", states); decoder.Set("_slotStages", stages);
            decoder.Set("_slotReadbackBytes", readbacks); decoder.Set("_slotHeaders", headers); decoder.Set("_slotPayloads", payloads); decoder.Set("_slotPayloadCounts", counts); decoder.Set("_slotReadCounts", readCounts);
            decoder.Set("_slotRequirePayloadCrc32", require); decoder.Set("_slotSafetyModes", safety); decoder.Set("_slotHeaderValid", valid); decoder.Set("_slotStreamIds", streams); decoder.Set("_slotFrameIndices", frames); decoder.Set("lastFrameValid", false);
        }
    }
    static void Prepare(Pair pair, int id, int index)
    {
        if (pair.kind == "transform")
        {
            pair.source.localPosition = new Vector3(id * .125f + index * .25f, .5f + index * .125f, -1.25f);
            pair.source.localRotation = Quaternion.AngleAxis((id % 120) * 3f + index * 7f, Vector3.up);
            pair.source.localScale = Vector3.one * (1f + ((id + index) % 4) * .0625f);
        }
        else
        {
            for (int i = 0; i < pair.sentRig.bones.Length; i++)
                pair.sentRig.bones[i].localRotation = Quaternion.AngleAxis((id % 180) * 2f + (i + 1) * 3f + index * 5f, Vector3.up);
            pair.sentRig.bones[0].localPosition = new Vector3(id * .0625f + index * .25f, 1f, -.125f);
        }
    }
    static void VerifyTarget(Pair pair)
    {
        if (pair.kind == "transform")
        {
            Check(Vector3.Distance(pair.source.localPosition, pair.destination.localPosition) < .002f, "Transform position differs");
            Check(Quaternion.Angle(pair.source.localRotation, pair.destination.localRotation) < .6f, "Transform rotation differs");
            Check(Vector3.Distance(pair.source.localScale, pair.destination.localScale) < .002f, "Transform scale differs");
        }
        else
        {
            Check(pair.sender.Get<int>("validBoneCount") == 17 && pair.receiver.Get<int>("validBoneCount") == 17, "Humanoid bone count differs");
            Check(pair.sender.Get<int>("encodedPoseBytes") == 123 && pair.sender.Get<int>("encodedRootMotionBytes") == 17, "Humanoid root/body packing differs");
            for (int i = 0; i < pair.sentRig.bones.Length; i++) Check(Quaternion.Angle(pair.sentRig.bones[i].rotation, pair.receivedRig.bones[i].rotation) < .6f, pair.boneFailureMessages[i]);
            Check(Vector3.Distance(pair.sentRig.bones[0].localPosition, pair.receivedRig.bones[0].localPosition) < .002f, "Humanoid root position differs");
        }
    }
    static IEnumerator Measure(Case item)
    {
        int firstProgram = programs.Count, firstOwned = owned.Count;
        Program codec = Codec(), encoder = Encoder(codec);
        var pairs = Enumerable.Range(0, item.transforms + item.humanoids).Select(i => MakePair(i < item.transforms ? "transform" : "humanoid", i)).ToArray();
        Bind(encoder, pairs, true);
        var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset"); decoder.Set("applyEveryFrame", false); decoder.Set("debugLog", false); decoder.Set("_crc32Table", Crc32Runtime.EnsureFastTable(null)); Bind(decoder, pairs, false);
        for (int i = 0; i < pairs.Length; i++) bindings.Add(Row(item.name, i, pairs[i].kind, pairs[i].id, pairs[i].hash, pairs[i].field, pairs[i].bodyBytes,
            pairs[i].sentRig == null ? "" : string.Join("/", pairs[i].sentRig.ids),
            pairs[i].sentRig == null ? "" : string.Join("/", pairs[i].sentRig.bones.Select(b => Array.IndexOf(pairs[i].sentRig.bones, b.parent)))));
        int total = item.warm + item.samples, maximum = 8 + pairs.Sum(p => 17 + p.bodyBytes);
        var trace = active = new CaseBuffer { item = item, rows = new Sample[total], packets = new byte[total][], headers = new byte[total][] };
        for (int i = 0; i < total; i++) { trace.packets[i] = new byte[maximum]; trace.headers[i] = new byte[56]; }
        var injection = new Injection(maximum);
        for (int i = 0; i < total; i++)
        {
            activeCase = item.name; activeIteration = i;
            Sample row = new Sample { iteration = i, inputId = item.unchanged ? 0 : i, frame = Time.frameCount, clock = Time.realtimeSinceStartupAsDouble };
            trace.captured = i + 1;
            try
            {
                row.sourceStart = Stopwatch.GetTimestamp(); for (int j = 0; j < pairs.Length; j++) Prepare(pairs[j], row.inputId, j); row.sourceStop = Stopwatch.GetTimestamp();
                row.encodedFrame = encoder.Get<uint>("frameIndex"); encoder.Timed("EncodeNow", out row.encodeStart, out row.encodeStop);
                Check(string.IsNullOrEmpty(encoder.Get<string>("lastError")), "Encode failed");
                bool emitted = !item.unchanged || i == 0; row.expectedFields = emitted ? pairs.Length : 0;
                uint actualFrame = encoder.Get<uint>("frameIndex");
                if (actualFrame != row.encodedFrame)
                {
                    byte[] header = encoder.Get<byte[]>("_headerBytes"), packet = encoder.Get<byte[]>("_payloadBytes"); row.payloadBytes = U16(header, 42);
                    Check(row.payloadBytes <= packet.Length, "Protected payload exceeds storage");
                    if (row.payloadBytes > trace.packets[i].Length) trace.packets[i] = new byte[row.payloadBytes];
                    Array.Copy(packet, trace.packets[i], row.payloadBytes); Array.Copy(header, trace.headers[i], 56);
                }
                Check(actualFrame == row.encodedFrame + (emitted ? 1u : 0u), "Unexpected output count"); Check(encoder.Get<int>("autoVariableCount") == row.expectedFields, "Field count differs");
                if (emitted)
                {
                    Check(row.payloadBytes == maximum, "Packed pose payload length differs");
                    row.gpu = encoder.Get<bool>("lastFrameUsedGpuLuma4"); Check(row.gpu, "Actual GPU Encoder path not used");
                    VerifyPacket(trace.packets[i], row.payloadBytes, trace.headers[i], row.encodedFrame, pairs);
                    injection.Prepare(decoder, trace.packets[i], row.payloadBytes, trace.headers[i], row.encodedFrame);
                    decoder.Timed("_update", out row.decodeStart, out row.decodeStop);
                    Check(decoder.Get<bool>("lastFrameValid") && decoder.Get<bool>("lastPayloadCrcVerified"), "Protected Decode failed");
                    row.appliedFields = decoder.Get<int>("lastAppliedVariableCount"); Check(row.appliedFields == pairs.Length && decoder.Get<int>("pendingDecodeCount") == 0, "Completed packet application count differs");
                }
                foreach (Pair pair in pairs)
                {
                    byte[] source = pair.sender.Get<byte[]>(pair.field), received = pair.receiver.Get<byte[]>(pair.field);
                    Check(source != null && source.Length == pair.bodyBytes && received != null && received.Length == pair.bodyBytes, "Public packed array length differs");
                    if (!ReferenceEquals(source, pair.oldSource)) row.sourceArrayReplacements++;
                    if (!ReferenceEquals(received, pair.oldReceiver)) row.receiverArrayReplacements++;
                    if (pair.oldSource != null) Check(ReferenceEquals(source, pair.oldSource), "Fixed-size source array was not reused");
                    if (pair.oldReceiver != null) Check(ReferenceEquals(received, pair.oldReceiver), "Fixed-size receiver array was not reused");
                    Check(!ReferenceEquals(source, received), "Receiver aliases sender packed bytes");
                    for (int j = 0; j < source.Length; j++) Check(source[j] == received[j], "Receiver packed bytes differ");
                    VerifyTarget(pair); pair.oldSource = source; pair.oldReceiver = received;
                }
                row.completed = true;
            }
            finally { trace.rows[i] = row; }
            yield return null;
        }
        FlushCase(); notes.Add("PASS " + item.name + "; calls=" + total + "; bindings=" + pairs.Length + "; protectedPackets=" + trace.rows.Count(r => r.payloadBytes > 0));
        Release(firstProgram, firstOwned, encoder);
    }
    static void RebindControl()
    {
        int firstProgram = programs.Count, firstOwned = owned.Count;
        Program codec = Codec(), encoder = Encoder(codec); Pair first = MakePair("transform", 0), replacement = MakePair("transform", 0);
        Bind(encoder, new[] { first }, true); Prepare(first, 11, 0); Prepare(replacement, 37, 0); encoder.Call("EncodeNow");
        uint previous = encoder.Get<uint>("frameIndex"); UdonBehaviour[] targets = encoder.Get<UdonBehaviour[]>("bindingUdonTargets"); targets[0] = replacement.sender.Backing;
        encoder.Call("EncodeNow"); Check(encoder.Get<uint>("frameIndex") == previous + 1, "Same-count replacement did not publish");
        byte[] body = replacement.sender.Get<byte[]>(replacement.field); Check(body != null && body.Length == 31, "Replacement capture did not execute");
        byte[] packet = encoder.Get<byte[]>("_payloadBytes"), header = encoder.Get<byte[]>("_headerBytes"); VerifyPacket(packet, U16(header, 42), header, previous, new[] { replacement });
        for (int i = 0; i < body.Length; i++) Check(packet[25 + i] == body[i], "Replacement retained old source value");
        notes.Add("PASS same-count in-place public binding target replacement publishes current replacement capture; separate untimed semantic control");
        Release(firstProgram, firstOwned, encoder);
    }
    static void Release(int firstProgram, int firstOwned, Program encoder)
    {
        encoder.Call("_onDisable");
        for (int i = programs.Count - 1; i >= firstProgram; i--) programs[i].Dispose();
        for (int i = owned.Count - 1; i >= firstOwned; i--) { if (owned[i] is RenderTexture texture) texture.Release(); if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.RemoveAt(i); }
    }
    static int U16(byte[] value, int offset) => value[offset] | value[offset + 1] << 8;
    static uint U32(byte[] value, int offset) => unchecked((uint)(value[offset] | value[offset + 1] << 8 | value[offset + 2] << 16 | value[offset + 3] << 24));
    static uint Crc(byte[] value, int count)
    {
        uint crc = 0xffffffffu; for (int i = 0; i < count; i++) { crc ^= value[i]; for (int bit = 0; bit < 8; bit++) crc = (crc & 1u) == 0u ? crc >> 1 : 0xedb88320u ^ crc >> 1; } return crc ^ 0xffffffffu;
    }
    static void VerifyPacket(byte[] packet, int length, byte[] header, uint frame, Pair[] pairs)
    {
        Check(U32(header, 52) == Crc(header, 52) && (U16(header, 10) & 1) != 0 && U16(header, 42) == length && U32(header, 44) == Crc(packet, length), "Independent header/payload CRC differs");
        Check(U32(header, 0) == ProtocolConstants.Magic && header[4] == ProtocolConstants.VersionMajor && header[5] >= ProtocolConstants.PayloadCrcVersionMinor && U16(header, 8) == 56 && U32(header, 20) == 1u && U32(header, 24) == frame, "Protected header identity/version/frame differs");
        Check(packet[0] == 1 && packet[1] == 0 && U16(packet, 2) == pairs.Length, "Network frame identity differs");
        int cursor = 8; foreach (Pair pair in pairs) pair.packetSeen = false;
        for (int i = 0; i < pairs.Length; i++)
        {
            int id = U16(packet, cursor), index = id - 1; Check(index >= 0 && index < pairs.Length && !pairs[index].packetSeen, "Unexpected or duplicate packed network ID"); Pair pair = pairs[index]; pair.packetSeen = true;
            Check(packet[cursor + 2] == NetworkFrameProtocol.MessageTypeVariableState && U16(packet, cursor + 6) == pair.bodyBytes + 9 && U16(packet, cursor + 8) == 1, "Variable message bounds differ");
            Check(U32(packet, cursor + 10) == pair.hash && packet[cursor + 14] == NetworkFrameProtocol.ValueTypeRawBytes && U16(packet, cursor + 15) == pair.bodyBytes, "Variable identity/body length differs");
            byte[] source = pair.sender.Get<byte[]>(pair.field);
            for (int j = 0; j < pair.bodyBytes; j++) Check(packet[cursor + 17 + j] == source[j], "Captured field body differs from actual component capture");
            cursor += 17 + pair.bodyBytes;
        }
        Check(cursor == length && pairs.All(x => x.packetSeen), "Packed packet cursor/count differs");
    }
    static void FlushCase()
    {
        CaseBuffer trace = active; if (trace == null) return; Case item = trace.item;
        Directory.CreateDirectory(Path.Combine(Root, "packets", item.name));
        using (var csv = new StreamWriter(Path.Combine(Root, "samples.csv"), true))
        for (int i = trace.flushed; i < trace.captured; i++)
        {
            Sample row = trace.rows[i]; string packet = "", header = "";
            if (row.payloadBytes > 0 && trace.packets[i] != null && trace.headers[i] != null)
            {
                packet = "packets/" + item.name + "/" + row.iteration.ToString("D5") + ".bin"; header = packet + ".header";
                using (FileStream output = File.Create(Path.Combine(Root, packet))) output.Write(trace.packets[i], 0, row.payloadBytes);
                File.WriteAllBytes(Path.Combine(Root, header), trace.headers[i]);
            }
            csv.WriteLine(Row(item.name, row.iteration, row.iteration >= item.warm, row.inputId, row.frame, row.clock, item.transforms, item.humanoids, item.unchanged,
                row.sourceStart, row.sourceStop, Ms(row.sourceStop - row.sourceStart), row.encodeStart, row.encodeStop, Ms(row.encodeStop - row.encodeStart), row.decodeStart, row.decodeStop, Ms(row.decodeStop - row.decodeStart), row.encodedFrame,
                row.payloadBytes, row.expectedFields, row.appliedFields, row.sourceArrayReplacements, row.receiverArrayReplacements, row.gpu, packet, header, row.completed));
            trace.flushed++;
        }
        active = null;
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        try { FlushCase(); } catch (Exception flushError) { notes.Add("CaseFlushError=" + flushError); error = error ?? flushError; }
        try
        {
            if (before != null)
            {
                var after = Sources(); File.WriteAllLines(Path.Combine(Root, "source-after.csv"), new[] { "path,sha256" }.Concat(after.Select(x => Row(x.Key, x.Value))));
                if (after.Count != before.Count || before.Any(x => !after.ContainsKey(x.Key) || after[x.Key] != x.Value)) error = error ?? new InvalidOperationException("Product source changed during measurement");
            }
        }
        catch (Exception cleanup) { error = error ?? cleanup; }
        foreach (var program in programs.ToArray()) program.Dispose();
        foreach (Object item in owned) { if (item is RenderTexture texture) texture.Release(); if (item != null) Object.DestroyImmediate(item); } owned.Clear(); RenderTexture.active = null;
        if (!string.IsNullOrWhiteSpace(Root))
        {
            Directory.CreateDirectory(Root); File.WriteAllLines(Path.Combine(Root, "artifact-hashes.csv"), artifacts); File.WriteAllLines(Path.Combine(Root, "bindings.csv"), bindings);
            notes.Add("Assertions=" + assertions); if (error != null) { notes.Add("FailureCase=" + activeCase + "; Sample=" + activeIteration); notes.Add(error.ToString()); }
            File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL" }.Concat(notes));
        }
        if (error != null) UnityEngine.Debug.LogException(error); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
