using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class PayloadBudgetProbe : TSMPNetworkBehaviour
{
    public bool bigEnabled = true;
    public bool aEnabled = true;
    public bool bEnabled = true;
    public int bigCommits;
    public int captures;
    public int committedFirstByte;
    public bool queueAccepted;
    public bool manualRpcBegan;
    public bool manualRpcWritten;
    public bool changeBudgetOnCapture;
    [TransSync("budget.big", Priority = 100, EnabledBy = nameof(bigEnabled), SentEvent = nameof(CommitBig))]
    public byte[] big = new byte[100];
    [TransSync("budget.a", SendOnChange = false, EnabledBy = nameof(aEnabled))]
    public int a = 17;
    [TransSync("budget.b", SendOnChange = false, EnabledBy = nameof(bEnabled))]
    public int b = 29;
#if !UDONSHARP && !COMPILER_UDONSHARP
    public bool manualWrite;
    public bool manualWritten;
#endif
    public override void TSMPBeforeEncode()
    {
        captures++;
        if (changeBudgetOnCapture)
        {
            changeBudgetOnCapture = false;
            transRpcEncoder.targetPayloadBytes = 0;
        }
#if !UDONSHARP && !COMPILER_UDONSHARP
        if (manualWrite)
            manualWritten = transRpcEncoder.WriteRawBytesVariable(1234u, big);
#endif
    }
    public void CommitBig()
    {
        bigCommits++;
        committedFirstByte = big.Length > 0 ? big[0] : -1;
    }
    public void QueueEvent()
    {
        queueAccepted = transRpcEncoder.QueueTransRpc(1, 99u, "BudgetEvent", 123);
    }
#if UDONSHARP || COMPILER_UDONSHARP
    public void WriteManualRpc()
    {
        transRpcEncoder.BeginFrame();
        manualRpcBegan = transRpcEncoder.BeginRpcCall(1, 99u);
        manualRpcWritten = transRpcEncoder.WriteStringRpcArgument(new string('x', 100));
    }
#endif
}
