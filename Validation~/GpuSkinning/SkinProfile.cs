#if !UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using K13A.TSMP;
using K13A.TSMP.Udon;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class SkinProfile : MonoBehaviour
{
    public TSMPDecoder decoder;
    public TSMPNetworkHumanoidPoseSync receiver;
    public Texture2D[] images;
    public MeshRenderer proxy;
    public SkinnedMeshRenderer body;
    const int Warm = 90, Samples = 240;
    readonly FrameTiming[] timing = new FrameTiming[1];
    readonly FrameTiming[] captured = new FrameTiming[Samples];
    readonly bool[] freshTiming = new bool[Samples];
    readonly float[] intervals = new float[Samples];
    readonly long[] allocations = new long[Samples];
    Func<TSMPDecoder,uint> appliedIndex;
    Camera view;
    RenderTexture stream;
    string root, mode;
    int frame, changes;
    uint last;
    ulong stamp;
    ProfilerRecorder gc;
    [DllImport("renderdoc", CallingConvention = CallingConvention.Cdecl)]
    static extern int RENDERDOC_GetAPI(int version, out IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void TriggerCapture();

    void Start()
    {
        mode = Environment.GetEnvironmentVariable("TSMP_SKIN_CASE") ?? "cpu";
        root = Environment.GetEnvironmentVariable("TSMP_SKIN_OUT");
        if (!decoder) { SceneManager.LoadScene(mode); return; }
        QualitySettings.vSyncCount=0;
        Application.runInBackground=true;
        Application.targetFrameRate=60;
        gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
        view=FindObjectOfType<Camera>();
        view.enabled=false;
        view.targetTexture=new RenderTexture(800,600,24,RenderTextureFormat.ARGB32);
        stream=new RenderTexture(images[0].width,images[0].height,0,images[0].graphicsFormat){filterMode=FilterMode.Point};
        stream.Create();
        decoder.sourceTexture=stream;
        var parameter=Expression.Parameter(typeof(TSMPDecoder));
        appliedIndex=Expression.Lambda<Func<TSMPDecoder,uint>>(Expression.Field(parameter,"_lastAppliedFrameIndex"),parameter).Compile();
    }

    void LateUpdate()
    {
        if (!decoder) return;
        try { Step(); }
        catch (Exception e) { Debug.LogException(e); enabled=false; Application.Quit(2); }
    }

    void Step()
    {
        view.Render();
        FrameTimingManager.CaptureFrameTimings();
        if(frame>=Warm && frame<Warm+Samples)
        {
            uint applied=appliedIndex(decoder);
            if(applied!=last) changes++;
            last=applied;
            bool fresh=FrameTimingManager.GetLatestTimings(1,timing)>0 && timing[0].frameStartTimestamp!=stamp;
            var t=timing[0]; stamp=t.frameStartTimestamp;
            int i=frame-Warm;
            captured[i]=t; freshTiming[i]=fresh; intervals[i]=Time.unscaledDeltaTime*1000; allocations[i]=gc.Valid?gc.LastValue:-1;
        }
        if (frame==Warm && Environment.GetEnvironmentVariable("TSMP_SKIN_CAPTURE")=="1")
        {
            if(RENDERDOC_GetAPI(10000,out var api)!=1) throw new Exception("RenderDoc is not attached");
            Marshal.GetDelegateForFunctionPointer<TriggerCapture>(Marshal.ReadIntPtr(api,15*IntPtr.Size))();
        }
        if (frame++ < Warm+Samples)
        {
            Graphics.CopyTexture(images[(frame-1)%images.Length],stream);
            decoder.DecodeNow();
            return;
        }
        if (decoder.readbackInFlight) return;
        if(!decoder.lastFrameValid || !string.IsNullOrEmpty(decoder.lastError) || changes < Samples*.5 ||
            (receiver && (decoder.lastAppliedVariableCount!=1 || receiver.poseBytes==null)))
            throw new Exception($"Invalid case {mode}: changes={changes} applied={decoder.lastAppliedVariableCount} visible={proxy && proxy.isVisible}/{body && body.isVisible} error={decoder.lastError}");
        var info=new List<string> { $"case={mode}", $"Unity={Application.unityVersion}", $"CPU={SystemInfo.processorType}",
            $"GPU={SystemInfo.graphicsDeviceName} {SystemInfo.graphicsDeviceVersion}", $"screen={Screen.width}x{Screen.height}",
            $"changes={changes}/{Samples}", $"busy={decoder.skippedBusyDecodeCount}", $"predicted={decoder.predictedReadbackCount}", $"applied={decoder.lastAppliedVariableCount}",
            $"selectedCpuBones={(receiver?receiver.validBoneCount:0)}", $"payload={decoder.lastPayloadSizeFromHeader}",
            $"frameTimings={FrameTimingManager.IsFeatureEnabled()}" };
        var screen=AsyncGPUReadback.Request(view.targetTexture,0,TextureFormat.RGBA32);
        screen.WaitForCompletion();
        if(screen.hasError) throw new Exception("Offscreen target read failed");
        int lit=screen.GetData<Color32>().Count(c=>c.r>16 && c.g>16 && c.b>16);
        bool visible=body || (proxy && proxy.enabled);
        if(visible ? lit<1000 : lit!=0) throw new Exception("Unexpected model coverage: "+lit);
        info.Add("modelPixels="+lit);
        if(body) MeshInfo(info,"native",body.sharedMesh);
        if(proxy)
        {
            MeshInfo(info,"baked",proxy.GetComponent<MeshFilter>().sharedMesh);
            var skin=(CustomRenderTexture)proxy.sharedMaterial.GetTexture("_TSMPSkinPalette");
            var pose=(CustomRenderTexture)skin.material.GetTexture("_Pose");
            var rig=skin.material.GetTexture("_Rig");
            var read=AsyncGPUReadback.Request(pose,0); read.WaitForCompletion();
            if(read.hasError || read.GetData<Vector4>()[55].w!=6) throw new Exception("No live pose palette");
            read=AsyncGPUReadback.Request(skin,0); read.WaitForCompletion();
            if(read.hasError || read.GetData<Vector4>()[0].w!=6) throw new Exception("No live skin palette");
            foreach(var t in new Texture[]{pose,skin,rig})
                info.Add($"{t.name}={t.width}x{t.height} runtimeBytes={Profiler.GetRuntimeMemorySizeLong(t)}");
        }
        if(decoder.shaderChannel!=ShaderReceiveChannel.Off && !Shader.GetGlobalTexture("_UdonTSMPChannel2"))
            throw new Exception("Missing shader channel");
        string file=Path.Combine(root,mode+"-"+(Environment.GetEnvironmentVariable("TSMP_SKIN_RUN")??"1"));
        var rows=new List<string>{"frame,frameMs,gpuMs,mainMs,mainPresentWaitMs,renderMs,gcBytes"};
        for(int i=0;i<Samples;i++)
        {
            var t=captured[i]; bool f=freshTiming[i];
            rows.Add(FormattableString.Invariant($"{i},{intervals[i]:F4},{(f?t.gpuFrameTime:-1):F5},{(f?t.cpuMainThreadFrameTime:-1):F5},{(f?t.cpuMainThreadPresentWaitTime:-1):F5},{(f?t.cpuRenderThreadFrameTime:-1):F5},{allocations[i]}"));
        }
        File.WriteAllLines(file+".csv",rows);
        File.WriteAllLines(file+".txt",info);
        Debug.Log("PASS skin profile "+string.Join(" ",info));
        gc.Dispose(); enabled=false; Application.Quit(0);
    }
    static void MeshInfo(List<string> info,string name,Mesh mesh) =>
        info.Add($"{name}Vertices={mesh.vertexCount} submeshes={mesh.subMeshCount} runtimeBytes={Profiler.GetRuntimeMemorySizeLong(mesh)}");
}
#endif
