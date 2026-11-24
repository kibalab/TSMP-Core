#if !UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Editor;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class SkinProfileBuild
{
    const string Folder = "Assets/SkinProfileData/";
    const string Model = "Packages/com.kibalab.tsmp.core/Samples/TSMPExample/Models/Shapell/data/shapell.fbx";
    const string Codec = "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab";
    static readonly uint Hash = StableHash.VariableHash(typeof(TSMPNetworkHumanoidPoseSync), "poseBytes", "humanoid.pose");
    static GameObject Rig() => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets","SkinProfileData");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var frames = Frames();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Choose profile scene").AddComponent<SkinProfile>();
        var paths = new List<string> { Folder+"Boot.unity" };
        EditorSceneManager.SaveScene(scene,paths[0]);
        foreach (string mode in new[]{"decode","publish","cpu","palettes","gpu","hybrid"})
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var animator = Rig().GetComponentInChildren<Animator>();
            animator.enabled = false;
            var body = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh)
                .OrderByDescending(r=>r.sharedMesh.vertexCount).First();
            foreach (var r in animator.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            var bounds = body.bounds;
            camera.transform.position = bounds.center + Vector3.forward * 3 * bounds.size.magnitude;
            camera.transform.LookAt(bounds.center);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.y,bounds.extents.x)*1.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.backgroundColor = Color.black;
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = camera.transform.rotation;
            var profile = new GameObject("Profile").AddComponent<SkinProfile>();
            profile.images = frames;
            var decoder = profile.decoder = profile.gameObject.AddComponent<TSMPDecoder>();
            decoder.sourceTexture = frames[0];
            decoder.applyEveryFrame = false;
            decoder.skipDuplicateFrames = false;
            decoder.sampleSize = 1;
            decoder.debugLog = false;
            decoder.blockSize = 8;
            decoder.codecHandlers = new[]{Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Codec)).GetComponent<TSMPCodec>()};
            decoder.payloadByteTexture = new RenderTexture(128,4,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Save(decoder.payloadByteTexture,mode+".renderTexture");
            if (mode != "cpu" && mode != "decode") decoder.shaderChannel = ShaderReceiveChannel.Channel2;
            bool native = mode=="cpu" || mode=="hybrid";
            bool bake = mode=="gpu" || mode=="palettes" || mode=="hybrid";
            var white = new Material(Shader.Find("Standard"));
            white.SetFloat("_Glossiness",0);
            Save(white,mode+" Standard.mat");
            body.sharedMaterials = Enumerable.Repeat(white,body.sharedMesh.subMeshCount).ToArray();
            body.enabled = native;
            body.shadowCastingMode = ShadowCastingMode.Off;
            body.receiveShadows = false;
            if (bake)
            {
                var roots = mode=="hybrid" ? new[]{animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)} : null;
                profile.proxy = HumanoidSkinBaker.Bake(animator,body,2,37,3,
                    AssetDatabase.GenerateUniqueAssetPath(Folder+mode+".asset"),roots);
                profile.proxy.enabled = mode!="palettes";
            }
            if (native)
            {
                profile.body = body;
                var receiver = animator.gameObject.AddComponent<TSMPNetworkHumanoidPoseSync>();
                receiver.animator = animator;
                receiver.cacheFixedReceiveRig = true;
                if (mode=="hybrid")
                {
                    var anchors = new HashSet<int>();
                    var bones = body.bones;
                    var localRoot = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    var used = new HashSet<Transform>();
                    foreach (var w in body.sharedMesh.boneWeights)
                    {
                        if (w.weight0>0) used.Add(bones[w.boneIndex0]);
                        if (w.weight1>0) used.Add(bones[w.boneIndex1]);
                        if (w.weight2>0) used.Add(bones[w.boneIndex2]);
                        if (w.weight3>0) used.Add(bones[w.boneIndex3]);
                    }
                    for(int i=0;i<(int)HumanBodyBones.LastBone;i++)
                    {
                        var t=animator.GetBoneTransform((HumanBodyBones)i);
                        if(t && !t.IsChildOf(localRoot) && used.Any(b=>b.IsChildOf(t))) anchors.Add(i);
                    }
                    receiver.boneIds = receiver.boneIds.Where(anchors.Contains).ToArray();
                    receiver.includeFingerBones = false;
                }
                decoder.bindingTargets = new Component[]{receiver};
                decoder.bindingNetworkIds = new ushort[]{37};
                decoder.bindingVariableHashes = new[]{Hash};
                decoder.bindingValueTypes = new[]{(byte)NetworkValueType.RawBytes};
                decoder.bindingFieldNames = new[]{nameof(receiver.poseBytes)};
                decoder.bindingDirections = new[]{(int)NetworkSyncDirection.ReceiveOnly};
                profile.receiver = receiver;
            }
            paths.Add(Folder+mode+".unity");
            EditorSceneManager.SaveScene(scene,paths.Last());
        }
        AssetDatabase.SaveAssets();
        QualitySettings.vSyncCount = 0;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.gpuSkinning = true;
        var report = BuildPipeline.BuildPlayer(paths.ToArray(),Path.Combine(Environment.GetEnvironmentVariable("TSMP_SKIN_OUT"),"Player","Skin.exe"),
            BuildTarget.StandaloneWindows64,BuildOptions.Development);
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Skin profile build failed: "+report.summary.result);
    }

    static void Save(Object obj, string name) => AssetDatabase.CreateAsset(obj,AssetDatabase.GenerateUniqueAssetPath(Folder+name));

    static Texture2D[] Frames()
    {
        var animator = Rig().GetComponentInChildren<Animator>();
        var tx = animator.gameObject.AddComponent<TSMPNetworkHumanoidPoseSync>();
        tx.animator = animator;
        var codec = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Codec)).GetComponent<TSMPCodec>();
        var frames = new Texture2D[8];
        var arm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        for(int i=0;i<frames.Length;i++)
        {
            arm.localRotation *= Quaternion.Euler(0,0,4);
            tx.TSMPBeforeEncode();
            var bytes = tx.poseBytes;
            if(tx.validBoneCount<40) throw new Exception("Not a full humanoid pose");
            var payload = new byte[25+bytes.Length];
            int msg = NetworkFrameWriter.BeginNetworkFrame(payload,0,1);
            int entry = NetworkFrameWriter.BeginVariableState(payload,msg,37,1);
            int data = NetworkFrameWriter.BeginVariableValue(payload,entry,Hash,(int)NetworkValueType.RawBytes);
            Array.Copy(bytes,0,payload,data,bytes.Length);
            int end = data+bytes.Length;
            NetworkFrameWriter.EndVariableValue(payload,entry,end);
            NetworkFrameWriter.EndVariableState(payload,msg,end,1);
            NetworkFrameWriter.EndNetworkFrame(payload,0,1);
            Array.Resize(ref payload,end);
            var header = FrameHeader.CreateDefault();
            header.ActiveWidthBlocks=80; header.ActiveHeightBlocks=45; header.FrameIndex=(uint)i+1;
            header.PayloadSize=(ushort)end;
            var wire = new byte[FrameHeader.Size]; header.WriteTo(wire,0);
            var image = frames[i] = new Texture2D(640,360,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point};
            if(!codec.TryWriteFrame(image,8,wire,payload,out string error)) throw new Exception(error);
            Save(image,"Frame.asset");
            if(i==0) Debug.Log($"Skin profile: bones={tx.validBoneCount} poseBytes={bytes.Length} payload={end}");
        }
        return frames;
    }
}
#endif
