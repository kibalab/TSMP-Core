using UnityEngine;

namespace K13A.TSMP
{
    public static class SymbolCodec
    {
        public static byte ByteToLowNibble(byte value)
        {
            return (byte)(value & 0x0F);
        }

        public static byte ByteToHighNibble(byte value)
        {
            return (byte)((value >> 4) & 0x0F);
        }

        public static byte NibblesToByte(byte low, byte high)
        {
            return (byte)((low & 0x0F) | ((high & 0x0F) << 4));
        }

        public static Color32 SymbolToLuma4Color(byte symbol)
        {
            byte luma = ProtocolConstants.GetLuma4PaletteValue(symbol);
            return new Color32(luma, luma, luma, 255);
        }

        public static Color32[] CreateLuma4Colors()
        {
            Color32[] colors = new Color32[16];
            FillLuma4Colors(colors);
            return colors;
        }

        public static void FillLuma4Colors(Color32[] colors)
        {
            if (colors == null)
                return;

            int count = Mathf.Min(colors.Length, 16);
            for (int i = 0; i < count; i++)
            {
                byte luma = ProtocolConstants.GetLuma4PaletteValue(i);
                colors[i] = new Color32(luma, luma, luma, 255);
            }
        }

        public static byte ClassifyLuma4(byte measuredLuma, byte[] measuredPalette)
        {
            bool hasMeasuredPalette = measuredPalette != null && measuredPalette.Length >= 16;
            int bestIndex = 0;
            int bestDistance = 999;

            for (int i = 0; i < 16; i++)
            {
                byte referenceLuma = hasMeasuredPalette ? measuredPalette[i] : ProtocolConstants.GetLuma4PaletteValue(i);
                int distance = Mathf.Abs(measuredLuma - referenceLuma);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return (byte)bestIndex;
        }
    }
}
