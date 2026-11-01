using K13A.TSMP;
using UnityEngine;

// Validation only: exercises the product helpers, without duplicating their implementation.
public sealed class Float32ContractProbe : TSMPBehaviour
{
    public float[] values, decoded;
    public byte[] bytes, readBytes;
    public int[] offsets;
    public int start, stride;
    public float scalar;
    public int returnOffset;
    public Vector2 vector2, readVector2;
    public Vector3 vector3, readVector3;
    public Quaternion quaternion, readQuaternion;

    public void ReadScalars()
    {
        for (int i = 0; i < decoded.Length; i++)
            decoded[i] = Binary.ReadFloat32LE(readBytes, start + i * stride);
    }

    public void WriteOne() { returnOffset = Binary.WriteFloat32LE(bytes, start, scalar); }

    public void WriteArray() { returnOffset = NetworkValueWriter.WriteFloat32Array(bytes, start, values); }
    public void ReadArray() { decoded = NetworkValueReader.CopyFloat32Array(readBytes, start, values.Length * 4, decoded); }

    public void WriteVectors()
    {
        offsets[0] = Binary.WriteVector2Float32LE(bytes, 3, vector2);
        offsets[1] = Binary.WriteVector3Float32LE(bytes, 16, vector3);
        offsets[2] = Binary.WriteQuaternionFloat32LE(bytes, 33, quaternion);
    }

    public void ReadVectors()
    {
        readVector2 = Binary.ReadVector2Float32LE(readBytes, 3);
        readVector3 = Binary.ReadVector3Float32LE(readBytes, 16);
        readQuaternion = Binary.ReadQuaternionFloat32LE(readBytes, 33);
    }
}
