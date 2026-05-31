namespace K13A.TSMP
{
    public static class EncoderUdonFrameWriter
    {
        public static bool BeginVariableState(
            byte[] buffer,
            int payloadOffset,
            int currentMessageStartOffset,
            uint frameIndex,
            int networkId,
            out int nextPayloadOffset,
            out int nextMessageStartOffset,
            out int nextVariableCount,
            out int nextRpcArgumentCount,
            out int nextMessageType,
            out string error)
        {
            nextPayloadOffset = payloadOffset;
            nextMessageStartOffset = currentMessageStartOffset;
            nextVariableCount = 0;
            nextRpcArgumentCount = 0;
            nextMessageType = 0;
            error = string.Empty;

            if (currentMessageStartOffset >= 0)
            {
                error = "A TSMP message is already open.";
                return false;
            }

            int writtenOffset = NetworkFrameWriter.BeginVariableState(buffer, payloadOffset, networkId, (ushort)(frameIndex & 0xFFFFu));
            if (writtenOffset < 0)
            {
                error = "Payload buffer is full.";
                return false;
            }

            nextMessageStartOffset = payloadOffset;
            nextPayloadOffset = writtenOffset;
            nextMessageType = NetworkFrameProtocol.MessageTypeVariableState;
            return true;
        }

        public static bool EndVariableState(
            byte[] buffer,
            int payloadOffset,
            int currentMessageStartOffset,
            int currentMessageType,
            int currentVariableCount,
            out int nextPayloadOffset,
            out int nextMessageStartOffset,
            out int nextVariableCount,
            out int nextRpcArgumentCount,
            out int nextMessageType,
            out bool messageWritten,
            out string error)
        {
            nextPayloadOffset = payloadOffset;
            nextMessageStartOffset = currentMessageStartOffset;
            nextVariableCount = currentVariableCount;
            nextRpcArgumentCount = 0;
            nextMessageType = currentMessageType;
            messageWritten = false;
            error = string.Empty;

            if (currentMessageStartOffset < 0 || currentMessageType != NetworkFrameProtocol.MessageTypeVariableState)
            {
                error = "No VariableState message is open.";
                return false;
            }

            if (currentVariableCount == 0)
            {
                CancelCurrentMessage(payloadOffset, currentMessageStartOffset, out nextPayloadOffset, out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType);
                return true;
            }

            if (!NetworkFrameWriter.EndVariableState(buffer, currentMessageStartOffset, payloadOffset, currentVariableCount))
            {
                error = "VariableState message is too large.";
                return false;
            }

            CloseCurrentMessage(out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType);
            messageWritten = true;
            return true;
        }

        public static bool BeginRpcCall(
            byte[] buffer,
            int payloadOffset,
            int currentMessageStartOffset,
            uint frameIndex,
            int networkId,
            uint rpcHash,
            out int nextPayloadOffset,
            out int nextMessageStartOffset,
            out int nextVariableCount,
            out int nextRpcArgumentCount,
            out int nextMessageType,
            out string error)
        {
            nextPayloadOffset = payloadOffset;
            nextMessageStartOffset = currentMessageStartOffset;
            nextVariableCount = 0;
            nextRpcArgumentCount = 0;
            nextMessageType = 0;
            error = string.Empty;

            if (currentMessageStartOffset >= 0)
            {
                error = "A TSMP message is already open.";
                return false;
            }

            int writtenOffset = NetworkFrameWriter.BeginRpcCall(buffer, payloadOffset, networkId, (ushort)(frameIndex & 0xFFFFu), rpcHash);
            if (writtenOffset < 0)
            {
                error = "Payload buffer is full.";
                return false;
            }

            nextMessageStartOffset = payloadOffset;
            nextPayloadOffset = writtenOffset;
            nextMessageType = NetworkFrameProtocol.MessageTypeRpcCall;
            return true;
        }

        public static bool EndRpcCall(
            byte[] buffer,
            int payloadOffset,
            int currentMessageStartOffset,
            int currentMessageType,
            int currentRpcArgumentCount,
            out int nextMessageStartOffset,
            out int nextVariableCount,
            out int nextRpcArgumentCount,
            out int nextMessageType,
            out string error)
        {
            nextMessageStartOffset = currentMessageStartOffset;
            nextVariableCount = 0;
            nextRpcArgumentCount = currentRpcArgumentCount;
            nextMessageType = currentMessageType;
            error = string.Empty;

            if (currentMessageStartOffset < 0 || currentMessageType != NetworkFrameProtocol.MessageTypeRpcCall)
            {
                error = "No RpcCall message is open.";
                return false;
            }

            if (!NetworkFrameWriter.EndRpcCall(buffer, currentMessageStartOffset, payloadOffset, currentRpcArgumentCount))
            {
                error = "RpcCall message is too large.";
                return false;
            }

            CloseCurrentMessage(out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType);
            return true;
        }

        public static bool WriteVariableValue(
            byte[] buffer,
            int payloadOffset,
            int currentMessageStartOffset,
            int currentMessageType,
            int currentVariableCount,
            uint variableHash,
            int valueType,
            object value,
            out int nextPayloadOffset,
            out int nextVariableCount,
            out string error)
        {
            nextPayloadOffset = payloadOffset;
            nextVariableCount = currentVariableCount;
            error = string.Empty;

            if (!NetworkValueCodec.IsSupportedValueType(valueType))
            {
                error = "Unsupported variable value type: " + valueType + ".";
                return false;
            }

            if (currentMessageStartOffset < 0 || currentMessageType != NetworkFrameProtocol.MessageTypeVariableState)
            {
                error = "No VariableState message is open.";
                return false;
            }

            int writtenOffset = NetworkValueEntryWriter.WriteVariableValue(buffer, payloadOffset, variableHash, valueType, value);
            if (writtenOffset < 0)
            {
                error = "Failed to write TSMP value.";
                return false;
            }

            nextPayloadOffset = writtenOffset;
            nextVariableCount = currentVariableCount + 1;
            return true;
        }

        public static bool WriteRpcArgument(
            byte[] buffer,
            int payloadOffset,
            int currentMessageStartOffset,
            int currentMessageType,
            int currentRpcArgumentCount,
            int valueType,
            object value,
            out int nextPayloadOffset,
            out int nextRpcArgumentCount,
            out string error)
        {
            nextPayloadOffset = payloadOffset;
            nextRpcArgumentCount = currentRpcArgumentCount;
            error = string.Empty;

            if (!NetworkValueCodec.IsSupportedValueType(valueType))
            {
                error = "Unsupported RPC argument value type: " + valueType + ".";
                return false;
            }

            if (currentMessageStartOffset < 0 || currentMessageType != NetworkFrameProtocol.MessageTypeRpcCall)
            {
                error = "No RpcCall message is open.";
                return false;
            }

            if (currentRpcArgumentCount >= NetworkFrameProtocol.ByteMaxValue)
            {
                error = "RPC argument count exceeds " + NetworkFrameProtocol.ByteMaxValue + ".";
                return false;
            }

            int writtenOffset = NetworkValueEntryWriter.WriteRpcArgument(buffer, payloadOffset, valueType, value);
            if (writtenOffset < 0)
            {
                error = "Failed to write TSMP value.";
                return false;
            }

            nextPayloadOffset = writtenOffset;
            nextRpcArgumentCount = currentRpcArgumentCount + 1;
            return true;
        }

        public static void CancelCurrentMessage(
            int payloadOffset,
            int currentMessageStartOffset,
            out int nextPayloadOffset,
            out int nextMessageStartOffset,
            out int nextVariableCount,
            out int nextRpcArgumentCount,
            out int nextMessageType)
        {
            nextPayloadOffset = currentMessageStartOffset >= 0 ? currentMessageStartOffset : payloadOffset;
            CloseCurrentMessage(out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType);
        }

        private static void CloseCurrentMessage(out int nextMessageStartOffset, out int nextVariableCount, out int nextRpcArgumentCount, out int nextMessageType)
        {
            nextMessageStartOffset = -1;
            nextVariableCount = 0;
            nextRpcArgumentCount = 0;
            nextMessageType = 0;
        }
    }
}
