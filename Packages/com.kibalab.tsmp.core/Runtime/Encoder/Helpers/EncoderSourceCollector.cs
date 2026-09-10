#if !COMPILER_UDONSHARP
using System.Collections.Generic;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public static class EncoderSourceCollector
    {
        public static void Collect(List<TSMPNetworkBehaviour> behaviours, UnityEngine.Component[] bindingTargets, TSMPNetworkBehaviour[] explicitBehaviours)
        {
            if (behaviours == null)
                return;

            behaviours.Clear();
            CollectBindingTargets(behaviours, bindingTargets);
#if !UDONSHARP && !COMPILER_UDONSHARP
            CollectExplicitBehaviours(behaviours, explicitBehaviours);
#endif
        }

        private static void CollectBindingTargets(List<TSMPNetworkBehaviour> behaviours, UnityEngine.Component[] bindingTargets)
        {
            if (bindingTargets == null)
                return;

            for (int i = 0; i < bindingTargets.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = bindingTargets[i] as TSMPNetworkBehaviour;
                if (CanAdd(behaviours, behaviour))
                    behaviours.Add(behaviour);
            }
        }

#if !UDONSHARP && !COMPILER_UDONSHARP
        private static void CollectExplicitBehaviours(List<TSMPNetworkBehaviour> behaviours, TSMPNetworkBehaviour[] explicitBehaviours)
        {
            if (explicitBehaviours == null)
                return;

            for (int i = 0; i < explicitBehaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = explicitBehaviours[i];
                if (CanAdd(behaviours, behaviour))
                    behaviours.Add(behaviour);
            }
        }
#endif

        private static bool CanAdd(List<TSMPNetworkBehaviour> behaviours, TSMPNetworkBehaviour behaviour)
        {
            if (behaviours == null || behaviour == null)
                return false;

            if (!behaviour.IsTSMPActive())
                return false;

            for (int i = 0; i < behaviours.Count; i++)
            {
                if (behaviours[i] == behaviour)
                    return false;
            }

            return true;
        }
    }
}
#endif
