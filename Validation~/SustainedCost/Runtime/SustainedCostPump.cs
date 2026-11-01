using System;
using UnityEngine;

// The Editor driver cannot itself be attached as a scene component.
[DefaultExecutionOrder(-10000)]
public sealed class SustainedCostPump : MonoBehaviour
{
    public static Action FrameStep;
    void Update() { FrameStep?.Invoke(); }
}
