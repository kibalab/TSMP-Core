using UnityEngine;

namespace K13A.TSMP.Udon
{
    public static class HumanoidBoneUtil
    {
        public const int FingerBoneCountPerHand = 15;

        public static bool IsValidHumanBoneId(int boneId)
        {
            return boneId >= 0 && boneId < (int)HumanBodyBones.LastBone;
        }

        public static bool IsLeftFingerBoneId(int boneId)
        {
            return boneId >= (int)HumanBodyBones.LeftThumbProximal && boneId <= (int)HumanBodyBones.LeftLittleDistal;
        }

        public static bool IsRightFingerBoneId(int boneId)
        {
            return boneId >= (int)HumanBodyBones.RightThumbProximal && boneId <= (int)HumanBodyBones.RightLittleDistal;
        }

        public static bool IsFingerBoneId(int boneId)
        {
            return IsLeftFingerBoneId(boneId) || IsRightFingerBoneId(boneId);
        }

        public static bool HasBoneId(bool[] boneFlags, int boneId)
        {
            return IsValidHumanBoneId(boneId) && boneFlags != null && boneId < boneFlags.Length && boneFlags[boneId];
        }

        public static bool HasLeftFingerBoneIds(bool[] boneFlags)
        {
            return HasBoneId(boneFlags, (int)HumanBodyBones.LeftThumbProximal) && HasBoneId(boneFlags, (int)HumanBodyBones.LeftLittleDistal);
        }

        public static bool HasRightFingerBoneIds(bool[] boneFlags)
        {
            return HasBoneId(boneFlags, (int)HumanBodyBones.RightThumbProximal) && HasBoneId(boneFlags, (int)HumanBodyBones.RightLittleDistal);
        }

        public static int AppendLeftFingerBoneIds(int[] target, int cursor)
        {
            if (target == null)
                return cursor;

            if (cursor + FingerBoneCountPerHand > target.Length)
                return cursor;

            target[cursor++] = (int)HumanBodyBones.LeftThumbProximal;
            target[cursor++] = (int)HumanBodyBones.LeftThumbIntermediate;
            target[cursor++] = (int)HumanBodyBones.LeftThumbDistal;
            target[cursor++] = (int)HumanBodyBones.LeftIndexProximal;
            target[cursor++] = (int)HumanBodyBones.LeftIndexIntermediate;
            target[cursor++] = (int)HumanBodyBones.LeftIndexDistal;
            target[cursor++] = (int)HumanBodyBones.LeftMiddleProximal;
            target[cursor++] = (int)HumanBodyBones.LeftMiddleIntermediate;
            target[cursor++] = (int)HumanBodyBones.LeftMiddleDistal;
            target[cursor++] = (int)HumanBodyBones.LeftRingProximal;
            target[cursor++] = (int)HumanBodyBones.LeftRingIntermediate;
            target[cursor++] = (int)HumanBodyBones.LeftRingDistal;
            target[cursor++] = (int)HumanBodyBones.LeftLittleProximal;
            target[cursor++] = (int)HumanBodyBones.LeftLittleIntermediate;
            target[cursor++] = (int)HumanBodyBones.LeftLittleDistal;
            return cursor;
        }

        public static int AppendRightFingerBoneIds(int[] target, int cursor)
        {
            if (target == null)
                return cursor;

            if (cursor + FingerBoneCountPerHand > target.Length)
                return cursor;

            target[cursor++] = (int)HumanBodyBones.RightThumbProximal;
            target[cursor++] = (int)HumanBodyBones.RightThumbIntermediate;
            target[cursor++] = (int)HumanBodyBones.RightThumbDistal;
            target[cursor++] = (int)HumanBodyBones.RightIndexProximal;
            target[cursor++] = (int)HumanBodyBones.RightIndexIntermediate;
            target[cursor++] = (int)HumanBodyBones.RightIndexDistal;
            target[cursor++] = (int)HumanBodyBones.RightMiddleProximal;
            target[cursor++] = (int)HumanBodyBones.RightMiddleIntermediate;
            target[cursor++] = (int)HumanBodyBones.RightMiddleDistal;
            target[cursor++] = (int)HumanBodyBones.RightRingProximal;
            target[cursor++] = (int)HumanBodyBones.RightRingIntermediate;
            target[cursor++] = (int)HumanBodyBones.RightRingDistal;
            target[cursor++] = (int)HumanBodyBones.RightLittleProximal;
            target[cursor++] = (int)HumanBodyBones.RightLittleIntermediate;
            target[cursor++] = (int)HumanBodyBones.RightLittleDistal;
            return cursor;
        }
    }
}