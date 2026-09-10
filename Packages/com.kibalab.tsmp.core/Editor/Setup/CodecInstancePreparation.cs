#if UDONSHARP
using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    internal static class CodecInstancePreparation
    {
        private sealed class Replacement
        {
            public UdonSharpBehaviour Source;
            public int Id;
            public GameObject Target;
            public System.Type Type;
            public Preset Values;
        }

        private struct Reference
        {
            public Component Owner;
            public int OwnerId;
            public string Path;
            public int ValueId;
        }

        public static void Prepare(GameObject root)
        {
            var pending = new List<Replacement>();
            var ids = new HashSet<int>();
            foreach (UdonSharpBehaviour proxy in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy) != null)
                    continue;
                var preset = new Preset(proxy)
                {
                    excludedProperties = new[] { "serializationData", "_udonSharpBackingUdonBehaviour" }
                };
                pending.Add(new Replacement { Source = proxy, Id = proxy.GetInstanceID(), Target = proxy.gameObject, Type = proxy.GetType(), Values = preset });
                ids.Add(proxy.GetInstanceID());
            }
            var references = new List<Reference>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
                            continue;
                        int id = property.objectReferenceValue.GetInstanceID();
                        if (ids.Contains(id))
                            references.Add(new Reference { Owner = component, OwnerId = component.GetInstanceID(), Path = property.propertyPath, ValueId = id });
                    }
                }
            }
            var replacements = new Dictionary<int, Component>();
            try
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                    Object.DestroyImmediate(pending[i].Source);
                foreach (Replacement item in pending)
                {
                    Component component = item.Target.AddUdonSharpComponent(item.Type);
                    if (!item.Values.ApplyTo(component))
                        throw new System.InvalidOperationException("Could not apply codec component settings: " + item.Type.Name);
                    replacements.Add(item.Id, component);
                }
                foreach (Reference reference in references)
                {
                    Component owner = replacements.TryGetValue(reference.OwnerId, out Component replacement) ? replacement : reference.Owner;
                    using (var serialized = new SerializedObject(owner))
                    {
                        SerializedProperty property = serialized.FindProperty(reference.Path);
                        if (property == null)
                            throw new System.InvalidOperationException("Codec reference was lost: " + reference.Path);
                        property.objectReferenceValue = replacements[reference.ValueId];
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            finally
            {
                foreach (Replacement item in pending)
                    Object.DestroyImmediate(item.Values);
            }
            foreach (UdonSharpBehaviour proxy in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        }
    }
}
#endif
