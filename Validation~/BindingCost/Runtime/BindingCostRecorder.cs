using UdonSharp;

// Enabled only for untimed callback ordering checks.
public sealed class BindingCostRecorder : UdonSharpBehaviour
{
    public int nextId, count;
    public int[] visits = new int[4096];
    public void Visit() { visits[count++] = nextId; }
}
