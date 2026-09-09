using System;
using System.IO;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDON
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;
using VRC.Editor;
#endif

public static class VrcSupportValidation
{
    private static bool failed;

    public static void InitializeSdk()
    {
        VRC.Editor.EnvConfig.SetActiveSDKDefines();
        AssetDatabase.SaveAssets();
    }

#if UDON
    public static async void BuildWorld()
    {
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        try
        {
            EditorSceneManager.OpenScene("Assets/Validation/World.unity");
            UpdateLayers.SetupEditorLayers();
            UpdateLayers.SetupCollisionLayerMatrix();
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (descriptor.GetComponent<VRC.Core.PipelineManager>() == null) descriptor.gameObject.AddComponent<VRC.Core.PipelineManager>();
            EditorSceneManager.SaveOpenScenes();
            EditorWindow.GetWindow<VRCSdkControlPanel>();
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder)) throw new InvalidOperationException("SDK world builder unavailable");
            if (!builder.IsValidBuilder(out string message)) throw new InvalidOperationException(message);
            string bundle = await builder.Build();
            if (!File.Exists(bundle)) throw new InvalidOperationException("SDK did not produce a world bundle");
            File.WriteAllText(output, "PASS\nLocalSdkWorldBundle=" + bundle + "\nBytes=" + new FileInfo(bundle).Length);
            Debug.Log("[VrcSupportValidation] Built world: " + bundle);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(output, "FAIL\n" + exception);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
#endif

    public static void Compile()
    {
        Application.logMessageReceived += OnLog;
        UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false });
        var programs = UdonSharpProgramAsset.GetAllUdonSharpPrograms();
        int tsmp = 0;
        foreach (var program in programs)
        {
            if (!AssetDatabase.GetAssetPath(program).Contains("com.kibalab.tsmp")) continue;
            tsmp++;
            bool valid = program.GetRealProgram() != null && program.GetRealProgram().ByteCode.Length > 0;
            Debug.Log("[VrcSupportValidation] Program=" + program.name + " valid=" + valid);
            if (!valid) failed = true;
        }
        if (tsmp < 12) failed = true;
        Directory.CreateDirectory("Assets/Validation");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab");
        var controller = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var source = new GameObject("Transform Source");
        var sync = source.AddUdonSharpComponent<TSMPNetworkTransformSync>();
        sync.target = source.transform;
        sync.networkId = 101;
        var setup = controller.GetComponentInChildren<TSMPSetup>(true);
        setup.ApplyNow();
        var encoder = controller.GetComponentInChildren<TSMPEncoder>(true);
        var decoder = controller.GetComponentInChildren<TSMPDecoder>(true);
        bool bindings = encoder.bindingUdonTargets != null && encoder.bindingUdonTargets.Any(t => t != null)
            && decoder.bindingUdonTargets != null && decoder.bindingUdonTargets.Any(t => t != null);
        Debug.Log("[VrcSupportValidation] Setup Udon bindings=" + bindings);
        if (!bindings) failed = true;
        var scene = new GameObject("World").AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
        var spawn = new GameObject("Spawn").transform;
        spawn.position = new Vector3(0, 1, -2);
        scene.spawns = new[] { spawn };
        GameObject.CreatePrimitive(PrimitiveType.Plane).name = "Floor";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Validation/World.unity");
        AssetDatabase.SaveAssets();
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        File.WriteAllText(output, (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\nTSMPPrograms=" + tsmp + "\nBuildType=Udon client\nBindings=" + bindings);
        Application.logMessageReceived -= OnLog;
        if (failed) throw new InvalidOperationException("Udon validation failed; see logs");
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true;
    }
}
