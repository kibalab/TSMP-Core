using System;

namespace K13A.TSMP
{
    public static class StableHash
    {
        private const uint OffsetBasis = 2166136261u;
        private const uint Prime = 16777619u;

        public static uint Fnv1A32(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            uint hash = OffsetBasis;
            for (int i = 0; i < value.Length; i++)
            {
                int code = value[i];
                if (code < 0x80)
                {
                    hash = AppendByte(hash, code);
                }
                else if (code < 0x800)
                {
                    hash = AppendByte(hash, 0xC0 | (code >> 6));
                    hash = AppendByte(hash, 0x80 | (code & 0x3F));
                }
                else if (code >= 0xD800 && code <= 0xDBFF && i + 1 < value.Length)
                {
                    int lo = value[i + 1];
                    if (lo >= 0xDC00 && lo <= 0xDFFF)
                    {
                        int codePoint = 0x10000 + ((code - 0xD800) << 10) + (lo - 0xDC00);
                        hash = AppendByte(hash, 0xF0 | (codePoint >> 18));
                        hash = AppendByte(hash, 0x80 | ((codePoint >> 12) & 0x3F));
                        hash = AppendByte(hash, 0x80 | ((codePoint >> 6) & 0x3F));
                        hash = AppendByte(hash, 0x80 | (codePoint & 0x3F));
                        i++;
                    }
                    else
                    {
                        hash = AppendReplacement(hash);
                    }
                }
                else if (code >= 0xDC00 && code <= 0xDFFF)
                {
                    hash = AppendReplacement(hash);
                }
                else
                {
                    hash = AppendByte(hash, 0xE0 | (code >> 12));
                    hash = AppendByte(hash, 0x80 | ((code >> 6) & 0x3F));
                    hash = AppendByte(hash, 0x80 | (code & 0x3F));
                }
            }

            return hash;
        }

        public static uint VariableHash(Type behaviourType, string fieldName, string key)
        {
            string stableKey = string.IsNullOrEmpty(key) ? fieldName : key;
            string typeName = behaviourType != null ? behaviourType.FullName : string.Empty;
            return Fnv1A32(typeName + "." + stableKey);
        }

        private static uint AppendByte(uint hash, int value)
        {
            hash ^= (byte)value;
            hash *= Prime;
            return hash;
        }

        private static uint AppendReplacement(uint hash)
        {
            hash = AppendByte(hash, 0xEF);
            hash = AppendByte(hash, 0xBF);
            hash = AppendByte(hash, 0xBD);
            return hash;
        }
    }
}
