using UnityEngine;

namespace K13A.TSMP.Udon
{
    public enum ReceiveInterpolationMode
    {
        None = 0,
        Discrete = 1,
        Continuous = 2
    }

    public abstract class TSMPNetworkBehaviour : TSMPBehaviour
    {
        public const string BeforeEncodeEventName = nameof(TSMPBeforeEncode);
        public const string LastVariableHashFieldName = nameof(lastVariableHash);
        public const string OnVariableReceivedEventName = nameof(OnTSMPVariableReceived);
        public const string LastRpcNetworkIdFieldName = nameof(lastRpcNetworkId);
        public const string LastRpcHashFieldName = nameof(lastRpcHash);
        public const string LastRpcArgumentCountFieldName = nameof(lastRpcArgumentCount);
        public const string LastRpcMethodNameFieldName = nameof(lastRpcMethodName);
        public const string OnRpcReceivedEventName = nameof(OnTSMPRpcReceived);

        [Header("TSMP Network")] public ushort networkId;
        public ReceiveInterpolationMode receiveInterpolation = ReceiveInterpolationMode.Discrete;
        public float continuousInterpolationRate = 24f;
        [HideInInspector] public TSMPEncoder transRpcEncoder;
        [HideInInspector] public uint lastVariableHash;
        [HideInInspector] public uint lastRpcHash;
        [HideInInspector] public ushort lastRpcNetworkId;
        [HideInInspector] public int lastRpcArgumentCount;
        [HideInInspector] public string lastRpcMethodName;

        public bool IsTSMPActive()
        {
            return enabled && gameObject.activeInHierarchy;
        }

        public virtual void OnTSMPVariableChanged(uint variableHash)
        {
            lastVariableHash = variableHash;
        }

        public virtual void TSMPBeforeEncode()
        {
        }

        public virtual void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            OnTSMPVariableChanged(lastVariableHash);
        }

        public float GetReceiveInterpolationStep()
        {
            float rate = continuousInterpolationRate;
            if (rate <= 0f)
                return 1f;

            float step = rate * Time.deltaTime;
            if (step < 0f)
                return 0f;
            if (step > 1f)
                return 1f;

            return step;
        }

        public virtual bool SendTransRPC(string methodName, RPCTarget target)
        {
            if (string.IsNullOrEmpty(methodName))
                return false;

            if (target == RPCTarget.Local || target == RPCTarget.All)
                SendTransRPCLocal(methodName);

            if (target == RPCTarget.Local)
                return true;
            if (target != RPCTarget.Remote && target != RPCTarget.All)
                return false;

            if (transRpcEncoder == null)
                return false;

            return transRpcEncoder.QueueTransRpc(networkId, StableHash.Fnv1A32(methodName), methodName);
        }

        private void SendTransRPCLocal(string methodName)
        {
#if UDONSHARP
            SendCustomEvent(methodName);
#else
            ComponentReflection.InvokeMethod(this, methodName, true);
#endif
        }

        public virtual void OnTSMPRpc(uint rpcHash)
        {
            lastRpcHash = rpcHash;
        }

        public virtual void OnTSMPRpcReceived()
        {
            OnTSMPRpc(lastRpcHash);
        }
    }

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
