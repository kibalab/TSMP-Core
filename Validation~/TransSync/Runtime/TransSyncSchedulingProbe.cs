using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class TransSyncSchedulingProbe : TSMPNetworkBehaviour
{
    public bool highEnabled;
    public bool peerEnabled;
    public bool lowEnabled;
    public bool changedEnabled;
    public bool pacedEnabled;
    public bool pollEnabled;
    public bool bytesEnabled;
    public bool textEnabled;
    public bool collisionEnabled;
    public bool bindingDirectionsEnabled;
    public int captures;
#if !UDONSHARP && !COMPILER_UDONSHARP
    public bool manualWrite;
    public bool manualWritten;
#endif

    [TransSync("high", Priority = 100, SendOnChange = false, EnabledBy = nameof(highEnabled))]
    public int high = 10;
    [TransSync("peer", Priority = 100, SendOnChange = false, EnabledBy = nameof(peerEnabled))]
    public int peer = 20;
    [TransSync("low", Priority = -100, SendOnChange = false, EnabledBy = nameof(lowEnabled))]
    public int low = 30;
    [TransSync("changed", EnabledBy = nameof(changedEnabled))]
    public int changed = 40;
    [TransSync("paced", MinSendInterval = .25f, EnabledBy = nameof(pacedEnabled))]
    public int paced = 50;
    [TransSync("poll", SendOnChange = false, MinSendInterval = .25f, EnabledBy = nameof(pollEnabled))]
    public int poll = 60;
    [TransSync("bytes", EnabledBy = nameof(bytesEnabled))]
    public byte[] bytes = new byte[] { 1, 2, 3 };
    [TransSync("text", EnabledBy = nameof(textEnabled))]
    public string text = "hello";

    [TransSync("high", Priority = -999, EnabledBy = nameof(collisionEnabled))]
    public int duplicateHigh = 99;
    [TransSync("binding.send", Direction = NetworkSyncDirection.SendOnly, Priority = 23, SendOnChange = false,
        MinSendInterval = .5f, SentEvent = nameof(TSMPBeforeEncode), EnabledBy = nameof(bindingDirectionsEnabled))]
    public int sendOnly = 70;
    [TransSync("binding.receive", Direction = NetworkSyncDirection.ReceiveOnly, Priority = -23, EnabledBy = nameof(bindingDirectionsEnabled))]
    public int receiveOnly = 80;

    public override void TSMPBeforeEncode()
    {
        captures++;
#if !UDONSHARP && !COMPILER_UDONSHARP
        if (manualWrite)
            manualWritten = transRpcEncoder.WriteRawBytesVariable(1234, new byte[] { 1, 2, 3 });
#endif
    }
}
