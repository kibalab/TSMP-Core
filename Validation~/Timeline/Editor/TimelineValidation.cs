using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
#if UDONSHARP
using UdonSharpEditor;
#endif

public static class TimelineValidation
{
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Test("First playing packet ignores drift threshold", FirstPlaying);
        Test("Paused packet builds and evaluates a graph", Paused);
        Test("Repeated stopped packets do not rebuild graphs", RepeatedStop);
        Test("Malformed packets have no Director side effects", Malformed);
        Test("Missing source clears old captured bytes", MissingSource);
        Test("Negative thresholds cannot force identical evaluations", Threshold);
        Test("Resume after Stop starts a valid graph", ResumeStopped);
        Test("Director replacement forces an exact initial seek", Replacement);
        Test("Continuous receive corrects drift between packets", Continuous);
        Test("Loop correction follows the shortest time offset", Loop);
        Test("None cancels pending corrections without stopping playback", None);
        Test("Packet size and version are validated", PacketShape);
        Test("Disabled receivers cancel interpolation", Disabled);
        Test("Disabled Directors and inactive parents reject capture and receive", DirectorAvailability);
        Test("Asset replacement clears the received timeline", AssetReplacement);
        Test("Seek preserves playback state and clamps time", Seek);
        Test("Captured timeline drives an actual animation track", AnimationTrack);
        Test("Continuous follows the playback clock without adding drift", PlaybackClock);
        Test("Missing samples cancel pending correction", MissingSample);
        Test("Malformed packet warnings are rate limited", WarningRate);
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"),
            (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (failed) throw new InvalidOperationException("Timeline regression failed");
    }

    private static void Test(string name, Action action)
    {
        try { action(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static TSMPNetworkTimelineSync Create()
    {
        var go = new GameObject("Timeline Sync");
#if UDONSHARP
        var sync = go.AddUdonSharpComponent<TSMPNetworkTimelineSync>();
#else
        var sync = go.AddComponent<TSMPNetworkTimelineSync>();
#endif
        sync.director = CreateDirector();
        return sync;
    }

    private static PlayableDirector CreateDirector()
    {
        var director = new GameObject("Director").AddComponent<PlayableDirector>();
        director.playOnAwake = false;
        director.timeUpdateMode = DirectorUpdateMode.Manual;
        var asset = ScriptableObject.CreateInstance<TimelineAsset>();
        asset.durationMode = TimelineAsset.DurationMode.FixedLength;
        asset.fixedDuration = 10;
        asset.CreateTrack<ActivationTrack>(null, "Track");
        director.playableAsset = asset;
        return director;
    }

    private static void Receive(TSMPNetworkTimelineSync sync, byte state, float time, float duration = 10)
    {
        byte[] packet = new byte[10];
        packet[0] = 1;
        packet[1] = state;
        Binary.WriteFloat32LE(packet, 2, time);
        Binary.WriteFloat32LE(packet, 6, duration);
        sync.timelineBytes = packet;
        sync.OnTSMPVariableReceived();
    }

    private static void Tick(TSMPNetworkTimelineSync sync, float delta)
    {
        var method = typeof(TSMPNetworkTimelineSync).GetMethod("TickTimeline", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(method != null, "Continuous Timeline update is missing");
        method.Invoke(sync, new object[] { delta });
    }

    private static void FirstPlaying()
    {
        var sync = Create();
        sync.timeApplyThreshold = 10;
        sync.director.initialTime = 1;
        Receive(sync, 2, .3f);
        Check(sync.director.state == PlayState.Playing, "Playing was not applied");
        Check(Math.Abs(sync.director.time - .3) < .0001, "Initial Play lost the received time");
        sync.director.Stop();
    }

    private static void Paused()
    {
        var sync = Create();
        int starts = 0;
        sync.director.played += unused => starts++;
        Receive(sync, 1, 3);
        Check(starts == 0, "Paused packet emitted a playback-start event");
        Check(sync.director.playableGraph.IsValid() && sync.director.state == PlayState.Paused, "Paused graph was not prepared");
        Check(sync.director.time == 3, "Paused seek was lost");
        Receive(sync, 2, 4);
        Check(sync.director.state == PlayState.Playing && sync.director.time == 4, "Resume discarded the packet time");
        sync.director.Stop();
    }

    private static void RepeatedStop()
    {
        var sync = Create();
        sync.Play();
        int stops = 0;
        sync.director.stopped += unused => stops++;
        for (int i = 0; i < 3; i++) Receive(sync, 0, 0);
        Check(stops == 1, "Repeated stopped packets rebuilt and stopped graphs: " + stops);
        Check(!sync.director.playableGraph.IsValid(), "Stop left a graph active");
    }

    private static void Malformed()
    {
        var sync = Create();
        Receive(sync, 2, 2);
        var cases = new[] { new[] { 255f, 4f, 10f }, new[] { 2f, float.NaN, 10f }, new[] { 2f, float.PositiveInfinity, 10f },
            new[] { 2f, -1f, 10f }, new[] { 2f, 4f, float.NaN }, new[] { 2f, 4f, -1f }, new[] { 2f, 11f, 10f } };
        foreach (var values in cases)
        {
            Receive(sync, (byte)values[0], values[1], values[2]);
            Check(sync.director.state == PlayState.Playing && sync.director.time == 2, "Malformed packet changed playback");
        }
        sync.director.Stop();
    }

    private static void MissingSource()
    {
        var sync = Create();
        sync.TSMPBeforeEncode();
        Check(sync.encodedTimelineBytes == 10, "Initial capture failed");
        sync.director = null;
        sync.TSMPBeforeEncode();
        Check(sync.encodedTimelineBytes == 0 && (sync.timelineBytes == null || sync.timelineBytes.Length == 0), "Removed source retained old payload");
    }

    private static void Threshold()
    {
        var sync = Create();
        sync.timeApplyThreshold = -1;
        Receive(sync, 2, 2);
        Receive(sync, 2, 2);
        Check(sync.director.time == 2, "Invalid threshold changed the time");
        sync.director.Stop();
    }

    private static void ResumeStopped()
    {
        var sync = Create();
        sync.Resume();
        Check(sync.director.playableGraph.IsValid() && sync.director.state == PlayState.Playing, "Resume did not start a stopped Director");
        sync.director.Stop();
    }

    private static void Replacement()
    {
        var sync = Create();
        Receive(sync, 2, 5);
        sync.director.Stop();
        sync.director = CreateDirector();
        sync.timeApplyThreshold = 10;
        Receive(sync, 2, 5);
        Check(sync.director.time == 5, "Replacement Director did not seek to the first packet");
        sync.director.Stop();
    }

    private static void Continuous()
    {
        var sync = Create();
        sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        sync.continuousInterpolationRate = 1;
        sync.timeApplyThreshold = 0;
        Receive(sync, 2, 2);
        Receive(sync, 2, 4);
        Check(sync.director.time == 2, "Continuous receive snapped immediately");
        Tick(sync, .1f);
        Check(sync.director.time > 2 && sync.director.time < 4, "Continuous receive did not smooth drift");
        Receive(sync, 1, 4);
        Tick(sync, .1f);
        Check(sync.director.time == 4 && sync.director.state == PlayState.Paused, "Paused Timeline kept interpolating");
        sync.director.Stop();
    }

    private static void Loop()
    {
        var sync = Create();
        sync.director.extrapolationMode = DirectorWrapMode.Loop;
        sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        sync.continuousInterpolationRate = 1;
        sync.timeApplyThreshold = 0;
        Receive(sync, 2, 9.9f);
        Receive(sync, 2, .1f);
        Tick(sync, .1f);
        Check(sync.director.time > 9.9 && sync.director.time < 10, "Loop correction rewound across the whole clip");
        sync.director.Stop();
    }

    private static void None()
    {
        var sync = Create();
        sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        Receive(sync, 2, 2);
        Receive(sync, 2, 4);
        sync.receiveInterpolation = ReceiveInterpolationMode.None;
        Receive(sync, 0, 0);
        Tick(sync, .1f);
        Check(sync.director.state == PlayState.Playing && sync.director.time == 2, "None changed playback");
        sync.director.Stop();
    }

    private static void PacketShape()
    {
        var sync = Create();
        Receive(sync, 2, 2);
        foreach (int size in new[] { 0, 1, 9, 11 })
        {
            sync.timelineBytes = new byte[size];
            sync.OnTSMPVariableReceived();
            Check(sync.director.time == 2 && sync.director.state == PlayState.Playing, "Invalid packet size affected playback: " + size);
        }
        Receive(sync, 2, 2);
        sync.timelineBytes[0] = 255;
        sync.timelineBytes[1] = 0;
        sync.OnTSMPVariableReceived();
        Check(sync.director.state == PlayState.Playing, "Unknown version stopped playback");
        sync.director.Stop();
    }

    private static void Disabled()
    {
        var sync = Create();
        sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        Receive(sync, 2, 2);
        Receive(sync, 2, 4);
        sync.enabled = false;
        Tick(sync, .1f);
        sync.enabled = true;
        Tick(sync, .1f);
        Check(sync.director.time == 2, "Disabled receiver retained a correction");
        sync.director.gameObject.SetActive(false);
        Receive(sync, 2, 5);
        Check(sync.director.time != 5, "Inactive Director accepted a packet");
        sync.director.Stop();
    }

    private static void DirectorAvailability()
    {
        var sync = Create();
        var parent = new GameObject("Director Parent");
        sync.director.transform.SetParent(parent.transform);
        for (int mode = 0; mode < 2; mode++)
        {
            Receive(sync, 1, 2);
            sync.TSMPBeforeEncode();
            Check(sync.encodedTimelineBytes == 10, "Active Director did not capture");
            if (mode == 0) sync.director.enabled = false;
            else parent.SetActive(false);
            Check(sync.director.gameObject.activeSelf, "Parent case changed the child's own active state");
            Receive(sync, 1, 5);
            Check(sync.director.time != 5, "Unavailable Director accepted a packet");
            sync.Seek(7);
            Check(sync.director.time != 7, "Unavailable Director accepted Seek");
            sync.TSMPBeforeEncode();
            Check(sync.timelineBytes == null && sync.encodedTimelineBytes == 0, "Unavailable Director retained captured bytes");
            if (mode == 0) sync.director.enabled = true;
            else parent.SetActive(true);
            Receive(sync, 1, 3);
            Check(sync.director.time == 3, "Reactivated Director did not recover");
        }
        sync.director.Stop();
    }

    private static void AssetReplacement()
    {
        var sync = Create();
        Receive(sync, 2, 5);
        var replacement = CreateDirector();
        sync.director.playableAsset = replacement.playableAsset;
        sync.timeApplyThreshold = 10;
        Receive(sync, 2, 4);
        Check(sync.director.time == 4, "New asset retained old correction state");
        sync.director.playableAsset = null;
        sync.TSMPBeforeEncode();
        Check(sync.encodedTimelineBytes == 0 && sync.timelineBytes == null, "Removed asset retained outgoing data");
        sync.director.Stop();
    }

    private static void Seek()
    {
        var sync = Create();
        int starts = 0;
        sync.director.played += unused => starts++;
        sync.Seek(3);
        Check(starts == 0, "Stopped seek emitted a playback-start event");
        Check(sync.director.state == PlayState.Paused && sync.director.time == 3, "Stopped seek did not prepare a paused graph");
        sync.Play();
        sync.Seek(5);
        Check(sync.director.state == PlayState.Playing && sync.director.time == 5, "Playing seek changed playback state");
        sync.Pause();
        sync.Seek(100);
        Check(sync.director.state == PlayState.Paused && sync.director.time == 10, "Seek did not clamp to duration");
        sync.Seek(float.NaN);
        Check(sync.director.time == 10, "Invalid seek changed time");
        sync.Stop();
    }

    private static void AnimationTrack()
    {
        var sender = Create();
        var receiver = Create();
        var animator = new GameObject("Animated target").AddComponent<Animator>();
        var asset = (TimelineAsset)receiver.director.playableAsset;
        var track = asset.CreateTrack<AnimationTrack>(null, "Position");
        track.trackOffset = TrackOffset.ApplySceneOffsets;
        var clip = track.CreateClip<AnimationPlayableAsset>();
        var animation = new AnimationClip();
        animation.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 10, 10));
        ((AnimationPlayableAsset)clip.asset).clip = animation;
        clip.duration = 10;
        receiver.director.SetGenericBinding(track, animator);
        sender.Seek(3);
        sender.TSMPBeforeEncode();
        receiver.timelineBytes = (byte[])sender.timelineBytes.Clone();
        receiver.OnTSMPVariableReceived();
        Check(Math.Abs(animator.transform.localPosition.x - 3) < .01f, "Animation track did not apply received time: " + animator.transform.localPosition.x);
        sender.Seek(6);
        sender.TSMPBeforeEncode();
        receiver.timelineBytes = (byte[])sender.timelineBytes.Clone();
        receiver.OnTSMPVariableReceived();
        Check(Math.Abs(animator.transform.localPosition.x - 6) < .01f, "Subsequent sample did not update the animation track");
        sender.Stop();
        receiver.Stop();
    }

    private static void PlaybackClock()
    {
        var sync = Create();
        sync.director.timeUpdateMode = DirectorUpdateMode.GameTime;
        sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        sync.continuousInterpolationRate = 1;
        Receive(sync, 2, 2);
        sync.director.time = 2.1;
        Tick(sync, .1f);
        Check(Math.Abs(sync.director.time - 2.1) < .0001, "Prediction introduced drift into synchronized playback");
        Receive(sync, 2, 4);
        sync.director.time = 2.2;
        Tick(sync, .1f);
        Check(Math.Abs(sync.director.time - 2.39) < .0001, "Correction did not account for the advancing target");
        sync.Stop();
    }

    private static void MissingSample()
    {
        var sync = Create();
        sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
        Receive(sync, 2, 2);
        Receive(sync, 2, 4);
        sync.timelineBytes = null;
        sync.OnTSMPVariableReceived();
        Tick(sync, .1f);
        Check(sync.director.time == 2, "Missing source retained a correction");
        sync.Stop();
    }

    private static void WarningRate()
    {
        var sync = Create();
        typeof(TSMPNetworkTimelineSync).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sync, null);
        int count = 0;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Warning && message.StartsWith("[TSMP Timeline] ")) count++;
        };
        Application.logMessageReceived += callback;
        try { for (int i = 0; i < 10; i++) Receive(sync, 255, 2); }
        finally { Application.logMessageReceived -= callback; }
        Check(count == 1, "Invalid packets did not emit one rate-limited warning: " + count);
    }
}
