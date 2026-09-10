using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace K13A.TSMP.Editor
{
    internal static class SetupResourceEditor
    {
        private const string Folder = "Assets/TSMPGenerated";

        public static void Prepare(TSMPSetup setup)
        {
            string sharedPath = null;
            if (setup.resources != null)
            {
                foreach (TSMPSetup other in Object.FindObjectsOfType<TSMPSetup>(true))
                {
                    if (other == setup || other.resources != setup.resources || !other.gameObject.scene.IsValid())
                        continue;
                    sharedPath = AssetDatabase.GetAssetPath(setup.resources);
                    setup.resources = null;
                    break;
                }
            }

            var replacements = new Dictionary<Object, Object>();
            using (var serialized = new SerializedObject(setup))
            {
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;
                    Object source = property.objectReferenceValue;
                    if (!(source is RenderTexture) && !(source is Material))
                        continue;
                    string path = AssetDatabase.GetAssetPath(source);
                    if (!path.StartsWith("Packages/", StringComparison.Ordinal) && path != sharedPath)
                        continue;
                    replacements[source] = Own(setup, source);
                }
            }
            RemapHierarchy(setup.gameObject, replacements, setup);
            foreach (TSMPCodec codec in setup.GetComponentsInChildren<TSMPCodec>(true))
                if (codec.GetComponentInParent<TSMPSetup>(true) == setup)
                    PrepareCodec(setup, codec.gameObject);
        }

        public static void PrepareCodec(TSMPSetup setup, GameObject root)
        {
            var replacements = new Dictionary<Object, Object>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    throw new InvalidOperationException("TSMP codec template contains a missing script: " + root.name);
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        Object source = property.objectReferenceValue;
                        if (source is Material || source is RenderTexture)
                            replacements[source] = Own(setup, source);
                    }
                }
            }
            RemapHierarchy(root, replacements);
        }

        private static Object Own(TSMPSetup setup, Object source)
        {
            if (source == null)
                return null;
            SetupResources resources = setup.resources;
            if (resources != null)
            {
                for (int i = 0; i < resources.copies.Length; i++)
                    if (resources.copies[i] == source)
                        return source;
                for (int i = 0; i < resources.sources.Length; i++)
                    if (resources.sources[i] == source && i < resources.copies.Length && resources.copies[i] != null)
                        return resources.copies[i];
            }
            else
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder("Assets", "TSMPGenerated");
                resources = ScriptableObject.CreateInstance<SetupResources>();
                resources.name = setup.name + " Resources";
                AssetDatabase.CreateAsset(resources, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + resources.name + ".asset"));
                setup.resources = resources;
                EditorUtility.SetDirty(setup);
            }

            Object copy = Object.Instantiate(source);
            copy.name = source.name;
            AssetDatabase.AddObjectToAsset(copy, resources);
            int count = resources.sources.Length;
            Array.Resize(ref resources.sources, count + 1);
            Array.Resize(ref resources.copies, count + 1);
            resources.sources[count] = source;
            resources.copies[count] = copy;
            EditorUtility.SetDirty(resources);
            AssetDatabase.SaveAssetIfDirty(resources);
            return copy;
        }

        private static void RemapHierarchy(GameObject root, Dictionary<Object, Object> replacements, TSMPSetup owner = null)
        {
            if (replacements.Count == 0)
                return;
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;
                if (owner != null && component.GetComponentInParent<TSMPSetup>(true) != owner)
                    continue;
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty property = serialized.GetIterator();
                    bool changed = false;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        Object current = property.objectReferenceValue;
                        if (current == null || !replacements.TryGetValue(current, out Object copy) || copy == current)
                            continue;
                        property.objectReferenceValue = copy;
                        changed = true;
                    }
                    if (!changed)
                        continue;
                    serialized.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
        }
    }
}
