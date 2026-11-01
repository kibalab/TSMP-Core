using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
#endif
using Object = UnityEngine.Object;

public static class TransSyncSchedulingValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Test("First send, unchanged suppression, scalar and in-place array changes", Changes);
        Test("Variable snapshot length, valid prefix, dirty tail and commit boundaries", VariableSnapshots);
        Test("Minimum interval coalesces latest value and SendOnChange=false polls", Intervals);
        Test("Refresh obeys minimum interval, zero disables refresh", Refresh);
        Test("Component send modes override only change filtering and switch live", ComponentModes);
        Test("Component send modes remain independent for shared field metadata", IndependentModes);
        Test("Send Mode supports serialized multi-edit, undo and SDK editor proxies", ModeInspector);
        Test("Priority crosses object boundaries, capacity defers lower priorities", Priority);
        Test("Equal priorities rotate after successful output", Fairness);
        Test("Output failure retains snapshots, times and pending state", FailedOutput);
        Test("Disabled fields and Network ID changes start fresh", Reconfigure);
        Test("Large fields do not prevent smaller fields or queued RPC", Capacity);
        Test("NaN, infinity and negative intervals normalize to zero", InvalidIntervals);
        Test("Snapshot and editor binding generation preserve all scheduling options", Metadata);
        Test("Setup and automatic binding paths agree on collisions, order, direction and RPC-only targets", BindingPaths);
#if !UDONSHARP
        Test("Native BeforeEncode manual writes remain independent of automatic scheduling", ManualWrites);
#endif
        Finish();
    }

    private static TransSyncSchedulingProbe Create(string name)
    {
        var owner = new GameObject(name);
#if UDONSHARP
        return owner.AddUdonSharpComponent<TransSyncSchedulingProbe>();
#else
        return owner.AddComponent<TransSyncSchedulingProbe>();
#endif
    }

    private sealed class Sender : IDisposable
    {
        public readonly TransSyncSchedulingProbe Probe = Create("Scheduling source");
        public readonly List<TSMPNetworkBehaviour> Sources;
        public readonly EncoderNativeSendState State = new EncoderNativeSendState();
        public readonly List<EncoderNativeFrameBuilder.QueuedRpc> Rpcs = new List<EncoderNativeFrameBuilder.QueuedRpc>();
        private readonly Dictionary<Type, TransSyncMetadata.Cache> cache = new Dictionary<Type, TransSyncMetadata.Cache>();
        private byte[] payload;
        private byte[] encoded;
        private int offset;
        private int start = -1;
        private int count;
        public int RpcCount;
        public Sender()
        {
            Probe.networkId = 1;
            Sources = new List<TSMPNetworkBehaviour> { Probe };
        }
        public Dictionary<uint, byte[]> Send(double now, bool commit = true, int capacity = 2048, float refresh = 0)
        {
            Check(EncoderNativeFrameBuilder.BuildNetworkPayload(Sources, Rpcs, cache, ref payload, ref encoded,
                ref offset, ref start, ref count, 1, capacity, out int messages, out int variables, out RpcCount,
                out int bytes, out string error, State, now, refresh), error);
            Check(bytes <= capacity, "Payload exceeds codec capacity");
            var values = ReadValues(encoded, bytes);
            Check(values.Count == State.WrittenCount, "Auto variable count differs from payload");
            if (commit && messages > 0) State.Commit(payload, now);
            return values;
        }
        public void Dispose()
        {
            foreach (var source in Sources) if (source != null) Object.DestroyImmediate(source.gameObject);
        }
    }

    private static uint Hash(string key) => StableHash.VariableHash(typeof(TransSyncSchedulingProbe), key, key);
    private static void Changes()
    {
        using (var s = new Sender())
        {
            s.Probe.changedEnabled = s.Probe.bytesEnabled = s.Probe.textEnabled = true;
            Check(s.Send(0).Count == 3, "First sample missing");
            Check(s.Send(.01).Count == 0, "Unchanged fields resent");
            s.Probe.bytes[1] = 9;
            s.Probe.text = "\U0001F441\uFE0F\U0001F441\uFE0F";
            var values = s.Send(.02);
            Check(values.Count == 2 && values[Hash("bytes")][1] == 9, "Array mutation not captured");
            Check(System.Text.Encoding.UTF8.GetString(values[Hash("text")]) == s.Probe.text, "Unicode changed");
            s.Probe.bytes = new byte[] { 1, 9, 3 };
            Check(s.Send(.03).Count == 0, "Equal array instance triggered send");
            s.Probe.bytes = new byte[0];
            Check(s.Send(.04)[Hash("bytes")].Length == 0, "Empty array lost");
            s.Probe.bytes = null;
            Check(s.Send(.05).Count == 0, "Wire-equivalent null differs from empty");
            s.Probe.changed++;
            Check(s.Send(.06).Count == 1, "Scalar change lost");
            Check(s.Probe.captures == 7, "BeforeEncode did not run exactly once per attempt");
        }
    }
    private static void Intervals()
    {
        using (var s = new Sender())
        {
            s.Probe.pacedEnabled = s.Probe.pollEnabled = true;
            Check(s.Send(0).Count == 2, "Initial interval delayed first sample");
            s.Probe.paced = 71;
            Check(s.Send(.1).Count == 0, "Minimum interval ignored");
            s.Probe.paced = 72;
            var values = s.Send(.25);
            Check(values.Count == 2 && Binary.ReadInt32LE(values[Hash("paced")], 0) == 72, "Intermediate instead of latest value sent");
            values = s.Send(.5);
            Check(values.Count == 1 && values.ContainsKey(Hash("poll")), "Poll/change modes are indistinguishable");
        }
    }

    private static byte[] SnapshotBytes(int length) => Enumerable.Repeat((byte)0x5a, length).ToArray();
    private static readonly int[] SnapshotLengths = { 8, 64, 8, 8, 64, 255, 256, 255, 255, 0, 0, 8 };
    private static void CheckRawSnapshotResult(Dictionary<uint, byte[]> values, byte[] expected, bool emitted, string scope)
    {
        if (emitted) Check(values.Count == 1 && values.TryGetValue(Hash("bytes"), out byte[] actual) && actual.SequenceEqual(expected), scope + " lost raw length/content");
        else Check(values.Count == 0, scope + " resent wire-equivalent bytes");
    }
    private static void CheckVariableSnapshot(byte[] snapshot, byte[] body, string scope)
    {
        int valid = NetworkFrameProtocol.VariableValueHeaderBytes + body.Length;
        Check(snapshot != null && snapshot.Length >= valid, scope + " snapshot capacity is short");
        Check(Binary.ReadUInt32LE(snapshot, 0) == Hash("bytes") && snapshot[4] == NetworkFrameProtocol.ValueTypeRawBytes
            && Binary.ReadUInt16LE(snapshot, NetworkFrameProtocol.VariableValueLengthOffset) == body.Length, scope + " snapshot header/length differs");
        Check(snapshot.Skip(NetworkFrameProtocol.VariableValueHeaderBytes).Take(body.Length).SequenceEqual(body), scope + " snapshot valid body differs");
    }
    private static byte[] NativeVariableSnapshot(Sender sender)
    {
        var entries = (System.Collections.IEnumerable)typeof(EncoderNativeSendState).GetField("_entries", Private).GetValue(sender.State);
        foreach (object entry in entries)
        {
            Type type = entry.GetType(); var field = (TransSyncMetadata.Field)type.GetField("Field").GetValue(entry);
            if (field.VariableHash == Hash("bytes")) return (byte[])type.GetField("Previous").GetValue(entry);
        }
        throw new InvalidOperationException("Native bytes snapshot entry missing");
    }
    private static void VariableSnapshots()
    {
        byte[] opaque = SnapshotBytes(16), shortOpaque = SnapshotBytes(8);
        Check(TransSyncSendScheduler.ShouldSend(true, 0, 1, true, 0, opaque, shortOpaque, shortOpaque.Length), "Generic opaque comparison lost strict length semantics");
        byte[] exact = TransSyncSendScheduler.Snapshot(shortOpaque, 0, shortOpaque.Length, opaque);
        Check(exact.Length == shortOpaque.Length && exact.SequenceEqual(shortOpaque) && !ReferenceEquals(exact, shortOpaque), "Generic Snapshot lost exact-length independent copy");
        using (var s = new Sender())
        {
            s.Probe.bytesEnabled = true; int previousLength = -1, tailChecks = 0; double now = 0;
            foreach (int length in SnapshotLengths)
            {
                s.Probe.bytes = SnapshotBytes(length); // Equal contents in a different array must not trigger a send.
                CheckRawSnapshotResult(s.Send(now++), s.Probe.bytes, previousLength != length, "Native variable sequence");
                byte[] snapshot = NativeVariableSnapshot(s); CheckVariableSnapshot(snapshot, s.Probe.bytes, "Native variable sequence");
                int valid = NetworkFrameProtocol.VariableValueHeaderBytes + length;
                if (tailChecks == 0 && snapshot.Length > valid)
                {
                    snapshot[snapshot.Length - 1] ^= 0xff;
                    CheckRawSnapshotResult(s.Send(now++), s.Probe.bytes, false, "Native dirty capacity tail"); tailChecks++;
                    CheckVariableSnapshot(NativeVariableSnapshot(s), s.Probe.bytes, "Native dirty capacity tail");
                }
                previousLength = length;
            }
            s.Probe.bytes[0] ^= 1;
            CheckRawSnapshotResult(s.Send(now++), s.Probe.bytes, true, "Native valid-prefix mutation");
            CheckRawSnapshotResult(s.Send(now++), s.Probe.bytes, false, "Native committed mutation");
            byte[] before = NativeVariableSnapshot(s), saved = (byte[])before.Clone();
            s.Probe.bytes = SnapshotBytes(32);
            CheckRawSnapshotResult(s.Send(now++, commit: false), s.Probe.bytes, true, "Native uncommitted length change");
            Check(ReferenceEquals(before, NativeVariableSnapshot(s)) && before.SequenceEqual(saved), "Native failed output changed snapshot");
            CheckRawSnapshotResult(s.Send(now++), s.Probe.bytes, true, "Native length-change retry");
            before = NativeVariableSnapshot(s); saved = (byte[])before.Clone(); s.Probe.bytes = SnapshotBytes(64);
            Check(s.Send(now++, capacity: 32).Count == 0 && s.State.DeferredCount == 1, "Native large raw field was not deferred");
            Check(ReferenceEquals(before, NativeVariableSnapshot(s)) && before.SequenceEqual(saved), "Native deferral changed snapshot");
            CheckRawSnapshotResult(s.Send(now++), s.Probe.bytes, true, "Native deferred length-change retry");
            CheckVariableSnapshot(NativeVariableSnapshot(s), s.Probe.bytes, "Native retry snapshot");
            s.State.Reset(); CheckRawSnapshotResult(s.Send(now), s.Probe.bytes, true, "Native reset with equal raw bytes");
            Results.Add("Native variable snapshot tail checks=" + tailChecks + "; zero is expected for the original exact-length implementation.");
        }
    }
    private static void Refresh()
    {
        using (var s = new Sender())
        {
            s.Probe.pacedEnabled = true;
            s.Send(0);
            Check(s.Send(.15, refresh: .1f).Count == 0, "Refresh violated interval");
            Check(s.Send(.25, refresh: .1f).Count == 1, "Refresh did not resend unchanged value");
            Check(s.Send(100, refresh: 0).Count == 0, "Zero refresh was not disabled");
            Check(s.Send(-1).Count == 1, "Clock restart did not invalidate timestamps");
        }
    }
    private static void Priority()
    {
        using (var s = new Sender())
        {
            s.Probe.lowEnabled = true;
            var other = Create("High priority second object");
            other.networkId = 2;
            other.highEnabled = true;
            s.Sources.Add(other);
            var values = s.Send(0, capacity: 29);
            Check(values.Count == 1 && values.ContainsKey(Hash("high")), "Priority applied only within a behaviour");
            Check(s.State.DeferredCount == 1, "Deferred field was not reported");
            other.highEnabled = false;
            Check(s.Send(.01, capacity: 29).ContainsKey(Hash("low")), "Deferred field was marked sent");
        }
    }

    private static void ComponentModes()
    {
        using (var s = new Sender())
        {
            Check(s.Probe.sendMode == SendMode.Default, "New components changed the default policy");
            s.Probe.changedEnabled = s.Probe.pollEnabled = true;
            Check(s.Send(0).Count == 2, "Default first send missing");
            Check(s.Send(.25).Keys.Single() == Hash("poll"), "Default ignored field attributes");
            s.Probe.sendMode = SendMode.OnChange;
            Check(s.Send(.5).Count == 0, "On Change did not override SendOnChange=false");
            Check(s.Send(.75, refresh: .5f).Count == 2, "On Change lost unchanged-value refresh");
            s.Probe.sendMode = SendMode.Always;
            Check(s.Send(.8).Keys.Single() == Hash("changed"), "Always bypassed the minimum interval");
            Check(s.Send(1).Count == 2, "Always did not resend unchanged values");
            s.Probe.sendMode = SendMode.Default;
            Check(s.Send(1.25).Keys.Single() == Hash("poll"), "Default did not restore field attributes");
            s.Probe.sendMode = (SendMode)99;
            Check(s.Send(1.5).Keys.Single() == Hash("poll"), "Unknown policy did not fall back to Default");
            s.Probe.sendMode = SendMode.Always;
            s.Probe.changedEnabled = false;
            Check(s.Send(1.75).Keys.Single() == Hash("poll"), "Always sent a disabled field");
            s.Probe.enabled = false;
            Check(s.Send(2).Count == 0, "Always sent a disabled component");
        }
        using (var s = new Sender())
        {
            s.Probe.sendMode = SendMode.Always;
            s.Probe.pacedEnabled = true;
            Check(s.Send(0, false).Count == 1 && s.Send(.01, false).Count == 1, "Always consumed failed output");
            s.Send(.02);
            s.Probe.sendMode = SendMode.OnChange;
            s.Probe.paced++;
            Check(s.Send(.1).Count == 0, "Changing mode reset the minimum interval");
            Check(s.Send(.28).Count == 1, "Mode switch lost the latest changed value");
        }
    }

    private static void IndependentModes()
    {
        using (var s = new Sender())
        {
            s.Probe.changedEnabled = true;
            s.Probe.sendMode = SendMode.Always;
            var other = Create("Independent mode source");
            other.networkId = 2;
            other.pollEnabled = true;
            other.sendMode = SendMode.OnChange;
            s.Sources.Add(other);
            Check(s.Send(0).Count == 2, "Independent first samples missing");
            Check(s.Send(.25).Keys.Single() == Hash("changed"), "One component changed another's mode");
            s.Probe.sendMode = SendMode.OnChange;
            other.sendMode = SendMode.Always;
            Check(s.Send(.5).Keys.Single() == Hash("poll"), "Live per-component mode switch failed");
            var metadata = TransSyncMetadata.GetOrCreate(null, typeof(TransSyncSchedulingProbe)).Fields;
            Check(metadata.Single(f => f.FieldInfo.Name == "changed").Sync.SendOnChange
                && !metadata.Single(f => f.FieldInfo.Name == "poll").Sync.SendOnChange, "Mode override mutated shared attribute metadata");
        }
    }

    private static void ModeInspector()
    {
        var first = Create("Mode inspector first");
        var second = Create("Mode inspector second");
        try
        {
            var serialized = new SerializedObject(new Object[] { first, second });
            var property = serialized.FindProperty("sendMode");
            Check(property != null && property.propertyType == SerializedPropertyType.Enum, "Send Mode is not a serialized enum");
            Check(property.enumDisplayNames.SequenceEqual(new[] { "Default", "On Change", "Always" }), "Dropdown labels differ");
            Undo.IncrementCurrentGroup();
            property.enumValueIndex = (int)SendMode.Always;
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Check(first.sendMode == SendMode.Always && second.sendMode == SendMode.Always, "Multi-edit failed");
#if UDONSHARP
            Check(EncoderUdonBindingRuntime.GetSendMode(UdonSharpEditorUtility.GetBackingUdonBehaviour(first)) == (int)SendMode.Always, "SDK Editor read stale backing mode instead of proxy");
#endif
            Undo.PerformUndo();
            Check(first.sendMode == SendMode.Default && second.sendMode == SendMode.Default, "Undo failed");
        }
        finally
        {
            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(second.gameObject);
        }
    }
    private static void Fairness()
    {
        using (var s = new Sender())
        {
            s.Probe.highEnabled = s.Probe.peerEnabled = true;
            uint a = s.Send(0, capacity: 29).Keys.Single();
            uint b = s.Send(.01, capacity: 29).Keys.Single();
            Check(a != b && s.Send(.02, capacity: 29).Keys.Single() == a, "Equal-priority fields starved");
        }
    }
    private static void FailedOutput()
    {
        using (var s = new Sender())
        {
            s.Probe.pacedEnabled = true;
            Check(s.Send(0, false).Count == 1 && s.Send(.01, false).Count == 1, "Failed first frame was marked sent");
            s.Probe.paced = 88;
            Check(Binary.ReadInt32LE(s.Send(.02)[Hash("paced")], 0) == 88, "Retry used stale candidate");
            s.Probe.paced = 99;
            Check(s.Send(.26).Count == 0, "Interval did not start at output success");
            Check(s.Send(.28).ContainsKey(Hash("paced")), "Latest value lost after failed frame");
        }
    }
    private static void Reconfigure()
    {
        using (var s = new Sender())
        {
            s.Probe.changedEnabled = true;
            s.Send(0);
            s.Probe.changedEnabled = false;
            Check(s.Send(.1).Count == 0, "Disabled field sent");
            s.Probe.changedEnabled = true;
            Check(s.Send(.2).Count == 1, "Reenabled field did not send initial state");
            s.Probe.networkId = 9;
            Check(s.Send(.3).Count == 1, "ID change inherited old send state");
        }
    }
    private static void Capacity()
    {
        using (var s = new Sender())
        {
            s.Probe.bytesEnabled = s.Probe.changedEnabled = true;
            s.Probe.bytes = new byte[512];
            s.Rpcs.Add(new EncoderNativeFrameBuilder.QueuedRpc { NetworkId = 1, RpcHash = 123, Arguments = new object[] { "Toggle", 1 }, RepeatsRemaining = 1 });
            var values = s.Send(0, capacity: 70);
            Check(s.RpcCount == 1 && values.ContainsKey(Hash("changed")), "Large variable starved RPC or smaller variable");
            Check(!values.ContainsKey(Hash("bytes")) && s.State.DeferredCount == 1, "Oversized variable was truncated");
        }
    }
    private static void InvalidIntervals()
    {
        foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(TransSyncSendScheduler.NormalizeInterval(value) == 0 && TransSyncSendScheduler.IsDue(true, 1, 1, value), "Invalid interval blocked sending");
    }
    private static void Metadata()
    {
        using (var s = new Sender())
        {
            s.Probe.highEnabled = s.Probe.pacedEnabled = true;
            TransSyncBindingSnapshot snapshot = TransSyncBindingSnapshotBuilder.Build(false);
            int high = Array.IndexOf(snapshot.FieldNames, "high");
            int paced = Array.IndexOf(snapshot.FieldNames, "paced");
            Check(high >= 0 && paced >= 0 && snapshot.Priorities[high] == 100 && !snapshot.SendOnChange[high]
                && snapshot.MinSendIntervals[paced] == .25f && snapshot.SendOnChange[paced], "Snapshot lost scheduling metadata");
            var owner = new GameObject("Scheduling encoder");
#if UDONSHARP
            var encoder = owner.AddUdonSharpComponent<TSMPEncoder>();
#else
            var encoder = owner.AddComponent<TSMPEncoder>();
#endif
            encoder.autoEncode = false;
            var method = typeof(K13A.TSMP.Editor.TransSyncBindingBuilder).GetMethod("AssignEncoderBindings", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new object[] { encoder, s.Sources.ToArray(), 0 });
            high = Array.IndexOf(encoder.bindingFieldNames, "high");
            paced = Array.IndexOf(encoder.bindingFieldNames, "paced");
            Check(encoder.bindingPriorities[high] == 100 && !encoder.bindingSendOnChange[high] && encoder.bindingMinSendIntervals[paced] == .25f, "Editor builder lost scheduling metadata");
            Object.DestroyImmediate(owner);
        }
    }

    private static void BindingPaths()
    {
        var first = Create("A binding owner");
        var duplicate = Create("Z duplicate owner");
        var rpcOnly = Create("RPC-only owner");
        var owner = new GameObject("Binding endpoints");
        try
        {
            first.networkId = duplicate.networkId = 73;
            rpcOnly.networkId = 74;
            first.highEnabled = first.pacedEnabled = first.collisionEnabled = first.bindingDirectionsEnabled = true;
            duplicate.highEnabled = true;
            var sources = new TSMPNetworkBehaviour[] { duplicate, rpcOnly, first };
            var send = TransSyncBindingSnapshotBuilder.Build(false, sources);
            var receive = TransSyncBindingSnapshotBuilder.Build(true, sources);
            Check(sources[0] == duplicate, "Binding compilation mutated caller order");
            Check(send.Collisions.Length == 2 && receive.Collisions.Length == 2, "Both same-component and cross-component collisions must be diagnosed");
            Check(send.Collisions.All(c => c.FirstTarget == first && c.FirstFieldName == "high"), "Collision ownership must retain the first ordered field");
            Check(send.Targets.Length == 3 && send.Targets.All(t => t == first), "Duplicate send bindings were retained");
            Check(send.FieldNames.SequenceEqual(new[] { "high", "paced", "sendOnly" }), "Send order/direction changed");
            Check(receive.Targets.Length == 5 && receive.FieldNames.Take(3).SequenceEqual(new[] { "high", "paced", "receiveOnly" }), "Receive order/direction changed");
            Check(receive.Targets[3] == rpcOnly && receive.Targets[4] == duplicate
                && receive.FieldNames.Skip(3).All(string.IsNullOrEmpty)
                && receive.VariableHashes.Skip(3).All(h => h == 0)
                && receive.ValueTypes.Skip(3).All(t => t == NetworkFrameProtocol.ValueTypeUnsupported), "RPC-only fallback was lost for empty or collision-only targets");
            Check(send.Priorities[0] == 100 && send.Priorities[2] == 23 && !send.SendOnChange[2]
                && send.MinSendIntervals[2] == .5f && send.SentEvents[2] == nameof(TransSyncSchedulingProbe.TSMPBeforeEncode), "Scheduling options changed while excluding collisions");
            var reversed = TransSyncBindingSnapshotBuilder.Build(false, sources.Reverse().ToArray());
            Check(send.Targets.SequenceEqual(reversed.Targets) && send.FieldNames.SequenceEqual(reversed.FieldNames), "Collision winner depends on caller enumeration order");

#if UDONSHARP
            var encoder = owner.AddUdonSharpComponent<TSMPEncoder>();
            var decoder = owner.AddUdonSharpComponent<TSMPDecoder>();
#else
            var encoder = owner.AddComponent<TSMPEncoder>();
            var decoder = owner.AddComponent<TSMPDecoder>();
#endif
            encoder.autoEncode = false;
            decoder.applyEveryFrame = false;
            const BindingFlags StaticPrivate = BindingFlags.NonPublic | BindingFlags.Static;
            Type setup = typeof(TSMPSetup).Assembly.GetType("K13A.TSMP.SetupApplier", true);
            setup.GetMethod("ApplyEncoderBindings", StaticPrivate).Invoke(null, new object[] { encoder });
            setup.GetMethod("ApplyDecoderBindings", StaticPrivate).Invoke(null, new object[] { decoder });
            CheckBindingTable(encoder, send, true);
            CheckBindingTable(decoder, receive, false);

            Type editor = typeof(K13A.TSMP.Editor.TransSyncBindingBuilder);
            editor.GetMethod("AssignEncoderBindings", StaticPrivate).Invoke(null, new object[] { encoder, sources, 0 });
            editor.GetMethod("RebuildDecoder", StaticPrivate).Invoke(null, new object[] { decoder, sources.Reverse().ToArray() });
            CheckBindingTable(encoder, send, true);
            CheckBindingTable(decoder, receive, false);
            Check(K13A.TSMP.Editor.TransSyncBindingBuilder.TryGetTransSyncCollisionWarning(first, out string firstWarning)
                && firstWarning.Contains("high")
                && K13A.TSMP.Editor.TransSyncBindingBuilder.TryGetTransSyncCollisionWarning(duplicate, out _), "Editor collision diagnostics were lost");
            Results.Add("Expected TransSync collision errors above are the negative fixture: first field wins in both setup and rebuild; RPC-only targets remain available.");
        }
        finally
        {
            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(duplicate.gameObject);
            Object.DestroyImmediate(rpcOnly.gameObject);
            Object.DestroyImmediate(owner);
        }
    }

    private static void CheckBindingTable(Component target, TransSyncBindingSnapshot expected, bool send)
    {
        Check(((Component[])target.GetType().GetField("bindingTargets").GetValue(target)).SequenceEqual(expected.Targets), "Binding targets differ between setup and editor rebuild");
        Check(((ushort[])target.GetType().GetField("bindingNetworkIds").GetValue(target)).SequenceEqual(expected.NetworkIds), "Binding IDs differ");
        Check(((uint[])target.GetType().GetField("bindingVariableHashes").GetValue(target)).SequenceEqual(expected.VariableHashes), "Binding hashes differ");
        Check(((byte[])target.GetType().GetField("bindingValueTypes").GetValue(target)).SequenceEqual(expected.ValueTypes), "Binding types differ");
        Check(((string[])target.GetType().GetField("bindingFieldNames").GetValue(target)).SequenceEqual(expected.FieldNames), "Binding fields differ");
        Check(((int[])target.GetType().GetField("bindingDirections").GetValue(target)).SequenceEqual(expected.Directions), "Binding directions differ");
        Check(((int[])target.GetType().GetField("bindingPriorities").GetValue(target)).SequenceEqual(expected.Priorities), "Binding priorities differ");
#if UDONSHARP
        Check(((UdonBehaviour[])target.GetType().GetField("bindingUdonTargets").GetValue(target)).SequenceEqual(expected.UdonTargets), "Backing Udon targets differ");
#endif
        if (send)
        {
            Check(((bool[])target.GetType().GetField("bindingSendOnChange").GetValue(target)).SequenceEqual(expected.SendOnChange), "Send-on-change metadata differs");
            Check(((float[])target.GetType().GetField("bindingMinSendIntervals").GetValue(target)).SequenceEqual(expected.MinSendIntervals), "Send intervals differ");
            Check(((string[])target.GetType().GetField("bindingSentEvents").GetValue(target)).SequenceEqual(expected.SentEvents), "Sent events differ");
        }
    }

    private static Dictionary<uint, byte[]> ReadValues(byte[] payload, int length)
    {
        var values = new Dictionary<uint, byte[]>();
        int messages = Binary.ReadUInt16LE(payload, 2);
        int offset = 8;
        for (int m = 0; m < messages; m++)
        {
            int end = offset + 8 + Binary.ReadUInt16LE(payload, offset + 6);
            Check(end <= length, "Message exceeds payload");
            if (payload[offset + 2] == NetworkFrameProtocol.MessageTypeVariableState)
            {
                int fields = Binary.ReadUInt16LE(payload, offset + 8);
                Check(fields > 0, "Empty VariableState emitted");
                int cursor = offset + 10;
                for (int f = 0; f < fields; f++)
                {
                    uint hash = Binary.ReadUInt32LE(payload, cursor);
                    int size = Binary.ReadUInt16LE(payload, cursor + 5);
                    Check(cursor + 7 + size <= end, "Value exceeds message");
                    values.Add(hash, payload.Skip(cursor + 7).Take(size).ToArray());
                    cursor += 7 + size;
                }
                Check(cursor == end, "Unexpected message tail");
            }
            offset = end;
        }
        Check(offset == length, "Unexpected payload tail");
        return values;
    }

#if !UDONSHARP
    private static void ManualWrites()
    {
        var source = Create("Manual writer");
        var owner = new GameObject("Manual encoder");
        var encoder = owner.AddComponent<TSMPEncoder>();
        encoder.autoEncode = false;
        var codecOwner = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
        var output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
        try
        {
            output.Create();
            encoder.output = output;
            encoder.selectedCodec = codecOwner.GetComponent<TSMPCodec>();
            encoder.networkBehaviours = new TSMPNetworkBehaviour[] { source };
            encoder.transSyncRefreshInterval = 0;
            source.networkId = 1;
            source.transRpcEncoder = encoder;
            source.manualWrite = source.changedEnabled = true;
            encoder.EncodeNow();
            Check(source.manualWritten && encoder.frameIndex == 1, "Manual API lost its open message context");
            var values = ReadValues((byte[])typeof(TSMPEncoder).GetField("_encodedPayload", Private).GetValue(encoder), encoder.payloadBytes);
            Check(values.Count == 2 && values[1234].SequenceEqual(new byte[] { 1, 2, 3 }), "Manual/automatic fields were not both written");
            encoder.EncodeNow();
            values = ReadValues((byte[])typeof(TSMPEncoder).GetField("_encodedPayload", Private).GetValue(encoder), encoder.payloadBytes);
            Check(encoder.frameIndex == 2 && values.Count == 1 && values.ContainsKey(1234), "Manual write was change-filtered");
        }
        finally
        {
            Object.DestroyImmediate(source.gameObject);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(codecOwner);
            Object.DestroyImmediate(output);
        }
    }
#endif

    private static void Test(string name, Action action)
    {
        try { action(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Finish()
    {
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (failed) throw new InvalidOperationException("TransSync scheduling validation failed");
    }

#if UDONSHARP
    public static void RunVm()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const string path = "Assets/Validation/TransSync/TransSyncSchedulingProbe.asset";
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/TransSync/Runtime/TransSyncSchedulingProbe.cs");
            AssetDatabase.CreateAsset(asset, path);
        }
        bool compileError = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) compileError = true;
        };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= callback; }
        Check(!compileError, "Udon client compilation failed");
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.TransSync.Vm", true);
        AttachVm();
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void AttachVm()
    {
        if (!SessionState.GetBool("TSMP.TransSync.Vm", false)) return;
        EditorApplication.playModeStateChanged -= VmEnteredPlay;
        EditorApplication.playModeStateChanged += VmEnteredPlay;
    }

    private static void VmEnteredPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool("TSMP.TransSync.Vm", false);
        EditorApplication.playModeStateChanged -= VmEnteredPlay;
        EditorApplication.delayCall += () =>
        {
            Test("Real Udon Encoder scheduling, snapshots, intervals, capacity and field bridge", VerifyVm);
            try { Finish(); }
            finally { EditorApplication.Exit(failed ? 1 : 0); }
        };
    }

    private sealed class VmProgram
    {
        public readonly IUdonProgram Program;
        public readonly IUdonVM Vm;
        public readonly UdonBehaviour Backing;
        public VmProgram(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            asset.UpdateProgram();
            Program = asset.GetRealProgram();
            Check(Program != null && Program.ByteCode.Length > 0, "Missing client bytecode: " + path);
            Vm = UdonEditorManager.Instance.ConstructUdonVM();
            Vm.LoadProgram(Program);
            Backing = new GameObject(asset.name + " VM").AddComponent<UdonBehaviour>();
            var type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Program.SymbolTable, Program.Heap }), "Heap references unresolved");
            type.GetField("_program", Private).SetValue(Backing, Program);
            type.GetField("_udonVM", Private).SetValue(Backing, Vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
        }
        public void Set<T>(string name, T value)
        {
            Program.Heap.SetHeapVariable(Program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        }
        public T Get<T>(string name) => (T)Program.Heap.GetHeapVariable(Program.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Vm.SetProgramCounter(Program.EntryPoints.GetAddressFromSymbol(name));
            Check(Vm.Interpret() == 0, "Udon VM event failed: " + name);
        }
    }

    private static void ConfigureVm(VmProgram encoder, VmProgram source, string[] fields, int[] priorities, bool[] changes, float[] intervals)
    {
        encoder.Set("bindingTargets", new Component[0]);
        encoder.Set("bindingUdonTargets", fields.Select(unused => source.Backing).ToArray());
        encoder.Set("bindingNetworkIds", fields.Select(unused => (ushort)1).ToArray());
        encoder.Set("bindingVariableHashes", fields.Select(Hash).ToArray());
        var metadata = TransSyncMetadata.GetOrCreate(null, typeof(TransSyncSchedulingProbe)).Fields;
        encoder.Set("bindingValueTypes", fields.Select(name => (byte)metadata.Single(f => f.FieldInfo.Name == name).ValueType).ToArray());
        encoder.Set("bindingFieldNames", fields);
        encoder.Set("bindingDirections", new int[fields.Length]);
        encoder.Set("bindingPriorities", priorities);
        encoder.Set("bindingSendOnChange", changes);
        encoder.Set("bindingMinSendIntervals", intervals);
    }

    private static Dictionary<uint, byte[]> EncodeVm(VmProgram encoder)
    {
        encoder.Call("ClearFrame");
        uint before = encoder.Get<uint>("frameIndex");
        encoder.Call("EncodeNow");
        Check(string.IsNullOrEmpty(encoder.Get<string>("lastError")), encoder.Get<string>("lastError"));
        if (encoder.Get<uint>("frameIndex") == before)
            return new Dictionary<uint, byte[]>();
        return ReadValues(encoder.Get<byte[]>("_payloadBytes"), encoder.Get<int>("payloadBytes"));
    }

    private static void VerifyVm()
    {
        var source = new VmProgram("Assets/Validation/TransSync/TransSyncSchedulingProbe.asset");
        var encoder = new VmProgram("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
        var output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
        output.Create();
        encoder.Set("output", output);
        encoder.Set("autoEncode", false);
        encoder.Set("clearAfterEncode", false);
        encoder.Set("debugLog", false);
        encoder.Set("transSyncRefreshInterval", 0f);
        encoder.Set("codecId", 0);
        encoder.Call("_onEnable");
        ConfigureVm(encoder, source, new[] { "changed", "bytes", "text" }, new[] { 0, 0, 0 }, new[] { true, true, true }, new float[3]);
        Check(EncodeVm(encoder).Count == 3, "Udon initial fields missing");
        Check(EncodeVm(encoder).Count == 0, "Udon unchanged fields resent");
        byte[] raw = source.Get<byte[]>("bytes");
        raw[1] = 42;
        source.Set("text", "\U0001F441\uFE0F");
        var values = EncodeVm(encoder);
        Check(values.Count == 2 && values[Hash("bytes")][1] == 42, "Udon array alias/change detection failed");
        Check(System.Text.Encoding.UTF8.GetString(values[Hash("text")]) == "\U0001F441\uFE0F", "Udon Unicode corrupted");
        Check(source.Get<int>("captures") == 3, "Udon before-encode event not dispatched once per source");
        Results.Add("PASS Udon scalar/string/array scheduling and real cross-behaviour field/event calls");

        ConfigureVm(encoder, source, new[] { "changed", "poll" }, new int[2], new[] { true, false }, new float[2]);
        Check(EncodeVm(encoder).Count == 2 && EncodeVm(encoder).Keys.Single() == Hash("poll"), "Udon Default ignored field modes");
        source.Set("sendMode", (int)SendMode.OnChange);
        Check(EncodeVm(encoder).Count == 0, "Udon On Change did not suppress polling field");
        encoder.Set("transSyncRefreshInterval", 1f);
        double[] refreshTimes = encoder.Get<double[]>("_sendLastTimes");
        for (int i = 0; i < refreshTimes.Length; i++) refreshTimes[i] = Time.realtimeSinceStartupAsDouble - 2;
        Check(EncodeVm(encoder).Count == 2, "Udon On Change lost refresh");
        encoder.Set("transSyncRefreshInterval", 0f);
        source.Set("sendMode", (int)SendMode.Always);
        int captures = source.Get<int>("captures");
        Check(EncodeVm(encoder).Count == 2 && EncodeVm(encoder).Count == 2, "Udon Always suppressed unchanged fields");
        Check(source.Get<int>("captures") == captures + 2, "Udon BeforeEncode was not called once per source and attempt");
        source.Set("sendMode", (int)SendMode.Default);
        Check(EncodeVm(encoder).Keys.Single() == Hash("poll"), "Udon Default did not restore attributes");
        source.Set("sendMode", 99);
        Check(EncodeVm(encoder).Keys.Single() == Hash("poll"), "Udon invalid mode did not use Default");
        source.Set("sendMode", (int)SendMode.Default);
        Results.Add("PASS Udon live component modes, refresh, fallback and once-per-source mode cache");

        ConfigureVm(encoder, source, new[] { "paced", "poll" }, new int[2], new[] { true, false }, new[] { 1000f, 1000f });
        Check(EncodeVm(encoder).Count == 2, "Udon interval delayed first sample");
        source.Set("paced", 77);
        Check(EncodeVm(encoder).Count == 0, "Udon ignored interval");
        source.Set("sendMode", (int)SendMode.Always);
        Check(EncodeVm(encoder).Count == 0, "Udon Always bypassed the minimum interval or reset timestamps");
        source.Set("sendMode", (int)SendMode.OnChange);
        Check(EncodeVm(encoder).Count == 0, "Udon On Change reset timestamps");
        source.Set("sendMode", (int)SendMode.Default);
        double[] times = encoder.Get<double[]>("_sendLastTimes");
        for (int i = 0; i < times.Length; i++) times[i] = Time.realtimeSinceStartupAsDouble - 2000;
        values = EncodeVm(encoder);
        Check(values.Count == 2 && Binary.ReadInt32LE(values[Hash("paced")], 0) == 77, "Udon interval retry lost latest sample");
        times = encoder.Get<double[]>("_sendLastTimes");
        for (int i = 0; i < times.Length; i++) times[i] = Time.realtimeSinceStartupAsDouble - 2000;
        values = EncodeVm(encoder);
        Check(values.Count == 1 && values.ContainsKey(Hash("poll")), "Udon SendOnChange=false did not poll");
        encoder.Set("transSyncRefreshInterval", 1f);
        values = EncodeVm(encoder);
        Check(values.Count == 1 && values.ContainsKey(Hash("paced")), "Udon refresh did not recover unchanged field");
        encoder.Set("transSyncRefreshInterval", 0f);
        Results.Add("PASS Udon minimum interval, latest-value coalescing, polling and refresh");

        ConfigureVm(encoder, source, new[] { "changed" }, new[] { 0 }, new[] { true }, new[] { 0f });
        EncodeVm(encoder);
        encoder.Set("output", (RenderTexture)null);
        source.Set("changed", 99);
        uint frame = encoder.Get<uint>("frameIndex");
        encoder.Call("EncodeNow");
        Check(encoder.Get<uint>("frameIndex") == frame, "Missing output was marked published");
        encoder.Set("output", output);
        Check(Binary.ReadInt32LE(EncodeVm(encoder)[Hash("changed")], 0) == 99, "Failed output consumed Udon send state");
        var hashes = encoder.Get<uint[]>("bindingVariableHashes");
        hashes[0] = 999;
        Check(EncodeVm(encoder).ContainsKey(999), "Same-size Udon binding mutation retained old snapshot");
        Results.Add("PASS Udon output failure and same-size binding reconfiguration");

        var small = new RenderTexture(640, 128, 0, RenderTextureFormat.ARGB32);
        small.Create();
        encoder.Set("output", small);
        source.Set("bytes", new byte[2000]);
        ConfigureVm(encoder, source, new[] { "bytes", "low", "high" }, new[] { 0, -100, 100 }, new[] { true, false, false }, new float[3]);
        values = EncodeVm(encoder);
        Check(values.ContainsKey(Hash("high")) && values.ContainsKey(Hash("low")) && !values.ContainsKey(Hash("bytes")), "Udon oversized field stopped usable fields");
        Check(encoder.Get<int>("deferredVariableCount") == 1, "Udon capacity deferral missing");
        Check(Binary.ReadUInt32LE(encoder.Get<byte[]>("_payloadBytes"), 18) == Hash("high"), "Udon ignored priority order");
        Check(encoder.Get<Texture2D>("outputTexture").GetPixels32().Any(pixel => pixel.r != 0), "Udon output texture is blank");
        Results.Add("PASS Udon capacity deferral and nonblank Luma4 output");

        var tiny = new RenderTexture(640, 56, 0, RenderTextureFormat.ARGB32);
        tiny.Create();
        encoder.Set("output", tiny);
        ConfigureVm(encoder, source, new[] { "low", "high" }, new[] { -100, 100 }, new[] { false, false }, new float[2]);
        encoder.Set("bindingNetworkIds", new ushort[] { 1, 2 });
        Check(EncodeVm(encoder).Keys.Single() == Hash("high"), "Udon lower priority consumed scarce capacity");
        ConfigureVm(encoder, source, new[] { "high", "peer" }, new[] { 100, 100 }, new[] { false, false }, new float[2]);
        encoder.Set("bindingNetworkIds", new ushort[] { 1, 2 });
        uint first = EncodeVm(encoder).Keys.Single();
        uint second = EncodeVm(encoder).Keys.Single();
        Check(first != second && EncodeVm(encoder).Keys.Single() == first, "Udon equal-priority rotation starved a field");
        Results.Add("PASS Udon strict priority and equal-priority rotation under actual codec capacity");

        encoder.Set("_pendingRpcNetworkIds", new int[32]);
        encoder.Set("_pendingRpcHashes", new uint[32]);
        encoder.Set("_pendingRpcMethodNames", new[] { "Toggle" }.Concat(new string[31]).ToArray());
        encoder.Set("_pendingRpcEventIds", new int[32]);
        encoder.Set("_pendingRpcRepeatsRemaining", new[] { 1 }.Concat(new int[31]).ToArray());
        encoder.Set("_pendingRpcCount", 1);
        Check(EncodeVm(encoder).Count == 0 && encoder.Get<int>("rpcMessageCount") == 1, "Udon variables starved queued RPC");
        Check(encoder.Get<int>("_pendingRpcCount") == 0 && encoder.Get<int>("deferredVariableCount") == 2, "Udon RPC commit/deferral failed");
        Check(EncodeVm(encoder).Count == 1, "Udon fields did not resume after RPC");
        Results.Add("PASS Udon RPC reservation, queue commit and deferred-variable retry");
        VerifyVariableSnapshotsVm(encoder, source, output);
    }

    private static void VerifyVariableSnapshotsVm(VmProgram encoder, VmProgram source, RenderTexture output)
    {
        encoder.Set("output", output); encoder.Set("targetPayloadBytes", 0); encoder.Set("transSyncRefreshInterval", 0f);
        source.Set("sendMode", (int)SendMode.Default);
        ConfigureVm(encoder, source, new[] { "bytes" }, new[] { 0 }, new[] { true }, new[] { 0f });
        int previousLength = -1, tailChecks = 0;
        foreach (int length in SnapshotLengths)
        {
            byte[] body = SnapshotBytes(length); source.Set("bytes", body);
            CheckRawSnapshotResult(EncodeVm(encoder), body, previousLength != length, "Udon variable sequence");
            byte[] snapshot = (byte[])encoder.Get<object[]>("_sendPrevious")[0]; CheckVariableSnapshot(snapshot, body, "Udon variable sequence");
            int valid = NetworkFrameProtocol.VariableValueHeaderBytes + length;
            if (tailChecks == 0 && snapshot.Length > valid)
            {
                snapshot[snapshot.Length - 1] ^= 0xff;
                CheckRawSnapshotResult(EncodeVm(encoder), body, false, "Udon dirty capacity tail"); tailChecks++;
                CheckVariableSnapshot((byte[])encoder.Get<object[]>("_sendPrevious")[0], body, "Udon dirty capacity tail");
            }
            previousLength = length;
        }
        byte[] changed = source.Get<byte[]>("bytes"); changed[0] ^= 1;
        CheckRawSnapshotResult(EncodeVm(encoder), changed, true, "Udon valid-prefix mutation");
        CheckRawSnapshotResult(EncodeVm(encoder), changed, false, "Udon committed mutation");
        byte[] before = (byte[])encoder.Get<object[]>("_sendPrevious")[0], saved = (byte[])before.Clone();
        byte[] grown = SnapshotBytes(32); source.Set("bytes", grown); encoder.Set("output", (RenderTexture)null);
        encoder.Call("ClearFrame"); uint frame = encoder.Get<uint>("frameIndex"); encoder.Call("EncodeNow");
        Check(encoder.Get<uint>("frameIndex") == frame && ReferenceEquals(before, (byte[])encoder.Get<object[]>("_sendPrevious")[0])
            && before.SequenceEqual(saved), "Udon failed length change modified snapshot/publication");
        encoder.Set("output", output);
        CheckRawSnapshotResult(EncodeVm(encoder), grown, true, "Udon failed length-change retry");
        before = (byte[])encoder.Get<object[]>("_sendPrevious")[0]; saved = (byte[])before.Clone();
        grown = SnapshotBytes(64); source.Set("bytes", grown); encoder.Set("targetPayloadBytes", 32);
        Check(EncodeVm(encoder).Count == 0 && encoder.Get<int>("deferredVariableCount") == 1, "Udon large raw field was not deferred");
        Check(ReferenceEquals(before, (byte[])encoder.Get<object[]>("_sendPrevious")[0]) && before.SequenceEqual(saved), "Udon deferral modified snapshot");
        encoder.Set("targetPayloadBytes", 0);
        CheckRawSnapshotResult(EncodeVm(encoder), grown, true, "Udon deferred length-change retry");
        CheckVariableSnapshot((byte[])encoder.Get<object[]>("_sendPrevious")[0], grown, "Udon retry snapshot");
        encoder.Set("streamId", encoder.Get<uint>("streamId") + 1);
        CheckRawSnapshotResult(EncodeVm(encoder), grown, true, "Udon stream reset with equal raw bytes");
        Results.Add("PASS Udon variable snapshot grow/shrink, empty/reference-equivalent suppression, valid-prefix changes, output failure/deferral retry and stream reset; tail checks=" + tailChecks + ". Original exact-length snapshots legitimately have no capacity tail.");
    }
#endif
}
