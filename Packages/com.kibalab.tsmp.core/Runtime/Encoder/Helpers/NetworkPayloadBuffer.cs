namespace K13A.TSMP
{
    public static class NetworkPayloadBuffer
    {
        public static int ClampCapacity(int byteCount)
        {
            return ClampCapacity(byteCount, NetworkFrameProtocol.MinimumPayloadBufferBytes);
        }

        public static int ClampCapacity(int byteCount, int minimumBytes)
        {
            int minimum = minimumBytes;
            if (minimum < NetworkFrameProtocol.NetworkHeaderBytes)
                minimum = NetworkFrameProtocol.NetworkHeaderBytes;

            if (byteCount < minimum)
                return minimum;
            if (byteCount > NetworkFrameProtocol.MaximumPayloadBytes)
                return NetworkFrameProtocol.MaximumPayloadBytes;

            return byteCount;
        }

        public static byte[] EnsureCapacity(byte[] buffer, int byteCount)
        {
            return EnsureCapacity(buffer, byteCount, NetworkFrameProtocol.MinimumPayloadBufferBytes);
        }

        public static byte[] EnsureCapacity(byte[] buffer, int byteCount, int minimumBytes)
        {
            int capacity = ClampCapacity(byteCount, minimumBytes);
            if (buffer == null)
                return new byte[capacity];
            if (buffer.Length != capacity)
                return new byte[capacity];

            return buffer;
        }

        public static byte[] CopyPrefix(byte[] source, int byteCount, byte[] destination)
        {
            if (byteCount < 0)
                byteCount = 0;

            if (destination == null)
                destination = new byte[byteCount];
            else if (destination.Length != byteCount)
                destination = new byte[byteCount];

            if (source == null)
                return destination;

            int count = byteCount;
            if (count > source.Length)
                count = source.Length;

            for (int i = 0; i < count; i++)
                destination[i] = source[i];

            return destination;
        }

        public static void Clear(byte[] buffer)
        {
            if (buffer == null)
                return;

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = 0;
        }
    }
}
