#if !COMPILER_UDONSHARP
using UnityEngine;

namespace K13A.TSMP
{
    public sealed class SetupResources : ScriptableObject
    {
        public Object[] sources = new Object[0];
        public Object[] copies = new Object[0];
    }
}
#endif
