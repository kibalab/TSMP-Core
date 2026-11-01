#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

// Validation only. Frame counters are observations of the last completed profiler
// frame, not measurements of work occurring inside an individual VM invocation.
public sealed class BindingCostTelemetry : IDisposable
{
    public readonly struct Snapshot
    {
        public readonly int frame, gc0, gc1, gc2, validMask, allocatedCount, usedCount, collectCount;
        public readonly long ticks, takeTicks, allocatedThread, allocatedFrame, usedBytes, collectNanoseconds;
        internal Snapshot(int frame, int gc0, int gc1, int gc2, int validMask, int allocatedCount, int usedCount, int collectCount,
            long ticks, long takeTicks, long allocatedThread, long allocatedFrame, long usedBytes, long collectNanoseconds)
        {
            this.frame = frame; this.gc0 = gc0; this.gc1 = gc1; this.gc2 = gc2; this.validMask = validMask;
            this.allocatedCount = allocatedCount; this.usedCount = usedCount; this.collectCount = collectCount;
            this.ticks = ticks; this.takeTicks = takeTicks; this.allocatedThread = allocatedThread;
            this.allocatedFrame = allocatedFrame; this.usedBytes = usedBytes; this.collectNanoseconds = collectNanoseconds;
        }
    }

    private struct Entry { public string name, boundary; public int sample; public bool measured; public Snapshot value; }
    private readonly string root;
    private readonly List<string> notes;
    private readonly Entry[] entries;
    private int next, countVerifiedMask;
    private bool initialized, scopedVerified, allocatedVerified, usedVerified, collectVerified;
    private ProfilerRecorder allocated, used, collect;
    private byte[] allocationControl;

    private BindingCostTelemetry(string root, List<string> notes, int capacity)
    {
        this.root = root; this.notes = notes; entries = new Entry[capacity];
    }

    public static BindingCostTelemetry Create(string root, List<string> notes, int maxSnapshots)
    {
        if (Environment.GetEnvironmentVariable("TSMP_BINDING_COST_TELEMETRY") != "1")
        { notes.Add("TailTelemetry=off; no recorders, control collections, extra frames or telemetry sample storage."); return null; }
        if (maxSnapshots <= 0) throw new ArgumentOutOfRangeException(nameof(maxSnapshots));
        return new BindingCostTelemetry(root, notes, maxSnapshots);
    }

    public IEnumerator Initialize()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var inventory = new List<string> { "name,category,unit,selected" };
        foreach (var handle in handles)
        {
            var description = ProfilerRecorderHandle.GetDescription(handle);
            string name = description.Name, unit = description.UnitType.ToString(), selected = "";
            if (name == "GC Allocated In Frame" && unit == "Bytes" && !allocated.Valid) { allocated = Start(handle); selected = "frameAllocated"; }
            else if (name == "GC Used Memory" && unit == "Bytes" && !used.Valid) { used = Start(handle); selected = "usedMemory"; }
            else if (name == "GC.Collect" && unit == "TimeNanoseconds" && !collect.Valid) { collect = Start(handle); selected = "gcCollect"; }
            inventory.Add(Row(name, description.Category.Name, unit, selected));
        }
        File.WriteAllLines(Path.Combine(root, "telemetry-inventory.csv"), inventory);
        notes.Add("TailTelemetry=on; initialization and positive controls run before case warmup. StopwatchFrequency=" + Stopwatch.Frequency);
        notes.Add("Telemetry boundaries: 0 before Encode, 1 after Encode, 2 before Decode, 3 after Decode; string boundaries are caller-defined. Take/Record are outside VM timers.");
        notes.Add("ProfilerRecorder values are last completed profiler-frame observations. No exact Unity frame mapping or per-VM attribution is claimed; validMask records recorder validity independently of positive controls. Unsupported/unverified values are -1.");
        yield return null;

        int allocationFrame = Time.frameCount;
        long usedBefore = Read(used, out _, out _), scopedBefore = ScopedRaw();
        allocationControl = new byte[65536];
        for (int i = 0; i < allocationControl.Length; i += 4096) allocationControl[i] = 1;
        long scopedAfter = ScopedRaw(), scopedDelta = scopedBefore >= 0 && scopedAfter >= scopedBefore ? scopedAfter - scopedBefore : -1;
        scopedVerified = scopedDelta >= allocationControl.Length;
        yield return null;

        long allocationValue = Read(allocated, out bool allocationValid, out int allocationCount);
        long usedAfter = Read(used, out bool usedValid, out int usedSamples);
        allocatedVerified = allocationValid && allocationCount > 0 && allocationValue >= allocationControl.Length;
        usedVerified = usedValid && usedSamples > 0 && usedBefore >= 0 && usedAfter - usedBefore >= allocationControl.Length;
        int forcedGcFrame = Time.frameCount;
        int before0 = GC.CollectionCount(0), before1 = GC.CollectionCount(1), before2 = GC.CollectionCount(2);
        GC.KeepAlive(allocationControl); allocationControl = null;
        GC.Collect();
        int after0 = GC.CollectionCount(0), after1 = GC.CollectionCount(1), after2 = GC.CollectionCount(2);
        if (after0 > before0) countVerifiedMask |= 1;
        if (after1 > before1) countVerifiedMask |= 2;
        if (after2 > before2) countVerifiedMask |= 4;
        yield return null;

        long collectValue = Read(collect, out bool collectValid, out int collectSamples);
        collectVerified = collectValid && collectSamples > 0 && collectValue > 0;
        File.WriteAllLines(Path.Combine(root, "telemetry-control.csv"), new[] {
            "allocationFrame,forcedGcFrame,knownBytes,scopedBefore,scopedAfter,scopedDelta,scopedVerified,allocatedRecorderValid,allocatedRecorderCount,allocatedFrameRaw,allocatedVerified,usedBeforeRaw,usedAfterRaw,usedVerified,gc0Before,gc0After,gc1Before,gc1After,gc2Before,gc2After,collectionCountVerifiedMask,collectRecorderValid,collectRecorderCount,collectNanosecondsRaw,collectVerified",
            Row(allocationFrame, forcedGcFrame, 65536, scopedBefore, scopedAfter, scopedDelta, scopedVerified,
                allocationValid, allocationCount, allocationValue, allocatedVerified, usedBefore, usedAfter, usedVerified,
                before0, after0, before1, after1, before2, after2, countVerifiedMask, collectValid, collectSamples, collectValue, collectVerified)
        });
        notes.Add("TelemetryPositiveControl=65536 retained touched bytes; scopedDelta=" + scopedDelta + "; scopedVerified=" + scopedVerified +
            "; frameAllocationVerified=" + allocatedVerified + "; usedMemoryVerified=" + usedVerified + "; collectionCountVerifiedMask=" + countVerifiedMask + "; collectMarkerVerified=" + collectVerified);
        notes.Add("Frame-allocation/used-memory control checks observe the known allocation amid background Editor activity; they do not isolate its exact byte contribution. Raw control values are retained. Unchanged collection counts or a zero GC.Collect marker do not exclude incremental GC work.");
        if (allocated.Valid) allocated.Reset();
        if (used.Valid) used.Reset();
        if (collect.Valid) collect.Reset();
        initialized = true;
    }

    private static ProfilerRecorder Start(ProfilerRecorderHandle handle)
    {
        return new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
    }

    private static long ScopedRaw()
    {
        try { return GC.GetAllocatedBytesForCurrentThread(); }
        catch (NotSupportedException) { return -1; }
    }

    private static long Read(ProfilerRecorder recorder, out bool valid, out int count)
    {
        valid = recorder.Valid; count = valid ? recorder.Count : 0;
        return valid && count > 0 ? recorder.LastValue : -1;
    }

    public Snapshot Take()
    {
        if (!initialized) throw new InvalidOperationException("Initialize telemetry before warmup.");
        long start = Stopwatch.GetTimestamp(); int frame = Time.frameCount;
        int g0 = (countVerifiedMask & 1) != 0 ? GC.CollectionCount(0) : -1;
        int g1 = (countVerifiedMask & 2) != 0 ? GC.CollectionCount(1) : -1;
        int g2 = (countVerifiedMask & 4) != 0 ? GC.CollectionCount(2) : -1;
        long a = Read(allocated, out bool av, out int ac), u = Read(used, out bool uv, out int uc), c = Read(collect, out bool cv, out int cc);
        long s = scopedVerified ? ScopedRaw() : -1, end = Stopwatch.GetTimestamp();
        return new Snapshot(frame, g0, g1, g2, (av ? 1 : 0) | (uv ? 2 : 0) | (cv ? 4 : 0), ac, uc, cc,
            start, end - start, s, allocatedVerified ? a : -1, usedVerified ? u : -1, collectVerified ? c : -1);
    }

    public void Record(string name, int sample, bool measured, int boundary, Snapshot snapshot)
    {
        string label;
        switch (boundary) { case 0: label = "beforeEncode"; break; case 1: label = "afterEncode"; break; case 2: label = "beforeDecode"; break; case 3: label = "afterDecode"; break; default: throw new ArgumentOutOfRangeException(nameof(boundary)); }
        Record(name, sample, measured, label, snapshot);
    }

    public void Record(string name, int sample, bool measured, string boundary, Snapshot snapshot)
    {
        if (next == entries.Length) throw new InvalidOperationException("Telemetry snapshot capacity exceeded; preserve the failed run.");
        entries[next++] = new Entry { name = name, sample = sample, measured = measured, boundary = boundary, value = snapshot };
    }

    public void Write()
    {
        using (var writer = new StreamWriter(Path.Combine(root, "telemetry.csv"), false))
        {
            writer.WriteLine("case,sample,measured,boundary,observedUnityFrame,observationTicks,takeTicks,gc0,gc1,gc2,allocatedThreadBytes,frameAllocatedBytes,usedMemoryBytes,collectNanoseconds,recorderValidMask,allocatedRecorderCount,usedRecorderCount,collectRecorderCount");
            for (int i = 0; i < next; i++)
            {
                Entry e = entries[i]; Snapshot s = e.value;
                writer.WriteLine(Row(e.name, e.sample, e.measured, e.boundary, s.frame, s.ticks, s.takeTicks, s.gc0, s.gc1, s.gc2,
                    s.allocatedThread, s.allocatedFrame, s.usedBytes, s.collectNanoseconds, s.validMask, s.allocatedCount, s.usedCount, s.collectCount));
            }
        }
    }

    public void Dispose()
    {
        if (allocated.Valid) allocated.Dispose();
        if (used.Valid) used.Dispose();
        if (collect.Valid) collect.Dispose();
    }

    private static string Row(params object[] values)
    {
        string[] parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++) parts[i] = "\"" + Convert.ToString(values[i], CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
        return string.Join(",", parts);
    }
}
#endif
