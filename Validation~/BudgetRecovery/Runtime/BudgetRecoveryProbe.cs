using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class BudgetRecoveryProbe : TSMPNetworkBehaviour
{
    public bool bigEnabled = true, aEnabled = true, bEnabled = true;
    [TransSync("budget.recovery.big", Priority = 100, EnabledBy = nameof(bigEnabled))] public byte[] big = new byte[100];
    [TransSync("budget.recovery.a", SendOnChange = false, EnabledBy = nameof(aEnabled))] public int a = 17;
    [TransSync("budget.recovery.b", SendOnChange = false, EnabledBy = nameof(bEnabled))] public int b = 29;
    public bool queueAccepted;
    public void QueueEvent() { queueAccepted = transRpcEncoder.QueueTransRpc(1, 99u, "ReceiveEvent", 123); }
    public int calls;
    public void ReceiveEvent() { calls++; }
}
