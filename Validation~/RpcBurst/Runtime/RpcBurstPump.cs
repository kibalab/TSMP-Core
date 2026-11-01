using System;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class RpcBurstPump : MonoBehaviour
{
    public static Action FrameStep;
    void Update() { FrameStep?.Invoke(); }
}
