using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

// Measurement-only binding target. Host checks full bytes outside VM timing.
public sealed class SustainedCostProbe : TSMPNetworkBehaviour
{
    [TransSync("sustained-cost", SendOnChange = false)] public byte[] packet;
    public int[] ids = new int[32768];
    public float[] times = new float[32768];
    public int[] frames = new int[32768];
    public int count, corrupt;
    public override void OnTSMPVariableReceived()
    {
        if (packet == null || packet.Length < 4 || count >= ids.Length) { corrupt++; return; }
        int id = packet[0] | (packet[1] << 8) | (packet[2] << 16) | (packet[3] << 24);
        ids[count] = id; times[count] = Time.realtimeSinceStartup; frames[count] = Time.frameCount; count++;
    }
}
