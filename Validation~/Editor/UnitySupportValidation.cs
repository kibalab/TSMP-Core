using System;
using System.Collections.Generic;
using System.IO;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnitySupportValidation
{
    private const string ScenePath = "Assets/Validation/Loopback.unity";

    public static void RunEditor()
    {
        CreateScene();
        EditorApplication.EnterPlaymode();
    }

    public static void BuildMono()
    {
        CreateScene();
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_BUILD");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("TSMP_VALIDATION_BUILD is required");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        File.WriteAllText(Path.ChangeExtension(output, ".build-report.txt"),
            "Result=" + report.summary.result + "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\nSize=" + report.summary.totalSize + "\nDuration=" + report.summary.totalTime + "\nBackend=Mono\nStripping=Disabled");
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Player build failed");
    }

    private static void CreateScene()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour)) throw new InvalidOperationException("Validation project unexpectedly has UdonSharp");
        if (PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Contains("UDON")) throw new InvalidOperationException("Unexpected Udon define");
        Directory.CreateDirectory("Assets/Validation/Generated");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AuditSamples();
        string unitySample = "Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab";
        var nativeSample = AssetDatabase.LoadAssetAtPath<GameObject>(unitySample);
        foreach (Transform child in nativeSample.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0) throw new InvalidOperationException("Unity sample has missing scripts");
        Debug.Log("[UnitySupportValidation] Shared controller: " + unitySample);
        var controller = (GameObject)PrefabUtility.InstantiatePrefab(nativeSample);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        controller.SetActive(false);
        var encoder = controller.GetComponentInChildren<TSMPEncoder>(true);
        encoder.autoEncode = false;
        var decoder = controller.GetComponentInChildren<TSMPDecoder>(true);
        decoder.applyEveryFrame = false;
        var setup = controller.GetComponentInChildren<TSMPSetup>(true);
        setup.applyOnValidate = false;
        setup.driveEncoderInEditor = false;
        setup.encoder = encoder;
        setup.decoder = decoder;
        setup.width = 640;
        setup.height = 360;
        setup.blockSize = 8;
        setup.encoderOutput = CreateTexture("Output", 640, 360);
        setup.payloadByteTexture = CreateTexture("Bytes", 1024, 1);
        var prefab = nativeSample.GetComponentInChildren<TSMPSetup>(true).codecPrefabs[0].gameObject;
        if (prefab == null) throw new InvalidOperationException("Real Luma4 prefab missing");
        setup.autoDiscoverCodecs = true;
        setup.codecPrefabs = new[] { prefab.GetComponent<TSMPCodec>() };

        var test = new GameObject("Loopback Validation").AddComponent<LoopbackValidation>();
        test.setup = setup;
        test.encoder = encoder;
        test.decoder = decoder;
        test.sentTransform = CreateTransform("Sent Transform", 101);
        test.receivedTransform = CreateTransform("Received Transform", 201);
        test.sentPose = CreateHumanoid("Sent Humanoid", 102);
        test.receivedPose = CreateHumanoid("Received Humanoid", 202);
        string clipPath = "Assets/Validation/Generated/Motion.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "Motion", frameRate = 60 };
            string armPath = AnimationUtility.CalculateTransformPath(test.sentPose.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), test.sentPose.transform);
            clip.SetCurve(armPath, typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 80));
            clip.SetCurve("Hips", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, .2f));
            clip.SetCurve("Hips", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0, 1, 1));
            clip.SetCurve("Hips", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0, 0, 1, .3f));
            AssetDatabase.CreateAsset(clip, clipPath);
        }
        test.motion = clip;
        test.sentValues = new GameObject("Sent Variables").AddComponent<LoopbackProbe>();
        test.sentValues.networkId = 103;
        test.receivedValues = new GameObject("Received Variables").AddComponent<LoopbackProbe>();
        test.receivedValues.networkId = 203;

        controller.SetActive(true);
        setup.ApplyNow();
        if (encoder.selectedCodec == null || decoder.codecHandlers == null || decoder.codecHandlers.Length == 0) throw new InvalidOperationException("Setup did not configure codecs");
        if (decoder.bindingTargets == null || decoder.bindingTargets.Length < 4) throw new InvalidOperationException("Setup did not generate native bindings");
        Debug.Log("[UnitySupportValidation] Setup built " + decoder.bindingTargets.Length + " native bindings");
        foreach (Transform child in controller.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                throw new InvalidOperationException("Shared controller contains missing scripts");
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 1, -5);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[UnitySupportValidation] Scene saved: " + ScenePath);
    }

    private static RenderTexture CreateTexture(string name, int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        texture.name = name;
        texture.filterMode = FilterMode.Point;
        string path = "Assets/Validation/Generated/" + name + ".renderTexture";
        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (existing != null) return existing;
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    private static TSMPNetworkTransformSync CreateTransform(string name, ushort id)
    {
        var sync = GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<TSMPNetworkTransformSync>();
        sync.name = name;
        sync.networkId = id;
        sync.target = sync.transform;
        sync.syncRigidbody = false;
        return sync;
    }

    private static TSMPNetworkHumanoidPoseSync CreateHumanoid(string name, ushort id)
    {
        var root = new GameObject(name);
        var bones = new Dictionary<HumanBodyBones, Transform>();
        AddBone(bones, HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
        AddBone(bones, HumanBodyBones.Spine, bones[HumanBodyBones.Hips], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.Chest, bones[HumanBodyBones.Spine], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.Neck, bones[HumanBodyBones.Chest], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.Head, bones[HumanBodyBones.Neck], new Vector3(0, .2f, 0));
        AddBone(bones, HumanBodyBones.LeftUpperArm, bones[HumanBodyBones.Chest], new Vector3(-.2f, .1f, 0));
        AddBone(bones, HumanBodyBones.LeftLowerArm, bones[HumanBodyBones.LeftUpperArm], new Vector3(-.3f, 0, 0));
        AddBone(bones, HumanBodyBones.LeftHand, bones[HumanBodyBones.LeftLowerArm], new Vector3(-.25f, 0, 0));
        AddBone(bones, HumanBodyBones.RightUpperArm, bones[HumanBodyBones.Chest], new Vector3(.2f, .1f, 0));
        AddBone(bones, HumanBodyBones.RightLowerArm, bones[HumanBodyBones.RightUpperArm], new Vector3(.3f, 0, 0));
        AddBone(bones, HumanBodyBones.RightHand, bones[HumanBodyBones.RightLowerArm], new Vector3(.25f, 0, 0));
        AddBone(bones, HumanBodyBones.LeftUpperLeg, bones[HumanBodyBones.Hips], new Vector3(-.1f, -.1f, 0));
        AddBone(bones, HumanBodyBones.LeftLowerLeg, bones[HumanBodyBones.LeftUpperLeg], new Vector3(0, -.4f, 0));
        AddBone(bones, HumanBodyBones.LeftFoot, bones[HumanBodyBones.LeftLowerLeg], new Vector3(0, -.4f, .05f));
        AddBone(bones, HumanBodyBones.RightUpperLeg, bones[HumanBodyBones.Hips], new Vector3(.1f, -.1f, 0));
        AddBone(bones, HumanBodyBones.RightLowerLeg, bones[HumanBodyBones.RightUpperLeg], new Vector3(0, -.4f, 0));
        AddBone(bones, HumanBodyBones.RightFoot, bones[HumanBodyBones.RightLowerLeg], new Vector3(0, -.4f, .05f));
        var human = new List<HumanBone>();
        foreach (var bone in bones)
            human.Add(new HumanBone { boneName = bone.Value.name, humanName = HumanTrait.BoneName[(int)bone.Key], limit = new HumanLimit { useDefaultValues = true } });
        var skeleton = new List<SkeletonBone>();
        foreach (var bone in root.GetComponentsInChildren<Transform>())
            skeleton.Add(new SkeletonBone { name = bone.name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale });
        var avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
        {
            human = human.ToArray(), skeleton = skeleton.ToArray(),
            armStretch = .05f, legStretch = .05f, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f
        });
        if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Generated Avatar is not humanoid");
        string path = "Assets/Validation/Generated/" + name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Avatar>(path);
        if (saved == null) { AssetDatabase.CreateAsset(avatar, path); saved = avatar; }
        else UnityEngine.Object.DestroyImmediate(avatar);
        var animator = root.AddComponent<Animator>();
        animator.avatar = saved;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var sync = root.AddComponent<TSMPNetworkHumanoidPoseSync>();
        sync.animator = animator;
        sync.networkId = id;
        sync.includeFingerBones = false;
        sync.ResolveBones();
        return sync;
    }

    private static void AddBone(Dictionary<HumanBodyBones, Transform> bones, HumanBodyBones id, Transform parent, Vector3 position)
    {
        var bone = new GameObject(id.ToString()).transform;
        bone.SetParent(parent, false);
        bone.localPosition = position;
        bones.Add(id, bone);
    }

    private static void AuditSamples()
    {
        string[] paths = {
            "Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab",
            "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"
        };
        foreach (string path in paths)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int missing = 0;
            if (root != null)
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            Debug.Log("[UnitySupportValidation] Sample audit: " + path + " missing=" + missing + " found=" + (root != null));
        }
    }

}
