namespace K13A.TSMP
{
    public static class Crc32Runtime
    {
        public const int TableSize = 256;
        public const int FastTableSize = TableSize * 8;

        public static uint[] EnsureTable(uint[] table)
        {
            if (table != null && (table.Length == TableSize || table.Length == FastTableSize))
                return table;

            table = new uint[TableSize];
            InitializeBaseTable(table);
            return table;
        }

        public static uint[] EnsureFastTable(uint[] table)
        {
            if (table != null && table.Length == FastTableSize)
                return table;

            var fast = new uint[FastTableSize];
            if (table != null && table.Length == TableSize)
            {
                for (int i = 0; i < TableSize; i++)
                    fast[i] = table[i];
            }
            else
                InitializeBaseTable(fast);

            for (int slice = 1; slice < 8; slice++)
            {
                int previous = (slice - 1) * TableSize;
                int current = slice * TableSize;
                for (int i = 0; i < TableSize; i++)
                {
                    uint crc = fast[previous + i];
                    fast[current + i] = fast[(int)(crc & 0xFFu)] ^ (crc >> 8);
                }
            }
            return fast;
        }

        private static void InitializeBaseTable(uint[] table)
        {
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
        }

        public static uint Compute(byte[] bytes, int offset, int count, uint[] table)
        {
            if (bytes == null || count <= 0)
                return 0u;
            if (offset < 0 || offset > bytes.Length - count)
                return 0u;
            if (table == null || (table.Length != TableSize && table.Length != FastTableSize))
                return 0u;

            uint crc = 0xFFFFFFFFu;
            int end = offset + count;
            int i = offset;
            int blockEnd = end - 7;
            if (table.Length == FastTableSize)
            {
                // Eight independent table contributions replace eight dependent
                // byte updates; all tables remain in one cached uint array.
                for (; i < blockEnd; i += 8)
                {
                    crc = table[7 * TableSize + (int)((crc ^ bytes[i]) & 0xFFu)]
                        ^ table[6 * TableSize + (int)(((crc >> 8) ^ bytes[i + 1]) & 0xFFu)]
                        ^ table[5 * TableSize + (int)(((crc >> 16) ^ bytes[i + 2]) & 0xFFu)]
                        ^ table[4 * TableSize + (int)(((crc >> 24) ^ bytes[i + 3]) & 0xFFu)]
                        ^ table[3 * TableSize + bytes[i + 4]]
                        ^ table[2 * TableSize + bytes[i + 5]]
                        ^ table[TableSize + bytes[i + 6]]
                        ^ table[bytes[i + 7]];
                }
            }
            else
            {
                for (; i < blockEnd; i += 8)
                {
                    crc = table[(int)((crc ^ bytes[i]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 1]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 2]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 3]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 4]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 5]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 6]) & 0xFFu)] ^ (crc >> 8);
                    crc = table[(int)((crc ^ bytes[i + 7]) & 0xFFu)] ^ (crc >> 8);
                }
            }
            for (; i < end; i++)
                crc = table[(int)((crc ^ bytes[i]) & 0xFFu)] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
