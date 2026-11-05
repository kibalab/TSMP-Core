#if UNITY_EDITOR && !UDONSHARP
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShowcaseBuilder
{
    public static void Build()
    {
        ShowcaseNativeSdkSetup.Configure();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var demo=new GameObject("TSMP two-process showcase").AddComponent<ShowcaseDriver>();
        demo.codecPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        demo.expandMaterial=AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat");
        demo.encodeMaterial=Resources.Load<Material>("TSMPEncodeLuma4");demo.stageShader=Shader.Find("Standard");
        string avatarPath=Environment.GetEnvironmentVariable("TSMP_SHOWCASE_AVATAR");
        string dancePath=Environment.GetEnvironmentVariable("TSMP_SHOWCASE_DANCE");
        if(!string.IsNullOrEmpty(avatarPath))
        {
            demo.avatarPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
            if(!string.IsNullOrEmpty(dancePath))
                foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(dancePath))
                    if(asset is AnimationClip clip&&!clip.name.StartsWith("__preview__")){demo.danceClip=clip;break;}
            if(demo.avatarPrefab==null||demo.danceClip==null||!demo.danceClip.humanMotion)
                throw new InvalidOperationException("Private avatar prefab or Humanoid dance dependency missing");
            // Prepare a Native-only copy. Keep the author's source prefab and
            // its VRChat upload components intact in the private project.
            var prepared=UnityEngine.Object.Instantiate(demo.avatarPrefab);
            foreach(var part in prepared.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(part.gameObject);
            if(Environment.GetEnvironmentVariable("TSMP_SHOWCASE_AOI_SKIRT")=="1")AoiSkirtPhysicsPreset.Apply(prepared);
            var animator=prepared.GetComponent<Animator>();
            if(animator!=null)animator.runtimeAnimatorController=null;
            demo.avatarPrefab=PrefabUtility.SaveAsPrefabAsset(prepared,"Assets/Showcase/PreparedAvatar.prefab");
            UnityEngine.Object.DestroyImmediate(prepared);
        }
        else if(!string.IsNullOrEmpty(dancePath))throw new InvalidOperationException("Dance path requires an avatar prefab path");
        if(demo.codecPrefab==null||demo.encodeMaterial==null||demo.stageShader==null)throw new InvalidOperationException("Demo dependency missing");
        PlayerSettings.colorSpace=ColorSpace.Gamma;PlayerSettings.productName="TSMP Showcase";
        PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.runInBackground=true;PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone,ManagedStrippingLevel.Disabled);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/Showcase/Showcase.unity");
        string path=Environment.GetEnvironmentVariable("TSMP_SHOWCASE_BUILD");Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Showcase/Showcase.unity"},locationPathName=path,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        File.WriteAllText(Path.ChangeExtension(path,".build-report.txt"),"Result="+report.summary.result+"\nErrors="+report.summary.totalErrors+"\nWarnings="+report.summary.totalWarnings);
        EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
    }
}
#endif
