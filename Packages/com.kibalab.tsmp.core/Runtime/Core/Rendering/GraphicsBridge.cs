using UnityEngine;

#if COMPILER_UDONSHARP
using VRC.SDKBase;
#endif

namespace K13A.TSMP
{
    public static class GraphicsBridge
    {
        public static void Blit(Texture source, RenderTexture destination)
        {
#if COMPILER_UDONSHARP
            VRCGraphics.Blit(source, destination);
#else
            Graphics.Blit(source, destination);
#endif
        }

        public static void Blit(Texture source, RenderTexture destination, Material material)
        {
#if COMPILER_UDONSHARP
            VRCGraphics.Blit(source, destination, material);
#else
            Graphics.Blit(source, destination, material);
#endif
        }
    }
}
