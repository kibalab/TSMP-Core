#if !UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CrowdProfileBuild
{
    const string Templates = "Assets/SkinProfileData/";
    const string Folder = "Assets/SkinCrowd/";
    static readonly uint Hash = StableHash.VariableHash(typeof(TSMPNetworkHumanoidPoseSync), "poseBytes", "humanoid.pose");

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SkinCrowd")) AssetDatabase.CreateFolder("Assets", "SkinCrowd");
        var scenes = new List<string> { Templates + "Boot.unity" };
        bool wide = Environment.GetEnvironmentVariable("TSMP_CROWD_WIDE") == "1";
        foreach (int count in wide ? new[] { 64, 80 } : new[] { 1, 8, 16, 32 })
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var expected = new Quaternion[count];
            var frames = Frames(count, expected);
            foreach (string mode in new[] { "cpu", "gpu", "hybrid" })
            {
                var scene = EditorSceneManager.OpenScene(Templates + mode + ".unity");
                var template = Object.FindObjectOfType<SkinProfile>();
                var bench = template.gameObject.AddComponent<CrowdProfile>();
                bench.source = template;
                template.enabled = false;
                template.images = frames;
                template.decoder.sourceTexture = frames[0];
                template.decoder.blockSize = 4;
                var rt = new RenderTexture(256, wide ? 32 : 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                AssetDatabase.CreateAsset(rt, AssetDatabase.GenerateUniqueAssetPath(Folder + "Payload.renderTexture"));
                template.decoder.payloadByteTexture = rt;
                bench.finalArm = expected;
                var path = Folder + mode + "-" + count + ".unity";
                EditorSceneManager.SaveScene(scene, path);
                scenes.Add(path);
            }
        }
        AssetDatabase.SaveAssets();
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.gpuSkinning = true;
        var exe = Path.Combine(Environment.GetEnvironmentVariable("TSMP_SKIN_OUT"), wide ? "WidePlayer" : "Player", "Crowd.exe");
        var report = BuildPipeline.BuildPlayer(scenes.ToArray(), exe, BuildTarget.StandaloneWindows64, BuildOptions.Development);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception(report.summary.result.ToString());
    }

    static Texture2D[] Frames(int count, Quaternion[] expected)
    {
        const string model = "Packages/com.kibalab.tsmp.core/Samples/TSMPExample/Models/Shapell/data/shapell.fbx";
        var a = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(model)).GetComponentInChildren<Animator>();
        var sender = a.gameObject.AddComponent<TSMPNetworkHumanoidPoseSync>();
        sender.animator = a;
        var arm = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var rest = arm.localRotation;
        var codec = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab").GetComponent<TSMPCodec>();
        var frames = new Texture2D[8];
        for (int f = 0; f < frames.Length; f++)
        {
            var payload = new byte[8 + count * 344];
            int pos = NetworkFrameWriter.BeginNetworkFrame(payload, 0, (uint)(f + 1));
            for (int k = 0; k < count; k++)
            {
                arm.localRotation = rest * Quaternion.Euler(0, 0, k * 3 + f * 2);
                sender.TSMPBeforeEncode();
                if (sender.poseBytes.Length != 327) throw new Exception("Expected 51 bones");
                int entry = NetworkFrameWriter.BeginVariableState(payload, pos, 37 + k, (ushort)(f + 1));
                int addr = NetworkFrameWriter.BeginVariableValue(payload, entry, Hash, (int)NetworkValueType.RawBytes);
                Buffer.BlockCopy(sender.poseBytes, 0, payload, addr, 327);
                int end = addr + 327;
                if (!NetworkFrameWriter.EndVariableValue(payload, entry, end) || !NetworkFrameWriter.EndVariableState(payload, pos, end, 1))
                    throw new Exception("Pose did not fit");
                pos = end;
                expected[k] = Quaternion.Inverse(a.transform.rotation) * arm.rotation;
            }
            if (pos != payload.Length || !NetworkFrameWriter.EndNetworkFrame(payload, 0, count)) throw new Exception("Invalid payload");
            var header = FrameHeader.CreateDefault();
            header.FrameIndex = (uint)(f + 1); header.BlockSize = 4;
            header.ActiveWidthBlocks = 320; header.ActiveHeightBlocks = 180; header.PayloadSize = (ushort)pos;
            var wire = new byte[FrameHeader.Size];
            header.WriteTo(wire, 0);
            var image = frames[f] = new Texture2D(1280, 720, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            if (!codec.TryWriteFrame(image, 4, wire, payload, out string error)) throw new Exception(error);
            var path = Folder + count + "-" + f + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!old) AssetDatabase.CreateAsset(image, path);
            else { EditorUtility.CopySerialized(image, old); EditorUtility.SetDirty(old); frames[f] = old; }
        }
        return frames;
    }
}
#endif
