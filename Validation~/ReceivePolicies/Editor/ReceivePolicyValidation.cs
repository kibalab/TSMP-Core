using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
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

public static class ReceivePolicyValidation
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Root = "Assets/Validation/ReceivePolicies/";
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunCases();
        Finish(false);
    }

    private static void RunCases()
    {
        Test("None ignores scalar and string fields, hash state and callbacks", Scalar);
        string[] fields = { "bytes", "bools", "ints", "floats", "vectors2", "vectors3", "rotations", "strings" };
        for (int i = 0; i < fields.Length; i++)
        {
            int index = i;
            Test("None preserves reused " + fields[i] + " across mode and length changes", () => Arrays(ArrayCacheCases.Types[index], fields[index]));
        }
        Test("A non-network component without receiveInterpolation still receives", Plain);
        Test("None does not suppress outgoing TransSync encoding", SendOnly);
        foreach (var mode in new[] { ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous })
        foreach (bool sync in new[] { false, true })
        foreach (bool local in new[] { false, true })
            Test("Rigidbody mode=" + mode + ", sync=" + sync + ", local=" + local, () => RigidbodyReception(mode, sync, local));
        foreach (var mode in new[] { ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous })
            Test("Disabling Rigidbody clears pending targets while " + mode, () => CancelRigidbodyTarget(mode));
        Test("Missing Rigidbody still receives Transform data", MissingRigidbody);
        Test("Transform receive reset clears targets and snaps the next applicable packet", TransformReceiveReset);
        Test("Humanoid receive reset clears targets and snaps the next applicable packet", HumanoidReceiveReset);
        Test("Fixed rig refreshes both hands then alternates by receipt", FixedRigHands);
        Test("BlendShape receive reset clears targets and snaps the next applicable packet", BlendShapePolicyCases.Reset);
        foreach (string change in BlendShapePolicyCases.Changes)
            Test("BlendShape target invalidation: " + change, () => BlendShapePolicyCases.Run(change));
        foreach (string selection in AnimatorPolicyCases.SelectionCases)
            Test("Animator layer selection: " + selection, () => AnimatorPolicyCases.Selection(selection));
        foreach (string test in AnimatorPolicyCases.TimeCases)
            Test("Animator time: " + test, () => AnimatorPolicyCases.Time(test));
        Test("Sent event follows successful output of its own field", SendCommitCases.Notifications);
#if UDONSHARP
        Test("Avatar root delta and keepalive commit only after output", SendCommitCases.Avatar);
#endif
    }

    internal sealed class Endpoint : IDisposable
    {
        public readonly Component Target;
#if UDONSHARP
        private readonly IUdonProgram program;
        private readonly IUdonVM vm;
#endif
        public Endpoint(Type type)
        {
#if UDONSHARP
            string path = type == typeof(TSMPEncoder) ? "Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset" :
                type.Assembly == typeof(TSMPEncoder).Assembly && typeof(TSMPNetworkBehaviour).IsAssignableFrom(type)
                    ? "Packages/com.kibalab.tsmp.core/Runtime/Network/" + type.Name + ".asset" : Root + type.Name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            program = asset.SerializedProgramAsset.RetrieveProgram();
            Check(program != null && program.ByteCode.Length > 0, "Missing bytecode: " + path);
            vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            var backing = new GameObject(type.Name).AddComponent<UdonBehaviour>();
            Target = backing;
            Check((bool)typeof(UdonBehaviour).GetMethod("ResolveUdonHeapReferences", Fields).Invoke(backing, new object[] { program.SymbolTable, program.Heap }), "Unresolved heap references");
            typeof(UdonBehaviour).GetField("_program", Fields).SetValue(backing, program);
            typeof(UdonBehaviour).GetField("_udonVM", Fields).SetValue(backing, vm);
            typeof(UdonBehaviour).GetField("_udonManager", Fields).SetValue(backing, UdonManager.Instance);
#else
            Target = new GameObject(type.Name).AddComponent(type);
#endif
        }

        public void Set(string name, object value)
        {
#if UDONSHARP
            uint address = program.SymbolTable.GetAddressFromSymbol(name);
            if (value != null && value.GetType().IsEnum) value = Convert.ToInt32(value);
            program.Heap.SetHeapVariable(address, value, program.Heap.GetHeapVariableType(address));
#else
            Target.GetType().GetField(name, Fields).SetValue(Target, value);
#endif
        }
        public T Get<T>(string name)
        {
#if UDONSHARP
            return (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
#else
            return (T)Target.GetType().GetField(name, Fields).GetValue(Target);
#endif
        }
        public void Call(string name)
        {
#if UDONSHARP
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name));
            Check(vm.Interpret() == 0, "Udon event failed: " + name);
#else
            Target.GetType().GetMethod(name, Fields).Invoke(Target, null);
#endif
        }
        public void Dispose() => Object.DestroyImmediate(Target.gameObject);
    }

    private static void Apply(Endpoint driver, Endpoint target, string field, int type, object value, int expected)
    {
        var payload = new byte[4096];
        int end = NetworkValueWriter.WriteObject(payload, 0, type, value);
        Check(end >= 0, "Fixture encoding failed");
        Array.Resize(ref payload, end);
        driver.Set("receiver", target.Target);
        driver.Set("fieldName", field);
        driver.Set("valueType", type);
        driver.Set("payload", payload);
        driver.Call("ApplyReceivedValue");
        Check(driver.Get<int>("appliedCount") == expected, "Unexpected applied count");
        Check(driver.Get<int>("rejectedCount") == 0, "Unexpected rejected value type");
    }

    private static void Scalar()
    {
        using (var driver = new Endpoint(typeof(ReceivePolicyProbe)))
        using (var target = new Endpoint(typeof(ReceivePolicyProbe)))
        {
            target.Set("receiveInterpolation", ReceiveInterpolationMode.None);
            target.Set("value", 17);
            target.Set("text", "local");
            target.Set("lastVariableHash", 777u);
            Apply(driver, target, "value", (int)NetworkValueType.Int32, 29, 0);
            Apply(driver, target, "text", (int)NetworkValueType.UTF8String, "remote", 0);
            Check(target.Get<int>("value") == 17 && target.Get<string>("text") == "local", "None overwrote source fields");
            Check(target.Get<uint>("lastVariableHash") == 777 && target.Get<int>("notifications") == 0, "None changed notification state");
            foreach (var mode in new[] { ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous })
            {
                target.Set("receiveInterpolation", mode);
                Apply(driver, target, "value", (int)NetworkValueType.Int32, (int)mode, 1);
                Check(target.Get<int>("value") == (int)mode && target.Get<uint>("lastVariableHash") == 100, "Enabled mode did not receive");
            }
            Check(target.Get<int>("notifications") == 2, "Incorrect callback count");
        }
    }

    private static void Arrays(int type, string field)
    {
        using (var driver = new Endpoint(typeof(ReceivePolicyProbe)))
        using (var target = new Endpoint(typeof(ReceivePolicyProbe)))
        {
            Array first = ArrayCacheCases.CreateValue(type, 2, 1);
            Apply(driver, target, field, type, first, 1);
            Array received = target.Get<Array>(field);
            ArrayCacheCases.AssertEqual(first, received);
            foreach (int count in new[] { 2, 3, 0 })
            {
                target.Set("receiveInterpolation", ReceiveInterpolationMode.None);
                target.Set("lastVariableHash", 777u);
                Apply(driver, target, field, type, ArrayCacheCases.CreateValue(type, count, 2), 0);
                Check(ReferenceEquals(received, target.Get<Array>(field)), "None replaced the previous array");
                ArrayCacheCases.AssertEqual(first, received);
                Check(target.Get<uint>("lastVariableHash") == 777 && target.Get<int>("notifications") == 1, "None changed callback state");
            }
            foreach (var mode in new[] { ReceiveInterpolationMode.Continuous, ReceiveInterpolationMode.Discrete })
            {
                target.Set("receiveInterpolation", mode);
                Array next = ArrayCacheCases.CreateValue(type, 2, (int)mode + 5);
                Apply(driver, target, field, type, next, 1);
                Check(ReferenceEquals(received, target.Get<Array>(field)), "Enabled mode lost its reusable array");
                ArrayCacheCases.AssertEqual(next, received);
                target.Set("receiveInterpolation", ReceiveInterpolationMode.None);
                Apply(driver, target, field, type, first, 0);
                ArrayCacheCases.AssertEqual(next, received);
            }
            Check(target.Get<int>("notifications") == 3, "Incorrect resumed callback count");
        }
    }

    private static void Plain()
    {
        using (var driver = new Endpoint(typeof(ReceivePolicyProbe)))
        using (var target = new Endpoint(typeof(PlainReceiveProbe)))
        {
            Apply(driver, target, "value", (int)NetworkValueType.Int32, 42, 1);
            Check(target.Get<int>("value") == 42 && target.Get<uint>("lastVariableHash") == 100 && target.Get<int>("notifications") == 1,
                "Ordinary component binding was blocked");
        }
    }

    private static void SendOnly()
    {
        using (var encoder = new Endpoint(typeof(TSMPEncoder)))
        using (var source = new Endpoint(typeof(ReceivePolicyProbe)))
        {
            var output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
            output.Create();
#if !UDONSHARP
            var codec = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
#endif
            try
            {
                source.Set("receiveInterpolation", ReceiveInterpolationMode.None);
                source.Set("networkId", (ushort)1);
                source.Set("value", 42);
                encoder.Set("output", output);
                encoder.Set("autoEncode", false);
                encoder.Set("clearAfterEncode", false);
                encoder.Set("debugLog", false);
#if UDONSHARP
                encoder.Set("bindingUdonTargets", new[] { (UdonBehaviour)source.Target });
                encoder.Set("bindingTargets", new Component[0]);
#else
                encoder.Set("bindingTargets", new[] { source.Target });
                encoder.Set("selectedCodec", codec.GetComponent<TSMPCodec>());
#endif
                encoder.Set("bindingNetworkIds", new ushort[] { 1 });
                encoder.Set("bindingVariableHashes", new uint[] { 100 });
                encoder.Set("bindingFieldNames", new[] { "value" });
                encoder.Set("bindingValueTypes", new[] { (byte)NetworkValueType.Int32 });
                encoder.Set("bindingDirections", new int[1]);
#if UDONSHARP
                encoder.Call("_onEnable");
#endif
                encoder.Call("EncodeNow");
                Check(encoder.Get<uint>("frameIndex") == 1 && encoder.Get<int>("variableMessageCount") == 1,
                    "None suppressed encoding: " + encoder.Get<string>("lastError"));
#if UDONSHARP
                byte[] bytes = encoder.Get<byte[]>("_payloadBytes");
#else
                byte[] bytes = encoder.Get<byte[]>("_encodedPayload");
#endif
                Check(Binary.ReadInt32LE(bytes, NetworkFrameProtocol.NetworkHeaderBytes + 8 + 2 + 7) == 42,
                    "Encoded field value differs");
            }
            finally
            {
#if UDONSHARP
                var texture = encoder.Get<Texture2D>("outputTexture");
                if (texture != null) Object.DestroyImmediate(texture);
#else
                Object.DestroyImmediate(codec);
#endif
                if (RenderTexture.active == output) RenderTexture.active = null;
                output.Release();
                Object.DestroyImmediate(output);
            }
        }
    }

    private static byte[] TransformPacket()
    {
        using (var sender = new Endpoint(typeof(TSMPNetworkTransformSync)))
        {
            sender.Target.transform.position = new Vector3(3, 4, 5);
            sender.Target.transform.rotation = Quaternion.Euler(10, 20, 30);
            sender.Target.transform.localScale = new Vector3(1.2f, 1.3f, 1.4f);
            Rigidbody body = sender.Target.gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = new Vector3(2, 3, 4);
            body.angularVelocity = new Vector3(.2f, .3f, .4f);
            sender.Set("target", body.transform);
            sender.Set("targetRigidbody", body);
            sender.Set("useLocalSpace", false);
            sender.Set("syncRigidbody", true);
            sender.Set("compressionMode", CompressionMode.Off);
            sender.Call("TSMPBeforeEncode");
            byte[] packet = (byte[])sender.Get<byte[]>("packedBytes").Clone();
            Check(packet.Length == 66 && (packet[1] & 192) == 192, "Rigidbody capture fixture is incomplete");
            Check(Binary.ReadVector3Float32LE(packet, 2) == new Vector3(3, 4, 5), "Capture fixture has not synchronized its Transform");
            return packet;
        }
    }

    private static Rigidbody CreateBody(Endpoint receiver)
    {
        Rigidbody body = receiver.Target.gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = 3;
        body.velocity = new Vector3(7, 8, 9);
        body.angularVelocity = new Vector3(1, 2, 3);
        receiver.Set("target", body.transform);
        receiver.Set("targetRigidbody", body);
        receiver.Set("continuousInterpolationRate", 0f);
        return body;
    }

    private static void TickTransform(Endpoint receiver)
    {
#if UDONSHARP
        receiver.Call("_postLateUpdate");
#else
        receiver.Call("LateUpdate");
#endif
    }

    private static void RigidbodyReception(ReceiveInterpolationMode mode, bool sync, bool local)
    {
        byte[] packet = TransformPacket();
        using (var receiver = new Endpoint(typeof(TSMPNetworkTransformSync)))
        {
            var parent = new GameObject("Receiver parent");
            try
            {
                parent.transform.position = new Vector3(10, 20, 30);
                parent.transform.rotation = Quaternion.Euler(0, 30, 0);
                parent.transform.localScale = Vector3.one * 2;
                receiver.Target.transform.SetParent(parent.transform, false);
                Rigidbody body = CreateBody(receiver);
                receiver.Set("receiveInterpolation", mode);
                receiver.Set("syncRigidbody", sync);
                receiver.Set("useLocalSpace", local);
                receiver.Set("packedBytes", packet);
                receiver.Call("OnTSMPVariableReceived");
                if (mode == ReceiveInterpolationMode.Continuous) TickTransform(receiver);
                Vector3 expectedPosition = new Vector3(3, 4, 5);
                Quaternion expectedRotation = Quaternion.Euler(10, 20, 30);
                if (local)
                {
                    expectedPosition = parent.transform.TransformPoint(expectedPosition);
                    expectedRotation = parent.transform.rotation * expectedRotation;
                }
                Vector3 actualPosition = sync ? body.position : body.transform.position;
                Quaternion actualRotation = sync ? body.rotation : body.transform.rotation;
                Check(Vector3.Distance(actualPosition, expectedPosition) < .001f, "Transform position stopped syncing");
                Check(Quaternion.Angle(actualRotation, expectedRotation) < .05f, "Transform rotation stopped syncing");
                Check(Vector3.Distance(body.transform.localScale, new Vector3(1.2f, 1.3f, 1.4f)) < .001f, "Transform scale stopped syncing");
                Check(Vector3.Distance(body.velocity, sync ? new Vector3(2, 3, 4) : new Vector3(7, 8, 9)) < .001f, "Velocity ignored Sync Rigidbody");
                Check(Vector3.Distance(body.angularVelocity, sync ? new Vector3(.2f, .3f, .4f) : new Vector3(1, 2, 3)) < .001f, "Angular velocity ignored Sync Rigidbody");
                Check(!body.useGravity && !body.isKinematic && body.mass == 3, "Local-only physics settings changed");
            }
            finally
            {
                receiver.Target.transform.SetParent(null, true);
                Object.DestroyImmediate(parent);
            }
        }
    }

    private static void CancelRigidbodyTarget(ReceiveInterpolationMode disabledMode)
    {
        byte[] packet = TransformPacket();
        using (var receiver = new Endpoint(typeof(TSMPNetworkTransformSync)))
        {
            Rigidbody body = CreateBody(receiver);
            receiver.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
            receiver.Set("packedBytes", packet);
            receiver.Call("OnTSMPVariableReceived");
            receiver.Set("syncRigidbody", false);
            receiver.Set("receiveInterpolation", disabledMode);
            TickTransform(receiver);
            Check(!receiver.Get<bool>("_continuousHasRigidbodyVelocity") && !receiver.Get<bool>("_continuousHasRigidbodyAngularVelocity"), "Disabled physics targets were retained");
            Check(body.velocity == new Vector3(7, 8, 9) && body.angularVelocity == new Vector3(1, 2, 3), "Disabled pending target changed physics");
            receiver.Set("syncRigidbody", true);
            receiver.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
            TickTransform(receiver);
            Check(body.velocity == new Vector3(7, 8, 9) && body.angularVelocity == new Vector3(1, 2, 3), "Reenable replayed stale physics");
            receiver.Call("OnTSMPVariableReceived");
            TickTransform(receiver);
            Check(Vector3.Distance(body.velocity, new Vector3(2, 3, 4)) < .001f, "New velocity did not resume");
            Check(Vector3.Distance(body.angularVelocity, new Vector3(.2f, .3f, .4f)) < .001f, "New angular velocity did not resume");
        }
    }

    private static void MissingRigidbody()
    {
        byte[] packet = TransformPacket();
        using (var receiver = new Endpoint(typeof(TSMPNetworkTransformSync)))
        {
            receiver.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
            receiver.Set("continuousInterpolationRate", 0f);
            receiver.Set("packedBytes", packet);
            receiver.Call("OnTSMPVariableReceived");
            TickTransform(receiver);
            Check(Vector3.Distance(receiver.Target.transform.position, new Vector3(3, 4, 5)) < .001f, "Missing Rigidbody blocked Transform application");
        }
    }

    private static void TransformReceiveReset()
    {
        byte[] stalePacket = TransformPacket();
        byte[] recoveryPacket = (byte[])stalePacket.Clone();
        byte[] followingPacket = (byte[])stalePacket.Clone();
        Binary.WriteVector3Float32LE(recoveryPacket, 2, new Vector3(11, 12, 13));
        Binary.WriteVector3Float32LE(followingPacket, 2, new Vector3(21, 22, 23));

        using (var receiver = new Endpoint(typeof(TSMPNetworkTransformSync)))
        {
            receiver.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
            receiver.Set("continuousInterpolationRate", 0f);
            receiver.Set("networkId", (ushort)19);
            receiver.Set("lastVariableHash", 123u);
            receiver.Set("packedBytes", stalePacket);
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Get<bool>("_hasContinuousTarget"), "Continuous target fixture was not armed");

            receiver.Call("ResetReceiveState");
            Check(!receiver.Get<bool>("_hasContinuousTarget"), "Reset retained the aggregate Transform target");
            Check(!receiver.Get<bool>("_continuousHasPosition") &&
                  !receiver.Get<bool>("_continuousHasRotation") &&
                  !receiver.Get<bool>("_continuousHasScale") &&
                  !receiver.Get<bool>("_continuousHasRigidbodyVelocity") &&
                  !receiver.Get<bool>("_continuousHasRigidbodyAngularVelocity"), "Reset retained a Transform target flag");
            Check(receiver.Get<Vector3>("_continuousPosition") == Vector3.zero &&
                  receiver.Get<Quaternion>("_continuousRotation") == Quaternion.identity &&
                  receiver.Get<Vector3>("_continuousScale") == Vector3.zero &&
                  receiver.Get<Vector3>("_continuousRigidbodyVelocity") == Vector3.zero &&
                  receiver.Get<Vector3>("_continuousRigidbodyAngularVelocity") == Vector3.zero, "Reset retained a Transform target value");
            Check(receiver.Get<bool>("_snapNextReceive"), "Reset did not arm the next applicable packet");
            Check(ReferenceEquals(receiver.Get<byte[]>("packedBytes"), stalePacket), "Reset replaced the public receive buffer");
            Check(receiver.Get<ReceiveInterpolationMode>("receiveInterpolation") == ReceiveInterpolationMode.Continuous &&
                  receiver.Get<float>("continuousInterpolationRate") == 0f &&
                  receiver.Get<ushort>("networkId") == 19 &&
                  receiver.Get<uint>("lastVariableHash") == 123u, "Reset changed public configuration or diagnostics");

            TickTransform(receiver);
            Check(receiver.Target.transform.position == Vector3.zero, "A stale Transform target was replayed after reset");

            receiver.Set("packedBytes", new byte[] { 2 });
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Get<bool>("_snapNextReceive"), "A malformed packet consumed the pending snap");

            receiver.Set("packedBytes", new byte[] { 2, 0 });
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Get<bool>("_snapNextReceive"), "An empty packet consumed the pending snap");

            receiver.Set("packedBytes", recoveryPacket);
            receiver.Call("OnTSMPVariableReceived");
            Check(Vector3.Distance(receiver.Target.transform.position, new Vector3(11, 12, 13)) < .001f, "The recovery packet did not snap immediately");
            Check(!receiver.Get<bool>("_snapNextReceive") && !receiver.Get<bool>("_hasContinuousTarget"), "The successful snap did not return to normal receive state");

            receiver.Target.transform.position = Vector3.zero;
            receiver.Set("packedBytes", followingPacket);
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Target.transform.position == Vector3.zero && receiver.Get<bool>("_hasContinuousTarget"), "Continuous interpolation did not resume after the snap");
            TickTransform(receiver);
            Check(Vector3.Distance(receiver.Target.transform.position, new Vector3(21, 22, 23)) < .001f, "The resumed continuous target was not applied");
        }
    }

    private static byte[] HumanoidPosePacket(Quaternion boneRotation, Vector3 rootPosition, Quaternion rootRotation)
    {
        byte[] packet = new byte[27];
        packet[0] = 4;
        packet[1] = 3;
        Binary.WriteUInt16LE(packet, 2, 1);
        packet[4] = (byte)HumanBodyBones.Head;
        Binary.WritePackedQuaternion12LE(packet, 5, boneRotation);
        Binary.WriteVector3Float32LE(packet, 10, rootPosition);
        Binary.WritePackedQuaternion12LE(packet, 22, rootRotation);
        return packet;
    }

    private static void TickHumanoid(Endpoint receiver)
    {
#if UDONSHARP
        receiver.Call("_postLateUpdate");
#else
        receiver.Call("LateUpdate");
#endif
    }

    private static void FixedRigHands()
    {
        const string model = "Packages/com.kibalab.tsmp.core/Samples/TSMPExample/Models/Shapell/data/shapell.fbx";
        using (var receiver = new Endpoint(typeof(TSMPNetworkHumanoidPoseSync)))
        {
            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(model));
            rig.transform.SetParent(receiver.Target.transform);
            var animator = rig.GetComponentInChildren<Animator>();
            receiver.Set("animator", animator);
            receiver.Set("boneIds", new[] { (int)HumanBodyBones.LeftHand, (int)HumanBodyBones.RightHand });
            receiver.Set("cacheFixedReceiveRig", true);
            receiver.Set("alternateHandUpdates", true);
            receiver.Set("alternateIncludeWrists", true);
            receiver.Set("includeFingerBones", false);
            receiver.Set("receiveInterpolation", ReceiveInterpolationMode.Discrete);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var packet = new byte[16];
            packet[0] = 4; packet[1] = 1; packet[2] = 2;
            packet[4] = (byte)HumanBodyBones.LeftHand; packet[10] = (byte)HumanBodyBones.RightHand;
            Quaternion previous = right.localRotation;
            for (int tick = 1; tick <= 3; tick++)
            {
                if (tick == 3) receiver.Call("InvalidateReceiveRig");
                var rotation = Quaternion.Euler(tick * 25, 0, 0);
                Binary.WritePackedQuaternion12LE(packet, 5, rotation);
                Binary.WritePackedQuaternion12LE(packet, 11, rotation);
                receiver.Set("poseBytes", packet);
                receiver.Call("OnTSMPVariableReceived");
                Check(Quaternion.Angle(left.localRotation, rotation) < .5f, "Left wrist");
                Check(Quaternion.Angle(right.localRotation, tick == 2 ? previous : rotation) < .5f, "Right wrist");
                previous = right.localRotation;
            }
        }
    }

    private static void HumanoidReceiveReset()
    {
        Quaternion staleBoneRotation = Quaternion.Euler(10, 20, 30);
        Quaternion recoveryBoneRotation = Quaternion.Euler(30, 40, 50);
        Quaternion followingBoneRotation = Quaternion.Euler(50, 60, 70);
        Quaternion staleRootRotation = Quaternion.Euler(0, 15, 0);
        Quaternion recoveryRootRotation = Quaternion.Euler(0, 35, 0);
        Quaternion followingRootRotation = Quaternion.Euler(0, 55, 0);
        byte[] stalePacket = HumanoidPosePacket(staleBoneRotation, new Vector3(1, 2, 3), staleRootRotation);
        byte[] recoveryPacket = HumanoidPosePacket(recoveryBoneRotation, new Vector3(4, 5, 6), recoveryRootRotation);
        byte[] followingPacket = HumanoidPosePacket(followingBoneRotation, new Vector3(7, 8, 9), followingRootRotation);

        using (var receiver = new Endpoint(typeof(TSMPNetworkHumanoidPoseSync)))
        {
            Transform root = new GameObject("Humanoid root").transform;
            Transform bone = new GameObject("Humanoid head").transform;
            root.SetParent(receiver.Target.transform, false);
            bone.SetParent(root, false);

            int lastBone = (int)HumanBodyBones.LastBone;
            int head = (int)HumanBodyBones.Head;
            Transform[] targetsById = new Transform[lastBone];
            targetsById[head] = bone;

            receiver.Set("autoResolveBones", false);
            receiver.Set("includeFingerBones", false);
            receiver.Set("boneIds", new[] { head });
            receiver.Set("boneTargets", new[] { bone });
            receiver.Set("_boneTargetsById", targetsById);
            receiver.Set("_rootMotionTarget", root);
            receiver.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
            receiver.Set("continuousInterpolationRate", 0f);
            receiver.Set("networkId", (ushort)23);
            receiver.Set("lastVariableHash", 456u);
            receiver.Set("poseBytes", stalePacket);
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Get<bool>("_hasContinuousBoneTargets") && receiver.Get<bool>("_hasContinuousRootTarget"), "Continuous Humanoid target fixture was not armed");

            receiver.Get<bool[]>("_hasReceivedBoneWorldRotation")[head] = true;
            receiver.Get<Quaternion[]>("_receivedBoneWorldRotations")[head] = staleBoneRotation;
            receiver.Get<bool[]>("_cachedHasReceivedBoneWorldRotation")[head] = true;
            receiver.Get<int[]>("_receivedAncestorBoneIdsById")[head] = (int)HumanBodyBones.Hips;
            receiver.Set("_receivedAncestorBoneIdsValid", true);

            receiver.Call("ResetReceiveState");
            Check(!receiver.Get<bool>("_hasContinuousBoneTargets") && !receiver.Get<bool>("_hasContinuousRootTarget"), "Reset retained an aggregate Humanoid target");
            Check(!receiver.Get<bool[]>("_hasTargetBoneRotation")[head] &&
                  !receiver.Get<bool[]>("_hasReceivedBoneWorldRotation")[head] &&
                  !receiver.Get<bool[]>("_cachedHasReceivedBoneWorldRotation")[head], "Reset retained a Humanoid target mask");
            Check(receiver.Get<Quaternion[]>("_targetBoneRotations")[head] == Quaternion.identity &&
                  receiver.Get<Quaternion[]>("_receivedBoneWorldRotations")[head] == Quaternion.identity, "Reset retained a Humanoid rotation target");
            Check(!receiver.Get<bool>("_receivedAncestorBoneIdsValid") &&
                  receiver.Get<int[]>("_receivedAncestorBoneIdsById")[head] == -1, "Reset retained the received-ancestor cache");
            Check(!receiver.Get<bool>("_continuousRootIsLocal") &&
                  receiver.Get<Vector3>("_continuousRootPosition") == Vector3.zero &&
                  receiver.Get<Quaternion>("_continuousRootRotation") == Quaternion.identity &&
                  !receiver.Get<bool>("_continuousRootHasRotation"), "Reset retained the Humanoid root target");
            Check(receiver.Get<bool>("_snapNextReceive"), "Humanoid reset did not arm the next applicable packet");
            Check(ReferenceEquals(receiver.Get<byte[]>("poseBytes"), stalePacket) &&
                  ReferenceEquals(receiver.Get<Transform[]>("_boneTargetsById"), targetsById) &&
                  receiver.Get<Transform>("_rootMotionTarget") == root, "Reset replaced public data or the resolved bone cache");
            Check(receiver.Get<ReceiveInterpolationMode>("receiveInterpolation") == ReceiveInterpolationMode.Continuous &&
                  receiver.Get<float>("continuousInterpolationRate") == 0f &&
                  receiver.Get<ushort>("networkId") == 23 &&
                  receiver.Get<uint>("lastVariableHash") == 456u, "Reset changed Humanoid configuration or diagnostics");

            TickHumanoid(receiver);
            Check(bone.localRotation == Quaternion.identity && root.localPosition == Vector3.zero && root.localRotation == Quaternion.identity,
                "A stale Humanoid target was replayed after reset");

            receiver.Set("poseBytes", new byte[] { 4, 1, 1, 0 });
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Get<bool>("_snapNextReceive"), "A malformed Humanoid packet consumed the pending snap");

            receiver.Set("poseBytes", new byte[] { 4, 1, 0, 0 });
            receiver.Call("OnTSMPVariableReceived");
            Check(receiver.Get<bool>("_snapNextReceive"), "An empty Humanoid packet consumed the pending snap");

            receiver.Set("poseBytes", recoveryPacket);
            receiver.Call("OnTSMPVariableReceived");
            Check(Quaternion.Angle(bone.localRotation, recoveryBoneRotation) < .5f &&
                  Vector3.Distance(root.localPosition, new Vector3(4, 5, 6)) < .001f &&
                  Quaternion.Angle(root.localRotation, recoveryRootRotation) < .5f, "The recovery Humanoid packet did not snap immediately");
            Check(!receiver.Get<bool>("_snapNextReceive") &&
                  !receiver.Get<bool>("_hasContinuousBoneTargets") &&
                  !receiver.Get<bool>("_hasContinuousRootTarget"), "The successful Humanoid snap did not return to normal receive state");

            bone.localRotation = Quaternion.identity;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            receiver.Set("poseBytes", followingPacket);
            receiver.Call("OnTSMPVariableReceived");
            Check(bone.localRotation == Quaternion.identity && root.localPosition == Vector3.zero && root.localRotation == Quaternion.identity &&
                  receiver.Get<bool>("_hasContinuousBoneTargets") && receiver.Get<bool>("_hasContinuousRootTarget"),
                "Continuous Humanoid interpolation did not resume after the snap");
            TickHumanoid(receiver);
            Check(Quaternion.Angle(bone.localRotation, followingBoneRotation) < .5f &&
                  Vector3.Distance(root.localPosition, new Vector3(7, 8, 9)) < .001f &&
                  Quaternion.Angle(root.localRotation, followingRootRotation) < .5f, "The resumed Humanoid target was not applied");
        }
    }

#if UDONSHARP
    public static void RunVm()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (string name in new[] { nameof(ReceivePolicyProbe), nameof(PlainReceiveProbe), nameof(SendCommitProbe), nameof(SendCommitCodec) })
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Root + name + ".asset") != null) continue;
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Root + "Runtime/" + name + ".cs");
            AssetDatabase.CreateAsset(asset, Root + name + ".asset");
        }
        CompileClient();
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.ReceivePolicies.Vm", true);
        Attach();
        EditorApplication.isPlaying = true;
    }

    private static void CompileClient()
    {
        bool error = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = true;
        };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= callback; }
        Check(!error, "Full Udon client compile failed");
    }

    [InitializeOnLoadMethod]
    private static void Attach()
    {
        if (!SessionState.GetBool("TSMP.ReceivePolicies.Vm", false)) return;
        EditorApplication.playModeStateChanged -= EnteredPlay;
        EditorApplication.playModeStateChanged += EnteredPlay;
    }

    private static void EnteredPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool("TSMP.ReceivePolicies.Vm", false);
        EditorApplication.playModeStateChanged -= EnteredPlay;
        EditorApplication.delayCall += () =>
        {
            Test("Full Udon client compilation in Play Mode", CompileClient);
            if (!failed) RunCases();
            Finish(true);
        };
    }
#endif

    private static void Finish(bool exit)
    {
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), (failed ? "FAIL" : "PASS") +
            "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (exit) EditorApplication.Exit(failed ? 1 : 0);
        else Check(!failed, "Receive policy regressions failed");
    }
    private static void Test(string name, Action action)
    {
        bool loggedError = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) loggedError = true;
        };
        Application.logMessageReceived += callback;
        try { action(); Check(!loggedError, "Test emitted an error log"); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
        finally { Application.logMessageReceived -= callback; }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
