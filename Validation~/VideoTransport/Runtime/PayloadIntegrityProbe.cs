#if !UDONSHARP && !COMPILER_UDONSHARP
using UnityEngine;

public sealed class PayloadIntegrityProbe : MonoBehaviour
{
    public int receivedValue;
    public uint lastVariableHash;
    public void OnTSMPVariableReceived() { }
}
#endif
