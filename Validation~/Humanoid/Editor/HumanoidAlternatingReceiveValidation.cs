// Reusable Editor-only checks. This file is not imported from Validation~.
// The same cases can run through C# or an adapter to a compiled UdonBehaviour.
using System;
using System.Collections.Generic;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public static class HumanoidAlternatingReceiveValidation
{
    public abstract class Driver
    {
        public abstract object Get(string name);
        public abstract void Set(string name, object value);
        public abstract void Receive(byte[] bytes);
        public abstract void Interpolate();
        public abstract void Reset();
    }

    sealed class CSharpDriver : Driver
    {
        readonly TSMPNetworkHumanoidPoseSync sync;
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public CSharpDriver(TSMPNetworkHumanoidPoseSync value) { sync = value; }
        public override object Get(string n) => typeof(TSMPNetworkHumanoidPoseSync).GetField(n, Fields).GetValue(sync);
        public override void Set(string n, object v) => typeof(TSMPNetworkHumanoidPoseSync).GetField(n, Fields).SetValue(sync, v);
        public override void Receive(byte[] bytes) { sync.poseBytes = bytes; sync.OnTSMPVariableReceived(); }
        public override void Interpolate() => typeof(TSMPNetworkHumanoidPoseSync).GetMethod("ApplyContinuousPose", Fields).Invoke(sync, null);
        public override void Reset() => sync.ResetReceiveState();
    }
    public static Driver CSharp(TSMPNetworkHumanoidPoseSync sync) => new CSharpDriver(sync);

    public sealed class Rig : IDisposable
    {
        public readonly GameObject root;
        public readonly Driver driver;
        public readonly Transform[] bones = new Transform[(int)HumanBodyBones.LastBone];
        public readonly int[] ids;
        public Rig(Func<GameObject, Driver> make, bool intermediate = false, bool missing = false)
        {
            root = new GameObject("Humanoid alternating receive validation") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetPositionAndRotation(new Vector3(2, 3, 1), Quaternion.Euler(12, 31, -7));
            driver = make(root);
            driver.Set("autoResolveBones", false);
            driver.Set("animator", root.AddComponent<Animator>());
            driver.Set("includeFingerBones", true);
            driver.Set("alternateHandUpdates", false);
            driver.Set("alternateIncludeWrists", false);
            driver.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
            driver.Set("continuousInterpolationRate", 0f);
            var list = new List<int> { 0, 7, (int)HumanBodyBones.LeftHand };
            for (int i = (int)HumanBodyBones.LeftThumbProximal; i <= (int)HumanBodyBones.LeftLittleDistal; i++) list.Add(i);
            list.Add((int)HumanBodyBones.RightHand);
            for (int i = (int)HumanBodyBones.RightThumbProximal; i <= (int)HumanBodyBones.RightLittleDistal; i++) list.Add(i);
            ids = list.ToArray();
            driver.Set("boneIds", ids);
            driver.Reset();
            // Public receive path prepares the arrays without private-method calls.
            driver.Receive(new byte[] { 4, 1, 0, 0 });
            bones[0] = Child(root.transform, "Hips"); bones[7] = Child(bones[0], "Spine");
            var parents = new int[bones.Length]; for (int i = 0; i < parents.Length; i++) parents[i] = -1;
            parents[7] = 0;
            for (int side = 0; side < 2; side++)
            {
                int hand = side == 0 ? (int)HumanBodyBones.LeftHand : (int)HumanBodyBones.RightHand;
                int first = side == 0 ? (int)HumanBodyBones.LeftThumbProximal : (int)HumanBodyBones.RightThumbProximal;
                bones[hand] = Child(bones[7], "Hand " + side); parents[hand] = 7;
                for (int finger = 0; finger < 5; finger++)
                {
                    Transform parent = intermediate ? Child(bones[hand], "Unmapped bridge") : bones[hand];
                    if (intermediate) parent.localRotation = Quaternion.Euler(4, 9, -3);
                    for (int joint = 0; joint < 3; joint++)
                    {
                        int id = first + finger * 3 + joint;
                        bones[id] = Child(parent, "Finger " + id); parents[id] = joint == 0 ? hand : id - 1; parent = bones[id];
                    }
                }
            }
            var targets = (Transform[])bones.Clone();
            if (missing) targets[(int)HumanBodyBones.LeftThumbProximal] = null;
            driver.Set("_boneTargetsById", targets);
            driver.Set("_boneParentIdsById", parents);
            driver.Set("_rootMotionTarget", bones[0]);
            driver.Set("_receivedAncestorBoneIdsValid", false);
            driver.Set("_handReceiveInitialized", false);
            driver.Set("_snapNextReceive", false);
        }
        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        public byte[] Packet(int tick, byte space, bool reverse = false, int[] selection = null)
        {
            int[] order = selection ?? ids;
            var bytes = new byte[4 + 6 * order.Length + 17]; bytes[0] = 4; bytes[1] = (byte)(space | 2);
            Binary.WriteUInt16LE(bytes, 2, (ushort)order.Length);
            for (int i = 0; i < order.Length; i++)
            {
                int id = order[reverse ? order.Length - 1 - i : i]; bytes[4 + i * 6] = (byte)id;
                Binary.WritePackedQuaternion12LE(bytes, 5 + i * 6, Quaternion.Euler(tick * 11 + id * .6f, tick * -13 + id, tick * 7));
            }
            Binary.WriteVector3Float32LE(bytes, 4 + order.Length * 6, new Vector3(tick * .1f, 1f, -.2f));
            Binary.WritePackedQuaternion12LE(bytes, 16 + order.Length * 6, Quaternion.Euler(0, tick * 5, 0));
            return bytes;
        }
        public Quaternion[] Locals()
        {
            var values = new Quaternion[bones.Length];
            foreach (int id in ids) values[id] = bones[id].localRotation;
            return values;
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Rotation(Quaternion a, Quaternion b, string message)
    { Require(Quaternion.Angle(a, b) < .08f, message + " angle=" + Quaternion.Angle(a, b)); }
    static bool IsSide(int id, bool right, bool wrists)
    {
        int first = right ? (int)HumanBodyBones.RightThumbProximal : (int)HumanBodyBones.LeftThumbProximal;
        return (id >= first && id < first + 15) || (wrists && id == (right ? (int)HumanBodyBones.RightHand : (int)HumanBodyBones.LeftHand));
    }

    public static string[] Run(Func<GameObject, Driver> make)
    {
        var passed = new List<string>();
        foreach (bool continuous in new[] { false, true })
        foreach (byte space in new byte[] { 0, 1, 4 })
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (bool bridge in new[] { false, true })
        {
            using (var r = new Rig(make, bridge))
            using (var reference = new Rig(go => CSharp(go.AddComponent<TSMPNetworkHumanoidPoseSync>()), bridge))
            {
                r.driver.Set("alternateHandUpdates", mode != 0); r.driver.Set("alternateIncludeWrists", mode == 2);
                r.driver.Set("receiveInterpolation", continuous ? ReceiveInterpolationMode.Continuous : ReceiveInterpolationMode.Discrete);
                reference.driver.Set("receiveInterpolation", continuous ? ReceiveInterpolationMode.Continuous : ReceiveInterpolationMode.Discrete);
                for (int tick = 1; tick <= 4; tick++)
                {
                    var old = r.Locals();
                    byte[] packet = r.Packet(tick, space, continuous);
                    r.driver.Receive(packet); reference.driver.Receive(packet);
                    if (continuous) { r.driver.Interpolate(); reference.driver.Interpolate(); }
                    foreach (int id in r.ids)
                    {
                        bool held = mode != 0 && tick > 1 && IsSide(id, tick % 2 == 0, mode == 2);
                        Rotation(r.bones[id].localRotation, held ? old[id] : reference.bones[id].localRotation,
                            $"mode={mode} continuous={continuous} space={space} bridge={bridge} tick={tick} bone={id}");
                    }
                    Require(Vector3.Distance(r.bones[0].position, reference.bones[0].position) < .0001f, "Root must update every receipt");
                }
                passed.Add($"mode={mode} continuous={continuous} space={space} bridge={bridge}");
            }
        }
        using (var r = new Rig(make))
        {
            Require(!(bool)r.driver.Get("alternateHandUpdates"), "OFF is the initial mode");
            r.driver.Set("alternateHandUpdates", true); r.driver.Receive(r.Packet(1, 1)); r.driver.Interpolate();
            var old = r.Locals(); var bad = r.Packet(2, 1); bad[4 + (r.ids.Length - 1) * 6] = 255;
            r.driver.Receive(bad); r.driver.Interpolate();
            foreach (int id in r.ids) Rotation(r.bones[id].localRotation, old[id], "Invalid skipped-side ID must reject the whole packet");
            Require(!(bool)r.driver.Get("_receiveRightHandNext"), "Invalid packet must not advance phase");
            r.driver.Receive(r.Packet(2, 1));
            int right = (int)HumanBodyBones.RightIndexProximal;
            Quaternion heldTarget = ((Quaternion[])r.driver.Get("_targetBoneRotations"))[right];
            r.bones[right].localRotation = Quaternion.identity;
            r.driver.Interpolate();
            Rotation(r.bones[right].localRotation, heldTarget, "Held side must still interpolate to its previous target");
            r.driver.Set("alternateIncludeWrists", true); r.driver.Receive(r.Packet(3, 1));
            Require(!(bool)r.driver.Get("_receiveRightHandNext"), "Mode change refreshes both sides then restarts left");
            r.driver.Set("alternateHandUpdates", false); r.driver.Receive(r.Packet(4, 1)); r.driver.Interpolate();
            Rotation(r.bones[right].localRotation, Binary.ReadPackedQuaternion12LE(r.Packet(4, 1), 5 + Array.IndexOf(r.ids, right) * 6), "OFF immediately refreshes both sides");
            r.driver.Set("alternateHandUpdates", true); r.driver.Reset(); r.driver.Receive(r.Packet(5, 1));
            Rotation(r.bones[right].localRotation, Binary.ReadPackedQuaternion12LE(r.Packet(5, 1), 5 + Array.IndexOf(r.ids, right) * 6), "Reset must snap both sides");
            passed.Add("invalid packet atomicity, held-side interpolation, runtime mode changes, OFF and reset snap");
        }
        using (var r = new Rig(make))
        {
            r.driver.Set("alternateHandUpdates", true); r.driver.Receive(r.Packet(1, 1)); r.driver.Interpolate();
            // No root tail: a packet containing only the skipped side must still advance phase.
            int id = (int)HumanBodyBones.RightIndexProximal;
            var b = r.Packet(2, 1, false, new[] { id }); Array.Resize(ref b, 10); b[1] = 1;
            r.driver.Receive(b); Require((bool)r.driver.Get("_receiveRightHandNext"), "Sparse packet must not starve the other side");
            r.driver.Receive(b); r.driver.Interpolate();
            Rotation(r.bones[id].localRotation, Binary.ReadPackedQuaternion12LE(b, 5), "Sparse right packet eventually updates");
            passed.Add("sparse packets and receipt-order alternation independent of render frame parity");
        }
        using (var r = new Rig(make, true, true))
        using (var reference = new Rig(go => CSharp(go.AddComponent<TSMPNetworkHumanoidPoseSync>()), true, true))
        {
            r.driver.Set("alternateHandUpdates", true);
            for (int tick = 1; tick < 4; tick++)
            {
                var b = r.Packet(tick, 4, true); r.driver.Receive(b); reference.driver.Receive(b);
                r.driver.Interpolate(); reference.driver.Interpolate();
                int id = tick == 3 ? (int)HumanBodyBones.RightThumbIntermediate : (int)HumanBodyBones.LeftThumbIntermediate;
                Rotation(r.bones[id].localRotation, reference.bones[id].localRotation, "Missing target and intermediate ancestor fallback");
            }
            passed.Add("missing target, unmapped intermediate transforms and full-packet ancestor cache");
        }
        return passed.ToArray();
    }
}
