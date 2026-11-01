#if UNITY_EDITOR && !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using K13A.TSMP;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ColorRasterBuild
{
    public static void Build()
    {
        string result = Environment.GetEnvironmentVariable("TSMP_COLOR_RASTER_RESULTS");
        string executable = Environment.GetEnvironmentVariable("TSMP_COLOR_RASTER_BUILD");
        if (string.IsNullOrWhiteSpace(result) || string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Build environment missing");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runner = new GameObject("Native color raster validation").AddComponent<ColorRasterValidation>();
        runner.prefabs = new[] { "Color256", "RGB16", "RGB20", "Luma4" }.Select(name =>
        {
            string path = "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<TSMPCodec>() == null) throw new InvalidOperationException("Prefab missing: " + path);
            return prefab.GetComponent<TSMPCodec>();
        }).ToArray();
        string hashes = string.Join("\n", new[] { "Packages/com.kibalab.tsmp.core", "Packages/com.kibalab.tsmp.codec.color256", "Packages/com.kibalab.tsmp.codec.rgb16", "Packages/com.kibalab.tsmp.codec.rgb20", "Packages/com.kibalab.tsmp.codec.luma4", "Assets/ColorRaster" }
            .SelectMany(directory => Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => new[] { ".cs", ".shader", ".cginc", ".prefab", ".mat", ".json" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(path => path, StringComparer.Ordinal).Select(path =>
            {
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() + "  " + path.Replace('\\', '/');
            }));
        runner.buildSourceHashes = hashes;
        File.WriteAllText(Path.Combine(result, "build-source-hashes.txt"), hashes);
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        string scene = "Assets/ColorRaster/Validation.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scene);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = executable,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        File.WriteAllText(Path.Combine(result, "build-report.txt"), "Result=" + report.summary.result + "\nErrors=" + report.summary.totalErrors +
            "\nWarnings=" + report.summary.totalWarnings + "\nBackend=Mono\nStripping=Disabled\nPhase=" + Environment.GetEnvironmentVariable("TSMP_COLOR_RASTER_PHASE"));
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
#endif
