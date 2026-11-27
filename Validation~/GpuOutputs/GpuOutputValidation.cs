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
    Material consumer;
    uint stream = Stream;
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
        decoder.shaderChannel = ShaderReceiveChannel.Channel0;
        consumer = new Material(Shader.Find("Hidden/TSMP Validation/Inspect GPU View"));
        var frame = Target(19, 5);
        var field = Target(19, 5);
        var probe = Target(7, 1);
        yield return null;

        byte[] expected = null, fieldBytes = null, metadata = null;
        for (int step = 0; step < 17; step++)
        {
            decoder.useCombinedByteOutput = step != 2;
            decoder.decodeSafetyMode = step == 7 ? 3 : 0;
            if (step == 14) stream++;
            if (step == 15) decoder.shaderChannel = ShaderReceiveChannel.Channel2;
            if (step == 16) decoder.shaderChannel = ShaderReceiveChannel.Off;
            bool present = step != 4 && step != 12 && step != 14;
            byte[] value = new byte[step == 3 ? 283 : step == 9 ? 0 : 279];
            new System.Random(step + 7).NextBytes(value);
            if (value.Length >= 4) Array.Copy(BitConverter.GetBytes(0.5f), value, 4);
            byte[] bytes = Payload(value, step == 4 || step == 14 ? Hash + 1 : Hash, step == 12 ? 14 : 13, step == 13);
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
            if (step == 16) Require(decoder.lastFrameValid, "Normal decoding requires no shader channel");
            if (step != 5 && step != 6 && step != 7 && step != 10 && step != 16)
            {
                Require(decoder.lastFrameValid, decoder.lastError);
                expected = bytes;
                if (present) fieldBytes = value;
                if (step == 14) fieldBytes = null;
                metadata = fieldBytes == null ? null : new uint[] { stream, index, (uint)fieldBytes.Length, (uint)NetworkValueType.RawBytes,
                    fieldBytes.Length == 0 ? 0u : 500u, fieldBytes.Length == 0 ? 0u : BitConverter.ToUInt16(fieldBytes, 3), 0u }
                    .SelectMany(BitConverter.GetBytes).ToArray();
            }
            int channel = step >= 15 ? 2 : 0;
            if (step == 16) expected = fieldBytes = metadata = null;
            CheckView(frame, 0, channel, expected, "frame at " + step);
            CheckView(field, 1, channel, fieldBytes, "field at " + step);
            CheckView(probe, 2, channel, metadata, "metadata at " + step);
            CheckView(frame, 0, channel == 0 ? 2 : 0, null, "inactive channel at " + step);
            if (step == 1) Require(decoder.combinedByteOutputCount > 0 && decoder.predictedReadbackCount > 0, "No direct prediction");
            if (step == 2) Require(decoder.predictedReadbackCount >= 2, "No packed prediction");
            if (step == 3) Require(decoder.predictionFallbackCount > 0, "No fallback");
            if (step == 6) Require(decoder.skippedDuplicateFrameCount > 0, "Duplicate was not filtered");
        }
        decoder.shaderChannel = ShaderReceiveChannel.Channel0;
        var first = decoder;
        var many = ManyFields();
        Send(many, ++index);
        float timeout = Time.realtimeSinceStartup + 10;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < timeout) yield return null;
        Require(!decoder.readbackInFlight && decoder.lastFrameValid, "Multi-row directory decode failed");
        byte[] marker = { 9, 8, 7, 6 };
        var large = Target(37, 8);
        CheckView(large, 0, 0, many, "multi-row frame");
        CheckView(field, 1, 0, marker, "multi-row field");

        Send(TypedFields(), ++index);
        timeout = Time.realtimeSinceStartup + 10;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < timeout) yield return null;
        Require(!decoder.readbackInFlight && decoder.lastFrameValid, "Typed fields did not decode");
        var typed = new Material(Shader.Find("Hidden/TSMP Validation/Typed Fields"));
        var checks = Target(32, 1);
        Graphics.Blit(null, checks, typed);
        AssertBytes(checks, Enumerable.Repeat((byte)255, checks.width * 4).ToArray(), "typed field checks");
        Destroy(typed);
        checks.Release();

        decoder = new GameObject("Independent decoder").AddComponent<TSMPDecoder>();
        decoder.applyEveryFrame = false;
        decoder.debugLog = false;
        decoder.sourceTexture = source;
        decoder.payloadByteTexture = Target(37, 8);
        decoder.codecHandlers = new[] { codec };
        decoder.shaderChannel = ShaderReceiveChannel.Channel3;
        yield return null;
        byte[] other = { 42 };
        Send(Payload(other, Hash), ++index);
        timeout = Time.realtimeSinceStartup + 10;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < timeout) yield return null;
        Require(!decoder.readbackInFlight && decoder.lastFrameValid, "Other decoder failed");
        CheckView(field, 1, 0, marker, "independent first channel");
        CheckView(field, 1, 3, other, "independent second channel");
        first.enabled = false;
        yield return null;
        CheckView(field, 1, 0, null, "disabled decoder");
        CheckView(field, 1, 3, other, "surviving decoder");
        decoder.enabled = false;
        Destroy(consumer);
        foreach (var target in new[] { frame, field, probe, large, decoder.payloadByteTexture, first.payloadByteTexture })
            target.Release();
    }

    static byte[] TypedFields()
    {
        var values = new object[] {
            false, int.MinValue + 1, 0f, new Vector2(-1, 0.5f), new Vector3(8, -2, 3), new Quaternion(1, -2, 3, 4),
            "A\uD55C\U0001F441", new byte[] {0, 128, 255, 64}, new[] {false, true}, new[] {0, -7}, new[] {1f, -2f, 3f, 4f},
            new[] {Vector2.zero, Vector2.up}, new[] {Vector3.zero, Vector3.back}, new[] {Quaternion.identity},
            new[] {"", "\u00E9", "\U0001F441"}, Enumerable.Range(0, 16).Select(n => (float)n).ToArray(),
            Array.Empty<int>(), "", Array.Empty<byte>(), Array.Empty<string>(), new byte[] {0xf0, 0x80, 0x80, 0x80},
            new byte[] {2, 0, 2, 0, 65}, new byte[] {0xed, 0xa0, 0x80}, new byte[] {0xe2, 0x82}
        };
        var payload = new byte[1024];
        int msg = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 0);
        int entry = NetworkFrameWriter.BeginVariableState(payload, msg, 27, 0);
        for (int i = 0; i < values.Length; i++)
        {
            var obj = values[i];
            Require(NetworkValueCodec.TryGetValueType(obj.GetType(), out var type), "No type for fixture " + i);
            NetworkValueType wireType = i < 20 ? type : i == 21 ? NetworkValueType.UTF8StringArray : NetworkValueType.UTF8String;
            int start = NetworkFrameWriter.BeginVariableValue(payload, entry, (uint)i + 1, (int)wireType);
            int end = NetworkValueWriter.WriteObject(payload, start, (int)type, obj);
            Require(end >= start && NetworkFrameWriter.EndVariableValue(payload, entry, end), "Invalid typed fixture");
            entry = end;
        }
        Require(NetworkFrameWriter.EndVariableState(payload, msg, entry, values.Length) &&
            NetworkFrameWriter.EndNetworkFrame(payload, 0, 1), "Invalid typed frame");
        Array.Resize(ref payload, entry);
        return payload;
    }

    static byte[] ManyFields()
    {
        var payload = new byte[1024];
        const int count = 66;
        int message = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 0);
        int entry = NetworkFrameWriter.BeginVariableState(payload, message, 13, 0);
        for (int i = count - 1; i >= 0; i--)
        {
            uint hash = i == 0 ? Hash : (uint)i;
            int offset = NetworkFrameWriter.BeginVariableValue(payload, entry, hash, (int)NetworkValueType.RawBytes);
            Binary.WriteUInt32LE(payload, offset, i == 0 ? 0x06070809u : (uint)i);
            Require(NetworkFrameWriter.EndVariableValue(payload, entry, offset + 4), "Invalid directory fixture");
            entry = offset + 4;
        }
        Require(NetworkFrameWriter.EndVariableState(payload, message, entry, count) &&
            NetworkFrameWriter.EndNetworkFrame(payload, 0, 1), "Invalid multi-row fixture");
        Array.Resize(ref payload, entry);
        return payload;
    }

    void CheckView(RenderTexture rt, int mode, int channel, byte[] expected, string label)
    {
        consumer.SetInteger("_Mode", mode);
        consumer.SetInteger("_Width", rt.width);
        consumer.SetInteger("_Channel", channel);
        Graphics.Blit(null, rt, consumer);
        AssertBytes(rt, expected ?? Array.Empty<byte>(), label);
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
        header.StreamId = stream;
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
