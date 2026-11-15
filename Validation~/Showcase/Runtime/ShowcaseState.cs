#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using K13A.TSMP;
using K13A.TSMP.Udon;

// Recording-only probe. These values travel inside the TSMP texture, not the
// localhost bridge header or the source-camera presentation sidecar.
public sealed class ShowcaseState : TSMPNetworkBehaviour
{
    [TransSync("showcase.sequence")] public int sequence;
    [TransSync("showcase.seconds")] public float seconds;
    [TransSync("showcase.message")] public string message = "HELLO TSMP";
    [TransSync("showcase.bytes")] public byte[] bytes = new byte[256];
    [NonSerialized] public TSMPDecoder observer;
    [NonSerialized] public int receivedCallbacks;
    [NonSerialized] public bool crcAtApply;
    [NonSerialized] public uint headerAtApply;
    public override void OnTSMPVariableReceived()
    {
        receivedCallbacks++;
        if (observer != null) { crcAtApply = observer.lastPayloadCrcVerified; headerAtApply = observer.lastFrameIndex; }
    }
}
#endif
