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
        decoder.shaderChannel = ShaderReceiveChannel.Channel1;
        decoder.shaderChannelMaterial = Resources.Load<Material>("TSMPShaderChannel");
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
        int value = NetworkFrameWriter.BeginVariableValue(payload, entry, 0xfacedeadu, (int)NetworkValueType.Int32);
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
        var nextImage = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        header.FrameIndex++;
        header.WriteTo(bytes, 0);
        Binary.WriteUInt32LE(payload, entry + NetworkFrameProtocol.VariableValueHashOffset, 7u);
        Binary.WriteUInt32LE(payload, value, 0xdeadbeefu);
        if (!codec.TryWriteFrame(nextImage, 8, bytes, payload, out error)) throw new Exception(error);
        AssetDatabase.CreateAsset(nextImage, Root + "/OtherField.asset");
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
            if (phase == 2 && ((bool)vm.GetProgramVariable("readbackInFlight") || (uint)vm.GetProgramVariable("lastFrameIndex") != 1u))
                return;
            var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var consumer = new Material(Shader.Find("Hidden/TSMP Validation/Inspect GPU View"));
            consumer.SetInteger("_Mode", 3);
            consumer.SetInteger("_Channel", 1);
            consumer.SetInteger("_Width", 1);
            Graphics.Blit(null, rt, consumer);
            var read = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            read.WaitForCompletion();
            bool valid = !read.hasError && read.GetData<byte>().Take(4).SequenceEqual(new byte[] { 1, 165, 254, 68 });
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(consumer);
            if (!valid)
                throw new Exception("Udon output is not the transmitted Int32");
            if (phase == 1)
            {
                phase++;
                vm.SetProgramVariable("sourceTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/OtherField.asset"));
                vm.SendCustomEvent("DecodeNow");
                return;
            }
            Debug.Log("PASS serialized Udon shader lookup and retained field");
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
