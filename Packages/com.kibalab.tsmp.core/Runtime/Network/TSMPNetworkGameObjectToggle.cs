using UnityEngine;

namespace K13A.TSMP.Udon
{
    [AddComponentMenu("TSMP/Network/GameObject Toggle")]
    public class TSMPNetworkGameObjectToggle : TSMPNetworkBehaviour
    {
        public GameObject targetObject;

#if UDONSHARP || COMPILER_UDONSHARP
        public override void Interact()
#else
        public void Interact()
#endif
        {
            SendTransRPC(nameof(ToggleObject), RPCTarget.All);
        }

        public void ToggleObject()
        {
            GameObject target = targetObject;
            if (target == null)
                return;

            target.SetActive(!target.activeSelf);
        }
    }
}
