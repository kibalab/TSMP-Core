using K13A.TSMP;

// Validation only. Product Binary and its special-value policy remain unchanged.
public sealed class Float32ApiProbe : TSMPBehaviour
{
    public float scalarValue, scalarResult, rawFloatResult;
    public int inputBits, rawBitsResult;
    public byte[] scalarBytes = new byte[4];
    public float[] values = new float[128];
    public float[] decoded = new float[128];
    public byte[] bytes = new byte[512];

    public void RawApi()
    {
        rawBitsResult = System.BitConverter.SingleToInt32Bits(scalarValue);
        rawFloatResult = System.BitConverter.Int32BitsToSingle(inputBits);
    }

    public void CurrentScalarWrite() { Binary.WriteFloat32LE(scalarBytes, 0, scalarValue); }
    public void BitcastScalarWrite() { WriteBitcast(scalarBytes, 0, scalarValue); }
    public void CurrentScalarRead() { scalarResult = Binary.ReadFloat32LE(scalarBytes, 0); }
    public void BitcastScalarRead() { scalarResult = ReadBitcast(scalarBytes, 0); }

    public void CurrentBatchWrite()
    {
        for (int i = 0; i < 128; i++) Binary.WriteFloat32LE(bytes, i * 4, values[i]);
    }
    public void BitcastBatchWrite()
    {
        for (int i = 0; i < 128; i++) WriteBitcast(bytes, i * 4, values[i]);
    }
    public void CurrentBatchRead()
    {
        for (int i = 0; i < 128; i++) decoded[i] = Binary.ReadFloat32LE(bytes, i * 4);
    }
    public void BitcastBatchRead()
    {
        for (int i = 0; i < 128; i++) decoded[i] = ReadBitcast(bytes, i * 4);
    }

    private void WriteBitcast(byte[] destination, int offset, float value)
    {
        int bits = value == 0f ? 0 : System.BitConverter.SingleToInt32Bits(value);
        destination[offset] = (byte)(bits & 255);
        destination[offset + 1] = (byte)((bits >> 8) & 255);
        destination[offset + 2] = (byte)((bits >> 16) & 255);
        destination[offset + 3] = (byte)((bits >> 24) & 255);
    }

    private float ReadBitcast(byte[] source, int offset)
    {
        int bits = Binary.ReadInt32LE(source, offset);
        if (bits == 0 || bits == int.MinValue) return 0f;
        if (((bits >> 23) & 255) == 255) return bits < 0 ? -float.MaxValue : float.MaxValue;
        return System.BitConverter.Int32BitsToSingle(bits);
    }
}
