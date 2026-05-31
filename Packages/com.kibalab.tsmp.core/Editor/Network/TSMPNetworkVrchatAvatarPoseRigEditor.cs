using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkVrchatAvatarPoseRig))]
    public sealed class TSMPNetworkVrchatAvatarPoseRigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            TSMPNetworkVrchatAvatarPoseRig rig = (TSMPNetworkVrchatAvatarPoseRig)target;
            if (GUILayout.Button("Resolve Humanoid Targets"))
            {
                Undo.RecordObject(rig, "Resolve TSMP VRChat Avatar Pose Rig");
                rig.ResolveTargets();
                EditorUtility.SetDirty(rig);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
