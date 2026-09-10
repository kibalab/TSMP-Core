using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharp;
using UdonSharpEditor;
#endif

public static class SharedWorkflowValidation
{
    private const string PrefabPath = "Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab";
    private const string ScenePath = "Assets/Validation/SharedWorkflow.unity";
    private static int phase;
    private static int ticks;
    private static int undoGroup;
    private static double deadline;
    private static double nextCheck;
    private static TSMPSetup original;
    private static TSMPSetup duplicate;
    private static string sourceHash;
    private static readonly StringBuilder Result = new StringBuilder();

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        sourceHash = Hash(PrefabPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab.GetComponentsInChildren<TSMPEncoder>(true).Length == 0, "SDK-neutral controller source");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        root.name = "First Controller";
        original = root.GetComponent<TSMPSetup>();
        var source = new GameObject("Transform Source");
#if UDONSHARP
        var sync = source.AddUdonSharpComponent<TSMPNetworkTransformSync>();
#else
        var sync = source.AddComponent<TSMPNetworkTransformSync>();
#endif
        sync.networkId = 401;
        sync.target = source.transform;
        deadline = EditorApplication.timeSinceStartup + 120;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++ticks < 20 || EditorApplication.timeSinceStartup < nextCheck)
            return;
        ticks = 0;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Workflow preparation timed out");
            if (phase == 0)
            {
                CheckPrepared(original);
                ((TSMPEncoder)original.encoder).frameRate = 71;
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Selection.activeGameObject = original.gameObject;
                Unsupported.DuplicateGameObjectsUsingPasteboard();
                var copy = Selection.activeGameObject;
                if (copy == original.gameObject)
                    throw new InvalidOperationException("Unity Duplicate did not select a copy");
                copy.name = "Second Controller";
                duplicate = copy.GetComponent<TSMPSetup>();
            }
            else if (phase == 1)
            {
                CheckPrepared(duplicate);
                Result.AppendLine("FirstResources=" + AssetDatabase.GetAssetPath(original.resources) + " Output=" + AssetDatabase.GetAssetPath(original.encoderOutput));
                Result.AppendLine("SecondResources=" + AssetDatabase.GetAssetPath(duplicate.resources) + " Output=" + AssetDatabase.GetAssetPath(duplicate.encoderOutput));
                Check(original.encoderOutput != duplicate.encoderOutput, "Duplicated controller has independent output");
                Check(original.payloadByteTexture != duplicate.payloadByteTexture, "Duplicated controller has independent readback texture");
                Check(((TSMPEncoder)original.encoder).frameRate == 71 && ((TSMPEncoder)duplicate.encoder).frameRate == 71, "User settings survive preparation and duplication");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                Undo.PerformUndo();
            }
            else if (phase == 2)
            {
                Check(GameObject.Find("Second Controller") == null, "Undo removes duplicated controller");
                Undo.PerformRedo();
            }
            else if (phase == 3)
            {
                duplicate = GameObject.Find("Second Controller").GetComponent<TSMPSetup>();
                CheckPrepared(duplicate);
                Check(original.encoderOutput != duplicate.encoderOutput, "Redo preserves resource isolation");
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
                EditorSceneManager.OpenScene(ScenePath);
            }
            else if (phase == 4)
            {
                original = GameObject.Find("First Controller").GetComponent<TSMPSetup>();
                duplicate = GameObject.Find("Second Controller").GetComponent<TSMPSetup>();
                CheckPrepared(original);
                CheckPrepared(duplicate);
                Check(original.encoderOutput != duplicate.encoderOutput, "Scene reload preserves resource isolation");
                Check(((TSMPEncoder)original.encoder).frameRate == 71, "Scene reload preserves user settings");
                Check(Hash(PrefabPath) == sourceHash, "Source prefab unchanged");
                CheckCodecTemplate(original);
                CheckNestedOwnership();
                Finish(true);
                return;
            }
            phase++;
            nextCheck = EditorApplication.timeSinceStartup + 1;
        }
        catch (Exception exception)
        {
            Result.AppendLine(exception.ToString());
            Debug.LogException(exception);
            Finish(false);
        }
    }

    private static void CheckPrepared(TSMPSetup setup)
    {
        Check(setup != null && setup.encoder != null && setup.decoder != null, "Components prepared automatically");
        Check(setup.GetComponentsInChildren<TSMPEncoder>(true).Length == 1 && setup.GetComponentsInChildren<TSMPDecoder>(true).Length == 1, "No duplicate encoder/decoder");
        var encoder = (TSMPEncoder)setup.encoder;
        var decoder = (TSMPDecoder)setup.decoder;
        Check(encoder.selectedCodec != null && decoder.codecHandlers.Length == 1, "Real Luma4 automatically discovered and instantiated");
        Check(setup.autoDiscoverCodecs, "Codec discovery remains enabled");
        Check(decoder.sourceTexture == encoder.output && setup.decoderSourceTexture == null, "Common sample loopback settings preserved");
        Check(encoder.bindingTargets != null && encoder.bindingTargets.Length > 0, "Bindings prepared automatically");
        Check(PrefabUtility.GetPrefabInstanceStatus(setup.gameObject) == PrefabInstanceStatus.Connected, "Controller prefab connection preserved");
        foreach (Transform child in setup.GetComponentsInChildren<Transform>(true))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "No missing script on " + child.name);
#if UDONSHARP
        Check(setup.GetComponentsInChildren<UdonSharpBehaviour>(true).All(x => UdonSharpEditorUtility.GetBackingUdonBehaviour(x) != null), "Every proxy has a backing UdonBehaviour");
        Check(encoder.selectedCodecUdonTarget != null, "Codec backing target assigned");
        Check(encoder.bindingUdonTargets.Any(x => x != null) && decoder.bindingUdonTargets.Any(x => x != null), "Udon binding targets assigned");
#endif
    }

    private static void CheckCodecTemplate(TSMPSetup setup)
    {
        GameObject template = UnityEngine.Object.Instantiate(setup.codecPrefabs[0].gameObject);
        template.SetActive(false);
        TSMPCodec source = template.GetComponent<TSMPCodec>();
        source.displayName = "Plugin fixture";
        var child = new GameObject("Nested encoder");
        child.transform.SetParent(template.transform, false);
        child.transform.localPosition = new Vector3(1, 2, 3);
        var sourceEncoder = child.AddComponent<TSMPEncoder>();
        sourceEncoder.autoEncode = false;
        sourceEncoder.frameRate = 43;
        sourceEncoder.selectedCodec = source;
        var reference = child.AddComponent<TSMPSpoutOutput>();
        reference.enabled = false;
        reference.autoConfigure = false;
        reference.encoder = sourceEncoder;
        TSMPCodec instance = null;
        try
        {
            instance = SetupInstantiation.InstantiateCodec(source, setup.transform, setup);
            var nested = instance.transform.Find("Nested encoder");
            var copiedEncoder = nested.GetComponent<TSMPEncoder>();
            Check(instance.displayName == "Plugin fixture" && copiedEncoder.frameRate == 43, "Plugin-specific serialized values preserved");
            Check(nested.localPosition == new Vector3(1, 2, 3), "Codec child hierarchy preserved");
            Check(copiedEncoder.selectedCodec == instance, "Cross-Udon component reference remapped");
            Check(nested.GetComponent<TSMPSpoutOutput>().encoder == copiedEncoder, "Native component reference to Udon component remapped");
#if UDONSHARP
            Check(instance.GetComponentsInChildren<UdonSharpBehaviour>(true).All(x => UdonSharpEditorUtility.GetBackingUdonBehaviour(x) != null), "Multi-component codec prepared for Udon");
#endif
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(template);
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance.gameObject);
        }
    }

    private static void CheckNestedOwnership()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var parentRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var childRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentRoot.transform);
        var parentSetup = parentRoot.GetComponent<TSMPSetup>();
        var childSetup = childRoot.GetComponent<TSMPSetup>();
        RenderTexture childTemplate = childSetup.encoderOutput;
        try
        {
            parentSetup.ApplyNow();
            Check(childSetup.encoderOutput == childTemplate, "Parent preparation leaves nested Controller references unchanged");
            childSetup.ApplyNow();
            Check(childSetup.encoderOutput != parentSetup.encoderOutput, "Nested Controllers have independent output resources");
            Check(childSetup.encoder != parentSetup.encoder && childSetup.decoder != parentSetup.decoder, "Nested Controllers own separate components");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parentRoot);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Result.AppendLine("PASS " + message);
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    private static void Finish(bool success)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), (success ? "PASS\n" : "FAIL\n") + Result);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
