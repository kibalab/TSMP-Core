#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using K13A.TSMP;
using K13A.TSMP.Udon;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-10000)]
public sealed class ShowcaseDriver : MonoBehaviour
{
    public GameObject codecPrefab;
    public Material expandMaterial, encodeMaterial;
    public Shader stageShader;
    public GameObject avatarPrefab;
    public AnimationClip danceClip;
    ShowcaseActors actors;
    ShowcasePipe pipe;
    ShowcaseState state;
    TSMPEncoder encoder;
    TSMPDecoder decoder;
    ShowcasePresentation presentation;
    ShowcaseRecorder recorder;
    RenderTexture streamRender;
    Texture2D previewRead, streamRead, previewInput, streamInput;
    byte[] previewBytes = new byte[480*480*4], streamBytes = new byte[640*360*4];
    string role, root, startPath, output;
    long beginTicks;
    double lastSend, lastAppliedTime, recordStarted;
    float duration;
    int sequence, lastAppliedSequence, encodedSeen, applied, captures, heldFrames;
    bool sender, finishing, holdingSeen, resumedSeen, everVerified;
    bool pendingSourceFrame;
    float pendingSourceSeconds;
    double maxHoldingSeconds;
    readonly List<string> trace = new List<string>{"wallSeconds,encodedFrame,appliedState,headerAtApply,sourceSeconds,crcAtApply,holding,cubeX,cubeY,cubeZ,message"};
    static double Now => Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    static string Arg(string key,string fallback)
    {
        var a=Environment.GetCommandLineArgs(); for(int i=0;i<a.Length-1;i++) if(a[i]==key) return a[i+1]; return fallback;
    }
    void Awake()
    {
        try
        {
            role=Arg("--role","receiver"); sender=role=="sender";
            root=Arg("--capture-root",Application.persistentDataPath);Directory.CreateDirectory(root);startPath=Path.Combine(root,"start-ticks.txt");
            output=Arg("--output",Path.Combine(root,"showcase.mp4"));duration=float.Parse(Arg("--duration","16"),CultureInfo.InvariantCulture);
            Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
            actors=new ShowcaseActors(stageShader,role,avatarPrefab,danceClip);
            File.WriteAllText(Path.Combine(root,role+"-actor.txt"),"Avatar="+(avatarPrefab==null?"Generated":avatarPrefab.name)+"\nHumanoidBones="+actors.humanoidSync.validBoneCount+"\nLocalDance="+(sender&&danceClip!=null?danceClip.name:"None")+"\n");
            var owner=new GameObject("TSMP "+role+" runtime");owner.SetActive(false);
            state=owner.AddComponent<ShowcaseState>();state.networkId=503;
            var codec=Instantiate(codecPrefab).GetComponent<TSMPCodec>();CloneMaterials(codec);
            streamRender=Texture(640,360);
            if(sender)
            {
                encoder=owner.AddComponent<TSMPEncoder>();encoder.autoEncode=false;encoder.debugLog=false;
                encoder.output=streamRender;encoder.selectedCodec=codec;encoder.useGpuLuma4=true;
                encoder.luma4EncodeMaterial=new Material(encodeMaterial);encoder.blockExpandMaterial=new Material(expandMaterial);
                encoder.sampleSize=4;encoder.frameIndex=1;encoder.transSyncRefreshInterval=0;
                actors.transformSync.sendMode=SendMode.Always;actors.humanoidSync.sendMode=SendMode.Always;state.sendMode=SendMode.Always;
                encoder.networkBehaviours=new TSMPNetworkBehaviour[]{actors.transformSync,actors.humanoidSync,state};
                previewRead=new Texture2D(480,480,TextureFormat.RGBA32,false,true);streamRead=new Texture2D(640,360,TextureFormat.RGBA32,false,true);
            }
            else
            {
                decoder=owner.AddComponent<TSMPDecoder>();decoder.sourceTexture=streamRender;decoder.payloadByteTexture=Texture(512,4);
                decoder.codecHandlers=new[]{codec};decoder.flipY=true;decoder.sampleSize=4;decoder.debugLog=false;
                decoder.usePredictedReadback=true;decoder.useCombinedByteOutput=true;decoder.overlapReadbacks=true;decoder.applyEveryFrame=true;
                Bind(decoder,actors.transformSync);state.observer=decoder;
                previewInput=new Texture2D(480,480,TextureFormat.RGBA32,false,true);streamInput=new Texture2D(640,360,TextureFormat.RGBA32,false,true);
                previewInput.filterMode=streamInput.filterMode=FilterMode.Point;
                presentation=gameObject.AddComponent<ShowcasePresentation>();presentation.sourceView=previewInput;
                presentation.streamView=streamInput;presentation.receiverView=actors.view;
                var screen=new GameObject("Showcase framebuffer").AddComponent<Camera>();screen.cullingMask=0;screen.clearFlags=CameraClearFlags.SolidColor;screen.backgroundColor=new Color(.025f,.045f,.075f);
                Screen.SetResolution(1066,600,false);
            }
            owner.SetActive(true);
            pipe=new ShowcasePipe(sender,int.Parse(Arg("--port","59837"),CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(root,role+"-ready.txt"),Process.GetCurrentProcess().Id.ToString());
            if(sender)StartCoroutine(PublishAfterPhysics());
            else StartCoroutine(Record());
        }
        catch(Exception e){Fail(e);}
    }
    void Bind(TSMPDecoder target,TSMPNetworkTransformSync cube)
    {
        var components=new Component[]{cube,actors.humanoidSync,state};
        var targets=new List<Component>();var ids=new List<ushort>();var hashes=new List<uint>();var types=new List<byte>();var names=new List<string>();
        foreach(var component in components)
        {
            var network=(TSMPNetworkBehaviour)component;
            foreach(var field in TransSyncMetadata.GetOrCreate(null,component.GetType()).Fields)
            {targets.Add(component);ids.Add(network.networkId);hashes.Add(field.VariableHash);types.Add((byte)field.ValueType);names.Add(field.FieldInfo.Name);}
        }
        target.bindingTargets=targets.ToArray();target.bindingNetworkIds=ids.ToArray();target.bindingVariableHashes=hashes.ToArray();target.bindingValueTypes=types.ToArray();target.bindingFieldNames=names.ToArray();target.bindingDirections=new int[targets.Count];
    }
    static RenderTexture Texture(int w,int h)
    {
        var t=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};t.Create();return t;
    }
    static void CloneMaterials(TSMPCodec codec)
    {
        var copies=new Dictionary<Material,Material>();
        Func<Material,Material> copy=m=>{if(m==null)return null;if(!copies.TryGetValue(m,out var v)){v=new Material(m);copies[m]=v;}return v;};
        foreach(var f in codec.GetType().GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
        {if(f.FieldType==typeof(Material))f.SetValue(codec,copy((Material)f.GetValue(codec)));else if(f.FieldType==typeof(Material[])&&f.GetValue(codec) is Material[] a){var v=new Material[a.Length];for(int i=0;i<a.Length;i++)v[i]=copy(a[i]);f.SetValue(codec,v);}}
    }
    void Update()
    {
        if(finishing||actors==null)return;
        try
        {
            if(beginTicks==0&&File.Exists(startPath))beginTicks=long.Parse(File.ReadAllText(startPath),CultureInfo.InvariantCulture);
            double t=beginTicks==0?-1:(Stopwatch.GetTimestamp()-beginTicks)/(double)Stopwatch.Frequency;
            if(sender)
            {
                if(Now-lastSend<1.0/30)return;lastSend=Now;
                pendingSourceSeconds=(float)Math.Max(0,t);
                actors.Animate(pendingSourceSeconds);
                pendingSourceFrame=true;
            }
            else
            {
                pipe.TryConsume((preview,stream,frame)=>{
                    previewInput.LoadRawTextureData(preview);previewInput.Apply(false,false);
                    if(stream!=null){streamInput.LoadRawTextureData(stream);streamInput.Apply(false,false);Graphics.Blit(streamInput,streamRender);encodedSeen=frame;}
                });
                if(state.sequence!=lastAppliedSequence&&state.receivedCallbacks>0)
                {
                    if(!state.crcAtApply)throw new InvalidOperationException("Receiver applied without payload CRC verification");
                    if(state.sequence<lastAppliedSequence)throw new InvalidOperationException("State order regressed");
                    uint seed=(uint)state.sequence*2654435761u;for(int i=0;i<state.bytes.Length;i++){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;if(state.bytes[i]!=(byte)seed)throw new InvalidOperationException("Decoded bytes differ");}
                    float s=state.seconds;var expected=new Vector3(.25f*Mathf.Sin(s),1+.04f*Mathf.Sin(2*s),.12f*Mathf.Cos(s));
                    if(avatarPrefab!=null)actors.VerifyImportedPose();
                    else if(Vector3.Distance(actors.bones[HumanBodyBones.Hips].localPosition,expected)>.006f)throw new InvalidOperationException("Decoded Humanoid root differs: actual="+actors.bones[HumanBodyBones.Hips].localPosition+" expected="+expected+" seq="+state.sequence+" seconds="+s+" header="+state.headerAtApply+" applied="+decoder.lastAppliedVariableCount+" poseBytes="+(actors.humanoidSync.poseBytes==null?-1:actors.humanoidSync.poseBytes.Length));
                    var expectedCube=new Vector3(.92f+.27f*Mathf.Sin(s*1.2f),.48f+.17f*Mathf.Sin(s*1.7f),-.3f+.25f*Mathf.Cos(s));
                    if(Vector3.Distance(actors.cube.localPosition,expectedCube)>.006f||Quaternion.Angle(actors.cube.localRotation,Quaternion.Euler(s*38f,s*55f,s*23f))>.6f)throw new InvalidOperationException("Decoded cube pose differs");
                    lastAppliedSequence=state.sequence;lastAppliedTime=Now;applied++;everVerified=true;
                    if(holdingSeen)resumedSeen=true;
                }
                bool holding=everVerified&&Now-lastAppliedTime>.3;
                if(t>=0&&holding){holdingSeen=true;heldFrames++;maxHoldingSeconds=Math.Max(maxHoldingSeconds,Now-lastAppliedTime);}
                if(t>=0)
                {var p=actors.cube.localPosition;trace.Add(string.Join(",",t.ToString("F4",CultureInfo.InvariantCulture),encodedSeen,state.sequence,state.headerAtApply,state.seconds.ToString("F4",CultureInfo.InvariantCulture),state.crcAtApply,holding,p.x.ToString("F4",CultureInfo.InvariantCulture),p.y.ToString("F4",CultureInfo.InvariantCulture),p.z.ToString("F4",CultureInfo.InvariantCulture),state.message));}
                if(!string.IsNullOrEmpty(pipe.Error))throw new InvalidOperationException(pipe.Error);
            }
        }
        catch(Exception e){Fail(e);}
    }
    IEnumerator PublishAfterPhysics()
    {
        while(!finishing)
        {
            // The SDK completes PhysBone jobs around PreLateUpdate. Render and
            // encode the same pose only after its post-simulation stage.
            yield return new WaitForEndOfFrame();
            if(finishing||!pendingSourceFrame)continue;
            pendingSourceFrame=false;
            try
            {
                float motion=pendingSourceSeconds;
                state.sequence=++sequence;state.seconds=motion;state.message=motion<4?"HELLO TSMP":motion<6.5f?"STILL MOVING":"SYNC RESTORED";
                uint seed=(uint)sequence*2654435761u;for(int i=0;i<state.bytes.Length;i++){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;state.bytes[i]=(byte)seed;}
                actors.RenderView();
                uint before=encoder.frameIndex;encoder.EncodeNow();
                if(encoder.frameIndex!=before+1||!encoder.lastFrameUsedGpuLuma4||!string.IsNullOrEmpty(encoder.lastError))throw new InvalidOperationException("Actual GPU encode failed: "+encoder.lastError);
                if(encoder.autoVariableCount!=6)throw new InvalidOperationException("Showcase needs all six fields in each frame; got "+encoder.autoVariableCount);
                if(sequence<=3)File.AppendAllText(Path.Combine(root,"sender-fields.txt"),"Frame="+before+" Fields="+encoder.autoVariableCount+" Payload="+encoder.payloadBytes+" Hips="+actors.bones[HumanBodyBones.Hips].localPosition+" Time="+state.seconds+"\n");
                Read(actors.view,previewRead,previewBytes);
                bool pause=motion>=4&&motion<6.5f;
                if(!pause)Read(streamRender,streamRead,streamBytes);
                if(sequence==2){File.WriteAllBytes(Path.Combine(root,"source-preview.png"),previewRead.EncodeToPNG());File.WriteAllBytes(Path.Combine(root,"source-stream.png"),streamRead.EncodeToPNG());}
                pipe.Publish(previewBytes,pause?null:streamBytes,(int)before);
                if(motion>duration+5)Application.Quit();
            }
            catch(Exception e){Fail(e);}
        }
    }
    static void Read(RenderTexture source,Texture2D texture,byte[] bytes)
    {
        var old=RenderTexture.active;RenderTexture.active=source;texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);RenderTexture.active=old;NativeArray<byte>.Copy(texture.GetRawTextureData<byte>(),bytes);
    }
    IEnumerator Record()
    {
        double deadline=Now+45;
        while(!everVerified&&Now<deadline)yield return null;
        if(!everVerified){Fail(new InvalidOperationException("No verified frame before recording"));yield break;}
        // Prepare render resources before the demo clock and capture.
        yield return new WaitForEndOfFrame();
        actors.RenderView();presentation.RenderFrame();
        yield return new WaitForSecondsRealtime(1.5f);
        beginTicks=Stopwatch.GetTimestamp()+Stopwatch.Frequency;File.WriteAllText(startPath,beginTicks.ToString(CultureInfo.InvariantCulture));
        while(Stopwatch.GetTimestamp()<beginTicks)yield return null;
        holdingSeen=false;resumedSeen=false;heldFrames=0;maxHoldingSeconds=0;
        yield return new WaitForEndOfFrame();
        try{actors.RenderView();presentation.RenderFrame();var target=presentation.RecordingTarget;recorder=new ShowcaseRecorder(Arg("--ffmpeg","C:/Windows/ffmpeg.exe"),output,target.width,target.height,30);recordStarted=Now;}
        catch(Exception e){Fail(e);yield break;}
        while(Now-recordStarted<duration&&!finishing)
        {
            yield return new WaitForEndOfFrame();
            try{actors.RenderView();presentation.RenderFrame();recorder.Capture(presentation.RecordingTarget);captures++;}catch(Exception e){Fail(e);}
        }
        if(finishing)yield break;
        finishing=true;
        try
        {
            recorder.Dispose();
            if(!string.IsNullOrEmpty(recorder.Error))throw new InvalidOperationException(recorder.Error);
            if(!holdingSeen||!resumedSeen||maxHoldingSeconds<1)throw new InvalidOperationException("Pause/resume not observed");
            File.WriteAllLines(Path.Combine(root,"receiver-trace.csv"),trace);
            File.WriteAllText(Path.Combine(root,"recording-result.txt"),"PASS\nScope=Two Native Unity processes, localhost uncompressed texture link; presentation preview is not a state transport.\nSenderPID="+pipe.RemoteProcessId+"\nReceiverPID="+Process.GetCurrentProcess().Id+"\nAppliedStates="+applied+"\nCapturedFrames="+captures+"\nWallSeconds="+(Now-recordStarted).ToString("F4",CultureInfo.InvariantCulture)+"\nHoldingObserved="+holdingSeen+"\nResumedObserved="+resumedSeen+"\nMaxHoldSeconds="+maxHoldingSeconds.ToString("F4",CultureInfo.InvariantCulture)+"\nVideo="+output+"\n");
            Application.Quit(0);
        }
        catch(Exception e){Fail(e);}
    }
    void Fail(Exception e)
    {
        finishing=true;UnityEngine.Debug.LogException(e);
        if(!string.IsNullOrEmpty(root)){File.WriteAllLines(Path.Combine(root,role+"-failure-trace.csv"),trace);File.WriteAllText(Path.Combine(root,role+"-result.txt"),"FAIL\n"+e);}
        try{recorder?.Dispose();}catch{}Application.Quit(1);
    }
    void OnDestroy(){pipe?.Dispose();actors?.Dispose();}
}
#endif
