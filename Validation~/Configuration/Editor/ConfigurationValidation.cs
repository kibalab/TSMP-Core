using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Editor;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
#if UDONSHARP
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
#endif
using Object = UnityEngine.Object;

public static class ConfigurationValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
#if UDONSHARP
        Test("Udon client compilation and codec query VM", CompileAndRunVm);
#endif
        Test("R06 reserve existing IDs, duplicates, object groups", NetworkIds);
        Test("R05 binding target, ID, hash and field replacement", Bindings);
        Test("R05 Animator parameter rename without resizing", AnimatorParameters);
        Test("R05 humanoid Animator, Avatar and bone selection replacement", Humanoid);
        Test("PR #1 humanoid local targets and ancestor cache after reconfiguration", HumanoidInterpolation);
        Test("R05 codec options and layout without replacing codec", CodecOptions);
        Test("S02 codec instance add, reorder, remove and reparent", CodecInstances);
        Test("S01 actual Timeline state, seek and zero-time pause", Timeline);
        Test("R10 payload buffer survives initialization and repeated headers", Buffers);
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        File.WriteAllText(output, (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (failed) throw new InvalidOperationException("Configuration validation failed");
    }

    private static T Create<T>(string name = null) where T : TSMPBehaviour
    {
        var owner = new GameObject(name ?? typeof(T).Name) { hideFlags = HideFlags.HideAndDontSave };
#if UDONSHARP
        return owner.AddUdonSharpComponent<T>();
#else
        return owner.AddComponent<T>();
#endif
    }

    private static void Test(string name, Action test)
    {
        try { test(); Results.Add("PASS " + name); }
        catch (Exception e) { failed = true; Results.Add("FAIL " + name + "\n" + e); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

#if UDONSHARP
    private static void CompileAndRunVm()
    {
        var asset = EnsureProgram("ConfigurationCodec");
        var lookupAsset = EnsureProgram("ConfigurationLookupProbe");
        bool error = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = true;
        };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= callback; }
        Check(!error, "Udon client compilation reported errors");
        IUdonProgram program = asset.GetRealProgram();
        Check(program != null && program.ByteCode.Length > 0, "No codec client bytecode was generated");
        IUdonVM vm = UdonEditorManager.Instance.ConstructUdonVM();
        vm.LoadProgram(program);
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("encoderRequestWidth"), 640, typeof(int));
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("encoderRequestHeight"), 360, typeof(int));
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("encoderRequestBlockSize"), 8, typeof(int));
        int[] previous = null;
        foreach (int option in new[] { 0, 2, 0, 5 })
        {
            program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("option"), option, typeof(int));
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("OnTSMPEncoderQuery"));
            Check(vm.Interpret() == 0, "Codec query VM execution failed");
            var result = (int[])program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("encoderQueryValues"));
            Check(result[2] == 5 + option && result[3] == 1024 + option && result[5] == option, "Udon codec query retained old options");
            Check(previous == null || ReferenceEquals(previous, result), "Udon codec query reallocated its result");
            previous = result;
        }
        VerifyLookupVm(lookupAsset.GetRealProgram());
        AssetDatabase.SaveAssets();
    }

    private static UdonSharpProgramAsset EnsureProgram(string name)
    {
        string path = "Assets/Validation/Configuration/" + name + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/Configuration/Runtime/" + name + ".cs");
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void VerifyLookupVm(IUdonProgram program)
    {
        IUdonVM vm = UdonEditorManager.Instance.ConstructUdonVM();
        vm.LoadProgram(program);
        ushort[] ids = { 2, 1 };
        uint[] hashes = { 200, 100 };
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("ids"), ids, typeof(ushort[]));
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("hashes"), hashes, typeof(uint[]));
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("Refresh"));
        Check(vm.Interpret() == 0, "Initial binding lookup VM execution failed");
        var initial = (ushort[])program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("lookupIds"));
        Check(initial.SequenceEqual(new ushort[] { 1, 2 }), "Initial VM lookup was not sorted");
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("Refresh"));
        Check(vm.Interpret() == 0, "Unchanged lookup VM execution failed");
        Check(ReferenceEquals(initial, program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("lookupIds"))), "Unchanged VM lookup was reallocated");
        ids[0] = 3;
        hashes[1] = 300;
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("Refresh"));
        Check(vm.Interpret() == 0, "Changed binding lookup VM execution failed");
        var resultIds = (ushort[])program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("lookupIds"));
        var resultHashes = (uint[])program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("lookupHashes"));
        Check(resultIds.SequenceEqual(new ushort[] { 1, 3 }) && resultHashes.SequenceEqual(new uint[] { 300, 200 }), "VM lookup retained same-size ID/hash changes");
        Results.Add("PASS Udon VM binding lookup refresh and unchanged-cache reuse");
    }

    private static void VerifyTimelineVm()
    {
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetworkTimelineSync.asset");
        asset.UpdateProgram();
        IUdonProgram program = asset.GetRealProgram();
        Check(program != null, "Timeline bytecode is unavailable");
        IUdonVM vm = UdonEditorManager.Instance.ConstructUdonVM();
        vm.LoadProgram(program);
        var director = new GameObject("Timeline VM").AddComponent<PlayableDirector>();
        var backing = new GameObject("Timeline VM Receiver").AddComponent<VRC.Udon.UdonBehaviour>();
        var resolve = typeof(VRC.Udon.UdonBehaviour).GetMethod("ResolveUdonHeapReferences", Private);
        Check((bool)resolve.Invoke(backing, new object[] { program.SymbolTable, program.Heap }), "Timeline VM heap references were not initialized");
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_onEnable"));
        Check(vm.Interpret() == 0, "Timeline VM OnEnable failed");
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = 10;
        timeline.CreateTrack<ActivationTrack>(null, "Track");
        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.timeUpdateMode = DirectorUpdateMode.Manual;
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("director"), director, typeof(PlayableDirector));
        string[] commands = { "Play", "Pause", "Resume", "Stop" };
        byte[] states = { 2, 1, 2, 0 };
        for (int i = 0; i < commands.Length; i++)
        {
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(commands[i]));
            Check(vm.Interpret() == 0, "Timeline VM command failed: " + commands[i]);
            byte state = (byte)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("_directorState"));
            Check(state == states[i], "Timeline VM state mismatch: " + commands[i]);
        }
        Results.Add("PASS Udon VM Timeline Play/Pause/Resume/Stop on a real PlayableDirector");
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("receiveInterpolation"), (int)ReceiveInterpolationMode.Discrete, typeof(int));
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("timeApplyThreshold"), 10f, typeof(float));
        director.initialTime = 1;
        ReceiveTimelineVm(vm, program, 2, .3f);
        Check(director.state == PlayState.Playing && Math.Abs(director.time - .3) < .0001, "Udon initial seek was lost");
        ReceiveTimelineVm(vm, program, 255, 4);
        ReceiveTimelineVm(vm, program, 2, float.NaN);
        Check(director.state == PlayState.Playing && Math.Abs(director.time - .3) < .0001, "Udon invalid packet changed playback");
        ReceiveTimelineVm(vm, program, 1, 4);
        Check(director.state == PlayState.Paused && director.time == 4, "Udon paused seek failed");
        int stops = 0;
        director.stopped += unused => stops++;
        ReceiveTimelineVm(vm, program, 0, 0);
        ReceiveTimelineVm(vm, program, 0, 0);
        Check(stops == 1 && !director.playableGraph.IsValid(), "Udon repeated Stop rebuilt graphs");
        int starts = 0;
        director.played += unused => starts++;
        ReceiveTimelineVm(vm, program, 1, 3);
        Check(starts == 0 && director.state == PlayState.Paused && director.time == 3, "Udon initial paused seek started playback");
        Results.Add("PASS Udon VM initial seek, paused seek, malformed packet rejection and idempotent Stop");

        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("receiveInterpolation"), (int)ReceiveInterpolationMode.Continuous, typeof(int));
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("timeApplyThreshold"), 0f, typeof(float));
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("continuousInterpolationRate"), 1f, typeof(float));
        ReceiveTimelineVm(vm, program, 2, 2);
        ReceiveTimelineVm(vm, program, 2, 4);
        Check(director.time == 2, "Udon Continuous snapped immediately");
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_update"));
        Check(vm.Interpret() == 0, "Udon Continuous update failed");
        Check(director.time > 2 && director.time < 4, "Udon Continuous did not smooth the time offset");
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("TSMPBeforeEncode"));
        Check(vm.Interpret() == 0, "Udon Timeline capture failed");
        byte[] packet = (byte[])program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("timelineBytes"));
        Check(packet.Length == 10 && packet[0] == 1 && packet[1] == 2, "Udon capture changed the Timeline wire format");
        Check(Math.Abs(Binary.ReadFloat32LE(packet, 2) - director.time) < .0001, "Udon capture lost the Director time");
        director.Stop();
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("Play"));
        Check(vm.Interpret() == 0 && director.state == PlayState.Playing, "Explicit Udon Play did not recover after the Director ended outside the component");
        director.Stop();
        Results.Add("PASS Udon VM Continuous update and v1 capture");
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("receiveInterpolation"), (int)ReceiveInterpolationMode.Discrete, typeof(int));
        var parent = new GameObject("Timeline VM Director Parent");
        director.transform.SetParent(parent.transform);
        for (int mode = 0; mode < 2; mode++)
        {
            ReceiveTimelineVm(vm, program, 1, 2);
            if (mode == 0) director.enabled = false;
            else parent.SetActive(false);
            Check(director.gameObject.activeSelf, "VM parent case changed child activeSelf");
            ReceiveTimelineVm(vm, program, 1, 5);
            Check(director.time != 5, "Unavailable VM Director accepted a packet");
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("TSMPBeforeEncode"));
            Check(vm.Interpret() == 0, "Unavailable VM Director capture failed");
            Check(program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("timelineBytes")) == null &&
                (int)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("encodedTimelineBytes")) == 0,
                "Unavailable VM Director retained captured bytes");
            if (mode == 0) director.enabled = true;
            else parent.SetActive(true);
            ReceiveTimelineVm(vm, program, 1, 3);
            Check(director.time == 3, "Reactivated VM Director did not recover");
        }
        director.Stop();
        Results.Add("PASS Udon VM disabled Director and inactive parent capture/receive guards with recovery");
    }

    private static void ReceiveTimelineVm(IUdonVM vm, IUdonProgram program, byte state, float time)
    {
        byte[] packet = new byte[10];
        packet[0] = 1;
        packet[1] = state;
        Binary.WriteFloat32LE(packet, 2, time);
        Binary.WriteFloat32LE(packet, 6, 10);
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("timelineBytes"), packet, typeof(byte[]));
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("OnTSMPVariableReceived"));
        Check(vm.Interpret() == 0, "Udon Timeline receive failed");
    }

    public static void RunTimelineVm()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("TSMP.Configuration.TimelineVm", true);
        AttachTimelineVm();
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void AttachTimelineVm()
    {
        if (!SessionState.GetBool("TSMP.Configuration.TimelineVm", false)) return;
        EditorApplication.playModeStateChanged -= OnTimelinePlayMode;
        EditorApplication.playModeStateChanged += OnTimelinePlayMode;
    }

    private static void OnTimelinePlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= OnTimelinePlayMode;
        SessionState.SetBool("TSMP.Configuration.TimelineVm", false);
        EditorApplication.delayCall += () =>
        {
            int exitCode = 0;
            try { VerifyTimelineVm(); }
            catch (Exception exception) { exitCode = 1; Results.Add(exception.ToString()); }
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"),
                (exitCode == 0 ? "PASS" : "FAIL") + "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
            EditorApplication.Exit(exitCode);
        };
    }
#endif

    private static object Call(object owner, string name, params object[] args)
    {
        return owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    }

    private static object Get(object owner, string name)
    {
        return owner.GetType().GetField(name, Private).GetValue(owner);
    }

    private static void Set(object owner, string name, object value)
    {
        owner.GetType().GetField(name, Private).SetValue(owner, value);
    }

    private static void NetworkIds()
    {
        var added = Create<TSMPNetworkTransformSync>("A-New");
        var old = Create<TSMPNetworkTransformSync>("B-Existing");
        var duplicate = Create<TSMPNetworkTransformSync>("C-Duplicate");
        var next = Create<TSMPNetworkTransformSync>("D-Existing");
        old.networkId = 1;
        duplicate.networkId = 1;
        next.networkId = 2;
        var list = new TSMPNetworkBehaviour[] { added, old, duplicate, next };
        var assign = typeof(TransSyncBindingBuilder).GetMethod("AssignNetworkIds", BindingFlags.NonPublic | BindingFlags.Static);
        assign.Invoke(null, new object[] { list });
        Check(old.networkId == 1 && next.networkId == 2, "Existing ID was stolen by a new object");
        Check(added.networkId == 3 && duplicate.networkId == 4, "New and duplicate IDs did not avoid reservations");
        Check((int)assign.Invoke(null, new object[] { list }) == 0, "Repeated rebuild reassigned IDs");
#if UDONSHARP
        var sibling = old.gameObject.AddUdonSharpComponent<TSMPNetworkBlendShapesSync>();
#else
        var sibling = old.gameObject.AddComponent<TSMPNetworkBlendShapesSync>();
#endif
        assign.Invoke(null, new object[] { new TSMPNetworkBehaviour[] { sibling, old, next } });
        Check(sibling.networkId == 1 && old.networkId == 1, "Components on the same object have different IDs");
    }

    private static void Bindings()
    {
        var a = Create<TSMPNetworkBlendShapesSync>();
        var b = Create<TSMPNetworkBlendShapesSync>();
        var d = Create<TSMPDecoder>();
        d.applyEveryFrame = false;
        d.bindingTargets = new Component[] { a };
#if UDONSHARP
        d.bindingUdonTargets = new[] { UdonSharpEditorUtility.GetBackingUdonBehaviour(a) };
#endif
        d.bindingNetworkIds = new ushort[] { 1 };
        d.bindingVariableHashes = new uint[] { 100 };
        d.bindingValueTypes = new byte[] { NetworkFrameProtocol.ValueTypeRawBytes };
        d.bindingFieldNames = new[] { "blendShapeBytes" };
        Apply(d, 1, 100, 11);
        byte[] previous = a.blendShapeBytes;
        Check(previous != null && previous[0] == 11, "Initial value was not applied");
        d.bindingTargets[0] = b;
#if UDONSHARP
        d.bindingUdonTargets[0] = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
#endif
        Apply(d, 1, 100, 22);
        Check(b.blendShapeBytes[0] == 22 && previous[0] == 11, "Target replacement changed the previous target's array");
        byte[] reused = b.blendShapeBytes;
        Apply(d, 1, 100, 23);
        Check(ReferenceEquals(reused, b.blendShapeBytes), "Unchanged binding allocated another value array");
        d.bindingNetworkIds[0] = 2;
        Apply(d, 2, 100, 33);
        Check(b.blendShapeBytes[0] == 33, "Network ID mutation was not detected");
        d.bindingVariableHashes[0] = 200;
        Apply(d, 2, 200, 44);
        Check(b.blendShapeBytes[0] == 44, "Variable hash mutation was not detected");
        Apply(d, 1, 100, 99);
        Check(b.blendShapeBytes[0] == 44, "Old ID/hash still resolved");
        d.bindingFieldNames[0] = "lastRpcPayload";
        Call(d, "EnsureBindingTargetCache", 1);
        Check(((byte[][])Get(d, "_rawByteValueArrays"))[0] == null, "Field change retained previous field's buffer");
#if UDONSHARP
        int count;
        var cached = EncoderUdonBindingRuntime.EnsureBindingTargetCache(d.bindingTargets, d.bindingUdonTargets, 1, null, 0, out count);
        d.bindingTargets[0] = a;
        d.bindingUdonTargets[0] = UdonSharpEditorUtility.GetBackingUdonBehaviour(a);
        var updated = EncoderUdonBindingRuntime.EnsureBindingTargetCache(d.bindingTargets, d.bindingUdonTargets, 1, cached, count, out count);
        Check(updated[0] == d.bindingUdonTargets[0], "Encoder Udon target replacement was not detected");
#endif
    }

    private static void Apply(TSMPDecoder decoder, ushort id, uint hash, byte value)
    {
        byte[] bytes = new byte[128];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, 1);
        int start = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, id, 1);
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, hash, NetworkFrameProtocol.ValueTypeRawBytes, new[] { value });
        Check(cursor > 0 && NetworkFrameWriter.EndVariableState(bytes, start, cursor, 1), "Packet construction failed");
        NetworkFrameWriter.EndNetworkFrame(bytes, 0, 1);
        Array.Resize(ref bytes, cursor);
        Set(decoder, "_payloadBytes", bytes);
        Set(decoder, "_payloadDataBytes", bytes.Length);
        Check((bool)Call(decoder, "ApplyNetworkFrame"), decoder.lastError);
    }

    private static void AnimatorParameters()
    {
        var sync = Create<TSMPNetworkAnimatorSync>();
        sync.parameterNames = new[] { "Old" };
        Call(sync, "EnsureParameterHashes");
        int[] hashes = (int[])Get(sync, "_parameterHashes");
        sync.parameterNames[0] = "New";
        Call(sync, "EnsureParameterHashes");
        Check(hashes[0] == Animator.StringToHash("New"), "In-place parameter rename retained the old hash");
        sync.parameterNames = new[] { "Replacement" };
        Call(sync, "EnsureParameterHashes");
        Check(hashes[0] == Animator.StringToHash("Replacement"), "Same-size parameter replacement retained the old hash");
        sync.parameterNames[0] = null;
        Call(sync, "EnsureParameterHashes");
        Check(hashes[0] == 0 && ReferenceEquals(hashes, Get(sync, "_parameterHashes")), "Cleared parameter or hash array reuse failed");
    }

    private static Animator Rig(string name)
    {
        var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        var bones = new Dictionary<HumanBodyBones, Transform>();
        Bone(bones, HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
        Bone(bones, HumanBodyBones.Spine, bones[HumanBodyBones.Hips], new Vector3(0, .2f, 0));
        Bone(bones, HumanBodyBones.Head, bones[HumanBodyBones.Spine], new Vector3(0, .5f, 0));
        foreach (bool left in new[] { true, false })
        {
            var arm = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            var elbow = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            var leg = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            var knee = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            var foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            float side = left ? -1 : 1;
            Bone(bones, arm, bones[HumanBodyBones.Spine], new Vector3(.2f * side, .3f, 0));
            Bone(bones, elbow, bones[arm], new Vector3(.3f * side, 0, 0));
            Bone(bones, hand, bones[elbow], new Vector3(.25f * side, 0, 0));
            Bone(bones, leg, bones[HumanBodyBones.Hips], new Vector3(.1f * side, -.1f, 0));
            Bone(bones, knee, bones[leg], new Vector3(0, -.4f, 0));
            Bone(bones, foot, bones[knee], new Vector3(0, -.4f, .05f));
        }
        var avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
        {
            human = bones.Select(pair => new HumanBone { boneName = pair.Value.name, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
            skeleton = root.GetComponentsInChildren<Transform>().Select(bone => new SkeletonBone { name = bone.name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale }).ToArray(),
            armStretch = .05f, legStretch = .05f, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f
        });
        Check(avatar.isValid && avatar.isHuman, "Test Avatar is invalid");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        return animator;
    }

    private static void Bone(Dictionary<HumanBodyBones, Transform> bones, HumanBodyBones id, Transform parent, Vector3 position)
    {
        var bone = new GameObject(id.ToString()).transform;
        bone.SetParent(parent, false);
        bone.localPosition = position;
        bones.Add(id, bone);
    }

    private static void Humanoid()
    {
        var a = Rig("First Rig");
        var b = Rig("Second Rig");
        var sync = Create<TSMPNetworkHumanoidPoseSync>();
        sync.includeFingerBones = false;
        sync.boneIds = new[] { (int)HumanBodyBones.Head, (int)HumanBodyBones.LeftHand };
        sync.animator = a;
        sync.ResolveBones();
        Check(sync.boneTargets[0] == a.GetBoneTransform(HumanBodyBones.Head), "Initial bone resolution failed");
        sync.animator = b;
        sync.ResolveBones();
        Check(sync.boneTargets[0] == b.GetBoneTransform(HumanBodyBones.Head), "Animator replacement retained old bones");
        sync.boneIds[0] = (int)HumanBodyBones.RightHand;
        sync.ResolveBones();
        Check(sync.boneTargets[0] == b.GetBoneTransform(HumanBodyBones.RightHand), "Same-size selection retained old bones");
        var targets = sync.boneTargets;
        sync.ResolveBones();
        Check(ReferenceEquals(targets, sync.boneTargets), "Unchanged rig allocated another target array");
        var avatar = b.avatar;
        b.avatar = null;
        sync.ResolveBones();
        Check(sync.validBoneCount == 0 && sync.boneTargets.All(target => target == null), "Removed Avatar retained resolved targets");
        b.avatar = Object.Instantiate(avatar);
        b.Rebind();
        sync.ResolveBones();
        Check(sync.validBoneCount == 2 && sync.boneTargets[0] == b.GetBoneTransform(HumanBodyBones.RightHand), "Avatar replacement did not recover");
        sync.boneIds[0] = -1;
        sync.ResolveBones();
        Check(sync.boneTargets[0] == null && sync.validBoneCount == 1, "Invalid replacement bone retained stale target");
        sync.includeFingerBones = true;
        sync.ResolveBones();
        Check(sync.boneIds.Length == 17 && sync.validBoneCount == 1, "Enabling fingers failed to expand/resynchronize selection");
        sync.boneIds = null;
        sync.ResolveBones();
        Check(sync.validBoneCount == 0 && sync.boneTargets == null, "Null bone selection retained active targets");
    }

    private static void HumanoidInterpolation()
    {
        var sender = Create<TSMPNetworkHumanoidPoseSync>();
        var receiver = Create<TSMPNetworkHumanoidPoseSync>();
        sender.animator = Rig("Interpolation sender");
        receiver.animator = Rig("Interpolation receiver");
        sender.includeFingerBones = receiver.includeFingerBones = false;
        receiver.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        int spine = (int)HumanBodyBones.Spine;
        int arm = (int)HumanBodyBones.LeftUpperArm;
        int elbow = (int)HumanBodyBones.LeftLowerArm;
        int hand = (int)HumanBodyBones.LeftHand;
        int[] ids = { spine, arm, elbow, hand };
        sender.boneIds = (int[])ids.Clone();
        receiver.boneIds = (int[])ids.Clone();
        sender.animator.GetBoneTransform(HumanBodyBones.Hips).localRotation = Quaternion.Euler(0, 25, 0);
        for (int i = 0; i < ids.Length; i++)
            sender.animator.GetBoneTransform((HumanBodyBones)ids[i]).localRotation = Quaternion.Euler(10 + i * 5, 0, 20 - i * 15);
        sender.TSMPBeforeEncode();
        receiver.poseBytes = (byte[])sender.poseBytes.Clone();
        receiver.OnTSMPVariableReceived();
        var rotations = (Quaternion[])Get(receiver, "_targetBoneRotations");
        foreach (int id in ids)
            Check(Quaternion.Angle(rotations[id], sender.animator.GetBoneTransform((HumanBodyBones)id).localRotation) < .2f,
                "Received world rotation was not converted to a local target: " + id);
        Check(!((bool[])Get(receiver, "_hasTargetBoneRotation"))[(int)HumanBodyBones.Hips], "Root rotation was scheduled twice");
        var ancestors = (int[])Get(receiver, "_receivedAncestorBoneIdsById");
        Check(ancestors[hand] == elbow, "Direct received parent was not cached");
        receiver.OnTSMPVariableReceived();
        Check(ReferenceEquals(ancestors, Get(receiver, "_receivedAncestorBoneIdsById")), "Unchanged ancestor cache was reallocated");

        sender.boneIds[2] = (int)HumanBodyBones.Head;
        sender.TSMPBeforeEncode();
        receiver.poseBytes = (byte[])sender.poseBytes.Clone();
        receiver.OnTSMPVariableReceived();
        Check(ancestors[hand] == arm, "Same-size received bone set retained the old ancestor");
        var targetHand = receiver.animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var targetArm = receiver.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var worldRotations = (Quaternion[])Get(receiver, "_receivedBoneWorldRotations");
        Quaternion parentRotation = worldRotations[arm] * Quaternion.Inverse(targetArm.rotation) * targetHand.parent.rotation;
        Quaternion expected = Quaternion.Inverse(parentRotation) * worldRotations[hand];
        Check(Quaternion.Angle(rotations[hand], expected) < .05f, "Skipped-parent rotation correction was lost");

        receiver.animator = Rig("Replacement interpolation receiver");
        receiver.animator.transform.rotation = Quaternion.Euler(0, 90, 0);
        receiver.OnTSMPVariableReceived();
        var lookup = (Transform[])Get(receiver, "_boneTargetsById");
        Check(lookup[hand] == receiver.animator.GetBoneTransform(HumanBodyBones.LeftHand), "Interpolation retained old rig transforms");
        Check(((int[])Get(receiver, "_receivedAncestorBoneIdsById"))[hand] == arm, "Ancestor cache was not rebuilt on rig replacement");
        sender.boneIds[2] = elbow;
        sender.TSMPBeforeEncode();
        receiver.poseBytes = (byte[])sender.poseBytes.Clone();
        receiver.OnTSMPVariableReceived();
        rotations = (Quaternion[])Get(receiver, "_targetBoneRotations");
        foreach (int id in ids)
            Check(Quaternion.Angle(rotations[id], sender.animator.GetBoneTransform((HumanBodyBones)id).localRotation) < .2f,
                "Rotated replacement rig changed a local target: " + id);
    }

    private static void CodecOptions()
    {
        var codec = Create<ConfigurationCodec>();
        int[] values = EncoderCodecRuntime.EnsureQueryValues(null);
        EncoderCodecRuntime.QueryDirect(codec, 640, 360, 8, values);
        codec.option = 2;
        EncoderCodecRuntime.QueryDirect(codec, 640, 360, 8, values);
        Check(values[2] == 7 && values[3] == 1026 && values[5] == 2, "Direct codec query retained old options");
        codec.OnTSMPEncoderQuery();
        int[] first = codec.encoderQueryValues;
        codec.option = 3;
        codec.OnTSMPEncoderQuery();
        Check(ReferenceEquals(first, codec.encoderQueryValues) && first[2] == 8 && first[5] == 3, "Compact query did not refresh/reuse its result");
#if UDONSHARP
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(codec);
        EncoderCodecRuntime.QueryBridge(backing, 640, 360, 8, 0, 100, values);
        codec.option = 4;
        EncoderCodecRuntime.QueryBridge(backing, 640, 360, 8, 0, 100, values);
        Check(values[2] == 9 && values[3] == 1028 && values[5] == 4, "Bridge codec query retained old options");
#endif
    }

    private static void CodecInstances()
    {
        var sourceA = Create<ConfigurationCodec>("Codec A");
        var sourceB = Create<ConfigurationCodec>("Codec B");
        var owner = new GameObject("Setup") { hideFlags = HideFlags.HideAndDontSave };
        var setup = owner.AddComponent<TSMPSetup>();
        setup.enabled = false;
        setup.codecPrefabs = new TSMPCodec[] { sourceA };
        Call(setup, "EnsureCodecInstances");
        var a = ((TSMPCodec[])Get(setup, "codecInstances"))[0];
        ((ConfigurationCodec)a).option = 9;
        a.name = "Configured instance";
        setup.codecPrefabs = new TSMPCodec[] { sourceB, sourceA, null };
        Call(setup, "EnsureCodecInstances");
        var instances = (TSMPCodec[])Get(setup, "codecInstances");
        Check(instances[1] == a && ((ConfigurationCodec)a).option == 9 && instances[2] == null, "Adding/reordering codecs destroyed configured instance");
        var b = instances[0];
        setup.codecPrefabs = new TSMPCodec[] { sourceA };
        Call(setup, "EnsureCodecInstances");
        Check(b == null && ((TSMPCodec[])Get(setup, "codecInstances"))[0] == a, "Removing codec destroyed retained instance");
        var parent = new GameObject("New codec root").transform;
        setup.codecInstanceRoot = parent;
        Call(setup, "EnsureCodecInstances");
        Check(a.transform.parent == parent && ((ConfigurationCodec)a).option == 9, "Moving codec root lost instance/options");
        var stable = Get(setup, "codecInstances");
        Call(setup, "EnsureCodecInstances");
        Check(ReferenceEquals(stable, Get(setup, "codecInstances")), "Unchanged codec list replaced arrays");
        setup.codecPrefabs = new TSMPCodec[] { sourceA, sourceA };
        Call(setup, "EnsureCodecInstances");
        instances = (TSMPCodec[])Get(setup, "codecInstances");
        Check(instances[0] == a && instances[1] != a && instances[1] != null, "Duplicate sources reused one instance twice");
        setup.codecPrefabs = Array.Empty<TSMPCodec>();
        Call(setup, "EnsureCodecInstances");
        Check(a == null && parent.childCount == 0, "Removed codecs were not cleaned up");
    }

    private static void Timeline()
    {
        var sync = Create<TSMPNetworkTimelineSync>();
        var director = sync.gameObject.AddComponent<PlayableDirector>();
        var asset = ScriptableObject.CreateInstance<TimelineAsset>();
        asset.durationMode = TimelineAsset.DurationMode.FixedLength;
        asset.fixedDuration = 10;
        asset.CreateTrack<ActivationTrack>(null, "Track");
        director.playableAsset = asset;
        director.playOnAwake = false;
        director.timeUpdateMode = DirectorUpdateMode.Manual;
        sync.director = director;
        sync.Play();
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 2, "First playing sample at time zero was marked stopped");
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 2, "Unchanged time changed playing to paused");
        sync.Pause();
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 1, "Paused at time zero was marked stopped");
        director.time = 4;
        director.Evaluate();
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 1, "Seeking while paused was marked playing");
        sync.Resume();
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 2, "Resume was not playing");
        sync.Stop();
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 0, "Stop was not stopped");
        director.Play();
        sync.TSMPBeforeEncode();
        Check(sync.timelineBytes[1] == 2, "Native external Director control was not observed");
        director.Stop();
    }

    private static void Buffers()
    {
        var decoder = Create<TSMPDecoder>();
        decoder.payloadByteTexture = new RenderTexture(2048, 1, 0);
        byte[] initial = new byte[278];
        Set(decoder, "_payloadBytes", initial);
        for (int i = 0; i < 20; i++)
        {
            Call(decoder, "InitializeBuffers");
            Check(ReferenceEquals(initial, Get(decoder, "_payloadBytes")), "InitializeBuffers replaced the actual-sized payload buffer");
            byte[] next;
            int block, sample, width, count, start, row;
            DecoderHeaderRuntime.ResolvePayloadLayout(true, 640, 8, 80, 0, 278, 5, 8, 0, 80, 4096, initial, out block, out sample, out width, out count, out next, out start, out row);
            Check(ReferenceEquals(initial, next), "Same-size header replaced payload buffer");
        }
        decoder.payloadBytesOverride = 512;
        Call(decoder, "InitializeBuffers");
        Call(decoder, "RequestPayloadReadback");
        Check(((byte[])Get(decoder, "_payloadBytes")).Length == 512, "Header-layout-disabled readback did not allocate requested size");
        decoder.payloadByteTexture.Release();
    }
}
