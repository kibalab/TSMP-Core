using K13A.TSMP;

// Test-only events: both algorithms execute in the same compiled Udon program.
public sealed class CrcPerformanceProbe : TSMPBehaviour
{
    public byte[] input;
    public int offset;
    public int count;
    public uint[] baselineTable;
    public uint[] productionTable;
    public uint result;

    public void RunProduction()
    {
        result = Crc32Runtime.Compute(input, offset, count, productionTable);
    }

    public void RunBaseline()
    {
        result = BaselineCompute(input, offset, count, baselineTable);
    }

    public void RunControl()
    {
        result = (uint)count;
    }

    // Frozen pre-optimization byte-loop. Keep this independent of production Compute.
    private static uint BaselineCompute(byte[] bytes, int start, int length, uint[] lookup)
    {
        if (bytes == null || length <= 0)
            return 0u;
        if (start < 0)
            return 0u;
        if (start + length > bytes.Length)
            return 0u;
        if (lookup == null || lookup.Length != 256)
            return 0u;

        uint crc = 0xFFFFFFFFu;
        int end = start + length;
        for (int i = start; i < end; i++)
            crc = lookup[(int)((crc ^ bytes[i]) & 0xFFu)] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
