namespace K13A.TSMP
{
    public static class Crc32Runtime
    {
        public const int TableSize = 256;

        public static uint[] EnsureTable(uint[] table)
        {
            if (table != null && table.Length == TableSize)
                return table;

            table = new uint[TableSize];
            for (int i = 0; i < TableSize; i++)
            {
                uint crc = (uint)i;
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 1u) != 0u)
                        crc = 0xEDB88320u ^ (crc >> 1);
                    else
                        crc >>= 1;
                }

                table[i] = crc;
            }

            return table;
        }

        public static uint Compute(byte[] bytes, int offset, int count, uint[] table)
        {
            if (bytes == null || count <= 0)
                return 0u;
            if (offset < 0)
                return 0u;
            if (offset + count > bytes.Length)
                return 0u;
            if (table == null || table.Length != TableSize)
                return 0u;

            uint crc = 0xFFFFFFFFu;
            int end = offset + count;

            for (int i = offset; i < end; i++)
                crc = table[(int)((crc ^ bytes[i]) & 0xFFu)] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
