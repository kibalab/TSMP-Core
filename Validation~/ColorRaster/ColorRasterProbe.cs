#if !UDONSHARP && !COMPILER_UDONSHARP
using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class ColorRasterProbe : TSMPNetworkBehaviour
{
    [TransSync("color-raster-packet", SendOnChange = false)]
    public byte[] packet;
}
#endif
