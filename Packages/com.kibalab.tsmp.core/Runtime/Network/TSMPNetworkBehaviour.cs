using UnityEngine;

namespace K13A.TSMP.Udon
{
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
#if UDONSHARP || COMPILER_UDONSHARP
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
}
