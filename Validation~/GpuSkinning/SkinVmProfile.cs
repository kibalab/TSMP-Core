#if UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using Object = UnityEngine.Object;
using Watch = System.Diagnostics.Stopwatch;

public static class SkinVmProfile
{
    const string Key="TSMP.SkinVmProfile", Folder="Assets/SkinProfileInputs/";
    static readonly int Count = int.Parse(Environment.GetEnvironmentVariable("TSMP_CROWD_N") ?? "1");
    static readonly bool Crowd = Environment.GetEnvironmentVariable("TSMP_CROWD_N") != null;
    static readonly string[] Modes = Crowd ? new[]{"cpu","gpu","hybrid"} : new[]{"decode","cpu-default","cpu","gpu","hybrid"};
    static string Input(int i) => Folder+(Crowd ? Count+"-" : "")+i+".asset";
    static readonly List<string> Rows=new List<string>{"round,mode,sample,submitUs,completionUs,poseUs,callbacks,applied"};
    static UdonBehaviour active;
    static HashSet<UdonBehaviour> targets;
    static UdonBehaviour[] decoders;
    static UdonBehaviour[][] receivers;
    static Texture2D[] inputs;
    static RenderTexture stream;
    static HashSet<uint> callbacks;
    static uint submit, poseEvent;
    static bool pending;
    static int round, ordinal, calls;
    static long submitTicks, callbackTicks, poseTicks;
    static double deadline;
    static Harmony hook;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var codecPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        for(int n=0;n<Modes.Length;n++)
        {
            string m=Modes[n];
            var codec=new GameObject("Luma4 "+m).AddUdonSharpComponent<TSMPCodecLuma4>();
            codec.byteDecodeMaterial=codecPrefab.GetComponent<TSMPCodecLuma4>().byteDecodeMaterial;
            UdonSharpEditorUtility.CopyProxyToUdon(codec);
            var dec=new GameObject("Decoder "+m).AddUdonSharpComponent<TSMPDecoder>();
            dec.sourceTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Input(0));
            if(!dec.sourceTexture) throw new Exception("Missing staged input textures");
            dec.codecHandlers=new TSMPCodec[]{codec};
            dec.blockSize=Crowd?4:8; dec.sampleSize=1; dec.applyEveryFrame=false; dec.debugLog=false; dec.skipDuplicateFrames=false;
            int textureRows = Crowd ? (Count > 32 ? 32 : 16) : 4;
            dec.payloadByteTexture=new RenderTexture(Crowd?256:128,textureRows,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            AssetDatabase.CreateAsset(dec.payloadByteTexture,AssetDatabase.GenerateUniqueAssetPath(Folder+m+".renderTexture"));
            dec.readbackPackMaterial=Resources.Load<Material>("TSMPReadbackPack");
            if(m=="gpu" || m=="hybrid")
            {
                dec.shaderChannel=ShaderReceiveChannel.Channel2;
                dec.shaderChannelMaterial=Resources.Load<Material>("TSMPShaderChannel");
            }
            if(m.StartsWith("cpu",StringComparison.Ordinal) || m=="hybrid")
            {
                dec.bindingTargets=new Component[Count]; dec.bindingUdonTargets=new UdonBehaviour[Count];
                dec.bindingNetworkIds=new ushort[Count]; dec.bindingVariableHashes=new uint[Count];
                dec.bindingValueTypes=new byte[Count]; dec.bindingDirections=new int[Count]; dec.bindingFieldNames=new string[Count];
                for(int k=0;k<Count;k++)
                {
                    var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.core/Samples/TSMPExample/Models/Shapell/data/shapell.fbx"));
                    model.name="Rig "+m+" "+k;
                    var a=model.GetComponentInChildren<Animator>();
                    a.enabled=false;
                    foreach(var r in model.GetComponentsInChildren<Renderer>()) r.enabled=false;
                    var rx=model.AddUdonSharpComponent<TSMPNetworkHumanoidPoseSync>();
                    rx.animator=a; rx.cacheFixedReceiveRig=m!="cpu-default";
                    if(m=="hybrid") { rx.boneIds=new[]{0,7,8,11,13}; rx.includeFingerBones=false; }
                    UdonSharpEditorUtility.CopyProxyToUdon(rx);
                    dec.bindingTargets[k]=rx; dec.bindingUdonTargets[k]=UdonSharpEditorUtility.GetBackingUdonBehaviour(rx);
                    dec.bindingNetworkIds[k]=(ushort)(37+k);
                    dec.bindingVariableHashes[k]=StableHash.VariableHash(typeof(TSMPNetworkHumanoidPoseSync),"poseBytes","humanoid.pose");
                    dec.bindingValueTypes[k]=(byte)NetworkValueType.RawBytes;
                    dec.bindingDirections[k]=(int)NetworkSyncDirection.ReceiveOnly; dec.bindingFieldNames[k]=nameof(rx.poseBytes);
                }
            }
            UdonSharpEditorUtility.CopyProxyToUdon(dec);
        }
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Folder+"Benchmark.unity");
        SessionState.SetBool(Key,true); Attach(); EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod] static void Attach()
    {
        if(!SessionState.GetBool(Key,false)) return;
        deadline=EditorApplication.timeSinceStartup+360;
        EditorApplication.update-=Tick; EditorApplication.update+=Tick;
    }

    static IUdonProgram Program(UdonBehaviour b) =>
        (IUdonProgram)typeof(UdonBehaviour).GetField("_program",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(b);

    static void Prefix(UdonBehaviour __instance,uint __0,out long __state)
    {
        __state=0;
        if(!pending) return;
        if((__instance==active && (__0==submit || callbacks.Contains(__0))) || (__0==poseEvent && targets.Contains(__instance)))
            __state=Watch.GetTimestamp();
    }
    static void Postfix(UdonBehaviour __instance,uint __0,long __state)
    {
        if(__state==0) return;
        long dt=Watch.GetTimestamp()-__state;
        if(__instance!=active) poseTicks+=dt;
        else if(__0==submit) submitTicks+=dt;
        else {callbackTicks+=dt; calls++;}
    }
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("VM case "+round+"/"+ordinal);
            if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if(decoders==null)
            {
                decoders=Modes.Select(m=>UdonSharpEditorUtility.GetBackingUdonBehaviour(GameObject.Find("Decoder "+m).GetComponent<TSMPDecoder>())).ToArray();
                receivers=Modes.Select(m=>Enumerable.Range(0,Count).Select(k=>GameObject.Find("Rig "+m+" "+k)).Where(o=>o)
                    .Select(o=>UdonSharpEditorUtility.GetBackingUdonBehaviour(o.GetComponent<TSMPNetworkHumanoidPoseSync>())).ToArray()).ToArray();
                inputs=Enumerable.Range(0,8).Select(i=>AssetDatabase.LoadAssetAtPath<Texture2D>(Input(i))).ToArray();
                stream=new RenderTexture(inputs[0].width,inputs[0].height,0,inputs[0].graphicsFormat){filterMode=FilterMode.Point};
                stream.Create();
            }
            if(decoders.Any(b=>!b.HasDoneStart)) return;
            if(hook==null)
            {
                hook=new Harmony("tsmp.validation.skin-profile");
                hook.Patch(AccessTools.Method(typeof(UdonBehaviour),"RunProgram",new[]{typeof(uint)}),
                    new HarmonyMethod(typeof(SkinVmProfile),nameof(Prefix)),new HarmonyMethod(typeof(SkinVmProfile),nameof(Postfix)));
            }
            int index=round<Modes.Length?round:2*Modes.Length-1-round;
            if(!pending)
            {
                if(ordinal==0)
                {
                    active=decoders[index]; targets=new HashSet<UdonBehaviour>(receivers[index]);
                    var e=Program(active).EntryPoints;
                    submit=e.GetAddressFromSymbol("DecodeNow");
                    callbacks=new HashSet<uint>(e.GetExportedSymbols().Where(s=>s.IndexOf("Readback",StringComparison.OrdinalIgnoreCase)>=0).Select(e.GetAddressFromSymbol));
                    if(callbacks.Count==0) throw new Exception("No exported readback event");
                    if(targets.Count>0) poseEvent=Program(receivers[index][0]).EntryPoints.GetAddressFromSymbol("OnTSMPVariableReceived");
                    active.SetProgramVariable("sourceTexture",stream);
                }
                submitTicks=callbackTicks=poseTicks=0; calls=0;
                Graphics.CopyTexture(inputs[ordinal%8],stream);
                pending=true;
                active.SendCustomEvent("DecodeNow"); return;
            }
            if((bool)active.GetProgramVariable("readbackInFlight")) return;
            pending=false;
            int applied=(int)active.GetProgramVariable("lastAppliedVariableCount");
            if(!(bool)active.GetProgramVariable("lastFrameValid") || (uint)active.GetProgramVariable("lastFrameIndex")!=1u+ordinal%8 ||
                applied!=targets.Count || submitTicks==0 || callbackTicks==0 || (targets.Count>0 && poseTicks==0))
                throw new Exception($"VM check {Modes[index]} {ordinal} {applied} ticks {submitTicks}/{callbackTicks}/{poseTicks}: {active.GetProgramVariable("lastError")}");
            if(ordinal>=16)
            {
                double us=1000000.0/Watch.Frequency;
                Rows.Add(FormattableString.Invariant($"{round},{Modes[index]},{ordinal-16},{submitTicks*us:F4},{callbackTicks*us:F4},{poseTicks*us:F4},{calls},{applied}"));
            }
            if(++ordinal<96) return;
            if((Modes[index]=="gpu" || Modes[index]=="hybrid") && !Shader.GetGlobalTexture("_UdonTSMPChannel2")) throw new Exception("Channel not published");
            foreach(var pose in receivers[index])
            {
                var packed=(byte[])pose.GetProgramVariable("poseBytes");
                var rig=(Animator)pose.GetProgramVariable("animator");
                bool matched=false;
                for(int i=0;i<Binary.ReadUInt16LE(packed,2);i++)
                {
                    int cursor=4+6*i;
                    if(packed[cursor]!=(int)HumanBodyBones.LeftUpperArm) continue;
                    var expected=rig.transform.rotation*Binary.ReadPackedQuaternion12LE(packed,cursor+1);
                    matched=Quaternion.Angle(rig.GetBoneTransform(HumanBodyBones.LeftUpperArm).rotation,expected)<.15f;
                }
                if(!matched) throw new Exception("Udon arm did not match the received pose: "+Modes[index]);
            }
            Debug.Log($"PASS VM profile block {round}: {Modes[index]}");
            ordinal=0;
            if(++round<2*Modes.Length) return;
            File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("TSMP_SKIN_OUT"),Crowd?"vm-"+Count+".csv":"vm.csv"),Rows);
            Finish(0);
        }
        catch(Exception e){ Debug.LogException(e); Finish(1); }
    }
    static void Finish(int code)
    {
        hook?.UnpatchAll("tsmp.validation.skin-profile"); SessionState.EraseBool(Key); EditorApplication.update-=Tick; EditorApplication.Exit(code);
    }
}
#endif
