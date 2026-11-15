#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using Object = UnityEngine.Object;

// An optional workload in the existing real-client-VM/GPU fixture. All hot-path
// observations use bounded buffers; serialization and assertions run after drain.
public static partial class SustainedCostValidation
{
    const string SceneStatePath = "Assets/Validation/SustainedCost/SceneCostState.asset";
    const int SceneCubes = 12, SceneBones = 51, SceneFields = 17, PacketStride = 2048;
    static SceneSession sceneSession;
    struct SceneEvent
    {
        public string role, label;
        public int frame, source, received, pendingBefore, pendingAfter, component, callbackSlot, requestAmbiguity;
        public bool top;
        public double clock;
        public long start, stop, nestedHost, callbackRequest, slot0Request, slot1Request, requestWaitLowerTicks, requestWaitUpperTicks;
    }
    struct SceneFrame
    {
        public int frame, source, received, pending, gc0, gc1, gc2;
        public double clock, ageClock;
        public long stepTicks, preparationTicks, snapshotTicks;
    }
    struct ScenePublication
    {
        public int id, frame, bytes, variables;
        public uint encodedFrame;
        public double sent, published;
        public long preparationTicks, snapshotTicks;
        public bool gpu;
    }
    struct SceneApplication
    {
        public int id, frame, bytes, fields;
        public uint encodedFrame;
        public double clock;
        public bool crc;
        public float seconds;
        public string message;
    }
    sealed class SceneSession
    {
        public Case item;
        public Program encoder, decoder, senderState, receiverState, senderHumanoid, receiverHumanoid;
        public readonly Program[] senders = new Program[13], receivers = new Program[13];
        public Animator sentAnimator, receivedAnimator;
        public Transform[] sentBones, receivedBones;
        public readonly Transform[] sourceCubes = new Transform[12], receiverCubes = new Transform[12];
        public readonly Rigidbody[] bodies = new Rigidbody[12];
        public PlayableGraph graph;
        public AnimationClipPlayable dance;
        public AnimationClip clip;
        public readonly Dictionary<uint, int> stateBits = new Dictionary<uint, int>();
        public byte[] stateBytes = new byte[256];
        public SceneEvent[] events;
        public SceneFrame[] frames;
        public ScenePublication[] publications;
        public SceneApplication[] applications;
        public byte[] sentPackets, receivedPackets, sentHeaders, receivedHeaders, receivedStateBytes;
        public Vector3[] sentPositions, receivedPositions, sentScales, receivedScales, sentHips, receivedHips;
        public Quaternion[] sentRotations, receivedRotations, sentBoneRotations, receivedBoneRotations;
        public int eventCount, frameCount, publicationCount, applicationCount, latestReceived, deadlineMisses, releases;
        public long observationTicks;
        public double start, begin, end, due;
        public uint applyingFrame;
        public int applyingMask;
        public bool recording, flushed;
        public int capacity;
        public float maximumDanceChangeDegrees, maximumCubeTravel;
        public int descendingCube0Samples, finalDynamicBodies;
        public long nextRequest;
        public readonly long[] requestIds = new long[2], requestObservedBefore = new long[2], requestObservedAfter = new long[2];
        public SceneSession(Case item)
        {
            this.item = item;
            capacity = Math.Max(256, (int)Math.Ceiling((item.warmSeconds + item.seconds + 10) * item.sourceHz) + 64);
            Check(capacity <= 6000, "Scene publication capacity too large");
            publications = new ScenePublication[capacity]; applications = new SceneApplication[capacity];
            events = new SceneEvent[capacity * 48]; frames = new SceneFrame[capacity * 5];
            sentPackets = new byte[capacity * PacketStride]; receivedPackets = new byte[capacity * PacketStride];
            sentHeaders = new byte[capacity * 56]; receivedHeaders = new byte[capacity * 56]; receivedStateBytes = new byte[capacity * 256];
            sentPositions = new Vector3[capacity * 12]; receivedPositions = new Vector3[capacity * 12];
            sentScales = new Vector3[capacity * 12]; receivedScales = new Vector3[capacity * 12];
            sentRotations = new Quaternion[capacity * 12]; receivedRotations = new Quaternion[capacity * 12];
            sentHips = new Vector3[capacity]; receivedHips = new Vector3[capacity];
            sentBoneRotations = new Quaternion[capacity * 51]; receivedBoneRotations = new Quaternion[capacity * 51];
        }
    }
    static void EnsureSceneStateAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SceneStatePath) != null) return;
        var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/SustainedCost/Runtime/SceneCostState.cs");
        Check(asset.sourceCsScript != null, "SceneCostState source missing");
        AssetDatabase.CreateAsset(asset, SceneStatePath);
    }
    static uint InterpretScene(IUdonVM vm, Program owner, Dictionary<uint, string> labels)
    {
        var scene = sceneSession; long observationStart = Stopwatch.GetTimestamp();
        bool top = depth++ == 0;
        string label; if (!labels.TryGetValue(vm.GetProgramCounter(), out label)) label = "unknown";
        bool decoderEvent = top && owner.role == "Decoder";
        bool componentEvent = (owner.role == "SceneSender" && label == "TSMPBeforeEncode") ||
            ((owner.role == "SceneTarget" || owner.role == "SceneState") && label == "OnTSMPVariableReceived");
        bool record = scene.recording && ((top && (owner.role == "Encoder" || owner.role == "Decoder")) || componentEvent);
        int pending = record ? scene.decoder.Get<int>("pendingDecodeCount") : 0;
        Array requestArray = decoderEvent ? (Array)scene.decoder.Code.Heap.GetHeapVariable(scene.decoder.Code.SymbolTable.GetAddressFromSymbol("_slotRequests")) : null;
        object oldRequest0 = requestArray != null ? requestArray.GetValue(0) : null, oldRequest1 = requestArray != null ? requestArray.GetValue(1) : null;
        int frame = Time.frameCount; double clock = Time.realtimeSinceStartupAsDouble;
        long nestedStart = scene.observationTicks, before = Stopwatch.GetTimestamp();
        uint result;
        try { result = vm.Interpret(); } finally { depth--; }
        long after = Stopwatch.GetTimestamp(), nested = scene.observationTicks - nestedStart;
        if ((owner.role == "SceneTarget" || owner.role == "SceneState") && label == "OnTSMPVariableReceived")
            ObserveSceneField(scene, owner);
        int callbackSlot = -1, requestAmbiguity = 0; long callbackRequest = -1, requestWaitLower = -1, requestWaitUpper = -1;
        if (decoderEvent)
        {
            object nowRequest0 = requestArray.GetValue(0), nowRequest1 = requestArray.GetValue(1);
            bool changed0 = !ReferenceEquals(oldRequest0, nowRequest0), changed1 = !ReferenceEquals(oldRequest1, nowRequest1);
            if (label == "_onAsyncGpuReadbackComplete")
            {
                if (oldRequest0 != null && changed0) callbackSlot = 0;
                if (oldRequest1 != null && changed1) { if (callbackSlot >= 0) { requestAmbiguity = 1; callbackSlot = -1; } else callbackSlot = 1; }
                if (callbackSlot >= 0 && scene.requestIds[callbackSlot] > 0)
                {
                    callbackRequest = scene.requestIds[callbackSlot];
                    requestWaitLower = before - scene.requestObservedAfter[callbackSlot]; requestWaitUpper = before - scene.requestObservedBefore[callbackSlot];
                }
                else requestAmbiguity = 1;
            }
            if (changed0) TrackSceneRequest(scene, 0, nowRequest0, before, after);
            if (changed1) TrackSceneRequest(scene, 1, nowRequest1, before, after);
        }
        if (record)
        {
            if (scene.eventCount == scene.events.Length) throw new InvalidOperationException("Scene event buffer full");
            scene.events[scene.eventCount++] = new SceneEvent { role = owner.role, label = label, frame = frame, clock = clock, top = top, component = owner.sceneIndex,
                source = owner.role == "Encoder" || owner.role == "SceneSender" ? scene.publicationCount + 1 : scene.publicationCount, received = scene.latestReceived, pendingBefore = pending,
                pendingAfter = scene.decoder.Get<int>("pendingDecodeCount"), start = before, stop = after, nestedHost = nested,
                callbackSlot = callbackSlot, callbackRequest = callbackRequest, requestAmbiguity = requestAmbiguity, requestWaitLowerTicks = requestWaitLower, requestWaitUpperTicks = requestWaitUpper,
                slot0Request = scene.requestIds[0], slot1Request = scene.requestIds[1] };
        }
        scene.observationTicks += before - observationStart + Stopwatch.GetTimestamp() - after;
        return result;
    }
    static void TrackSceneRequest(SceneSession scene, int slot, object request, long before, long after)
    {
        if (request == null) { scene.requestIds[slot] = 0; return; }
        scene.requestIds[slot] = ++scene.nextRequest; scene.requestObservedBefore[slot] = before; scene.requestObservedAfter[slot] = after;
    }
    static void ObserveSceneField(SceneSession scene, Program owner)
    {
        uint frame = scene.decoder.Get<uint>("lastFrameIndex");
        if (frame != scene.applyingFrame)
        {
            if (scene.applyingMask != 0) throw new InvalidOperationException("Incomplete scene field cohort");
            scene.applyingFrame = frame;
        }
        int bit = owner.sceneIndex;
        if (owner.role == "SceneState" && !scene.stateBits.TryGetValue(owner.Get<uint>("lastVariableHash"), out bit))
            throw new InvalidOperationException("Unknown scene state field");
        int flag = 1 << bit;
        if ((scene.applyingMask & flag) != 0) throw new InvalidOperationException("Duplicate field in scene cohort");
        scene.applyingMask |= flag;
        if (scene.applyingMask != (1 << SceneFields) - 1) return;
        int index = scene.applicationCount;
        if (index == scene.applications.Length) throw new InvalidOperationException("Scene application buffer full");
        byte[] payload = scene.decoder.Get<byte[]>("_payloadBytes"); int length = scene.decoder.Get<int>("_payloadDataBytes");
        if (length > PacketStride) throw new InvalidOperationException("Scene received packet exceeds buffer");
        var application = new SceneApplication { id = scene.receiverState.Get<int>("sequence"), frame = Time.frameCount,
            clock = Time.realtimeSinceStartupAsDouble, bytes = length, fields = SceneFields, encodedFrame = frame,
            crc = scene.decoder.Get<bool>("lastPayloadCrcVerified"), seconds = scene.receiverState.Get<float>("seconds"),
            message = scene.receiverState.Get<string>("message") };
        scene.applications[index] = application;
        Array.Copy(payload, 0, scene.receivedPackets, index * PacketStride, length);
        Array.Copy(scene.decoder.Get<byte[]>("_headerBytes"), 0, scene.receivedHeaders, index * 56, 56);
        Array.Copy(scene.receiverState.Get<byte[]>("bytes"), 0, scene.receivedStateBytes, index * 256, 256);
        for (int cube = 0; cube < 12; cube++)
        {
            int offset = index * 12 + cube; Transform target = scene.receiverCubes[cube];
            scene.receivedPositions[offset] = target.localPosition; scene.receivedRotations[offset] = target.localRotation; scene.receivedScales[offset] = target.localScale;
        }
        for (int bone = 0; bone < 51; bone++) scene.receivedBoneRotations[index * 51 + bone] = scene.receivedBones[bone].rotation;
        scene.receivedHips[index] = scene.receivedBones[0].localPosition;
        scene.applicationCount++; scene.latestReceived = application.id; scene.applyingMask = 0;
    }
    static Animator SceneAvatar(string path, bool sender)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Check(prefab != null, "Scene avatar missing: " + path);
        var clone = Keep(Object.Instantiate(prefab)); clone.name = sender ? "Scene source Aoi" : "Scene received Aoi";
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour != null) behaviour.enabled = false;
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; body.detectCollisions = false; }
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var animator = clone.GetComponentInChildren<Animator>(true); Check(animator != null && animator.isHuman && animator.avatar.isValid, "Aoi needs a valid Human Avatar");
        animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = sender;
        return animator;
    }
    static void SceneCubeSetup(SceneSession scene)
    {
        var material = Keep(new PhysicMaterial("Scene rain contact") { dynamicFriction = .5f, staticFriction = .65f, bounciness = .28f, bounceCombine = PhysicMaterialCombine.Maximum });
        var floor = Keep(new GameObject("Scene source rain floor")); floor.transform.position = new Vector3(.2f, -.06f, 0);
        floor.transform.localScale = new Vector3(3.5f, .1f, 2.9f); floor.AddComponent<BoxCollider>().sharedMaterial = material;
        for (int i = 0; i < 12; i++)
        {
            var source = Keep(new GameObject("Scene source cube " + i)); float size = .22f + .02f * (i % 3);
            source.transform.localScale = Vector3.one * size; source.transform.position = new Vector3(.6f + i * .03f, -3, 0);
            source.AddComponent<BoxCollider>().sharedMaterial = material;
            var body = source.AddComponent<Rigidbody>(); body.mass = .8f; body.drag = .025f; body.angularDrag = .08f;
            body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            scene.sourceCubes[i] = source.transform; scene.bodies[i] = body;
            var receiver = Keep(new GameObject("Scene received cube " + i)); receiver.transform.localScale = Vector3.one * size;
            scene.receiverCubes[i] = receiver.transform;
        }
    }
    static void PrepareScene(SceneSession scene, double seconds)
    {
        scene.dance.SetTime(seconds % scene.clip.length); scene.graph.Evaluate(0);
        while (seconds >= scene.releases * .23)
        {
            int ordinal = scene.releases++, i = ordinal % 12, cycle = ordinal / 12; Rigidbody body = scene.bodies[i];
            body.isKinematic = false; body.useGravity = true;
            body.position = new Vector3(.67f + .27f * (ordinal % 4), 2.43f + .1f * (ordinal % 3), -.23f + .23f * ((ordinal / 4) % 3));
            body.rotation = Quaternion.Euler(13 + i * 17, i * 31 + cycle * 13, 9 + i * 23);
            body.velocity = new Vector3((i % 2 == 0 ? 1 : -1) * .045f, -.15f, ((i % 3) - 1) * .035f);
            body.angularVelocity = new Vector3(1.1f + i * .13f, .7f - i * .09f, 1.4f - i * .11f); body.WakeUp();
        }
        int id = scene.publicationCount + 1; scene.senderState.Set("sequence", id); scene.senderState.Set("seconds", (float)seconds);
        uint pattern = unchecked((uint)id * 2654435761u + 2246822519u);
        for (int i = 0; i < 256; i++) scene.stateBytes[i] = PatternByte(ref pattern);
    }
    static void PublishScene(SceneSession scene)
    {
        int index = scene.publicationCount; if (index == scene.capacity - 1) throw new InvalidOperationException("Scene publication buffer full");
        long prep = Stopwatch.GetTimestamp(); double sentTime = Time.realtimeSinceStartupAsDouble;
        PrepareScene(scene, sentTime - scene.start); prep = Stopwatch.GetTimestamp() - prep;
        uint encodedFrame = scene.encoder.Get<uint>("frameIndex");
        scene.encoder.Backing.SendCustomEvent("EncodeNow"); double publishedTime = Time.realtimeSinceStartupAsDouble;
        long snapshot = Stopwatch.GetTimestamp();
        byte[] header = scene.encoder.Get<byte[]>("_headerBytes"); int length = header[42] | (header[43] << 8);
        if (length > PacketStride) throw new InvalidOperationException("Scene source packet exceeds buffer");
        Array.Copy(scene.encoder.Get<byte[]>("_payloadBytes"), 0, scene.sentPackets, index * PacketStride, length);
        Array.Copy(header, 0, scene.sentHeaders, index * 56, 56);
        for (int cube = 0; cube < 12; cube++)
        {
            int offset = index * 12 + cube; Transform target = scene.sourceCubes[cube];
            scene.sentPositions[offset] = target.localPosition; scene.sentRotations[offset] = target.localRotation; scene.sentScales[offset] = target.localScale;
        }
        for (int bone = 0; bone < 51; bone++) scene.sentBoneRotations[index * 51 + bone] = scene.sentBones[bone].rotation;
        scene.sentHips[index] = scene.sentBones[0].localPosition;
        var row = new ScenePublication { id = index + 1, frame = Time.frameCount, bytes = length, variables = scene.encoder.Get<int>("autoVariableCount"),
            encodedFrame = encodedFrame, sent = sentTime, published = publishedTime, preparationTicks = prep, gpu = scene.encoder.Get<bool>("lastFrameUsedGpuLuma4") };
        row.snapshotTicks = Stopwatch.GetTimestamp() - snapshot; scene.publications[index] = row; scene.publicationCount++;
    }
    static void BindScene(Program owner, SceneSession scene, bool sending)
    {
        var targets = new UdonBehaviour[17]; var ids = new ushort[17]; var hashes = new uint[17]; var types = new byte[17]; var names = new string[17];
        for (int i = 0; i < 13; i++)
        {
            targets[i] = (sending ? scene.senders[i] : scene.receivers[i]).Backing; ids[i] = (ushort)(i + 1);
            hashes[i] = StableHash.Fnv1A32(i == 12 ? "humanoid.pose" : "transform.packed"); types[i] = (byte)NetworkFrameProtocol.ValueTypeRawBytes; names[i] = i == 12 ? "poseBytes" : "packedBytes";
        }
        var fields = TransSyncMetadata.GetOrCreate(null, typeof(SceneCostState)).Fields; Check(fields.Length == 4, "Scene state needs four fields");
        for (int i = 0; i < 4; i++)
        {
            int index = 13 + i; targets[index] = (sending ? scene.senderState : scene.receiverState).Backing; ids[index] = 14;
            hashes[index] = fields[i].VariableHash; types[index] = (byte)fields[i].ValueType; names[index] = fields[i].FieldInfo.Name;
            if (sending) scene.stateBits.Add(hashes[index], index);
        }
        owner.Set("bindingTargets", new Component[0]); owner.Set("bindingUdonTargets", targets); owner.Set("bindingNetworkIds", ids);
        owner.Set("bindingVariableHashes", hashes); owner.Set("bindingValueTypes", types); owner.Set("bindingFieldNames", names);
        owner.Set("bindingDirections", new int[17]); owner.Set("bindingPriorities", new int[17]);
        if (sending) { owner.Set("bindingSendOnChange", new bool[17]); owner.Set("bindingMinSendIntervals", new float[17]); }
    }
    static IEnumerator MeasureScene(Case item)
    {
        Check(item.codec == "Luma4" && !item.stress, "Scene mode currently requires clean Luma4");
        Check(item.sourceHz > 0 && item.renderHz > 0 && item.seconds > 0 && item.warmSeconds >= 0, "Invalid scene timing");
        Check(item.safetyMode == 0 || item.safetyMode == 2 || item.safetyMode == 3, "Invalid scene safety mode");
        int programStart = programs.Count, ownedStart = owned.Count;
        var scene = sceneSession = new SceneSession(item); Application.targetFrameRate = item.renderHz;
        scene.sentAnimator = SceneAvatar(item.avatarPath, true); scene.receivedAnimator = SceneAvatar(item.avatarPath, false);
        scene.clip = AssetDatabase.LoadAllAssetsAtPath(item.dancePath).OfType<AnimationClip>().FirstOrDefault(c => c.humanMotion && !c.name.StartsWith("__"));
        Check(scene.clip != null && scene.clip.length > 0, "Scene Human dance clip missing");
        scene.graph = PlayableGraph.Create("Scene source Human dance"); scene.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        scene.dance = AnimationClipPlayable.Create(scene.graph, scene.clip); scene.dance.SetApplyFootIK(false); scene.dance.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(scene.graph, "Scene source Animator", scene.sentAnimator).SetSourcePlayable(scene.dance); scene.graph.Play(); scene.graph.Evaluate(0);
        SceneCubeSetup(scene);
        for (int i = 0; i < 13; i++)
        {
            string path = "Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetwork" + (i == 12 ? "HumanoidPoseSync" : "TransformSync") + ".asset";
            var sender = scene.senders[i] = new Program(path, role: "SceneSender"); var receiver = scene.receivers[i] = new Program(path, role: "SceneTarget"); sender.sceneIndex = receiver.sceneIndex = i;
            sender.Set("networkId", (ushort)(i + 1)); receiver.Set("networkId", (ushort)(i + 1)); sender.Set("sendMode", (int)SendMode.Always); receiver.Set("receiveInterpolation", (int)ReceiveInterpolationMode.Discrete);
            if (i < 12)
            {
                sender.Set("target", scene.sourceCubes[i]); receiver.Set("target", scene.receiverCubes[i]); sender.Set("syncRigidbody", false); receiver.Set("syncRigidbody", false);
                sender.Set("targetRigidbody", scene.bodies[i]); sender.Set("compressionMode", (int)CompressionMode.RotOnly); receiver.Set("compressionMode", (int)CompressionMode.RotOnly);
            }
            else
            {
                scene.senderHumanoid = sender; scene.receiverHumanoid = receiver; sender.Set("animator", scene.sentAnimator); receiver.Set("animator", scene.receivedAnimator);
                sender.Set("includeFingerBones", true); receiver.Set("includeFingerBones", true); sender.Call("ResolveBones"); receiver.Call("ResolveBones");
                int[] ids = sender.Get<int[]>("boneIds");
                scene.sentBones = ids.Select(id => scene.sentAnimator.GetBoneTransform((HumanBodyBones)id)).Where(b => b != null).ToArray();
                scene.receivedBones = ids.Select(id => scene.receivedAnimator.GetBoneTransform((HumanBodyBones)id)).Where(b => b != null).ToArray();
                Check(sender.Get<int>("validBoneCount") == 51 && receiver.Get<int>("validBoneCount") == 51 && scene.sentBones.Length == 51 && scene.receivedBones.Length == 51, "Aoi TSMP mapping must contain exactly 51 bones");
                notes.Add("Scene " + item.name + " Human bone IDs=" + string.Join("/", ids));
            }
        }
        scene.senderState = new Program(SceneStatePath, role: "SceneSender"); scene.senderState.sceneIndex = 13;
        scene.receiverState = new Program(SceneStatePath, role: "SceneState"); scene.receiverState.sceneIndex = 13;
        scene.senderState.Set("networkId", (ushort)14); scene.receiverState.Set("networkId", (ushort)14); scene.senderState.Set("sendMode", (int)SendMode.Always); scene.senderState.Set("bytes", scene.stateBytes);
        scene.senderState.Set("message", "HELLO TSMP");
        var source = Target(640, 360); var output = Target(2048, 1); var native = CloneCodec("Luma4"); WarmGraphics(native, source, output);
        var codec = new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", native);
        scene.encoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset", role: "Encoder");
        scene.encoder.Set("output", source); scene.encoder.Set("autoEncode", false); scene.encoder.Set("debugLog", false); scene.encoder.Set("sampleSize", 4); scene.encoder.Set("frameIndex", 1u);
        scene.encoder.Set("selectedCodecUdonTarget", codec.Backing); scene.encoder.Set("useGpuLuma4", true); scene.encoder.Set("luma4EncodeMaterial", Keep(new Material(Resources.Load<Material>("TSMPEncodeLuma4"))));
        scene.encoder.Set("blockExpandMaterial", Keep(new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"))));
        scene.encoder.Set("transSyncRefreshInterval", 1f); scene.encoder.Set("targetPayloadBytes", 0); BindScene(scene.encoder, scene, true); scene.encoder.Call("_onEnable");
        scene.decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset", role: "Decoder");
        scene.decoder.Set<Texture>("sourceTexture", source); scene.decoder.Set("payloadByteTexture", output); scene.decoder.Set("readbackPackMaterial", Resources.Load<Material>("TSMPReadbackPack"));
        scene.decoder.Set("codecHandlers", new[] { codec.Backing }); BindScene(scene.decoder, scene, false);
        scene.decoder.Set("applyEveryFrame", true); scene.decoder.Set("skipDuplicateFrames", true); scene.decoder.Set("requirePayloadCrc32", true); scene.decoder.Set("debugLog", false); scene.decoder.Set("flipY", true);
        scene.decoder.Set("recoverHeaderCrc", false); scene.decoder.Set("maxCrcRecoveryAttempts", item.budget); scene.decoder.Set("usePredictedReadback", true); scene.decoder.Set("useCombinedByteOutput", true); scene.decoder.Set("overlapReadbacks", true);
        scene.decoder.Set("decodeSafetyMode", item.safetyMode); scene.decoder.Call("_onEnable");
        scene.start = Time.realtimeSinceStartupAsDouble; PublishScene(scene); scene.decoder.Call("DecodeNow");
        double timeout = Time.realtimeSinceStartupAsDouble + 30;
        while (scene.decoder.Get<bool>("readbackInFlight") && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
        Check(!scene.decoder.Get<bool>("readbackInFlight") && scene.decoder.Get<bool>("lastFrameValid"), "Scene preflight failed: " + scene.decoder.Get<string>("lastError"));
        Check(item.safetyMode != 0 || scene.applicationCount == 1, "Scene preflight must apply one full frame");
        Check(scene.encoder.Get<int>("autoVariableCount") == 17 && scene.senderHumanoid.Get<int>("encodedPoseBytes") == 327, "Scene field/bone payload mismatch");
        scene.begin = Time.realtimeSinceStartupAsDouble + item.warmSeconds; scene.end = scene.begin + item.seconds; scene.due = Time.realtimeSinceStartupAsDouble; scene.recording = true;
        step = () =>
        {
            long started = Stopwatch.GetTimestamp(); double now = Time.realtimeSinceStartupAsDouble; long prep = 0, snapshot = 0;
            if (now >= scene.due)
            {
                int slots = Math.Max(1, (int)Math.Floor((now - scene.due) * item.sourceHz) + 1); if (now >= scene.begin && now < scene.end) scene.deadlineMisses += slots - 1;
                scene.due += slots / (double)item.sourceHz; PublishScene(scene); var publication = scene.publications[scene.publicationCount - 1]; prep = publication.preparationTicks; snapshot = publication.snapshotTicks;
            }
            scene.decoder.Backing.SendCustomEvent("_update");
            if (scene.frameCount == scene.frames.Length) throw new InvalidOperationException("Scene frame buffer full");
            scene.frames[scene.frameCount++] = new SceneFrame { frame = Time.frameCount, source = scene.publicationCount, received = scene.latestReceived, clock = now,
                ageClock = Time.realtimeSinceStartupAsDouble, pending = scene.decoder.Get<int>("pendingDecodeCount"), gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
                preparationTicks = prep, snapshotTicks = snapshot, stepTicks = Stopwatch.GetTimestamp() - started };
        };
        while (Time.realtimeSinceStartupAsDouble < scene.end + 1) yield return null;
        step = null; scene.recording = false; scene.decoder.Set("applyEveryFrame", false);
        timeout = Time.realtimeSinceStartupAsDouble + 10;
        while (scene.decoder.Get<bool>("readbackInFlight") && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
        Check(!scene.decoder.Get<bool>("readbackInFlight"), "Scene drain timeout"); VerifyScene(scene); FlushScene();
        scene.decoder.Call("_onDisable"); scene.encoder.Call("_onDisable"); if (scene.graph.IsValid()) scene.graph.Destroy(); sceneSession = null;
        foreach (var program in programs.Skip(programStart).ToArray()) { program.Dispose(); programs.Remove(program); }
        foreach (var value in owned.Skip(ownedStart).Reverse().ToArray()) { if (value is RenderTexture target) target.Release(); if (value != null) Object.DestroyImmediate(value); owned.Remove(value); }
        notes.Add("PASS scene " + item.name + "; publications=" + scene.publicationCount + "; complete 17-field applications=" + scene.applicationCount + "; source physics releases=" + scene.releases);
        UnityEngine.Debug.Log("Sustained scene complete " + item.name);
    }
    static void VerifyScene(SceneSession scene)
    {
        Check(scene.applyingMask == 0, "Scene partial final application");
        foreach (var program in programs) Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(program.Backing), "Scene Udon program failed");
        Check(scene.publicationCount > 1 && scene.releases >= 12, "Scene continuous source did not run");
        for (int cube = 0; cube < 12; cube++) if (!scene.bodies[cube].isKinematic && scene.bodies[cube].useGravity) scene.finalDynamicBodies++;
        for (int i = 0; i < scene.publicationCount; i++)
        {
            var p = scene.publications[i]; Check(p.variables == 17 && p.gpu && p.encodedFrame == (uint)p.id, "Scene publication schema/GPU/frame mismatch");
            for (int bone = 0; bone < 51; bone++) scene.maximumDanceChangeDegrees = Mathf.Max(scene.maximumDanceChangeDegrees, Quaternion.Angle(scene.sentBoneRotations[bone], scene.sentBoneRotations[i * 51 + bone]));
            for (int cube = 0; cube < 12; cube++) scene.maximumCubeTravel = Mathf.Max(scene.maximumCubeTravel, Vector3.Distance(scene.sentPositions[cube], scene.sentPositions[i * 12 + cube]));
            if (i > 0 && scene.sentPositions[i * 12].y < scene.sentPositions[(i - 1) * 12].y - .001f) scene.descendingCube0Samples++;
        }
        Check(scene.maximumDanceChangeDegrees > 1 && scene.maximumCubeTravel > .5f && scene.descendingCube0Samples > 5 && scene.finalDynamicBodies == 12, "Scene source animation/physics motion absent");
        if (scene.item.safetyMode != 0) { Check(scene.applicationCount == 0, "Nonapply control unexpectedly applied targets"); return; }
        Check(scene.applicationCount > 1, "Scene no sustained applications"); int previous = 0;
        for (int i = 0; i < scene.applicationCount; i++)
        {
            var a = scene.applications[i]; Check(a.id > previous && a.id <= scene.publicationCount, "Scene duplicate or out-of-order application"); previous = a.id;
            int source = a.id - 1; var p = scene.publications[source];
            Check(a.crc && a.fields == 17 && a.encodedFrame == p.encodedFrame && a.bytes == p.bytes, "Scene header/CRC/cohort mismatch");
            for (int b = 0; b < a.bytes; b++) Check(scene.receivedPackets[i * PacketStride + b] == scene.sentPackets[source * PacketStride + b], "Scene protected payload mismatch");
            for (int b = 0; b < 56; b++) Check(scene.receivedHeaders[i * 56 + b] == scene.sentHeaders[source * 56 + b], "Scene protected header mismatch");
            Check(a.message == "HELLO TSMP" && Math.Abs(a.seconds - (float)(p.sent - scene.start)) < .00001f, "Scene string/seconds state mismatch");
            uint pattern = unchecked((uint)a.id * 2654435761u + 2246822519u);
            for (int b = 0; b < 256; b++) Check(scene.receivedStateBytes[i * 256 + b] == PatternByte(ref pattern), "Scene byte field mismatch");
            for (int cube = 0; cube < 12; cube++)
            {
                int s = source * 12 + cube, r = i * 12 + cube;
                Check(Vector3.Distance(scene.sentPositions[s], scene.receivedPositions[r]) < .002f, "Scene received cube position mismatch");
                Check(Quaternion.Angle(scene.sentRotations[s], scene.receivedRotations[r]) < .6f, "Scene received cube rotation mismatch");
                Check(Vector3.Distance(scene.sentScales[s], scene.receivedScales[r]) < .002f, "Scene received cube scale mismatch");
            }
            for (int bone = 0; bone < 51; bone++) Check(Quaternion.Angle(scene.sentBoneRotations[source * 51 + bone], scene.receivedBoneRotations[i * 51 + bone]) < .6f, "Scene received Human world rotation mismatch");
            Check(Vector3.Distance(scene.sentHips[source], scene.receivedHips[i]) < .002f, "Scene received Human hips position mismatch");
        }
    }
    static void FlushScene()
    {
        var scene = sceneSession; if (scene == null || scene.flushed) return; scene.flushed = true;
        string prefix = Path.Combine(Root, scene.item.name + "-scene-");
        using (var writer = new StreamWriter(prefix + "vm.csv"))
        {
            writer.WriteLine("role,event,frame,clock,source,received,pendingBefore,pendingAfter,startTicks,endTicks,netVmMs,grossVmMs,nestedHostMs,top,component,callbackSlot,callbackRequest,requestWaitLowerMs,requestWaitUpperMs,requestAmbiguity,slot0Request,slot1Request");
            for (int i = 0; i < scene.eventCount; i++) { var e = scene.events[i]; writer.WriteLine(Row(e.role, e.label, e.frame, e.clock, e.source, e.received, e.pendingBefore, e.pendingAfter, e.start, e.stop, Ms(e.stop - e.start - e.nestedHost), Ms(e.stop - e.start), Ms(e.nestedHost), e.top, e.component, e.callbackSlot, e.callbackRequest, e.requestWaitLowerTicks >= 0 ? Ms(e.requestWaitLowerTicks) : -1, e.requestWaitUpperTicks >= 0 ? Ms(e.requestWaitUpperTicks) : -1, e.requestAmbiguity, e.slot0Request, e.slot1Request)); }
        }
        using (var writer = new StreamWriter(prefix + "frames.csv"))
        {
            writer.WriteLine("frame,clock,measured,source,received,ageMs,pending,gc0,gc1,gc2,stepMs,preparationMs,sourceSnapshotMs,ageSampleClock,receivedSentClock");
            for (int i = 0; i < scene.frameCount; i++) { var f = scene.frames[i]; double sentTime = f.received > 0 ? scene.publications[f.received - 1].sent : -1; writer.WriteLine(Row(f.frame, f.clock, f.clock >= scene.begin && f.clock < scene.end, f.source, f.received, sentTime >= 0 ? (f.ageClock - sentTime) * 1000 : -1, f.pending, f.gc0, f.gc1, f.gc2, Ms(f.stepTicks), Ms(f.preparationTicks), Ms(f.snapshotTicks), f.ageClock, sentTime)); }
        }
        using (var writer = new StreamWriter(prefix + "publications.csv"))
        {
            writer.WriteLine("id,frame,sent,published,measured,encodedFrame,bytes,variables,gpu,preparationMs,snapshotMs");
            for (int i = 0; i < scene.publicationCount; i++) { var p = scene.publications[i]; writer.WriteLine(Row(p.id, p.frame, p.sent, p.published, p.sent >= scene.begin && p.sent < scene.end, p.encodedFrame, p.bytes, p.variables, p.gpu, Ms(p.preparationTicks), Ms(p.snapshotTicks))); }
        }
        using (var writer = new StreamWriter(prefix + "applications.csv"))
        {
            writer.WriteLine("ordinal,id,frame,applied,sent,measuredSource,latencyMs,encodedFrame,bytes,fields,crc");
            for (int i = 0; i < scene.applicationCount; i++) { var a = scene.applications[i]; double sentTime = a.id > 0 && a.id <= scene.publicationCount ? scene.publications[a.id - 1].sent : -1; writer.WriteLine(Row(i, a.id, a.frame, a.clock, sentTime, sentTime >= scene.begin && sentTime < scene.end, (a.clock - sentTime) * 1000, a.encodedFrame, a.bytes, a.fields, a.crc)); }
        }
        using (var writer = new BinaryWriter(File.Open(prefix + "packets.bin", FileMode.Create)))
        {
            writer.Write(1); writer.Write(scene.publicationCount); writer.Write(scene.applicationCount);
            for (int i = 0; i < scene.publicationCount; i++) { var p = scene.publications[i]; writer.Write(p.id); writer.Write(p.bytes); writer.Write(scene.sentHeaders, i * 56, 56); writer.Write(scene.sentPackets, i * PacketStride, p.bytes); }
            for (int i = 0; i < scene.applicationCount; i++) { var a = scene.applications[i]; writer.Write(a.id); writer.Write(a.bytes); writer.Write(scene.receivedHeaders, i * 56, 56); writer.Write(scene.receivedPackets, i * PacketStride, a.bytes); }
        }
        File.WriteAllLines(prefix + "scope.txt", new[] {
            "begin=" + scene.begin.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "end=" + scene.end.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "warmSeconds=" + scene.item.warmSeconds, "sourceTargetHz=" + scene.item.sourceHz, "renderTargetHz=" + scene.item.renderHz, "senderDeadlineMisses=" + scene.deadlineMisses,
            "safetyMode=" + scene.item.safetyMode, "sourcePhysicsReleases=" + scene.releases,
            "sourceMaxBoneChangeDegrees=" + scene.maximumDanceChangeDegrees, "sourceMaxCubeTravel=" + scene.maximumCubeTravel,
            "sourceCube0DescendingSamples=" + scene.descendingCube0Samples, "sourceFinalDynamicGravityBodies=" + scene.finalDynamicBodies,
            "counterWholeSession.skippedBusyDecodeCount=" + (scene.decoder != null ? scene.decoder.Get<int>("skippedBusyDecodeCount") : -1),
            "counterWholeSession.skippedDuplicateFrameCount=" + (scene.decoder != null ? scene.decoder.Get<int>("skippedDuplicateFrameCount") : -1),
            "counterWholeSession.skippedOutOfOrderFrameCount=" + (scene.decoder != null ? scene.decoder.Get<int>("skippedOutOfOrderFrameCount") : -1),
            "counterWholeSession.predictedReadbackCount=" + (scene.decoder != null ? scene.decoder.Get<int>("predictedReadbackCount") : -1),
            "counterWholeSession.predictionFallbackCount=" + (scene.decoder != null ? scene.decoder.Get<int>("predictionFallbackCount") : -1),
            "counterWholeSession.combinedByteOutputCount=" + (scene.decoder != null ? scene.decoder.Get<int>("combinedByteOutputCount") : -1),
            "counterWholeSession.headerRecoveryAttemptCount=" + (scene.decoder != null ? scene.decoder.Get<int>("headerRecoveryAttemptCount") : -1),
            "counterWholeSession.payloadRecoveryAttemptCount=" + (scene.decoder != null ? scene.decoder.Get<int>("payloadRecoveryAttemptCount") : -1),
            "counterWholeSession.recoveredHeaderCount=" + (scene.decoder != null ? scene.decoder.Get<int>("recoveredHeaderCount") : -1),
            "counterWholeSession.recoveredPayloadCount=" + (scene.decoder != null ? scene.decoder.Get<int>("recoveredPayloadCount") : -1),
            "Aoi 51 transmitted bones, fingers included, eyes excluded; 12 RotOnly local Transform fields without Rigidbody velocity + 4 state fields; 17 fields total.",
            "Source encoded frame texture 640x360, Luma4 sampleSize4/blockSize8; byte output2048x1. Application timestamp is complete-field application before Decoder's final return/cleanup.",
            "Real compiled client Udon Encoder, GPU Luma4 texture, actual VRC async readbacks, compiled Decoder and receiver Transform/Humanoid application. No byte injection.",
            "Native source Playables and twelve Rigidbody/BoxCollider objects prepare motion; preparation is separate from net VM events. Receiver Animator disabled and receiver cubes have no physics.",
            "preparationMs includes manual Playables evaluation, state update and cube recycling. Unity FixedUpdate physics simulation occurs outside VM calls and is not individually timed.",
            "All avatar MonoBehaviours (including VRCPhysBones), all Renderers and avatar colliders disabled. No skinning, skirt/clothing physics, drawing, recording, VRChat/CDN or FPS claim.",
            "Each receive sample is taken after all 17 actual OnTSMPVariableReceived callbacks within one protected frame. Field callback observation overhead is measured and subtracted from net VM time; gross VM time is also retained.",
            "Event/pose/packet/frame buffers allocated before preflight. CSV and binary output, payload equality and pose checks occur after drain. Source-age sample uses applied sequence's original source timestamp.",
            "Only top=True Encoder/Decoder events may be summed for total VM CPU. Nested SceneSender TSMPBeforeEncode and SceneTarget/SceneState receive callbacks are component breakdowns included in their parent. Component0..11=cubes,12=Human,13=state.",
            "Request wait bounds are callback Interpret start minus the issuing Interpret end/start, when exactly one old request reference is observed replaced in that callback. They describe observed request-to-callback wall wait, not GPU execution. requestAmbiguity=1 excludes unproven associations. Slot request IDs identify request objects, not accepted packet/source identity.",
            "counterWholeSession values include preflight, warmup, measurement, tail and drain. Decoder exposes no aggregate corrupt-frame or accepted-admission counter; absent source sequences alone do not identify the skip/reject cause.",
            "Nonapply safety controls, when selected, have no application latency/age result. Binary packet archive: little endian version1, sourceCount, rxCount; each source then rx entry int32 id,int32 length,56 header bytes,length payload bytes."
        });
    }
}
#endif
