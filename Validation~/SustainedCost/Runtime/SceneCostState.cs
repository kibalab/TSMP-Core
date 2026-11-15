using K13A.TSMP;
using K13A.TSMP.Udon;

// The four state fields used beside the Humanoid and twelve Transform fields.
public sealed class SceneCostState : TSMPNetworkBehaviour
{
    [TransSync("showcase.sequence")] public int sequence;
    [TransSync("showcase.seconds")] public float seconds;
    [TransSync("showcase.message")] public string message = "HELLO TSMP";
    [TransSync("showcase.bytes")] public byte[] bytes = new byte[256];
}
