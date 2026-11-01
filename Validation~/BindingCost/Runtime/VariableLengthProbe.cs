using K13A.TSMP;
using K13A.TSMP.Udon;

// Validation only: production/source buffer generation is controlled by the host.
public sealed class VariableLengthProbe : TSMPNetworkBehaviour
{
    [TransSync("variable-length.data")] public byte[] data;
    public int beforeCount, receivedCount;
    public override void TSMPBeforeEncode() { beforeCount++; }
    public override void OnTSMPVariableReceived() { receivedCount++; }
}
