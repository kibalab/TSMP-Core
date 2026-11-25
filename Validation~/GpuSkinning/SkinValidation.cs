#if !UDONSHARP
using System;
using System.Collections;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Editor;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class SkinValidation
{
    const string Folder = "Assets/SkinValidationAssets";
    const string Key = "TSMP.ValidateSkin";
    static IEnumerator test;
    static double deadline;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SkinValidationAssets");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.core/Samples/TSMPExample/Models/Shapell/data/shapell.fbx");
        var sender = MakeRig(model,"Sender");
        var receiver = MakeRig(model,"Receiver");
        receiver.transform.SetPositionAndRotation(new Vector3(-1,2,0.7f),Quaternion.Euler(3,57,4));
        receiver.transform.localScale = Vector3.one * 1.3f;
        sender.transform.SetPositionAndRotation(new Vector3(2,1,3),Quaternion.Euler(10,19,0));
        var real = receiver.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.sharedMesh && r.sharedMesh.boneWeights.Length > 0)
            .OrderByDescending(r => r.sharedMesh.vertexCount).First();
        var modelSkin = HumanoidSkinBaker.Bake(receiver,real,2,37,5,AssetDatabase.GenerateUniqueAssetPath(Folder+"/ModelSkin.asset"));
        Debug.Log($"Skinned model fixture: {real.name}, vertices={real.sharedMesh.vertexCount}, bones={real.bones.Length}");
        modelSkin.name = "Model Skin";
        real.name = "Model Source";
        var source = MakeMesh(receiver);
        var proxy = HumanoidSkinBaker.Bake(receiver,source,2,37,5,AssetDatabase.GenerateUniqueAssetPath(Folder+"/Skin.asset"));
        proxy.name = "Proxy";
        var hybrid = MakeMesh(receiver);
        hybrid.name = "Hybrid Source";
        hybrid.enabled = true;
        var original = hybrid.sharedMesh;
        original.subMeshCount = 3;
        original.SetTriangles(new[]{1,2,3}, 0);
        original.SetTriangles(Array.Empty<int>(), 1);
        original.SetTriangles(new[]{0,1,2}, 2);
        var slots = new[] {"Native", "Unused", "GPU", "Overlay"}
            .Select(n => new Material(Shader.Find("Standard")) { name = n }).ToArray();
        foreach (var mat in slots) AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(Folder+"/"+mat.name+".mat"));
        hybrid.sharedMaterials = slots;
        var delta = new Vector3[4];
        delta[3] = Vector3.forward;
        original.AddBlendShapeFrame("Fabric", 100f, delta, null, null);
        var hybridProxy = HumanoidSkinBaker.Bake(receiver, hybrid, 2, 37, 5,
            AssetDatabase.GenerateUniqueAssetPath(Folder+"/Hybrid.asset"), new[]{hybrid.bones[3]});
        hybridProxy.name = "Hybrid GPU";
        var compactPalette = hybridProxy.sharedMaterial.GetTexture("_TSMPSkinPalette");
        if (hybrid.sharedMesh.subMeshCount != 1 || hybridProxy.GetComponent<MeshFilter>().sharedMesh.subMeshCount != 1 ||
            !hybrid.sharedMaterials.SequenceEqual(new[]{slots[0]}) ||
            !hybridProxy.sharedMaterials.Select(m => m.name).SequenceEqual(slots.Skip(2).Select(m => m.name + " GPU Skin")))
            throw new Exception("Partition lost material order or replayed an overlay on the wrong triangles");
        if (!hybrid.enabled || hybrid.sharedMesh.vertexCount != 3 || hybrid.sharedMesh.GetTriangles(0).Length != 3 ||
            compactPalette.width != 2 + 3 * 3 ||
            hybrid.sharedMesh.blendShapeCount != 1 || hybrid.sharedMesh.boneWeights[2].boneIndex0 != 3 ||
            hybridProxy.GetComponent<MeshFilter>().sharedMesh.vertexCount != 3 || original.triangles.Length != 6)
            throw new Exception("Mixed triangle partition failed");
        hybrid.SetBlendShapeWeight(0, 0);
        new GameObject("Camera").AddComponent<Camera>();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Folder+"/Check.unity");
        SessionState.SetBool(Key,true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    static Animator MakeRig(GameObject model, string name)
    {
        var animator = Object.Instantiate(model).GetComponentInChildren<Animator>();
        animator.name = name;
        animator.enabled = false;
        foreach (var r in animator.GetComponentsInChildren<Renderer>()) r.enabled = false;
        return animator;
    }

    static SkinnedMeshRenderer MakeMesh(Animator rig)
    {
        var obj = new GameObject("Probe Source");
        obj.transform.SetParent(rig.transform,false);
        obj.transform.localPosition = new Vector3(.01f,.03f,-.04f);
        obj.transform.localRotation = Quaternion.Euler(1,11,2);
        var renderer = obj.AddComponent<SkinnedMeshRenderer>();
        var hips = rig.GetBoneTransform(HumanBodyBones.Hips);
        var arm = rig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var hand = rig.GetBoneTransform(HumanBodyBones.LeftHand);
        var helper = new GameObject("Aux").transform;
        helper.SetParent(hand,false);
        helper.localPosition = new Vector3(.04f,.01f,0);
        var bones = new[] { hips,arm,hand,helper };
        var mesh = new Mesh {name="Probe Mesh"};
        mesh.vertices = bones.Select(b=>obj.transform.InverseTransformPoint(b.position)+new Vector3(.01f,.02f,.03f)).ToArray();
        mesh.normals = Enumerable.Repeat(Vector3.up,4).ToArray();
        mesh.tangents = Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray();
        mesh.triangles = new[] {0,1,2,1,2,3};
        mesh.bindposes = bones.Select(b=>b.worldToLocalMatrix*obj.transform.localToWorldMatrix).ToArray();
        mesh.boneWeights = new[] {
            new BoneWeight {boneIndex0=0,weight0=1},
            new BoneWeight {boneIndex0=1,weight0=1},
            new BoneWeight {boneIndex0=2,weight0=.7f,boneIndex1=1,weight1=.3f},
            new BoneWeight {boneIndex0=3,weight0=.4f,boneIndex1=1,weight1=.3f,boneIndex2=2,weight2=.2f,boneIndex3=0,weight3=.1f} };
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Folder+"/Input.asset"));
        renderer.sharedMesh=mesh; renderer.bones=bones; renderer.rootBone=hips;
        return renderer;
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        if (!SessionState.GetBool(Key,false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        deadline = EditorApplication.timeSinceStartup + 90;
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("GPU skinning timed out");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (test == null) test = Check();
            if (test.MoveNext()) return;
            Finish(0);
        }
        catch(Exception e) { Debug.LogException(e); Finish(1); }
    }

    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(Key);
        EditorApplication.Exit(code);
    }

    static IEnumerator Check()
    {
        var src = GameObject.Find("Sender").GetComponent<Animator>();
        var dst = GameObject.Find("Receiver").GetComponent<Animator>();
        var tx = src.gameObject.AddComponent<TSMPNetworkHumanoidPoseSync>();
        var rx = dst.gameObject.AddComponent<TSMPNetworkHumanoidPoseSync>();
        rx.receiveInterpolation = ReceiveInterpolationMode.Discrete;
        tx.animator=src; rx.animator=dst;
        tx.includeFingerBones = rx.includeFingerBones = false;
        int[] ids = {(int)HumanBodyBones.Hips,(int)HumanBodyBones.LeftUpperArm,(int)HumanBodyBones.LeftHand};
        tx.boneIds=ids; rx.boneIds=ids;
        var source = GameObject.Find("Probe Source").GetComponent<SkinnedMeshRenderer>();
        var proxy = GameObject.Find("Proxy").GetComponent<MeshRenderer>();
        var points = Object.Instantiate(proxy.GetComponent<MeshFilter>().sharedMesh);
        points.SetIndices(new[]{0,1,2,3},MeshTopology.Points,0);
        var actualMaterial = proxy.sharedMaterial;
        var hybrid = GameObject.Find("Hybrid Source").GetComponent<SkinnedMeshRenderer>();
        var hybridGpu = GameObject.Find("Hybrid GPU").GetComponent<MeshRenderer>();
        var hybridPoints = Object.Instantiate(hybridGpu.GetComponent<MeshFilter>().sharedMesh);
        hybridPoints.SetIndices(new[]{0,1,2},MeshTopology.Points,0);
        if (!actualMaterial.SetPass(0) || ShaderUtil.ShaderHasError(actualMaterial.shader)) throw new Exception("Surface shader failed to compile");
        var palette = (CustomRenderTexture)actualMaterial.GetTexture("_TSMPSkinPalette");
        var probe = new Material(Shader.Find("Hidden/TSMP Validation/Skin Probe"));
        probe.SetTexture("_TSMPSkinPalette",palette);
        var hybridProbe = new Material(probe);
        hybridProbe.SetTexture("_TSMPSkinPalette", hybridGpu.sharedMaterial.GetTexture("_TSMPSkinPalette"));
        var expectedRest = Expected(source);
        var real = dst.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name=="Model Source");
        var model = GameObject.Find("Model Skin").GetComponent<MeshRenderer>();
        var modelMesh = model.GetComponent<MeshFilter>().sharedMesh;
        int[] samples = Enumerable.Range(0,256).Select(i=>i*(modelMesh.vertexCount-1)/255).ToArray();
        var modelPoints = SamplePoints(modelMesh,samples);
        var modelProbe = new Material(probe);
        modelProbe.SetTexture("_TSMPSkinPalette",model.sharedMaterial.GetTexture("_TSMPSkinPalette"));
        var codec = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab")).GetComponent<TSMPCodec>();
        var decoder = new GameObject("Decoder").AddComponent<TSMPDecoder>();
        var image = new Texture2D(640,360,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Point};
        decoder.applyEveryFrame=false;
        decoder.sourceTexture=image; decoder.blockSize=8; decoder.codecHandlers=new[]{codec};
        decoder.payloadByteTexture=new RenderTexture(64,8,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        decoder.shaderChannel=ShaderReceiveChannel.Channel2;
        int until = Time.frameCount+4;
        while(Time.frameCount<until) yield return null;
        Compare(points,proxy,probe,expectedRest,"no input");

        for(int step=0;step<6;step++)
        {
            var hips = src.GetBoneTransform(HumanBodyBones.Hips);
            hips.position += new Vector3(.3f,.12f,.05f);
            hips.rotation *= Quaternion.Euler(-8,20,6);
            src.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation *= Quaternion.Euler(30,5,10);
            src.GetBoneTransform(HumanBodyBones.LeftHand).localRotation *= Quaternion.Euler(5,15,35);
            tx.rootSyncSpace=step==0?HumanoidRootSyncSpace.Local:HumanoidRootSyncSpace.World;
            tx.TSMPBeforeEncode();
            rx.poseBytes = (byte[])tx.poseBytes.Clone();
            rx.OnTSMPVariableReceived();
            byte[] packed=(byte[])tx.poseBytes.Clone();
            bool invalid = step >= 2 && step < 5;
            int lastRecord = 4 + (tx.validBoneCount - 1) * 6;
            if(step==2) packed[0]=99;
            if(step==3) packed[lastRecord]=packed[4];
            if(step==4) packed[lastRecord]=255;
            var payload=new byte[25+packed.Length];
            int msg=NetworkFrameWriter.BeginNetworkFrame(payload,0,1);
            int entry=NetworkFrameWriter.BeginVariableState(payload,msg,37,1);
            uint hash=StableHash.VariableHash(typeof(TSMPNetworkHumanoidPoseSync),"poseBytes","humanoid.pose");
            int value=NetworkFrameWriter.BeginVariableValue(payload,entry,hash,(int)NetworkValueType.RawBytes);
            Array.Copy(packed,0,payload,value,packed.Length);
            int end=value+packed.Length;
            NetworkFrameWriter.EndVariableValue(payload,entry,end);
            NetworkFrameWriter.EndVariableState(payload,msg,end,1);
            NetworkFrameWriter.EndNetworkFrame(payload,0,1);
            Array.Resize(ref payload,end);
            var header=FrameHeader.CreateDefault();
            header.ActiveWidthBlocks=80; header.ActiveHeightBlocks=45;
            header.FrameIndex=(uint)step+1; header.PayloadSize=(ushort)payload.Length;
            var bytes=new byte[FrameHeader.Size];
            header.WriteTo(bytes,0);
            if(!codec.TryWriteFrame(image,8,bytes,payload,out string error)) throw new Exception(error);
            decoder.DecodeNow();
            while(decoder.readbackInFlight) yield return null;
            if(!decoder.lastFrameValid) throw new Exception(decoder.lastError);
            until=Time.frameCount+5;
            while(Time.frameCount<until) yield return null;
            Compare(points,proxy,probe,invalid?expectedRest:Expected(source),invalid?"bad packet "+step:step==0?"local":"world");
            if(!invalid)
            {
                var seam = Expected(hybrid);
                var full = Expected(source);
                Compare(hybridPoints, hybridGpu, hybridProbe, new[]{full[0], seam[0], seam[1]}, "hybrid boundary "+step);
                var baked=new Mesh();
                source.BakeMesh(baked,false);
                Compare(points,proxy,probe,baked.normals.Take(2).Select(n=>source.transform.TransformDirection(n).normalized).ToArray(),"normals "+step,1);
                Compare(points,proxy,probe,baked.tangents.Take(2).Select(t=>source.transform.TransformDirection((Vector3)t).normalized).ToArray(),"tangents "+step,2);
                Object.Destroy(baked);
                var modelWorld = Expected(real);
                Compare(modelPoints,model,modelProbe,samples.Select(i=>modelWorld[i]).ToArray(),"model "+step);
            }
        }
        var localBone = hybrid.bones[3];
        var before = Expected(hybrid);
        localBone.localPosition += Vector3.up * .2f;
        var after = Expected(hybrid);
        if (Vector3.Distance(before[2], after[2]) < .03f || Vector3.Distance(before[0], after[0]) > .0001f)
            throw new Exception("Client-local bone did not deform just its weighted vertices");
        tx.excludedSendBoneRoots = new[] { src.GetBoneTransform(HumanBodyBones.LeftUpperArm) };
        tx.TSMPBeforeEncode();
        if (tx.validBoneCount != 1 || tx.poseBytes.Length != 27 || tx.poseBytes[4] != (byte)HumanBodyBones.Hips)
            throw new Exception("Excluded arm or hand was transmitted");
        tx.excludedSendBoneRoots[0] = src.GetBoneTransform(HumanBodyBones.LeftHand);
        tx.TSMPBeforeEncode();
        if (tx.validBoneCount != 2) throw new Exception("Editing a send root did not update the packet");
        tx.excludedSendBoneRoots = null;
        tx.TSMPBeforeEncode();
        if (tx.validBoneCount != 3) throw new Exception("Removing the local mask did not restore capture");
        rx.cacheFixedReceiveRig = true;
        rx.excludedSendBoneRoots = new[] { dst.GetBoneTransform(HumanBodyBones.LeftUpperArm) };
        var localHand = dst.GetBoneTransform(HumanBodyBones.LeftHand);
        foreach (var mode in new[]{ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous})
        {
            rx.receiveInterpolation = mode;
            tx.excludedSendBoneRoots = null;
            tx.TSMPBeforeEncode();
            Quaternion start = localHand.localRotation * Quaternion.Euler(35,10,4);
            localHand.localRotation = start;
            rx.poseBytes = tx.poseBytes;
            rx.OnTSMPVariableReceived();
            until = Time.frameCount + 3;
            while (Time.frameCount < until) yield return null;
            if (Quaternion.Angle(start,localHand.localRotation) < 1f)
                throw new Exception("Receiver's send exclusion suppressed incoming hand: "+mode);
            Vector3 origin = dst.GetBoneTransform(HumanBodyBones.Hips).position;
            Quaternion q = localHand.localRotation * Quaternion.Euler(43,12,6);
            localHand.localRotation = q;
            src.GetBoneTransform(HumanBodyBones.Hips).position += Vector3.right * .5f;
            tx.excludedSendBoneRoots = new[] { src.GetBoneTransform(HumanBodyBones.LeftUpperArm) };
            tx.TSMPBeforeEncode();
            rx.poseBytes = tx.poseBytes;
            rx.OnTSMPVariableReceived();
            until = Time.frameCount + 3;
            while (Time.frameCount < until) yield return null;
            if (Quaternion.Angle(q,localHand.localRotation) > .1f ||
                Vector3.Distance(origin,dst.GetBoneTransform(HumanBodyBones.Hips).position) < .001f)
                throw new Exception("Excluded chain or Hips ownership failed: "+mode);
        }
        CheckSparseReceive(tx, rx);
        Debug.Log("PASS mixed skin, send-only subtree exclusions and independent receive; GPU humanoid skinning: "+SystemInfo.graphicsDeviceName);
    }

    static void CheckSparseReceive(TSMPNetworkHumanoidPoseSync tx, TSMPNetworkHumanoidPoseSync rx)
    {
        var arm = rx.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var hips = rx.animator.GetBoneTransform(HumanBodyBones.Hips);
        rx.cacheFixedReceiveRig = false;
        rx.includeFingerBones = false;
        rx.boneIds = new[] { (int)HumanBodyBones.LeftUpperArm };
        rx.receiveInterpolation = ReceiveInterpolationMode.Discrete;
        tx.boneIds = new[] { 13, 14, 15, 16, 17, 18, 0 };
        tx.excludedSendBoneRoots = null;
        tx.rootSyncSpace = HumanoidRootSyncSpace.World;
        tx.TSMPBeforeEncode();
        byte[] packet = tx.poseBytes;
        int count = Binary.ReadUInt16LE(packet, 2), tail = 4 + count * 6;
        if (count < 6) throw new Exception("Not a sparse receive fixture");
        var start = arm.rotation * Quaternion.Euler(20,0,0);
        var position = hips.position + Vector3.right;
        hips.position = position;
        arm.rotation = start;
        foreach (int kind in new[] {0, 1, 2})
        {
            var bad = (byte[])packet.Clone();
            if (kind < 2) bad[tail - 6] = kind == 0 ? bad[4] : (byte)255;
            else Array.Copy(BitConverter.GetBytes(float.NaN), 0, bad, tail, 4);
            rx.poseBytes = bad;
            rx.OnTSMPVariableReceived();
            if (Vector3.Distance(hips.position, position) > .001f || Quaternion.Angle(arm.rotation, start) > .1f)
                throw new Exception("Partially applied malformed sparse pose " + kind);
        }
        rx.poseBytes = packet;
        rx.OnTSMPVariableReceived();
        if (Quaternion.Angle(arm.rotation,start) < 1 || Vector3.Distance(hips.position,tx.animator.GetBoneTransform(HumanBodyBones.Hips).position) > .001f)
            throw new Exception("Sparse arm or root tail not applied");
    }

    static Mesh SamplePoints(Mesh mesh,int[] samples)
    {
        var positions=mesh.vertices;
        var ids=new System.Collections.Generic.List<Vector4>();
        var weights=new System.Collections.Generic.List<Vector4>();
        mesh.GetUVs(2,ids); mesh.GetUVs(3,weights);
        var result=new Mesh {vertices=samples.Select(i=>positions[i]).ToArray()};
        result.SetUVs(2,samples.Select(i=>ids[i]).ToList());
        result.SetUVs(3,samples.Select(i=>weights[i]).ToList());
        result.SetIndices(Enumerable.Range(0,samples.Length).ToArray(),MeshTopology.Points,0);
        return result;
    }

    static Vector3[] Expected(SkinnedMeshRenderer source)
    {
        var mesh=new Mesh();
        source.BakeMesh(mesh,false);
        var positions=mesh.vertices.Select(v=>source.transform.position+source.transform.rotation*v).ToArray();
        Object.Destroy(mesh);
        return positions;
    }

    static void Compare(Mesh points, MeshRenderer proxy, Material probe, Vector3[] expected,string label,int mode=0)
    {
        var rt=RenderTexture.GetTemporary(expected.Length,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        probe.SetInt("_PointCount",expected.Length);
        probe.SetInt("_ProbeMode",mode);
        var cb=new CommandBuffer();
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(false,true,Color.clear);
        cb.DrawMesh(points,proxy.localToWorldMatrix,probe,0,0);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        var read=AsyncGPUReadback.Request(rt,0);
        read.WaitForCompletion();
        if(read.hasError) throw new Exception("Probe readback failed");
        var data=read.GetData<Vector4>();
        float max=0;
        for(int i=0;i<expected.Length;i++)
        {
            float error=Vector3.Distance(data[i],expected[i]);
            if(data[i].w!=1 || !(error<.001f)) throw new Exception($"{label} vertex {i}: {data[i]:F5} != {expected[i]:F5} ({error})");
            max=Mathf.Max(max,error);
        }
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log($"{label}: max vertex error {max:G4} world units");
    }
}
#endif
