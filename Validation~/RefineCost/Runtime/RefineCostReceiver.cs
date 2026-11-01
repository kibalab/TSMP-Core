using UdonSharp;

// Measurement-only receiver. Decoder still uses its production binding/dispatch.
public class RefineCostReceiver : UdonSharpBehaviour
{
    public byte[] receivedValue;
    public uint lastVariableHash;
    public int notifications;

    public void OnTSMPVariableReceived() { notifications++; }
}
