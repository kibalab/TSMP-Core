namespace K13A.TSMP
{
    public static class ProtocolConstants
    {
        public const uint Magic = 0x504D5354u;

        public const byte VersionMajor = 0;
        public const byte VersionMinor = 1;

        public const int DefaultBlockSize = 8;
        public const int DefaultSampleSize = 4;
        public const int DefaultReservedRows = 9;

        public const float DefaultSafetyFactor = 1.30f;

        public const int Luma4BitsPerSymbol = 4;

        public static byte GetLuma4PaletteValue(int index)
        {
            int normalized = index & 0x0F;
            int value = 24 + normalized * 14;
            if (normalized == 15)
                value = 232;

            return (byte)value;
        }
    }
}
