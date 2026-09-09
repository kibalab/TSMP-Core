#if !COMPILER_UDONSHARP
using UnityEngine;

namespace K13A.TSMP
{
    public static class SetupInstantiation
    {
#if UNITY_EDITOR
        public static System.Action<TSMPSetup> PrepareAction;
        public static System.Func<TSMPCodec, Transform, TSMPSetup, TSMPCodec> InstantiateCodecAction;
#endif

        public static void Prepare(TSMPSetup setup)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && PrepareAction != null)
            {
                PrepareAction(setup);
                return;
            }
#endif
#if !UDONSHARP
            if (setup.configureEncoder && setup.encoder == null)
            {
                setup.encoder = setup.GetComponentInChildren<TSMPEncoder>(true);
                if (setup.encoder == null)
                    setup.encoder = CreateChild(setup.transform, "Encoder").AddComponent<TSMPEncoder>();
            }
            if (setup.configureDecoder && setup.decoder == null)
            {
                setup.decoder = setup.GetComponentInChildren<Udon.TSMPDecoder>(true);
                if (setup.decoder == null)
                    setup.decoder = CreateChild(setup.transform, "Decoder").AddComponent<Udon.TSMPDecoder>();
            }
#endif
        }

        public static TSMPCodec InstantiateCodec(TSMPCodec source, Transform parent, TSMPSetup setup)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && InstantiateCodecAction != null)
                return InstantiateCodecAction(source, parent, setup);
#endif
            return Object.Instantiate(source, parent);
        }

#if !UDONSHARP
        private static GameObject CreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing.gameObject;
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }
#endif
    }
}
#endif
