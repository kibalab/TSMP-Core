using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class FrameStructureProbe : TSMPNetworkBehaviour
{
    public int receivedValue = -7;
    public int notifications;
    public int toggleCalls;
    public bool toggle = true;
    public byte[] input;
    public int inputLength;
    public int messageCount;
    public bool valid;
    public string error;

    public override void OnTSMPVariableReceived() { notifications++; }
    public void ToggleProbe() { toggle = !toggle; toggleCalls++; }
    public void ValidateStructure()
    {
        valid = NetworkFrameReader.TryValidateNetworkFrame(input, inputLength, out messageCount, out error);
    }
    public void Noop() { }
}
