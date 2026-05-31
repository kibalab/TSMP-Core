using UnityEngine;
using K13A.TSMP.Udon;

#if UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class DecoderRpcDispatcher
    {
#if UDONSHARP
        public static void Dispatch(UdonBehaviour[] targets, int targetCount, ushort[] bindingNetworkIds, ushort networkId, uint rpcHash, int argumentCount, string methodName)
        {
            if (targets == null || targetCount <= 0)
                return;

            for (int i = 0; i < targetCount; i++)
            {
                UdonBehaviour target = targets[i];
                if (target == null)
                    continue;
                if (!BindingTable.IsUdonTargetActive(target))
                    continue;
                if (!MatchesNetworkId(bindingNetworkIds, i, networkId))
                    continue;
                if (WasUdonTargetDispatched(targets, bindingNetworkIds, i, target, networkId))
                    continue;

                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcNetworkIdFieldName, networkId);
                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcHashFieldName, rpcHash);
                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcArgumentCountFieldName, argumentCount);
                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcMethodNameFieldName, methodName);
                if (string.IsNullOrEmpty(methodName))
                    TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.OnRpcReceivedEventName);
                else
                    TSMPBehaviour.SendCustomEvent(target, methodName);
            }
        }
#else
        public static void Dispatch(Component[] targets, int targetCount, ushort[] bindingNetworkIds, ushort networkId, uint rpcHash, int argumentCount, string methodName)
        {
            if (targets == null || targetCount <= 0)
                return;

            for (int i = 0; i < targetCount; i++)
            {
                Component target = targets[i];
                if (target == null)
                    continue;
                if (!BindingTable.IsComponentTargetActive(target))
                    continue;
                if (!MatchesNetworkId(bindingNetworkIds, i, networkId))
                    continue;
                if (WasComponentTargetDispatched(targets, bindingNetworkIds, i, target, networkId))
                    continue;

                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcNetworkIdFieldName, networkId);
                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcHashFieldName, rpcHash);
                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcArgumentCountFieldName, argumentCount);
                TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastRpcMethodNameFieldName, methodName);
                if (string.IsNullOrEmpty(methodName))
                    TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.OnRpcReceivedEventName);
                else
                    TSMPBehaviour.SendCustomEvent(target, methodName);
            }
        }
#endif

        private static bool MatchesNetworkId(ushort[] bindingNetworkIds, int index, ushort networkId)
        {
            if (bindingNetworkIds == null)
                return true;
            if (index < 0 || index >= bindingNetworkIds.Length)
                return true;

            return bindingNetworkIds[index] == networkId;
        }

#if UDONSHARP
        private static bool WasUdonTargetDispatched(UdonBehaviour[] targets, ushort[] bindingNetworkIds, int currentIndex, UdonBehaviour target, ushort networkId)
        {
            if (targets == null || target == null)
                return false;

            for (int i = 0; i < currentIndex; i++)
            {
                if (i >= targets.Length)
                    return false;
                if (targets[i] != target)
                    continue;
                if (MatchesNetworkId(bindingNetworkIds, i, networkId))
                    return true;
            }

            return false;
        }
#else
        private static bool WasComponentTargetDispatched(Component[] targets, ushort[] bindingNetworkIds, int currentIndex, Component target, ushort networkId)
        {
            if (targets == null || target == null)
                return false;

            for (int i = 0; i < currentIndex; i++)
            {
                if (i >= targets.Length)
                    return false;
                if (targets[i] != target)
                    continue;
                if (MatchesNetworkId(bindingNetworkIds, i, networkId))
                    return true;
            }

            return false;
        }
#endif
    }
}
