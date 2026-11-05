#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Configuration only. The original Aoi model and VRChat SDK must be installed.
public static class AoiSkirtPhysicsPreset
{
    const string BoneType = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
    const string ColliderType = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider";
    public const float LegColliderRadiusMeters = .055f;
    public const float BackSkirtRadiusMeters = .04f;
    static readonly Dictionary<string, string[]> Bindings = new Dictionary<string, string[]>
    {
        {"PB_F_Skirt_00", new[]{"PBC_L_UpperLeg1", "PBC_R_UpperLeg1"}},
        {"PB_FL_Skirt_00", new[]{"PBC_L_UpperLeg1"}},
        {"PB_FR_Skirt_00", new[]{"PBC_R_UpperLeg1"}},
        {"PB_L_Skirt_00", new[]{"PBC_L_UpperLeg1", "PBC_L_UpperLeg2", "PBC_L_UpperLeg3"}},
        {"PB_LF_Skirt_00", new[]{"PBC_L_UpperLeg1", "PBC_L_UpperLeg4"}},
        {"PB_R_Skirt_00", new[]{"PBC_R_UpperLeg1", "PBC_R_UpperLeg2", "PBC_R_UpperLeg3"}},
        {"PB_RF_Skirt_00", new[]{"PBC_R_UpperLeg1", "PBC_R_UpperLeg4"}},
        {"PB_Skirt", new[]{"PBC_L_UpperLeg1", "PBC_L_UpperLeg2", "PBC_R_UpperLeg1", "PBC_R_UpperLeg2"}}
    };
    [MenuItem("TSMP/Showcase/Apply Aoi Skirt Physics to Selected Avatar")]
    static void ApplySelected()
    {
        if (Selection.activeGameObject == null) throw new InvalidOperationException("Select the Aoi avatar root first.");
        if (EditorUtility.IsPersistent(Selection.activeGameObject)) throw new InvalidOperationException("Place Aoi in the scene and select that instance to preserve the source prefab.");
        Apply(Selection.activeGameObject);
        Debug.Log("Aoi skirt physics applied; original force curves and avatar descriptor preserved.");
    }
    public static void Apply(GameObject avatar)
    {
        if (avatar == null) throw new ArgumentNullException(nameof(avatar));
        var all = avatar.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToArray();
        var bones = all.Where(c => c.GetType().FullName == BoneType && Bindings.ContainsKey(c.name)).ToDictionary(c => c.name);
        var colliders = all.Where(c => c.GetType().FullName == ColliderType && c.name.StartsWith("PBC_") && c.name.Contains("UpperLeg")).ToDictionary(c => c.name);
        if (bones.Count != 8 || colliders.Count != 8 || Bindings.Values.SelectMany(v => v).Any(n => !colliders.ContainsKey(n)))
            throw new InvalidOperationException("This preset needs the original Aoi v1.02 skirt PhysBones and eight thigh colliders.");
        // Validate all names before changing anything. Preserve author capsule
        // positions, heights, limits, and secondary motion forces.
        foreach (var collider in colliders.Values)
        {
            Undo.RecordObject(collider, "Aoi skirt collision clearance");
            var data = new SerializedObject(collider);
            data.FindProperty("radius").floatValue = LegColliderRadiusMeters;
            data.FindProperty("bonesAsSpheres").boolValue = false;
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(collider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        foreach (var entry in Bindings)
        {
            var bone = bones[entry.Key];
            Undo.RecordObject(bone, "Aoi skirt PhysBone bindings");
            bone.enabled = true;
            var data = new SerializedObject(bone);
            var list = data.FindProperty("colliders");
            list.arraySize = entry.Value.Length;
            for (int i = 0; i < entry.Value.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = colliders[entry.Value[i]];
            if (entry.Key == "PB_Skirt")
            {
                data.FindProperty("radius").floatValue = BackSkirtRadiusMeters;
                data.FindProperty("radiusCurve").animationCurveValue = new AnimationCurve(
                    new Keyframe(0, .25f), new Keyframe(.58f, .55f), new Keyframe(1, 1));
            }
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(bone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        }
    }
}
#endif
