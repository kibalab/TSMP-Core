using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public sealed class LoopbackValidation : MonoBehaviour
{
    public TSMPSetup setup;
    public TSMPEncoder encoder;
    public TSMPDecoder decoder;
    public TSMPNetworkTransformSync sentTransform;
    public TSMPNetworkTransformSync receivedTransform;
    public TSMPNetworkHumanoidPoseSync sentPose;
    public TSMPNetworkHumanoidPoseSync receivedPose;
    public LoopbackProbe sentValues;
    public LoopbackProbe receivedValues;
    public AnimationClip motion;
    private bool failed;
    private bool expectInvalidHeader;
    private int framesChecked;
    private readonly List<string> checks = new List<string>();

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        Application.logMessageReceived += OnLog;
        yield return null;
        setup.enabled = false;
        encoder.autoEncode = false;
        decoder.applyEveryFrame = false;
        ConfigureBindings();
        Check(SystemInfo.supportsAsyncGPUReadback, "GPU readback supported: " + SystemInfo.graphicsDeviceName);
        Check(sentPose.animator.isHuman && receivedPose.animator.isHuman, "Real humanoid Avatars loaded");
        sentPose.ResolveBones();
        receivedPose.ResolveBones();
        sentValues.transRpcEncoder = encoder;
        sentValues.SendTransRPC(nameof(LoopbackProbe.RecordRpc), RPCTarget.Remote);
        Check(sentValues.rpcCalls == 0, "Remote RPC does not execute on sender");
        for (int i = 0; i < 6 && !failed; i++)
        {
            sentTransform.target.localPosition = new Vector3(i * 0.31f, 0.2f, -1.2f);
            sentTransform.target.localRotation = Quaternion.Euler(5f, i * 17f, 10f);
            sentValues.number = 100 + i;
            sentValues.text = "Frame " + i + " \U0001f441\ufe0f\U0001f441\ufe0f";
            Transform arm = sentPose.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            motion.SampleAnimation(sentPose.animator.gameObject, (i + 1) * 0.1f);
            Transform hips = sentPose.animator.GetBoneTransform(HumanBodyBones.Hips);
            Check(Quaternion.Angle(Quaternion.identity, arm.localRotation) > 1f, "AnimationClip updates the humanoid source rig");
            encoder.EncodeNow();
            Check(string.IsNullOrEmpty(encoder.lastError), "Encode: " + encoder.lastError);
            Check(encoder.payloadBytes > 0, "Non-empty payload: " + encoder.payloadBytes);
            yield return null;
            decoder.DecodeNow();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (decoder.readbackInFlight && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(!decoder.readbackInFlight && decoder.lastFrameValid, "Decode: " + decoder.lastError);
            Check(Vector3.Distance(sentTransform.target.localPosition, receivedTransform.target.localPosition) < 0.002f, "Transform position applied");
            Check(Quaternion.Angle(sentTransform.target.localRotation, receivedTransform.target.localRotation) < 0.5f, "Transform rotation applied");
            Check(receivedValues.number == sentValues.number && receivedValues.text == sentValues.text, "Component int/Unicode string round-trip");
            Check(Quaternion.Angle(arm.rotation, receivedPose.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).rotation) < 0.5f, "Humanoid arm rotation applied");
            Check(Vector3.Distance(hips.localPosition, receivedPose.animator.GetBoneTransform(HumanBodyBones.Hips).localPosition) < 0.002f, "Humanoid root position applied");
            framesChecked++;
        }
        Check(receivedValues.rpcCalls == 1, "Remote RPC applied exactly once despite repeats: " + receivedValues.rpcCalls);
        int previousValue = receivedValues.number;
        RenderTexture blank = new RenderTexture(encoder.output.width, encoder.output.height, 0, RenderTextureFormat.ARGB32);
        blank.Create();
        Graphics.Blit(Texture2D.blackTexture, blank);
        decoder.sourceTexture = blank;
        expectInvalidHeader = true;
        decoder.DecodeNow();
        float timeout = Time.realtimeSinceStartup + 10f;
        while (decoder.readbackInFlight && Time.realtimeSinceStartup < timeout)
            yield return null;
        Check(!decoder.lastFrameValid && receivedValues.number == previousValue, "Blank input rejected without variable mutation");
        expectInvalidHeader = false;
        blank.Release();
        Finish();
    }

    private void ConfigureBindings()
    {
        TSMPNetworkBehaviour[] sources = { sentTransform, sentPose, sentValues };
        TSMPNetworkBehaviour[] destinations = { receivedTransform, receivedPose, receivedValues };
        encoder.networkBehaviours = sources;
        encoder.bindingTargets = Array.Empty<Component>();
        var targets = new List<Component>();
        var ids = new List<ushort>();
        var hashes = new List<uint>();
        var types = new List<byte>();
        var names = new List<string>();
        var directions = new List<int>();
        var priorities = new List<int>();
        var cache = new Dictionary<Type, TransSyncMetadata.Cache>();
        for (int i = 0; i < sources.Length; i++)
        {
            foreach (var field in TransSyncMetadata.GetOrCreate(cache, sources[i].GetType()).Fields)
            {
                targets.Add(destinations[i]);
                ids.Add(sources[i].networkId);
                hashes.Add(field.VariableHash);
                types.Add((byte)field.ValueType);
                names.Add(field.FieldInfo.Name);
                directions.Add(0);
                priorities.Add(0);
            }
        }
        decoder.bindingTargets = targets.ToArray();
        decoder.bindingNetworkIds = ids.ToArray();
        decoder.bindingVariableHashes = hashes.ToArray();
        decoder.bindingValueTypes = types.ToArray();
        decoder.bindingFieldNames = names.ToArray();
        decoder.bindingDirections = directions.ToArray();
        decoder.bindingPriorities = priorities.ToArray();
    }

    private void Check(bool valid, string message)
    {
        checks.Add((valid ? "PASS " : "FAIL ") + message);
        if (!valid) failed = true;
        Debug.Log("[UnitySupportValidation] " + checks[checks.Count - 1]);
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
        if (expectInvalidHeader && type == LogType.Error && message.StartsWith("[TSMP] Header magic mismatch.", StringComparison.Ordinal)) return;
        failed = true;
        checks.Add(type + ": " + message + "\n" + stack);
    }

    private void Finish()
    {
        string path = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.persistentDataPath, "tsmp-validation.txt");
        File.WriteAllText(path, (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\nGPU=" + SystemInfo.graphicsDeviceName + "\nAPI=" + SystemInfo.graphicsDeviceType + "\nFrames=" + framesChecked + "\n" + string.Join("\n", checks));
        Debug.Log("[UnitySupportValidation] Result: " + path);
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failed ? 1 : 0);
#else
        Application.Quit(failed ? 1 : 0);
#endif
    }
}
