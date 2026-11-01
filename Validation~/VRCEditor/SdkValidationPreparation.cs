#if UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;

// Validation-project bootstrap only; not included in the runtime package.
public static class SdkValidationPreparation
{
    public static void Run()
    {
        var assets = new List<UdonSharpProgramAsset>();
        var skipped = new List<string>();
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        bool compileError = false;
        Application.LogCallback listener = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) compileError = true;
        };
        try
        {
            string[] roots = {
                "Assets/Validation/ArrayCache/Udon", "Assets/Validation/Configuration/Runtime",
                "Assets/Validation/RpcDelivery/Runtime", "Assets/Validation/TransSync/Runtime",
                "Assets/Validation/ReceivePolicies/Runtime"
            };
            var required = new HashSet<string> {
                "Assets/Validation/ArrayCache/Udon/DecoderArrayCacheVmProbe.cs",
                "Assets/Validation/Configuration/Runtime/ConfigurationCodec.cs",
                "Assets/Validation/Configuration/Runtime/ConfigurationLookupProbe.cs",
                "Assets/Validation/RpcDelivery/Runtime/RpcQueueProbe.cs",
                "Assets/Validation/TransSync/Runtime/TransSyncSchedulingProbe.cs",
                "Assets/Validation/ReceivePolicies/Runtime/PlainReceiveProbe.cs",
                "Assets/Validation/ReceivePolicies/Runtime/ReceivePolicyProbe.cs",
                "Assets/Validation/ReceivePolicies/Runtime/SendCommitCodec.cs",
                "Assets/Validation/ReceivePolicies/Runtime/SendCommitProbe.cs"
            };
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", roots).OrderBy(value => value))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // This native fault-injection fixture is deliberately excluded by !UDONSHARP.
                if (path == "Assets/Validation/RpcDelivery/Runtime/RpcDeliveryFaultCodec.cs")
                {
                    skipped.Add("NativeOnly=" + path);
                    continue;
                }
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Type type = script.GetClass();
                if (type == null) throw new InvalidOperationException("Uncompiled validation script: " + path);
                if (type.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(type)) continue;
                string parent = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
                string assetPath = parent + "/" + type.Name + ".asset";
                var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    asset.sourceCsScript = script;
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                if (asset.sourceCsScript != script) throw new InvalidOperationException("Unexpected program source: " + assetPath);
                assets.Add(asset);
                required.Remove(path);
            }
            if (required.Count != 0) throw new InvalidOperationException("Required SDK validation programs were not prepared: " + string.Join(", ", required));
            if (assets.Count == 0) throw new InvalidOperationException("No SDK validation programs were prepared.");
            AssetDatabase.SaveAssets();
            Application.logMessageReceived += listener;
            try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
            finally { Application.logMessageReceived -= listener; }
            if (compileError) throw new InvalidOperationException("SDK preparation client compilation reported errors.");
            foreach (var asset in assets)
                if (asset.GetRealProgram() == null || asset.GetRealProgram().ByteCode.Length == 0)
                    throw new InvalidOperationException("Missing prepared bytecode: " + AssetDatabase.GetAssetPath(asset));
            AssetDatabase.SaveAssets();
            File.WriteAllText(output, "PASS\nPreparedPrograms=" + assets.Count + "\nBuildType=Udon client\n" +
                string.Join("\n", assets.Select(AssetDatabase.GetAssetPath).Concat(skipped)));
            // Return from the import/compile callback before entering Unity shutdown.
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(output, "FAIL\nPreparationFailure\n" + error);
            throw;
        }
    }
}
#endif
