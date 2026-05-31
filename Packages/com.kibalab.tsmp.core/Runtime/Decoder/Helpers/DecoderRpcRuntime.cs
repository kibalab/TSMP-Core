namespace K13A.TSMP
{
    public static class DecoderRpcRuntime
    {
        public static bool TryReadRpcCall(
            byte[] payloadBytes,
            int bodyStart,
            int bodyEnd,
            byte[] argumentTypes,
            int[] argumentOffsets,
            int[] argumentLengths,
            out byte[] nextArgumentTypes,
            out int[] nextArgumentOffsets,
            out int[] nextArgumentLengths,
            out uint rpcHash,
            out int argumentCount,
            out string methodName,
            out int eventId,
            out string error)
        {
            nextArgumentTypes = argumentTypes;
            nextArgumentOffsets = argumentOffsets;
            nextArgumentLengths = argumentLengths;
            rpcHash = 0u;
            argumentCount = 0;
            methodName = string.Empty;
            eventId = 0;
            error = null;

            int cursor;
            if (!NetworkFrameReader.TryReadRpcCallHeader(payloadBytes, bodyStart, bodyEnd, out rpcHash, out argumentCount, out cursor))
            {
                error = "RpcCall body is too small.";
                return false;
            }

            nextArgumentTypes = EnsureArgumentTypes(nextArgumentTypes, argumentCount);
            nextArgumentOffsets = EnsureIntArray(nextArgumentOffsets, argumentCount);
            nextArgumentLengths = EnsureIntArray(nextArgumentLengths, argumentCount);

            if (!NetworkFrameReader.TryReadCompleteRpcCall(payloadBytes, bodyStart, bodyEnd, nextArgumentTypes, nextArgumentOffsets, nextArgumentLengths, out rpcHash, out argumentCount))
            {
                error = "RpcCall body is malformed.";
                return false;
            }

            methodName = TryReadMethodName(payloadBytes, nextArgumentTypes, nextArgumentOffsets, nextArgumentLengths, argumentCount, rpcHash);
            eventId = TryReadEventId(payloadBytes, nextArgumentTypes, nextArgumentOffsets, nextArgumentLengths, argumentCount);
            return true;
        }

        private static string TryReadMethodName(byte[] payloadBytes, byte[] argumentTypes, int[] argumentOffsets, int[] argumentLengths, int argumentCount, uint rpcHash)
        {
            if (payloadBytes == null || argumentCount <= 0 || argumentTypes == null || argumentOffsets == null || argumentLengths == null)
                return string.Empty;
            if (argumentTypes.Length <= 0 || argumentOffsets.Length <= 0 || argumentLengths.Length <= 0)
                return string.Empty;
            if (argumentTypes[0] != NetworkFrameProtocol.ValueTypeUTF8String)
                return string.Empty;

            int offset = argumentOffsets[0];
            int length = argumentLengths[0];
            if (offset < 0 || length <= 0)
                return string.Empty;
            int end = offset + length;
            if (end > payloadBytes.Length)
                return string.Empty;

            string methodName = Utf8.ReadString(payloadBytes, offset, length);
            if (string.IsNullOrEmpty(methodName))
                return string.Empty;
            if (StableHash.Fnv1A32(methodName) != rpcHash)
                return string.Empty;

            return methodName;
        }

        private static int TryReadEventId(byte[] payloadBytes, byte[] argumentTypes, int[] argumentOffsets, int[] argumentLengths, int argumentCount)
        {
            if (payloadBytes == null || argumentCount <= 1 || argumentTypes == null || argumentOffsets == null || argumentLengths == null)
                return 0;
            if (argumentTypes.Length <= 1 || argumentOffsets.Length <= 1 || argumentLengths.Length <= 1)
                return 0;
            if (argumentTypes[1] != NetworkFrameProtocol.ValueTypeInt32)
                return 0;
            if (argumentLengths[1] != NetworkFrameProtocol.Int32Bytes)
                return 0;

            int offset = argumentOffsets[1];
            if (offset < 0)
                return 0;
            if (offset + NetworkFrameProtocol.Int32Bytes > payloadBytes.Length)
                return 0;

            int eventId = Binary.ReadInt32LE(payloadBytes, offset);
            if (eventId <= 0)
                return 0;

            return eventId;
        }

        private static byte[] EnsureArgumentTypes(byte[] values, int count)
        {
            if (count < 0)
                count = 0;
            if (values == null)
                return new byte[count];
            if (values.Length < count)
                return new byte[count];

            return values;
        }

        private static int[] EnsureIntArray(int[] values, int count)
        {
            if (count < 0)
                count = 0;
            if (values == null)
                return new int[count];
            if (values.Length < count)
                return new int[count];

            return values;
        }
    }
}
