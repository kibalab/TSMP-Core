namespace K13A.TSMP
{
    public static class NetworkFrameReader
    {
        public static bool TryReadNetworkFrameHeader(byte[] buffer, out int messageCount, out int nextOffset)
        {
            messageCount = 0;
            nextOffset = 0;

            if (!CanRead(buffer, 0, NetworkFrameProtocol.NetworkHeaderBytes, GetLength(buffer)))
                return false;

            int versionMajor = buffer[NetworkFrameProtocol.NetworkVersionMajorOffset];
            if (versionMajor != NetworkFrameProtocol.NetworkVersionMajor)
                return false;

            messageCount = Binary.ReadUInt16LE(buffer, NetworkFrameProtocol.NetworkMessageCountOffset);
            nextOffset = NetworkFrameProtocol.NetworkHeaderBytes;
            return true;
        }

        public static bool TryReadMessageHeader(byte[] buffer, int offset, int limit, out ushort networkId, out int messageType, out int bodyStart, out int bodyEnd, out int nextOffset)
        {
            networkId = 0;
            messageType = 0;
            bodyStart = 0;
            bodyEnd = 0;
            nextOffset = offset;

            if (!CanRead(buffer, offset, NetworkFrameProtocol.MessageHeaderBytes, limit))
                return false;

            networkId = Binary.ReadUInt16LE(buffer, offset + NetworkFrameProtocol.MessageNetworkIdOffset);
            messageType = buffer[offset + NetworkFrameProtocol.MessageTypeOffset];
            if (!IsSupportedMessageType(messageType))
                return false;

            int bodyLength = Binary.ReadUInt16LE(buffer, offset + NetworkFrameProtocol.MessageBodyLengthOffset);
            bodyStart = offset + NetworkFrameProtocol.MessageHeaderBytes;
            bodyEnd = bodyStart + bodyLength;
            if (bodyEnd < bodyStart)
                return false;
            if (bodyEnd > limit)
                return false;

            nextOffset = bodyEnd;
            return true;
        }

        public static bool TryReadVariableStateHeader(byte[] buffer, int bodyStart, int bodyEnd, out int variableCount, out int nextOffset)
        {
            variableCount = 0;
            nextOffset = bodyStart;

            if (!CanRead(buffer, bodyStart, NetworkFrameProtocol.VariableStateBodyHeaderBytes, bodyEnd))
                return false;

            variableCount = Binary.ReadUInt16LE(buffer, bodyStart);
            nextOffset = bodyStart + NetworkFrameProtocol.VariableStateBodyHeaderBytes;
            return true;
        }

        public static bool TryReadVariableEntry(byte[] buffer, int offset, int bodyEnd, out uint variableHash, out int valueType, out int valueOffset, out int valueLength, out int nextOffset)
        {
            variableHash = 0u;
            valueType = 0;
            valueOffset = 0;
            valueLength = 0;
            nextOffset = offset;

            if (!CanRead(buffer, offset, NetworkFrameProtocol.VariableValueHeaderBytes, bodyEnd))
                return false;

            variableHash = Binary.ReadUInt32LE(buffer, offset + NetworkFrameProtocol.VariableValueHashOffset);
            valueType = buffer[offset + NetworkFrameProtocol.VariableValueTypeOffset];
            valueLength = Binary.ReadUInt16LE(buffer, offset + NetworkFrameProtocol.VariableValueLengthOffset);
            if (!NetworkValueCodec.IsValueLengthValid(valueType, valueLength))
                return false;

            valueOffset = offset + NetworkFrameProtocol.VariableValueHeaderBytes;
            nextOffset = valueOffset + valueLength;
            if (nextOffset < valueOffset)
                return false;
            if (nextOffset > bodyEnd)
                return false;

            return true;
        }

        public static bool TryReadRpcCallHeader(byte[] buffer, int bodyStart, int bodyEnd, out uint rpcHash, out int argumentCount, out int nextOffset)
        {
            rpcHash = 0u;
            argumentCount = 0;
            nextOffset = bodyStart;

            if (!CanRead(buffer, bodyStart, NetworkFrameProtocol.RpcCallBodyHeaderBytes, bodyEnd))
                return false;

            rpcHash = Binary.ReadUInt32LE(buffer, bodyStart + NetworkFrameProtocol.RpcCallHashOffset);
            argumentCount = buffer[bodyStart + NetworkFrameProtocol.RpcCallArgumentCountOffset];
            nextOffset = bodyStart + NetworkFrameProtocol.RpcCallBodyHeaderBytes;
            return true;
        }

        public static bool TryReadRpcArgument(byte[] buffer, int offset, int bodyEnd, out int valueType, out int valueOffset, out int valueLength, out int nextOffset)
        {
            valueType = 0;
            valueOffset = 0;
            valueLength = 0;
            nextOffset = offset;

            if (!CanRead(buffer, offset, NetworkFrameProtocol.RpcArgumentHeaderBytes, bodyEnd))
                return false;

            valueType = buffer[offset + NetworkFrameProtocol.RpcArgumentTypeOffset];
            valueLength = Binary.ReadUInt16LE(buffer, offset + NetworkFrameProtocol.RpcArgumentLengthOffset);
            if (!NetworkValueCodec.IsValueLengthValid(valueType, valueLength))
                return false;

            valueOffset = offset + NetworkFrameProtocol.RpcArgumentHeaderBytes;
            nextOffset = valueOffset + valueLength;
            if (nextOffset < valueOffset)
                return false;
            if (nextOffset > bodyEnd)
                return false;

            return true;
        }

        public static bool TryReadRpcArguments(byte[] buffer, int offset, int bodyEnd, int argumentCount, byte[] argumentTypes, int[] argumentOffsets, int[] argumentLengths, out int nextOffset)
        {
            nextOffset = offset;
            if (argumentCount < 0)
                return false;
            if (argumentTypes == null || argumentTypes.Length < argumentCount)
                return false;
            if (argumentOffsets == null || argumentOffsets.Length < argumentCount)
                return false;
            if (argumentLengths == null || argumentLengths.Length < argumentCount)
                return false;

            int cursor = offset;
            for (int i = 0; i < argumentCount; i++)
            {
                int valueType;
                int valueOffset;
                int valueLength;
                int nextArgumentOffset;
                if (!TryReadRpcArgument(buffer, cursor, bodyEnd, out valueType, out valueOffset, out valueLength, out nextArgumentOffset))
                    return false;

                argumentTypes[i] = (byte)valueType;
                argumentOffsets[i] = valueOffset;
                argumentLengths[i] = valueLength;
                cursor = nextArgumentOffset;
            }

            nextOffset = cursor;
            return true;
        }

        public static bool TryReadCompleteRpcCall(byte[] buffer, int bodyStart, int bodyEnd, byte[] argumentTypes, int[] argumentOffsets, int[] argumentLengths, out uint rpcHash, out int argumentCount)
        {
            rpcHash = 0u;
            argumentCount = 0;

            int cursor;
            if (!TryReadRpcCallHeader(buffer, bodyStart, bodyEnd, out rpcHash, out argumentCount, out cursor))
                return false;

            if (!TryReadRpcArguments(buffer, cursor, bodyEnd, argumentCount, argumentTypes, argumentOffsets, argumentLengths, out cursor))
                return false;

            return cursor == bodyEnd;
        }

        public static bool IsVariableStateMessage(int messageType)
        {
            return messageType == NetworkFrameProtocol.MessageTypeVariableState;
        }

        public static bool IsRpcCallMessage(int messageType)
        {
            return messageType == NetworkFrameProtocol.MessageTypeRpcCall;
        }

        private static bool IsSupportedMessageType(int messageType)
        {
            if (IsVariableStateMessage(messageType))
                return true;

            return IsRpcCallMessage(messageType);
        }

        private static bool CanRead(byte[] buffer, int offset, int byteCount, int limit)
        {
            if (buffer == null)
                return false;
            if (offset < 0)
                return false;
            if (byteCount < 0)
                return false;
            if (limit < 0)
                return false;
            if (limit > buffer.Length)
                limit = buffer.Length;
            if (offset > limit)
                return false;

            return byteCount <= limit - offset;
        }

        private static int GetLength(byte[] buffer)
        {
            return buffer != null ? buffer.Length : 0;
        }
    }
}
