#if UDONSHARP
using System;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.Udon;

public static class GpuOutputUdonValidation
{
    const string Key = "TSMP.GpuOutputVm";
    const string Root = "Assets/GpuOutputFixture";
    static int phase;
    static double since;

    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "GpuOutputFixture");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Decoder");
        var decoder = go.AddUdonSharpComponent<TSMPDecoder>();
        var view = new GameObject("Output").AddUdonSharpComponent<TSMPGpuOutput>();
        view.input = GpuOutputInput.TransSyncField;
        view.networkId = 13;
        view.variableHash = 0xfacedeadu;
        view.material = new Material(Shader.Find("TSMP/GPU Output/Copy Bytes"));
        AssetDatabase.CreateAsset(view.material, Root + "/Copy.mat");
        view.destination = Target("View");
        decoder.gpuOutputs = new[] { view };
        decoder.applyEveryFrame = false;
        decoder.payloadByteTexture = Target("Bytes");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        var codec = new GameObject("Luma4").AddUdonSharpComponent<TSMPCodecLuma4>();
        codec.byteDecodeMaterial = prefab.GetComponent<TSMPCodecLuma4>().byteDecodeMaterial;
        UdonSharpEditorUtility.CopyProxyToUdon(codec);
        decoder.codecHandlers = new TSMPCodec[] { codec };
        var image = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        var payload = new byte[29];
        int msg = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 1);
        int entry = NetworkFrameWriter.BeginVariableState(payload, msg, 13, 1);
        int value = NetworkFrameWriter.BeginVariableValue(payload, entry, view.variableHash, (int)NetworkValueType.Int32);
        Binary.WriteUInt32LE(payload, value, 0x44fea501u);
        NetworkFrameWriter.EndVariableValue(payload, entry, value + 4);
        NetworkFrameWriter.EndVariableState(payload, msg, value + 4, 1);
        NetworkFrameWriter.EndNetworkFrame(payload, 0, 1);
        var header = FrameHeader.CreateDefault();
        header.ActiveWidthBlocks = 80;
        header.ActiveHeightBlocks = 45;
        header.PayloadSize = (ushort)payload.Length;
        var bytes = new byte[FrameHeader.Size];
        header.WriteTo(bytes, 0);
        if (!codec.TryWriteFrame(image, 8, bytes, payload, out var error)) throw new Exception(error);
        AssetDatabase.CreateAsset(image, Root + "/Input.asset");
        decoder.sourceTexture = image;
        UdonSharpEditorUtility.CopyProxyToUdon(view);
        UdonSharpEditorUtility.CopyProxyToUdon(decoder);
        var world = new GameObject("World").AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
        world.spawns = new[] { new GameObject("Spawn").transform };
        GameObject.CreatePrimitive(PrimitiveType.Plane);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Root + "/Vm.unity");
        AssetDatabase.SaveAssets();
        SessionState.SetBool(Key, true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    static RenderTexture Target(string name)
    {
        var rt = new RenderTexture(16, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        AssetDatabase.CreateAsset(rt, Root + "/" + name + ".renderTexture");
        return rt;
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        since = EditorApplication.timeSinceStartup;
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - since > 35) throw new Exception("Udon loopback timeout");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var proxy = UnityEngine.Object.FindObjectOfType<TSMPDecoder>();
            var vm = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (phase == 0)
            {
                if (!vm.HasDoneStart) return;
                phase++;
                vm.SendCustomEvent("DecodeNow");
                return;
            }
            if (!(bool)vm.GetProgramVariable("lastFrameValid"))
            {
                var error = vm.GetProgramVariable("lastError") as string;
                if (!string.IsNullOrEmpty(error)) throw new Exception(error);
                return;
            }
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(Root + "/View.renderTexture");
            var read = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            read.WaitForCompletion();
            if (read.hasError || !read.GetData<byte>().Take(4).SequenceEqual(new byte[] { 1, 165, 254, 68 }))
                throw new Exception("Udon output is not the transmitted Int32");
            Debug.Log("PASS serialized Udon GPU field loopback");
            Finish(0);
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }

    static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
}
#endif
