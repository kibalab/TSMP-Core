#if UNITY_EDITOR
using System.Collections.Generic;
#endif
using UnityEngine;

namespace K13A.TSMP
{
    [CreateAssetMenu(menuName = "TSMP/Codec Catalog", fileName = "TSMPCodecCatalog")]
    public sealed class TSMPCodecCatalog : ScriptableObject
    {
        public string packageName;
        public TextAsset packageJson;
        public TSMPCodec[] codecPrefabs;
    }

    public static class CodecRuntimeTable
    {
        public static TSMPCodec[] GetActiveCodecs(TSMPCodec[] instances, TSMPCodec[] prefabs)
        {
            if (instances != null && instances.Length > 0)
                return instances;

            return prefabs;
        }

        public static TSMPCodec[] Build(TSMPCodec[] activeCodecs)
        {
            int count = CountNonNull(activeCodecs);
            TSMPCodec[] table = new TSMPCodec[count];
            int cursor = 0;
            if (activeCodecs != null)
            {
                for (int i = 0; i < activeCodecs.Length; i++)
                {
                    TSMPCodec codec = activeCodecs[i];
                    if (codec == null)
                        continue;

                    table[cursor] = codec;
                    cursor++;
                }
            }

            return table;
        }

        public static TSMPCodec GetSelected(TSMPCodec[] activeCodecs, int selectedIndex)
        {
            if (activeCodecs == null || activeCodecs.Length == 0)
                return null;

            int index = Mathf.Clamp(selectedIndex, 0, activeCodecs.Length - 1);
            return activeCodecs[index];
        }

        private static int CountNonNull(TSMPCodec[] codecs)
        {
            int count = 0;
            if (codecs == null)
                return 0;

            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i] != null)
                    count++;
            }

            return count;
        }
    }

#if UNITY_EDITOR
    public static class SetupCodecDiscovery
    {
        private static readonly HashSet<string> ReportedCodecIdCollisions = new HashSet<string>();

        [System.Serializable]
        private sealed class PackageJsonInfo
        {
            public string description;
            public PackageJsonAuthor author;
        }

        [System.Serializable]
        private sealed class PackageJsonAuthor
        {
            public string name;
        }

        public static TSMPCodec[] DiscoverCodecPrefabs()
        {
            List<TSMPCodec> codecs = new List<TSMPCodec>();
            string[] catalogGuids = UnityEditor.AssetDatabase.FindAssets("t:TSMPCodecCatalog");
            if (catalogGuids != null)
            {
                for (int i = 0; i < catalogGuids.Length; i++)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(catalogGuids[i]);
                    TSMPCodecCatalog catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<TSMPCodecCatalog>(path);
                    if (catalog == null || catalog.codecPrefabs == null)
                        continue;

                    EnsurePackageJsonReference(catalog, path);

                    for (int c = 0; c < catalog.codecPrefabs.Length; c++)
                        AddUniqueCodec(codecs, catalog.codecPrefabs[c], path);
                }
            }

            codecs.Sort(CompareCodecs);
            return codecs.ToArray();
        }

        public static bool AreSameCodecs(TSMPCodec[] left, TSMPCodec[] right)
        {
            int leftLength = left != null ? left.Length : 0;
            int rightLength = right != null ? right.Length : 0;
            if (leftLength != rightLength)
                return false;

            for (int i = 0; i < leftLength; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        public static int FindCodecIndex(TSMPCodec[] codecs, ushort codecId, int fallback)
        {
            if (codecs == null || codecs.Length == 0)
                return 0;

            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i] != null && codecs[i].codecId == codecId)
                    return i;
            }

            return Mathf.Clamp(fallback, 0, codecs.Length - 1);
        }

        public static TSMPCodecCatalog FindCatalog(TSMPCodec codec)
        {
            if (codec == null)
                return null;

            string[] catalogGuids = UnityEditor.AssetDatabase.FindAssets("t:TSMPCodecCatalog");
            if (catalogGuids == null)
                return null;

            for (int i = 0; i < catalogGuids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(catalogGuids[i]);
                TSMPCodecCatalog catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<TSMPCodecCatalog>(path);
                if (catalog == null || catalog.codecPrefabs == null)
                    continue;

                for (int c = 0; c < catalog.codecPrefabs.Length; c++)
                {
                    if (catalog.codecPrefabs[c] == codec)
                    {
                        EnsurePackageJsonReference(catalog, path);
                        return catalog;
                    }
                }
            }

            return null;
        }

        public static bool TryGetPackageInfo(TSMPCodec codec, out string author, out string description)
        {
            author = string.Empty;
            description = string.Empty;

            TSMPCodecCatalog catalog = FindCatalog(codec);
            if (catalog == null || catalog.packageJson == null)
                return false;

            PackageJsonInfo info = JsonUtility.FromJson<PackageJsonInfo>(catalog.packageJson.text);
            if (info == null)
                return false;

            description = info.description == null ? string.Empty : info.description;
            author = info.author != null && info.author.name != null ? info.author.name : string.Empty;
            return !string.IsNullOrEmpty(author) || !string.IsNullOrEmpty(description);
        }

        private static void AddUniqueCodec(List<TSMPCodec> codecs, TSMPCodec codec, string catalogPath)
        {
            if (codec == null)
                return;

            for (int i = 0; i < codecs.Count; i++)
            {
                TSMPCodec existing = codecs[i];
                if (existing == codec)
                    return;

                if (existing != null && existing.codecId == codec.codecId)
                {
                    WarnDuplicateCodecId(existing, codec, catalogPath);
                    return;
                }
            }

            codecs.Add(codec);
        }

        private static void EnsurePackageJsonReference(TSMPCodecCatalog catalog, string catalogPath)
        {
            if (catalog == null || catalog.packageJson != null)
                return;

            TextAsset packageJson = FindPackageJson(catalogPath);
            if (packageJson == null)
                return;

            UnityEditor.Undo.RecordObject(catalog, "Assign TSMP codec package.json");
            catalog.packageJson = packageJson;
            UnityEditor.EditorUtility.SetDirty(catalog);
        }

        private static TextAsset FindPackageJson(string catalogPath)
        {
            if (string.IsNullOrEmpty(catalogPath))
                return null;

            string directory = System.IO.Path.GetDirectoryName(catalogPath);
            while (!string.IsNullOrEmpty(directory))
            {
                string packageJsonPath = directory.Replace('\\', '/') + "/package.json";
                TextAsset packageJson = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(packageJsonPath);
                if (packageJson != null)
                    return packageJson;

                int slash = directory.LastIndexOf('/');
                if (slash < 0)
                    break;

                directory = directory.Substring(0, slash);
            }

            return null;
        }

        private static void WarnDuplicateCodecId(TSMPCodec existing, TSMPCodec duplicate, string catalogPath)
        {
            string existingPath = UnityEditor.AssetDatabase.GetAssetPath(existing);
            string duplicatePath = UnityEditor.AssetDatabase.GetAssetPath(duplicate);
            string key = existing.codecId + "|" + existingPath + "|" + duplicatePath;
            if (!ReportedCodecIdCollisions.Add(key))
                return;

            Debug.LogWarning(
                "TSMP codec id collision: codec id " + existing.codecId
                + " is already provided by '" + existingPath
                + "' and duplicate prefab '" + duplicatePath
                + "' from catalog '" + catalogPath
                + "' was ignored.",
                duplicate);
        }

        private static int CompareCodecs(TSMPCodec left, TSMPCodec right)
        {
            if (left == right)
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int idCompare = left.codecId.CompareTo(right.codecId);
            if (idCompare != 0)
                return idCompare;

            return string.CompareOrdinal(left.displayName, right.displayName);
        }
    }
#endif
}
