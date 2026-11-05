#if UNITY_EDITOR && !UDONSHARP
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// The VRChat SDK defaults to building asset bundles. These importer overrides
// are for the isolated Native recording project, never the user's VRC project.
public static class ShowcaseNativeSdkSetup
{
    public static void Configure()
    {
        const string sdk = "Packages/com.vrchat.base";
        if (AssetImporter.GetAtPath(sdk + "/Runtime/VRCSDK/Plugins/VRCCore-Editor.dll") == null) return;
        string project = Path.GetDirectoryName(Application.dataPath);
        if (!Directory.Exists(Path.Combine(project, sdk)))
            throw new InvalidOperationException("Native recording requires a private embedded SDK copy in Packages/com.vrchat.base.");
        Set("VRCCore-Editor.dll", true, false);
        Set("SDKBase-Legacy.dll", true, false);
        Set("VRCCore-Standalone.dll", false, true);
    }
    static void Set(string file, bool editor, bool player)
    {
        var importer = (PluginImporter)AssetImporter.GetAtPath("Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/" + file);
        if (importer == null) throw new InvalidOperationException("Missing SDK plugin: " + file);
        if (!importer.GetCompatibleWithAnyPlatform() && importer.GetCompatibleWithEditor() == editor &&
            importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64) == player) return;
        importer.SetCompatibleWithAnyPlatform(false);
        importer.SetCompatibleWithEditor(editor);
        importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, player);
        importer.SaveAndReimport();
    }
}
#endif
