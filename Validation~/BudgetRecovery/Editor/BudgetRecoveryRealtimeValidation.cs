#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Validation-only wall-clock complement to the deterministic scheduler test.
// Actual EncodeNow owns its clock, state lifetime, serialization and commits.
public static class BudgetRecoveryRealtimeValidation
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const double Duration = 8.5, Period = 1.0 / 30.0;
    static readonly string[] Fields = { "high", "medium", "slow" };
    static readonly float[] Intervals = { 0.1f, 0.4f, 1.5f };
    static readonly List<Case> Cases = new List<Case>();
    static readonly List<string> Trace = new List<string>();
    static readonly List<string> Report = new List<string>();
    static readonly List<Packet> Packets = new List<Packet>();
    static readonly uint[] Crc = Crc32Runtime.EnsureFastTable(null);
    static string Root => Environment.GetEnvironmentVariable("TSMP_BUDGET_RECOVERY_RESULTS");
    static double Clock => Time.realtimeSinceStartupAsDouble;
    static double origin, next, previousTick;
    static uint[] hashes;
    static int tick, checks, missed;
    static double maxTickGap;
    static object Get(object item, string name) => item.GetType().GetField(name, Flags).GetValue(item);
    static void Set(object item, string name, object value) => item.GetType().GetField(name, Flags).SetValue(item, value);
    static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string Row(params object[] items) => string.Join(",", items.Select(item => "\"" + Convert.ToString(item, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static uint Hash(string field) => TransSyncMetadata.GetOrCreate(null, typeof(RefreshRecoveryProbe)).Fields.Single(f => f.FieldInfo.Name == field).VariableHash;
    sealed class Packet
    {
        public byte[] Payload, Header;
        public string Name, HeaderName;
        public FrameHeader Parsed;
        public byte[][] Values;
        public int Mask, RpcCount;
    }
    sealed class Case : IDisposable
    {
        public string Name;
        public double Loss, Changed = -1, LossEnd = -1, Restarted = -1;
        public bool Held, Restart;
        public GameObject Owner, CodecOwner;
        public TSMPEncoder Encoder;
        public TSMPDecoder Decoder;
        public RefreshRecoveryProbe Source, Receiver;
        public RenderTexture Output;
        public readonly bool[] Sent = new bool[3];
        public readonly double[] LastSent = { -1, -1, -1 }, Restored = { -1, -1, -1 }, FirstPostLossSend = { -1, -1, -1 };
        public readonly int[] Previous = { -1, -1, -1 }, Expected = new int[3], Emitted = new int[3];
        public readonly HashSet<uint> EventStreams = new HashSet<uint>();
        public int Value, Writes, NoOutput, CallsBeforeRestart = -1;
        public Packet Visible;
        public Case(string name, double loss, bool held, bool restart, int index)
        {
            Name = name; Loss = loss; Held = held; Restart = restart;
            Owner = new GameObject(name + " actual clock"); Encoder = Owner.AddComponent<TSMPEncoder>(); Encoder.autoEncode = false; Encoder.debugLog = false;
            Check(Encoder.targetPayloadBytes == 0 && Encoder.transSyncRefreshInterval == 1f && Encoder.transRpcRepeatFrames == 4, "Production defaults changed");
            Source = Owner.AddComponent<RefreshRecoveryProbe>(); Receiver = Owner.AddComponent<RefreshRecoveryProbe>(); Source.networkId = Receiver.networkId = 1;
            Encoder.streamId = (uint)(101 + index * 10); Encoder.networkBehaviours = new TSMPNetworkBehaviour[] { Source }; Source.transRpcEncoder = Encoder;
            CodecOwner = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
            Encoder.selectedCodec = CodecOwner.GetComponent<TSMPCodec>();
            Output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32); Check(Output.Create(), "Output allocation failed"); Encoder.output = Output;
            Decoder = Owner.AddComponent<TSMPDecoder>(); Decoder.applyEveryFrame = false; Decoder.debugLog = false;
            Decoder.bindingTargets = Fields.Select(_ => (Component)Receiver).ToArray(); Decoder.bindingNetworkIds = new ushort[] { 1, 1, 1 };
            Decoder.bindingVariableHashes = (uint[])hashes.Clone(); Decoder.bindingFieldNames = Fields; Decoder.bindingDirections = new int[3];
            Decoder.bindingValueTypes = Fields.Select(_ => (byte)NetworkFrameProtocol.ValueTypeRawBytes).ToArray();
        }
        public void Change(int value)
        {
            Value = value;
            foreach (string field in Fields) Set(Source, field, Enumerable.Repeat((byte)value, 32).ToArray());
            Check(Encoder.QueueTransRpc(1, StableHash.Fnv1A32("ReceiveEvent"), "ReceiveEvent", 501), "Event queue rejected");
        }
        public void Dispose()
        {
            Object.DestroyImmediate(Owner); Object.DestroyImmediate(CodecOwner); Output.Release(); Object.DestroyImmediate(Output);
        }
    }
    public static void Run()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Root) || File.Exists(Path.Combine(Root, "result.txt"))) throw new InvalidOperationException("Fresh results required");
            Directory.CreateDirectory(Root); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Check(!Application.isPlaying, "Start from EditMode");
            SessionState.SetBool("TSMP.BudgetRecoveryRealtime", true); AttachPlay(); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }
    [InitializeOnLoadMethod] static void AttachPlay()
    {
        EditorApplication.playModeStateChanged -= EnteredPlay; EditorApplication.playModeStateChanged += EnteredPlay;
    }
    static void EnteredPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.BudgetRecoveryRealtime", false)) return;
        SessionState.SetBool("TSMP.BudgetRecoveryRealtime", false); EditorApplication.delayCall += StartClocked;
    }
    static void StartClocked()
    {
        try
        {
            Check(Application.isPlaying, "Actual PlayMode clock required");
            Application.runInBackground = true;
            hashes = Fields.Select(Hash).ToArray();
            Report.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType);
            Report.Add("Actual Native EncodeNow; production clock=Time.realtimeSinceStartupAsDouble in PlayMode; defaults refresh1s/repeats4/target0 retained. Seven concurrent scenarios,30Hz deadline opportunities,8.5seconds.");
            Cases.Add(new Case("drop-0.1", .1, false, false, 0)); Cases.Add(new Case("drop-0.4", .4, false, false, 1)); Cases.Add(new Case("drop-1.5", 1.5, false, false, 2));
            Cases.Add(new Case("hold-0.4", .4, true, false, 3)); Cases.Add(new Case("hold-1.5-restart", 1.5, true, true, 4)); Cases.Add(new Case("drop-0.1-restart-dedup", .1, false, true, 5));
            // The short-loss boundary may cover all four copies under real scheduler jitter.
            // This intact control guarantees the old stream actually populated deduplication.
            Cases.Add(new Case("no-loss-restart-dedup", 0.0, false, true, 6));
            Trace.Add("case,observation,deadline_index,deadline,tick_entry,missed_deadlines,call_start,send_time,call_end,source_changed,loss_end,restarted,stream,frame,output,payload_bytes,sent_field_mask,rpc_written,queue_count,head_repeats,dropped,held,apply_start,apply_end,high,medium,slow,rpc_calls,expected_rpc_calls,high_last_send,medium_last_send,slow_last_send,gpu_luma4,packet,header,applied_packet");
            origin = Clock; next = origin; previousTick = origin;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }
    static void Tick()
    {
        try
        {
            double now = Clock;
            if (now < next) return;
            if (now - origin >= Duration) { Validate(); Finish(null); return; }
            int index = (int)Math.Floor((now - origin) / Period), missedNow = Math.Max(0, index - tick);
            missed += missedNow; maxTickGap = Math.Max(maxTickGap, now - previousTick); previousTick = now;
            double deadline = origin + index * Period;
            for (int step = 0; step < Cases.Count; step++) Work(Cases[(step + tick) % Cases.Count], index, deadline, now, missedNow);
            tick = index + 1; next = origin + tick * Period;
        }
        catch (Exception error) { Finish(error); }
    }
    static void Work(Case c, int deadlineIndex, double deadline, double tickEntry, int missedNow)
    {
        double begin = Clock;
        if (c.Changed < 0 && begin - origin >= .5) { c.Changed = begin; c.LossEnd = begin + c.Loss; c.Change(42); }
        if (c.Restart && c.Restarted < 0 && begin - origin >= 4.0)
        {
            c.Restarted = begin; c.CallsBeforeRestart = c.Receiver.calls; c.Encoder.streamId++; c.Encoder.ResetFrameIndex(); c.Change(77);
            for (int f = 0; f < 3; f++) { c.Sent[f] = false; c.LastSent[f] = -1; c.Previous[f] = -1; }
        }
        uint frame = c.Encoder.frameIndex; int queueBefore = ((IList)Get(c.Encoder, "_queuedRpcs")).Count;
        c.Encoder.EncodeNow(); double end = Clock, send = (double)Get(c.Encoder, "_sendTime");
        Check(string.IsNullOrEmpty(c.Encoder.lastError), "Actual EncodeNow failed " + c.Encoder.lastError);
        bool output = c.Encoder.frameIndex != frame;
        Check(c.Encoder.frameIndex == frame + (output ? 1u : 0u), "Frame sequence changed unexpectedly");
        int mask = 0, rpcs = 0; Packet packet = null;
        if (output)
        {
            c.Writes++; packet = ReadPacket(c, frame, deadlineIndex); mask = packet.Mask; rpcs = packet.RpcCount;
            Check(rpcs == (queueBefore > 0 ? 1 : 0), "RPC scheduler wrote an unexpected count");
        }
        else { c.NoOutput++; Check(c.Encoder.payloadBytes == 0 && c.Encoder.messageCount == 0, "Empty send retained packet counters"); }
        // Independent eligibility decision against actual production successful-output stamps.
        // No time value is written into the Encoder or scheduler.
        for (int f = 0; f < 3; f++)
        {
            bool due = !c.Sent[f] || send < c.LastSent[f] || send - c.LastSent[f] >= Intervals[f];
            bool changedOrRefreshed = !c.Sent[f] || c.Value != c.Previous[f] || send < c.LastSent[f] || send - c.LastSent[f] >= 1.0f;
            Check(((mask & 1 << f) != 0) == (due && changedOrRefreshed), c.Name + " actual-clock field eligibility mismatch " + Fields[f]);
            if ((mask & 1 << f) == 0) continue;
            Check(packet.Values[f].Length == 32 && packet.Values[f].All(b => b == c.Value), "Encoded state differs from unchanged source");
            double successful = SuccessfulTime(c.Encoder, Fields[f]);
            Check(successful >= send && successful <= end, "Successful stamp outside actual EncodeNow interval");
            c.Sent[f] = true; c.LastSent[f] = successful; c.Previous[f] = c.Value; c.Emitted[f]++;
        }
        bool drop = c.Changed >= 0 && send >= c.Changed && send < c.LossEnd, hold = drop && c.Held;
        double applyStart = -1, applyEnd = -1; string applied = "";
        if (output && !drop)
        {
            for (int f = 0; f < 3; f++) if ((mask & 1 << f) != 0 && c.Value == 42 && c.Changed >= 0 && c.FirstPostLossSend[f] < 0 && send >= c.LossEnd) c.FirstPostLossSend[f] = send;
            c.Visible = packet;
        }
        if ((output && !drop) || (hold && c.Visible != null))
        {
            Packet visible = c.Visible; applyStart = Clock; Deliver(c.Decoder, visible); applyEnd = Clock; applied = visible.Name;
            for (int f = 0; f < 3; f++) if ((visible.Mask & 1 << f) != 0) c.Expected[f] = visible.Values[f][0];
            if (visible.RpcCount > 0) c.EventStreams.Add(visible.Parsed.StreamId);
        }
        for (int f = 0; f < 3; f++)
        {
            byte[] value = (byte[])Get(c.Receiver, Fields[f]); Check(value.Length == 32 && value.All(b => b == c.Expected[f]), "Actual received state does not match visible original packet");
            if (value[0] == 42 && c.Restored[f] < 0) c.Restored[f] = applyEnd;
        }
        Check(c.Receiver.calls == c.EventStreams.Count, "Actual event delivery/dedup differs from visible copies");
        var queue = (IList)Get(c.Encoder, "_queuedRpcs"); int repeats = queue.Count > 0 ? (int)Get(queue[0], "RepeatsRemaining") : 0;
        Trace.Add(Row(c.Name, Trace.Count, deadlineIndex, Number(deadline), Number(tickEntry), missedNow, Number(begin), Number(send), Number(end), Number(c.Changed), Number(c.LossEnd), Number(c.Restarted), c.Encoder.streamId, frame,
            output ? 1 : 0, output ? packet.Payload.Length : 0, mask, rpcs, queue.Count, repeats, drop ? 1 : 0, hold ? 1 : 0, Number(applyStart), Number(applyEnd), c.Receiver.high[0], c.Receiver.medium[0], c.Receiver.slow[0], c.Receiver.calls, c.EventStreams.Count,
            Number(c.LastSent[0]), Number(c.LastSent[1]), Number(c.LastSent[2]), output && c.Encoder.lastFrameUsedGpuLuma4 ? 1 : 0, packet?.Name ?? "", packet?.HeaderName ?? "", applied));
    }
    static double SuccessfulTime(TSMPEncoder encoder, string field)
    {
        foreach (object entry in (IEnumerable)Get(Get(encoder, "_sendState"), "_entries"))
        {
            var metadata = (TransSyncMetadata.Field)Get(entry, "Field");
            if (metadata.FieldInfo.Name == field) return (double)Get(entry, "LastSent");
        }
        throw new InvalidOperationException("Missing successful send entry " + field);
    }
    static Packet ReadPacket(Case c, uint frame, int deadlineIndex)
    {
        var p = new Packet { Payload = ((byte[])Get(c.Encoder, "_encodedPayload")).Take(c.Encoder.payloadBytes).ToArray(), Header = ((byte[])Get(c.Encoder, "_header")).ToArray(), Values = new byte[3][] };
        p.Name = "packets/" + c.Name + "/" + deadlineIndex.ToString("D3") + ".bin"; p.HeaderName = "packets/" + c.Name + "/" + deadlineIndex.ToString("D3") + ".header.bin"; Packets.Add(p);
        Check(FrameHeader.TryRead(p.Header, 0, out p.Parsed), "Actual Encoder header CRC invalid");
        Check(p.Parsed.PayloadSize == p.Payload.Length && p.Parsed.FrameIndex == frame && p.Parsed.StreamId == c.Encoder.streamId, "Actual protected identity/size differs");
        Check(p.Parsed.PayloadCrc32 == Crc32Runtime.Compute(p.Payload, 0, p.Payload.Length, Crc), "Actual Encoder payload CRC invalid");
        int cursor = 8, messages = p.Payload[2] | p.Payload[3] << 8;
        for (int m = 0; m < messages; m++)
        {
            Check(cursor + 8 <= p.Payload.Length, "Message bounds"); int end = cursor + 8 + (p.Payload[cursor + 6] | p.Payload[cursor + 7] << 8), type = p.Payload[cursor + 2];
            Check(end <= p.Payload.Length, "Message tail bounds");
            if (type == NetworkFrameProtocol.MessageTypeVariableState)
            {
                int count = p.Payload[cursor + 8] | p.Payload[cursor + 9] << 8, value = cursor + 10;
                for (int f = 0; f < count; f++)
                {
                    Check(value + 7 <= end, "Entry bounds"); int length = p.Payload[value + 5] | p.Payload[value + 6] << 8;
                    uint hash = (uint)(p.Payload[value] | p.Payload[value + 1] << 8 | p.Payload[value + 2] << 16 | p.Payload[value + 3] << 24);
                    int index = Array.IndexOf(hashes, hash); Check(index >= 0 && (p.Mask & 1 << index) == 0 && value + 7 + length <= end, "Unexpected/duplicate field " + c.Name + "; key=" + hash + "; index=" + index + "; mask=" + p.Mask + "; length=" + length + "; cursor=" + value + "; end=" + end);
                    p.Values[index] = p.Payload.Skip(value + 7).Take(length).ToArray(); p.Mask |= 1 << index; value += 7 + length;
                }
                Check(value == end, "Variable message tail");
            }
            else { Check(type == NetworkFrameProtocol.MessageTypeRpcCall, "Unexpected message type"); p.RpcCount++; }
            cursor = end;
        }
        Check(cursor == p.Payload.Length, "Network packet tail"); Check(p.RpcCount == c.Encoder.rpcMessageCount, "RPC diagnostic differs from bytes");
        return p;
    }
    static void Deliver(TSMPDecoder decoder, Packet packet)
    {
        Set(decoder, "_payloadBytes", packet.Payload); Set(decoder, "_payloadDataBytes", packet.Payload.Length); Set(decoder, "_headerBytes", packet.Header);
        Set(decoder, "_requestRequirePayloadCrc32", true); Set(decoder, "_requestSafetyMode", 0); Set(decoder, "_requestUseHeaderLayout", false); Set(decoder, "_activeSlot", -1);
        decoder.lastHeaderValid = true; decoder.lastFrameValid = false; decoder.lastFrameIndex = packet.Parsed.FrameIndex; decoder.lastStreamId = packet.Parsed.StreamId; decoder.lastError = "";
        typeof(TSMPDecoder).GetMethod("CompletePayload", Flags).Invoke(decoder, null);
        Check(decoder.lastFrameValid && decoder.lastPayloadCrcVerified, "Actual protected application failed " + decoder.lastError);
    }
    static void Validate()
    {
        foreach (Case c in Cases)
        {
            Check(c.Changed >= 0 && c.NoOutput > 100 && c.Writes < 50, "Realtime workload did not exercise unchanged refresh");
            for (int f = 0; f < 3; f++)
            {
                Check(c.Restored[f] >= c.FirstPostLossSend[f] && c.FirstPostLossSend[f] >= c.LossEnd, "Missing/early actual-clock recovery " + c.Name + "/" + Fields[f]);
                Check(c.FirstPostLossSend[f] <= c.LossEnd + Math.Max(1.0, Intervals[f]) + maxTickGap + .1, "Eligible refresh recovery bound exceeded " + c.Name + "/" + Fields[f]);
            }
            if (c.Restart) { Check(c.Restarted >= 0 && c.Receiver.calls == c.CallsBeforeRestart + 1 && c.Receiver.high.All(b => b == 77) && c.Receiver.medium.All(b => b == 77) && c.Receiver.slow.All(b => b == 77), "New stream state/event did not recover exactly once"); }
            if (c.Name == "no-loss-restart-dedup") Check(c.CallsBeforeRestart == 1 && c.Receiver.calls == 2, "Intact old stream did not populate deduplication before event ID reuse");
            Report.Add(c.Name + ": change=" + Number(c.Changed - origin) + "; lossEnd=" + Number(c.LossEnd - origin) + "; firstPostLossHigh/Medium/SlowSend=" + string.Join("/", c.FirstPostLossSend.Select(v => Number(v - origin))) + "; firstApply=" + string.Join("/", c.Restored.Select(v => Number(v - origin))) + "; emitted=" + string.Join("/", c.Emitted) + "; output=" + c.Writes + "; noOutput=" + c.NoOutput + "; events=" + c.Receiver.calls + "; preRestartEvents=" + c.CallsBeforeRestart);
        }
        Report.Add("ClockOrigin=" + Number(origin) + "; observedDuration=" + Number(Clock - origin) + "; nextDeadlineIndex=" + tick + "; missedDeadlineSlots=" + missed + "; maxTickGap=" + Number(maxTickGap));
        Report.Add("Eligibility checked each actual EncodeNow against its _sendTime and prior successful Entry.LastSent; no clock injection. RPC expectation derives actual visible copy times, allowing 0.1s boundary jitter. Native GPU Luma4 usage recorded per emitted packet; Decoder completion uses original protected bytes, bypassing readback/header duplicate suppression. No network/live-client/FPS claim.");
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        if (error != null) Report.Add(error.ToString());
        foreach (Packet p in Packets) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, p.Name))); File.WriteAllBytes(Path.Combine(Root, p.Name), p.Payload); File.WriteAllBytes(Path.Combine(Root, p.HeaderName), p.Header); }
        File.WriteAllLines(Path.Combine(Root, "realtime-trace.csv"), Trace);
        File.WriteAllLines(Path.Combine(Root, "result.txt"), new[] { error == null ? "PASS" : "FAIL", "Assertions=" + checks }.Concat(Report));
        foreach (Case c in Cases) c.Dispose(); Cases.Clear();
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif

