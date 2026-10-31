using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class RefreshRecoveryProbe : TSMPNetworkBehaviour
{
    [TransSync("refresh.high", Priority = 100, MinSendInterval = 0.1f)] public byte[] high = new byte[32];
    [TransSync("refresh.medium", Priority = 50, MinSendInterval = 0.4f)] public byte[] medium = new byte[32];
    [TransSync("refresh.slow", Priority = 0, MinSendInterval = 1.5f)] public byte[] slow = new byte[32];
    public int calls;
    public void ReceiveEvent() { calls++; }
}
