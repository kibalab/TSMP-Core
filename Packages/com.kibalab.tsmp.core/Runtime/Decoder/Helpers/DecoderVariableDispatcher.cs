using UnityEngine;
using K13A.TSMP.Udon;

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class DecoderVariableDispatcher
    {
#if UDONSHARP || COMPILER_UDONSHARP
        public static bool IsTargetActive(UdonBehaviour[] targets, int index)
        {
            if (targets == null)
                return false;
            if (index < 0 || index >= targets.Length)
                return false;

            UdonBehaviour target = targets[index];
            if (target == null)
                return false;

            return BindingTable.IsUdonTargetActive(target);
        }

        public static bool Apply(UdonBehaviour[] targets, int index, string fieldName, uint variableHash, object decodedValue)
        {
            if (decodedValue == null)
                return false;
            if (!IsTargetActive(targets, index))
                return false;

            UdonBehaviour target = targets[index];
            TSMPBehaviour.SetProgramVariable(target, fieldName, decodedValue);
            TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastVariableHashFieldName, variableHash);
            TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.OnVariableReceivedEventName);
            return true;
        }
#else
        public static bool IsTargetActive(Component[] targets, int index)
        {
            if (targets == null)
                return false;
            if (index < 0 || index >= targets.Length)
                return false;

            Component target = targets[index];
            if (target == null)
                return false;

            return BindingTable.IsComponentTargetActive(target);
        }

        public static bool Apply(Component[] targets, int index, string fieldName, uint variableHash, object decodedValue)
        {
            if (decodedValue == null)
                return false;
            if (!IsTargetActive(targets, index))
                return false;

            Component target = targets[index];
            TSMPBehaviour.SetProgramVariable(target, fieldName, decodedValue);
            TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastVariableHashFieldName, variableHash);
            TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.OnVariableReceivedEventName);
            return true;
        }
#endif
    }
}
