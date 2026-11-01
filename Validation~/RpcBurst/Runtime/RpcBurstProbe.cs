using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
using VRC.Udon;

// Validation-only actual sender/receiver program. The host checks event IDs and state.
public sealed class RpcBurstProbe : TSMPNetworkBehaviour
{
    [TransSync("rpc-burst-state", SendOnChange = false)] public int marker;
    public int nextEventId;
    public bool accepted;
    public UdonBehaviour rpcDecoder;
    public int[] eventIds = new int[2048];
    public float[] eventTimes = new float[2048];
    public int eventCount;
    public int[] markers = new int[4096];
    public float[] markerTimes = new float[4096];
    public int markerCount;
    public void QueueEvent()
    {
        accepted = transRpcEncoder.QueueTransRpc(1, StableHash.Fnv1A32(nameof(ReceiveEvent)), nameof(ReceiveEvent), nextEventId);
    }
    public void ReceiveEvent()
    {
        eventIds[eventCount] = (int)rpcDecoder.GetProgramVariable("lastRpcEventId");
        eventTimes[eventCount] = Time.realtimeSinceStartup;
        eventCount++;
    }
    public override void OnTSMPVariableReceived()
    {
        markers[markerCount] = marker;
        markerTimes[markerCount] = Time.realtimeSinceStartup;
        markerCount++;
    }
}
