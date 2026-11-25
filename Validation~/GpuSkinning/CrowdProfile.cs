#if !UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K13A.TSMP.Udon;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CrowdProfile : MonoBehaviour
{
    public SkinProfile source;
    public Quaternion[] finalArm;
    const int Warm = 64, Samples = 160;
    readonly FrameTiming[] timing = new FrameTiming[1], measured = new FrameTiming[Samples];
    readonly bool[] fresh = new bool[Samples];
    RenderTexture stream;
    TSMPNetworkHumanoidPoseSync[] receivers;
    CustomRenderTexture[] poses;
    Camera view;
    int frame, changes, settle;
    uint last;
    ulong stamp;
    float deadline;

    void Start()
    {
        try { Setup(); }
        catch (Exception e) { Fail(e); }
    }

    void Setup()
    {
        var d = source.decoder;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        view = FindObjectOfType<Camera>();
        view.enabled = false;
        view.targetTexture = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
        stream = new RenderTexture(1280, 720, 0, source.images[0].graphicsFormat) { filterMode = FilterMode.Point };
        stream.Create();
        d.sourceTexture = stream;
        int n = finalArm.Length, cols = Mathf.CeilToInt(Mathf.Sqrt(n)), rows = (n + cols - 1) / cols;
        float cell = 2 * view.orthographicSize;
        view.orthographicSize *= Mathf.Max(rows, cols * .75f);
        var first = source.receiver ? source.receiver.animator : source.proxy.GetComponentInParent<Animator>();
        var origin = first.transform.position;
        poses = source.proxy ? new CustomRenderTexture[n] : null;
        receivers = source.receiver ? new TSMPNetworkHumanoidPoseSync[n] : null;
        for (int i = 0; i < n; i++)
        {
            var rig = i == 0 ? first : Instantiate(first);
            rig.transform.position = origin + new Vector3((i % cols - (cols - 1) * .5f) * cell, (i / cols - (rows - 1) * .5f) * cell, 0);
            if (receivers != null) receivers[i] = rig.GetComponent<TSMPNetworkHumanoidPoseSync>();
            if (poses == null) continue;
            var surface = rig.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.enabled && r.sharedMaterial.HasProperty("_TSMPSkinPalette"));
            var skin = (CustomRenderTexture)surface.sharedMaterial.GetTexture("_TSMPSkinPalette");
            var pose = (CustomRenderTexture)skin.material.GetTexture("_Pose");
            if (i > 0)
            {
                pose = NewPalette(pose);
                Vector4 field = pose.material.GetVector("_Field"); field.y = 37 + i;
                pose.material.SetVector("_Field", field);
                skin = NewPalette(skin);
                skin.material.SetTexture("_Pose", pose);
                var materials = surface.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    materials[m] = new Material(materials[m]);
                    materials[m].SetTexture("_TSMPSkinPalette", skin);
                }
                surface.sharedMaterials = materials;
            }
            poses[i] = pose;
        }
        if (receivers != null)
        {
            d.bindingTargets = receivers.Cast<Component>().ToArray();
            d.bindingNetworkIds = Enumerable.Range(37, n).Select(i => (ushort)i).ToArray();
            d.bindingVariableHashes = Enumerable.Repeat(d.bindingVariableHashes[0], n).ToArray();
            d.bindingValueTypes = Enumerable.Repeat(d.bindingValueTypes[0], n).ToArray();
            d.bindingFieldNames = Enumerable.Repeat(d.bindingFieldNames[0], n).ToArray();
            d.bindingDirections = Enumerable.Repeat(d.bindingDirections[0], n).ToArray();
        }
        deadline = Time.realtimeSinceStartup + 75;
    }

    static CustomRenderTexture NewPalette(CustomRenderTexture p) =>
        new CustomRenderTexture(p.width, p.height, p.format, RenderTextureReadWrite.Linear)
        {
            material = new Material(p.material), filterMode = p.filterMode, wrapMode = p.wrapMode,
            updateMode = p.updateMode, initializationColor = Color.clear, initializationMode = p.initializationMode
        };

    void LateUpdate()
    {
        try { Step(); } catch (Exception e) { Fail(e); }
    }

    void Step()
    {
        if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(source.decoder.lastError);
        view.Render();
        FrameTimingManager.CaptureFrameTimings();
        var d = source.decoder;
        if (frame >= Warm && frame < Warm + Samples)
        {
            int i = frame - Warm;
            fresh[i] = FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].frameStartTimestamp != stamp;
            stamp = timing[0].frameStartTimestamp;
            measured[i] = timing[0];
            if (d.lastFrameValid && last != d.lastFrameIndex) changes++;
        }
        if (frame == Warm) SkinProfile.CaptureIfRequested();
        last = d.lastFrameIndex;
        if (frame < Warm + Samples)
        {
            Graphics.CopyTexture(source.images[frame++ % 8], stream);
            d.DecodeNow();
            return;
        }
        if (d.readbackInFlight) return;
        if (!d.lastFrameValid || d.lastFrameIndex != 8)
        {
            Graphics.CopyTexture(source.images[7], stream);
            d.DecodeNow();
            return;
        }
        if (++settle < 8) return;
        if (d.lastAppliedVariableCount != (receivers == null ? 0 : receivers.Length)) throw new Exception("Wrong field count: " + d.lastAppliedVariableCount);
        for (int i = 0; i < finalArm.Length; i++)
        {
            if (poses != null)
            {
                var read = AsyncGPUReadback.Request(poses[i], 0);
                read.WaitForCompletion();
                if (read.hasError) throw new Exception("Could not read pose palette");
                var data = read.GetData<Vector4>();
                var q = data[(int)HumanBodyBones.LeftUpperArm];
                if (data[55].w != 6 || Quaternion.Angle(new Quaternion(q.x,q.y,q.z,q.w), finalArm[i]) > .15f)
                    throw new Exception("Wrong GPU arm " + i + ": " + q + ", expected " + finalArm[i]);
            }
            if (receivers != null)
            {
                var a = receivers[i].animator;
                if (Quaternion.Angle(a.GetBoneTransform(HumanBodyBones.LeftUpperArm).rotation, a.transform.rotation * finalArm[i]) > .15f)
                    throw new Exception("Wrong CPU arm " + i);
            }
        }
        var screen = AsyncGPUReadback.Request(view.targetTexture, 0, TextureFormat.RGBA32);
        screen.WaitForCompletion();
        if (screen.hasError) throw new Exception("No image");
        int lit = screen.GetData<Color32>().Count(c => c.r > 16 && c.g > 16 && c.b > 16);
        if (lit < 1000 || changes == 0) throw new Exception("Blank image or no input changes");
        var rows = new List<string> { "sample,fresh,gpuMs,mainMs,changes,pixels" };
        for (int i = 0; i < Samples; i++)
            rows.Add(FormattableString.Invariant($"{i},{fresh[i]},{measured[i].gpuFrameTime:F5},{measured[i].cpuMainThreadFrameTime:F5},{changes},{lit}"));
        var file = Environment.GetEnvironmentVariable("TSMP_SKIN_CASE") + "-" + Environment.GetEnvironmentVariable("TSMP_SKIN_RUN") + ".csv";
        File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("TSMP_SKIN_OUT"), file), rows);
        Debug.Log($"PASS crowd {file}: {changes}/{Samples} changes, {lit} pixels, {d.lastPayloadSizeFromHeader} bytes");
        enabled = false; Application.Quit(0);
    }

    void Fail(Exception e) { Debug.LogException(e); enabled = false; Application.Quit(2); }
}
#endif
