#if !UDONSHARP && !COMPILER_UDONSHARP
using System.Collections.Generic;
using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class BudgetDeliveryProbe : TSMPNetworkBehaviour
{
    [TransSync("delivery.critical", Priority = 100)] public byte[] critical;
    [TransSync("delivery.medium", Priority = 50)] public byte[] medium;
    [TransSync("delivery.bulk", Priority = 0)] public byte[] bulk;
    public TSMPDecoder decoder;
    public readonly List<int> events = new List<int>();
    public void ReceiveBudgetEvent() { events.Add(decoder.lastRpcEventId); }
    public override void OnTSMPVariableReceived() { }

    public static int Generation(int frame, int period) => 1 + (frame - 1) / period * period;
    public static byte[] Value(int field, int generation, int size)
    {
        var bytes = new byte[size];
        uint state = 0x9e3779b9u ^ ((uint)generation * 0x85ebca6bu) ^ ((uint)(field + 1) * 0xc2b2ae35u);
        for (int i = 0; i < bytes.Length; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            bytes[i] = (byte)state;
        }
        Binary.WriteUInt32LE(bytes, 0, (uint)generation);
        return bytes;
    }
}
#endif
