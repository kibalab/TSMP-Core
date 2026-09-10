using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class LoopbackProbe : TSMPNetworkBehaviour
{
    [TransSync("validation.number")] public int number;
    [TransSync("validation.text")] public string text = string.Empty;
    public int rpcCalls;

    public void RecordRpc()
    {
        rpcCalls++;
    }
}
