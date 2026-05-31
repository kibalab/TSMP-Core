using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPNetworkHumanoidPoseSync))]
    [CanEditMultipleObjects]
    public sealed class TSMPNetworkHumanoidPoseSyncEditor : TSMPNetworkBehaviourEditor
    {
        private struct RigPoint
        {
            public HumanBodyBones Bone;
            public float X;
            public float Y;
            public int Parent;
            public float Radius;

            public RigPoint(HumanBodyBones bone, float x, float y, int parent, float radius)
            {
                Bone = bone;
                X = x;
                Y = y;
                Parent = parent;
                Radius = radius;
            }
        }

        private static readonly HumanBodyBones[] Body =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.Neck,
            HumanBodyBones.Head,
        };

        private static readonly HumanBodyBones[] LeftArm =
        {
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
        };

        private static readonly HumanBodyBones[] RightArm =
        {
            HumanBodyBones.RightShoulder,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
        };

        private static readonly HumanBodyBones[] LeftLeg =
        {
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.LeftToes,
        };

        private static readonly HumanBodyBones[] RightLeg =
        {
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
            HumanBodyBones.RightToes,
        };

        private static readonly RigPoint[] RigPoints =
        {
            new RigPoint(HumanBodyBones.Hips, 0.50f, 0.43f, -1, 8f),
            new RigPoint(HumanBodyBones.Spine, 0.50f, 0.34f, 0, 7f),
            new RigPoint(HumanBodyBones.Chest, 0.50f, 0.27f, 1, 7f),
            new RigPoint(HumanBodyBones.UpperChest, 0.50f, 0.22f, 2, 6f),
            new RigPoint(HumanBodyBones.Neck, 0.50f, 0.16f, 3, 6f),
            new RigPoint(HumanBodyBones.Head, 0.50f, 0.08f, 4, 10f),

            new RigPoint(HumanBodyBones.LeftShoulder, 0.40f, 0.22f, 3, 6f),
            new RigPoint(HumanBodyBones.LeftUpperArm, 0.30f, 0.28f, 6, 7f),
            new RigPoint(HumanBodyBones.LeftLowerArm, 0.22f, 0.36f, 7, 7f),
            new RigPoint(HumanBodyBones.LeftHand, 0.15f, 0.45f, 8, 8f),

            new RigPoint(HumanBodyBones.RightShoulder, 0.60f, 0.22f, 3, 6f),
            new RigPoint(HumanBodyBones.RightUpperArm, 0.70f, 0.28f, 10, 7f),
            new RigPoint(HumanBodyBones.RightLowerArm, 0.78f, 0.36f, 11, 7f),
            new RigPoint(HumanBodyBones.RightHand, 0.85f, 0.45f, 12, 8f),

            new RigPoint(HumanBodyBones.LeftUpperLeg, 0.43f, 0.55f, 0, 8f),
            new RigPoint(HumanBodyBones.LeftLowerLeg, 0.38f, 0.70f, 14, 8f),
            new RigPoint(HumanBodyBones.LeftFoot, 0.36f, 0.86f, 15, 8f),
            new RigPoint(HumanBodyBones.LeftToes, 0.31f, 0.94f, 16, 6f),

            new RigPoint(HumanBodyBones.RightUpperLeg, 0.57f, 0.55f, 0, 8f),
            new RigPoint(HumanBodyBones.RightLowerLeg, 0.62f, 0.70f, 18, 8f),
            new RigPoint(HumanBodyBones.RightFoot, 0.64f, 0.86f, 19, 8f),
            new RigPoint(HumanBodyBones.RightToes, 0.69f, 0.94f, 20, 6f),
        };

        public override void OnInspectorGUI()
        {
            if (DrawUdonSharpHeader())
                return;

            serializedObject.Update();

            DrawTSMPNetworkSection(true);
            DrawProperty("animator");
            DrawProperty("autoResolveBones");
            DrawProperty("rootSyncSpace");

            using (new EditorGUI.DisabledScope(true))
            {
                DrawProperty("validBoneCount");
                DrawProperty("encodedPoseBytes");
                DrawProperty("encodedRootMotionBytes");
            }

            serializedObject.ApplyModifiedProperties();

            TSMPNetworkHumanoidPoseSync sync = (TSMPNetworkHumanoidPoseSync)target;
            EnsureDefaultSelection(sync);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Humanoid Rig Map", EditorStyles.boldLabel);

            DrawRigMap(sync);
        }

        private static void DrawRigMap(TSMPNetworkHumanoidPoseSync sync)
        {
            Rect outer = GUILayoutUtility.GetRect(280f, 390f, GUILayout.ExpandWidth(true));
            float size = Mathf.Min(outer.width - 20f, outer.height - 20f);
            Rect map = new Rect(outer.x + (outer.width - size) * 0.5f, outer.y + 10f, size, size);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(outer, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f));
                Handles.BeginGUI();
                for (int i = 0; i < RigPoints.Length; i++)
                {
                    int parent = RigPoints[i].Parent;
                    if (parent < 0)
                        continue;

                    bool selected = ContainsBone(sync.boneIds, RigPoints[i].Bone) && ContainsBone(sync.boneIds, RigPoints[parent].Bone);
                    Handles.color = selected ? new Color(0.20f, 0.55f, 0.95f, 0.90f) : new Color(0.42f, 0.42f, 0.42f, 0.45f);
                    Handles.DrawAAPolyLine(3f, ToPoint(map, RigPoints[parent]), ToPoint(map, RigPoints[i]));
                }

                Handles.EndGUI();
            }

            for (int i = 0; i < RigPoints.Length; i++)
                DrawRigNode(sync, map, RigPoints[i]);

            Rect allButton = new Rect(outer.xMax - 98f, outer.y + 8f, 45f, 20f);
            Rect clearButton = new Rect(outer.xMax - 53f, outer.y + 8f, 45f, 20f);
            if (GUI.Button(allButton, new GUIContent("All", "Select all body bones"), EditorStyles.miniButtonLeft))
                SetAll(sync, true);
            if (GUI.Button(clearButton, new GUIContent("Clear", "Clear body bone selection"), EditorStyles.miniButtonRight))
                SetAll(sync, false);

            Rect fingerToggle = new Rect(outer.xMax - 158f, outer.yMax - 26f, 150f, 18f);
            bool includeFingers = GUI.Toggle(fingerToggle, sync.includeFingerBones, new GUIContent("Include Finger Bones", "Sync finger bones when the matching Hand bone is selected"));
            if (includeFingers != sync.includeFingerBones)
            {
                Undo.RecordObject(sync, "Change TSMP Finger Bone Sync");
                sync.includeFingerBones = includeFingers;
                sync.boneTargets = null;
                EditorUtility.SetDirty(sync);
            }
        }

        private static void DrawRigNode(TSMPNetworkHumanoidPoseSync sync, Rect map, RigPoint point)
        {
            bool selected = ContainsBone(sync.boneIds, point.Bone);
            bool mapped = HasAvatarBone(sync, point.Bone);
            Vector2 center = ToPoint(map, point);
            float radius = point.Radius;
            Rect rect = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);

            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                if (!mapped)
                    Handles.color = selected ? new Color(0.85f, 0.58f, 0.16f, 1f) : new Color(0.34f, 0.34f, 0.34f, 0.95f);
                else
                    Handles.color = selected ? new Color(0.12f, 0.60f, 1f, 1f) : new Color(0.50f, 0.50f, 0.50f, 1f);

                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                Handles.color = EditorGUIUtility.isProSkin ? Color.white : Color.black;
                Handles.DrawWireDisc(center, Vector3.forward, radius);
                Handles.EndGUI();
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            string tooltip = ObjectNames.NicifyVariableName(point.Bone.ToString());
            if (!mapped)
                tooltip += " (not mapped)";

            if (GUI.Button(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none))
                SetBone(sync, point.Bone, !selected);
        }

        private static Vector2 ToPoint(Rect map, RigPoint point)
        {
            return new Vector2(map.x + map.width * point.X, map.y + map.height * point.Y);
        }

        private static bool HasAvatarBone(TSMPNetworkHumanoidPoseSync sync, HumanBodyBones bone)
        {
            if (sync == null || sync.animator == null)
                return true;

            Avatar avatar = sync.animator.avatar;
            if (avatar == null || !avatar.isHuman)
                return false;

            return sync.animator.GetBoneTransform(bone) != null;
        }

        private static void EnsureDefaultSelection(TSMPNetworkHumanoidPoseSync sync)
        {
            if (sync.boneIds != null && sync.boneIds.Length > 0)
                return;

            SetAll(sync, true);
        }

        private static bool ContainsBone(int[] boneIds, HumanBodyBones bone)
        {
            if (boneIds == null)
                return false;

            int id = (int)bone;
            for (int i = 0; i < boneIds.Length; i++)
            {
                if (boneIds[i] == id)
                    return true;
            }

            return false;
        }

        private static void SetBone(TSMPNetworkHumanoidPoseSync sync, HumanBodyBones bone, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP Humanoid Bone Selection");

            bool exists = ContainsBone(sync.boneIds, bone);
            if (selected == exists)
                return;

            int currentLength = sync.boneIds != null ? sync.boneIds.Length : 0;
            int nextLength = selected ? currentLength + 1 : Mathf.Max(0, currentLength - 1);
            int[] next = new int[nextLength];
            int cursor = 0;

            if (sync.boneIds != null)
            {
                int removeId = (int)bone;
                for (int i = 0; i < sync.boneIds.Length; i++)
                {
                    if (!selected && sync.boneIds[i] == removeId)
                        continue;

                    if (cursor < next.Length)
                        next[cursor++] = sync.boneIds[i];
                }
            }

            if (selected && cursor < next.Length)
                next[cursor] = (int)bone;

            sync.boneIds = next;
            sync.boneTargets = null;
            EditorUtility.SetDirty(sync);
        }

        private static void SetAll(TSMPNetworkHumanoidPoseSync sync, bool selected)
        {
            Undo.RecordObject(sync, "Change TSMP Humanoid Bone Selection");
            sync.boneIds = selected ? BuildAllBoneIds() : new int[0];
            sync.boneTargets = null;
            EditorUtility.SetDirty(sync);
        }

        private static int[] BuildAllBoneIds()
        {
            HumanBodyBones[] all = Concat(Body, LeftArm, RightArm, LeftLeg, RightLeg);
            int[] ids = new int[all.Length];
            for (int i = 0; i < all.Length; i++)
                ids[i] = (int)all[i];
            return ids;
        }

        private static HumanBodyBones[] Concat(params HumanBodyBones[][] groups)
        {
            int count = 0;
            for (int i = 0; i < groups.Length; i++)
                count += groups[i].Length;

            HumanBodyBones[] result = new HumanBodyBones[count];
            int cursor = 0;
            for (int g = 0; g < groups.Length; g++)
            {
                HumanBodyBones[] group = groups[g];
                for (int i = 0; i < group.Length; i++)
                    result[cursor++] = group[i];
            }

            return result;
        }
    }
}
