using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

public sealed class ReceivePolicyProbe : TSMPNetworkBehaviour
{
    [TransSync] public int value;
    public string text;
    public byte[] bytes;
    public bool[] bools;
    public int[] ints;
    public float[] floats;
    public Vector2[] vectors2;
    public Vector3[] vectors3;
    public Quaternion[] rotations;
    public string[] strings;
    public int notifications;
    public byte[] payload;
    public int valueType;
    public string fieldName;
    public int appliedCount;
    public int rejectedCount;
    public int receiveAction;
#if UDONSHARP || COMPILER_UDONSHARP
    public UdonBehaviour receiver;
#else
    public Component receiver;
#endif
    private byte[][] _bytes = new byte[2][];
    private bool[][] _bools = new bool[2][];
    private int[][] _ints = new int[2][];
    private float[][] _floats = new float[2][];
    private Vector2[][] _vectors2 = new Vector2[2][];
    private Vector3[][] _vectors3 = new Vector3[2][];
    private Quaternion[][] _rotations = new Quaternion[2][];
    private string[][] _strings = new string[2][];

    public void ApplyReceivedValue()
    {
        appliedCount = DecoderVariableRuntime.ApplyVariableValue(payload, 1, 100u, valueType, 0, payload.Length, 1,
#if UDONSHARP || COMPILER_UDONSHARP
            new UdonBehaviour[] { receiver },
#else
            new Component[] { receiver },
#endif
            new ushort[] { 1 }, new uint[] { 100 }, new byte[] { (byte)valueType }, new string[] { fieldName },
            new ushort[] { 1 }, new uint[] { 100 }, new int[] { 0 }, 1,
            _bytes, _bools, _ints, _floats, _vectors2, _vectors3, _rotations, _strings, out rejectedCount);
    }

    public void ApplyReceivedPair()
    {
        appliedCount = DecoderVariableRuntime.ApplyVariableValue(payload, 1, 100u, valueType, 0, payload.Length, 2,
#if UDONSHARP || COMPILER_UDONSHARP
            new UdonBehaviour[] { receiver, receiver },
#else
            new Component[] { receiver, receiver },
#endif
            new ushort[] { 1, 1 }, new uint[] { 100, 100 }, new byte[] { (byte)valueType, (byte)valueType }, new string[] { fieldName, fieldName },
            new ushort[] { 1, 1 }, new uint[] { 100, 100 }, new int[] { 0, 1 }, 2,
            _bytes, _bools, _ints, _floats, _vectors2, _vectors3, _rotations, _strings, out rejectedCount);
    }

    public void ApplyDirectValue()
    {
        appliedCount = DecoderVariableDispatcher.Apply(
#if UDONSHARP || COMPILER_UDONSHARP
            new UdonBehaviour[] { receiver },
#else
            new Component[] { receiver },
#endif
            0, fieldName, 100u, NetworkValueReader.ReadScalarObject(payload, valueType, 0, payload.Length)) ? 1 : 0;
        rejectedCount = 0;
    }

    public override void OnTSMPVariableReceived()
    {
        notifications++;
        if (receiveAction == 1)
            receiveInterpolation = ReceiveInterpolationMode.None;
        else if (receiveAction == 2)
            gameObject.SetActive(false);
    }
}
