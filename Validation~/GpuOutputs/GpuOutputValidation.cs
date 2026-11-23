#if UNITY_EDITOR && !UDONSHARP
using System;
using System.Collections;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class GpuOutputValidation : MonoBehaviour
{
    const uint Hash = 0xfedcba98u;
    const uint Stream = 0xff123456u;
    TSMPDecoder decoder;
    TSMPCodec codec;
    TSMPGpuOutput field, frame;
    Texture2D source;
    uint index = 0xee123456u;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("GPU output loopback").AddComponent<GpuOutputValidation>();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/GpuOutputValidation.unity");
        EditorApplication.EnterPlaymode();
    }

    IEnumerator Start()
    {
        var test = CheckFrames();
        while (true)
        {
            try { if (!test.MoveNext()) break; }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); yield break; }
            yield return test.Current;
        }
        Debug.Log("PASS GPU output loopback: " + SystemInfo.graphicsDeviceName);
        EditorApplication.Exit(0);
    }

    IEnumerator CheckFrames()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        codec = Instantiate(prefab).GetComponent<TSMPCodec>();
        source = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        decoder = gameObject.AddComponent<TSMPDecoder>();
        decoder.applyEveryFrame = false;
        decoder.debugLog = false;
        decoder.sourceTexture = source;
        decoder.payloadByteTexture = Target(37, 8);
        decoder.codecHandlers = new[] { codec };
        frame = Output(GpuOutputInput.NetworkFrame);
        field = Output(GpuOutputInput.TransSyncField);
        var probe = Output(GpuOutputInput.TransSyncField, "Hidden/TSMP Validation/Inspect GPU View", 7, 1);
        decoder.gpuOutputs = new[] { frame, field, probe };
        yield return null;

        byte[] expected = null, fieldBytes = null, metadata = null;
        for (int step = 0; step < 15; step++)
        {
            decoder.useCombinedByteOutput = step != 2;
            decoder.decodeSafetyMode = step == 7 ? 3 : 0;
            field.enabled = step != 11;
            if (step == 14) decoder.gpuOutputs = Array.Empty<TSMPGpuOutput>();
            bool present = step != 4 && step != 12;
            byte[] value = new byte[step == 3 ? 283 : step == 9 ? 0 : 279];
            new System.Random(step + 7).NextBytes(value);
            if (value.Length >= 4) Array.Copy(BitConverter.GetBytes(0.5f), value, 4);
            byte[] bytes = Payload(value, step == 4 ? Hash + 1 : Hash, step == 12 ? 14 : 13, step == 13);
            if (step == 5) Array.Resize(ref bytes, bytes.Length + 1);
            if (step != 6) index++;
            uint sentIndex = step == 6 ? index - 1 : index;
            Send(bytes, sentIndex, step == 10);
            if (step == 8)
            {
                value[4] ^= 0xff;
                bytes = Payload(value, Hash);
                Send(bytes, ++index);
                Require(decoder.pendingDecodeCount == 2, "Did not exercise the second slot");
            }
            float end = Time.realtimeSinceStartup + 10;
            while (decoder.readbackInFlight && Time.realtimeSinceStartup < end) yield return null;
            Require(!decoder.readbackInFlight, "Readback timed out");
            if (step == 5 || step == 10) Require(!decoder.lastFrameValid, "Malformed frame accepted");
            if (step == 14) Require(decoder.lastFrameValid, "Normal decoding requires no GPU outputs");
            if (step != 5 && step != 6 && step != 7 && step != 10 && step != 14)
            {
                Require(decoder.lastFrameValid, decoder.lastError);
                expected = bytes;
                if (present)
                {
                    if (field.enabled) fieldBytes = value;
                    metadata = new uint[] { Stream, index, (uint)value.Length, (uint)NetworkValueType.RawBytes,
                        value.Length == 0 ? 0u : 500u, value.Length == 0 ? 0u : BitConverter.ToUInt16(value, 3), 0u }
                        .SelectMany(BitConverter.GetBytes).ToArray();
                }
            }
            Debug.Log($"GPU step={step} accepted={decoder.lastFrameIndex} predicted={decoder.predictedReadbackCount} combined={decoder.combinedByteOutputCount} fallback={decoder.predictionFallbackCount} status={decoder.lastError}");
            AssertBytes(frame.destination, expected, "frame at " + step);
            AssertBytes(field.destination, fieldBytes, "field at " + step);
            AssertBytes(probe.destination, metadata, "view metadata at " + step);
            if (step == 1) Require(decoder.combinedByteOutputCount > 0 && decoder.predictedReadbackCount > 0, "No direct prediction");
            if (step == 2) Require(decoder.predictedReadbackCount >= 2, "No packed prediction");
            if (step == 3) Require(decoder.predictionFallbackCount > 0, "No fallback");
            if (step == 6) Require(decoder.skippedDuplicateFrameCount > 0, "Duplicate was not filtered");
        }
    }

    TSMPGpuOutput Output(GpuOutputInput input, string shader = "TSMP/GPU Output/Copy Bytes", int width = 19, int height = 5)
    {
        var pass = gameObject.AddComponent<TSMPGpuOutput>();
        pass.input = input;
        pass.networkId = 13;
        pass.variableHash = Hash;
        pass.material = new Material(Shader.Find(shader));
        pass.destination = Target(width, height);
        return pass;
    }

    static RenderTexture Target(int width, int height)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.Create();
        return rt;
    }

    static byte[] Payload(byte[] value, uint hash, int networkId = 13, bool repeat = false)
    {
        var payload = new byte[512];
        int message = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 0);
        int entry = NetworkFrameWriter.BeginVariableState(payload, message, networkId, 0);
        if (repeat)
        {
            int first = NetworkFrameWriter.BeginVariableValue(payload, entry, hash, (int)NetworkValueType.RawBytes);
            payload[first] = 42;
            Require(NetworkFrameWriter.EndVariableValue(payload, entry, first + 1), "Invalid repeated entry");
            entry = first + 1;
        }
        int offset = NetworkFrameWriter.BeginVariableValue(payload, entry, hash, (int)NetworkValueType.RawBytes);
        Array.Copy(value, 0, payload, offset, value.Length);
        int end = offset + value.Length;
        Require(NetworkFrameWriter.EndVariableValue(payload, entry, end) &&
            NetworkFrameWriter.EndVariableState(payload, message, end, repeat ? 2 : 1) &&
            NetworkFrameWriter.EndNetworkFrame(payload, 0, 1), "Invalid fixture");
        Array.Resize(ref payload, end);
        return payload;
    }

    void Send(byte[] bytes, uint frameIndex, bool corruptHeader = false)
    {
        var header = FrameHeader.CreateDefault();
        header.ActiveWidthBlocks = 80;
        header.ActiveHeightBlocks = 45;
        header.FrameIndex = frameIndex;
        header.StreamId = Stream;
        header.PayloadSize = (ushort)bytes.Length;
        var headerBytes = new byte[FrameHeader.Size];
        header.WriteTo(headerBytes, 0);
        if (corruptHeader) headerBytes[FrameHeader.CrcOffset] ^= 1;
        Require(codec.TryWriteFrame(source, 8, headerBytes, bytes, out var error), error);
        decoder.DecodeNow();
    }

    static void AssertBytes(RenderTexture rt, byte[] expected, string label)
    {
        var read = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
        read.WaitForCompletion();
        Require(!read.hasError, "Output readback failed");
        var data = read.GetData<byte>().ToArray();
        for (int i = 0; i < data.Length; i++)
        {
            int value = i < expected.Length ? expected[i] : 0;
            Require(data[i] == value, $"{label}: byte {i}, expected {value}, got {data[i]}");
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
