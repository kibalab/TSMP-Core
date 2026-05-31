#if !COMPILER_UDONSHARP
namespace K13A.TSMP
{
    public static class Crc32
    {
        private static readonly uint[] Table = Crc32Runtime.EnsureTable(null);

        public static uint Compute(byte[] data)
        {
            return Compute(data, 0, data != null ? data.Length : 0);
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            return Crc32Runtime.Compute(data, offset, count, Table);
        }

        public static uint Update(uint crc, byte value)
        {
            crc ^= 0xFFFFFFFFu;
            crc = Table[(crc ^ value) & 0xFFu] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
#endif
