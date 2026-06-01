using UnityEditor;

namespace K13A.TSMP.Editor
{
    internal static class ForceUdonCompileOnce
    {
        [MenuItem("TSMP/Debug/Force Script Reimport")]
        private static void ForceScriptReimport()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
    }
}
