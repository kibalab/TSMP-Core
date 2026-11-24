#if UDONSHARP && UDON
using System;
using System.IO;
using System.Linq;
using K13A.TSMP;
using K13A.TSMP.Editor;
using K13A.TSMP.Udon;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Editor;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;

public static class SkinWorldValidation
{
    public static async void Build()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            const string folder = "Assets/SkinWorldValidation";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets","SkinWorldValidation");
            var rig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.kibalab.tsmp.core/Samples/TSMPExample/Models/Shapell/data/shapell.fbx"));
            var animator = rig.GetComponentInChildren<Animator>();
            var renderer = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh && r.sharedMesh.boneWeights.Length > 0)
                .OrderByDescending(r => r.sharedMesh.vertexCount).First();
            var controller = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab"));
            var pose = animator.gameObject.AddUdonSharpComponent<TSMPNetworkHumanoidPoseSync>();
            pose.animator = animator; pose.networkId = 37; pose.receiveInterpolation = ReceiveInterpolationMode.Discrete;
            var physicsRoot = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            pose.excludedSendBoneRoots = new[] { physicsRoot };
            physicsRoot.gameObject.AddComponent<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>().rootTransform = physicsRoot;
            HumanoidSkinBaker.Bake(animator,renderer,0,37,5,AssetDatabase.GenerateUniqueAssetPath(folder+"/Skin.asset"),new[] { physicsRoot });
            if (!renderer.enabled || !AssetDatabase.IsSubAsset(renderer.sharedMesh))
                throw new Exception("Missing native half of mixed skin");
            UdonSharpEditorUtility.CopyProxyToUdon(pose);
            controller.GetComponentInChildren<TSMPSetup>(true).ApplyNow();
            var decoder = controller.GetComponentInChildren<TSMPDecoder>(true);
            decoder.shaderChannel = ShaderReceiveChannel.Channel0;
            UdonSharpEditorUtility.CopyProxyToUdon(decoder);
            var world = new GameObject("World").AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
            world.spawns = new[] { new GameObject("Spawn").transform };
            world.gameObject.AddComponent<VRC.Core.PipelineManager>();
            GameObject.CreatePrimitive(PrimitiveType.Plane);
            UpdateLayers.SetupEditorLayers();
            UpdateLayers.SetupCollisionLayerMatrix();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), folder+"/World.unity");
            AssetDatabase.SaveAssets();
            EditorWindow.GetWindow<VRCSdkControlPanel>();
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder))
                throw new Exception("No SDK builder");
            if (!builder.IsValidBuilder(out var reason)) throw new Exception(reason);
            string bundle = await builder.Build();
            if (!File.Exists(bundle)) throw new Exception("No world bundle");
            Debug.Log("PASS local GPU skin world bundle: "+bundle+" bytes="+new FileInfo(bundle).Length);
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
#endif
