namespace K13A.TSMP
{
    public static class NetworkFrameWriter
    {
#if !COMPILER_UDONSHARP
        public const int WriteRpcErrorNone = 0;
        public const int WriteRpcErrorArgumentCount = 1;
        public const int WriteRpcErrorBegin = 2;
        public const int WriteRpcErrorUnsupportedArgument = 3;
        public const int WriteRpcErrorArgumentWrite = 4;
        public const int WriteRpcErrorEnd = 5;
        public const int WriteVariableStateErrorNone = 0;
        public const int WriteVariableStateErrorBegin = 1;
        public const int WriteVariableStateErrorFieldWrite = 2;
        public const int WriteVariableStateErrorEnd = 3;
#endif

        public static int BeginNetworkFrame(byte[] buffer, int offset, uint sequence)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.NetworkHeaderBytes))
                return -1;

            int frameStartOffset = offset;
            buffer[frameStartOffset + NetworkFrameProtocol.NetworkVersionMajorOffset] = NetworkFrameProtocol.NetworkVersionMajor;
            buffer[frameStartOffset + NetworkFrameProtocol.NetworkVersionMinorOffset] = NetworkFrameProtocol.NetworkVersionMinor;
            Binary.WriteUInt16LE(buffer, frameStartOffset + NetworkFrameProtocol.NetworkMessageCountOffset, 0);
            Binary.WriteUInt32LE(buffer, frameStartOffset + NetworkFrameProtocol.NetworkSequenceOffset, sequence);
            return frameStartOffset + NetworkFrameProtocol.NetworkHeaderBytes;
        }

        public static int BeginVariableState(byte[] buffer, int offset, int networkId, ushort sequence)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.VariableStateBodyHeaderBytes))
                return -1;

            int messageStartOffset = offset;
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.MessageNetworkIdOffset, ClampUInt16(networkId));
            buffer[messageStartOffset + NetworkFrameProtocol.MessageTypeOffset] = (byte)NetworkFrameProtocol.MessageTypeVariableState;
            buffer[messageStartOffset + NetworkFrameProtocol.MessageFlagsOffset] = 0;
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.MessageSequenceOffset, sequence);
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.MessageBodyLengthOffset, 0);
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.VariableCountOffset, 0);
            return messageStartOffset + NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.VariableStateBodyHeaderBytes;
        }

        public static int BeginRpcCall(byte[] buffer, int offset, int networkId, ushort sequence, uint rpcHash)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.RpcCallBodyHeaderBytes))
                return -1;

            int messageStartOffset = offset;
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.MessageNetworkIdOffset, ClampUInt16(networkId));
            buffer[messageStartOffset + NetworkFrameProtocol.MessageTypeOffset] = (byte)NetworkFrameProtocol.MessageTypeRpcCall;
            buffer[messageStartOffset + NetworkFrameProtocol.MessageFlagsOffset] = 0;
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.MessageSequenceOffset, sequence);
            Binary.WriteUInt16LE(buffer, messageStartOffset + NetworkFrameProtocol.MessageBodyLengthOffset, 0);
            Binary.WriteUInt32LE(buffer, messageStartOffset + NetworkFrameProtocol.RpcHashOffset, rpcHash);
            buffer[messageStartOffset + NetworkFrameProtocol.RpcArgumentCountOffset] = 0;
            return messageStartOffset + NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.RpcCallBodyHeaderBytes;
        }

        public static int BeginVariableValue(byte[] buffer, int offset, uint variableHash, int valueType)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.VariableValueHeaderBytes))
                return -1;

            Binary.WriteUInt32LE(buffer, offset + NetworkFrameProtocol.VariableValueHashOffset, variableHash);
            buffer[offset + NetworkFrameProtocol.VariableValueTypeOffset] = (byte)valueType;
            Binary.WriteUInt16LE(buffer, offset + NetworkFrameProtocol.VariableValueLengthOffset, 0);
            return offset + NetworkFrameProtocol.VariableValueHeaderBytes;
        }

        public static int BeginRpcArgument(byte[] buffer, int offset, int valueType)
        {
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.RpcArgumentHeaderBytes))
                return -1;

            buffer[offset + NetworkFrameProtocol.RpcArgumentTypeOffset] = (byte)valueType;
            Binary.WriteUInt16LE(buffer, offset + NetworkFrameProtocol.RpcArgumentLengthOffset, 0);
            return offset + NetworkFrameProtocol.RpcArgumentHeaderBytes;
        }

        public static bool EndVariableValue(byte[] buffer, int valueStartOffset, int payloadOffset)
        {
            return EndValue(buffer, valueStartOffset, payloadOffset, NetworkFrameProtocol.VariableValueHeaderBytes, NetworkFrameProtocol.VariableValueLengthOffset);
        }

        public static bool EndRpcArgument(byte[] buffer, int argumentStartOffset, int payloadOffset)
        {
            return EndValue(buffer, argumentStartOffset, payloadOffset, NetworkFrameProtocol.RpcArgumentHeaderBytes, NetworkFrameProtocol.RpcArgumentLengthOffset);
        }

        private static bool EndValue(byte[] buffer, int valueStartOffset, int payloadOffset, int valueHeaderBytes, int valueLengthOffset)
        {
            int valueLength = payloadOffset - (valueStartOffset + valueHeaderBytes);
            return PatchUInt16(buffer, valueStartOffset + valueLengthOffset, valueLength);
        }

        private static bool PatchMessageBodyLength(byte[] buffer, int messageStartOffset, int payloadOffset)
        {
            int lengthOffset = messageStartOffset + NetworkFrameProtocol.MessageBodyLengthOffset;
            int bodyLength = payloadOffset - (lengthOffset + NetworkFrameProtocol.UInt16Bytes);
            return PatchUInt16(buffer, lengthOffset, bodyLength);
        }

        public static bool EndVariableState(byte[] buffer, int messageStartOffset, int payloadOffset, int variableCount)
        {
            if (!PatchMessageBodyLength(buffer, messageStartOffset, payloadOffset))
                return false;

            return PatchVariableCount(buffer, messageStartOffset, variableCount);
        }

        public static bool EndRpcCall(byte[] buffer, int messageStartOffset, int payloadOffset, int argumentCount)
        {
            if (!PatchMessageBodyLength(buffer, messageStartOffset, payloadOffset))
                return false;

            return PatchRpcArgumentCount(buffer, messageStartOffset, argumentCount);
        }

#if !COMPILER_UDONSHARP
        public static int WriteVariableState(
            byte[] buffer,
            int offset,
            K13A.TSMP.Udon.TSMPNetworkBehaviour behaviour,
            TransSyncMetadata.Field[] fields,
            ushort networkId,
            ushort sequence,
            out int variableCount,
            out int failedFieldIndex,
            out int error)
        {
            variableCount = 0;
            failedFieldIndex = -1;
            error = WriteVariableStateErrorNone;

            int messageStartOffset = offset;
            int cursor = BeginVariableState(buffer, offset, networkId, sequence);
            if (cursor < 0)
            {
                error = WriteVariableStateErrorBegin;
                return -1;
            }

            cursor = WriteVariableEntries(buffer, cursor, behaviour, fields, out variableCount, out failedFieldIndex, out error);
            if (cursor < 0)
                return -1;

            if (variableCount == 0)
                return offset;

            if (!EndVariableState(buffer, messageStartOffset, cursor, variableCount))
            {
                error = WriteVariableStateErrorEnd;
                return -1;
            }

            return cursor;
        }

        public static int WriteVariableEntries(
            byte[] buffer,
            int offset,
            K13A.TSMP.Udon.TSMPNetworkBehaviour behaviour,
            TransSyncMetadata.Field[] fields,
            out int variableCount,
            out int failedFieldIndex,
            out int error)
        {
            variableCount = 0;
            failedFieldIndex = -1;
            error = WriteVariableStateErrorNone;

            int cursor = offset;
            int fieldCount = fields != null ? fields.Length : 0;
            for (int i = 0; i < fieldCount; i++)
            {
                TransSyncMetadata.Field field = fields[i];
                if (!TransSyncMetadata.CanSend(field))
                    continue;

                if (!TransSyncMetadata.IsFieldEnabled(behaviour, field))
                    continue;

                object value = field.FieldInfo.GetValue(behaviour);
                int nextOffset = NetworkValueEntryWriter.WriteVariableValue(buffer, cursor, field.VariableHash, (int)field.ValueType, value);
                if (nextOffset < 0)
                {
                    failedFieldIndex = i;
                    error = WriteVariableStateErrorFieldWrite;
                    return -1;
                }

                cursor = nextOffset;
                variableCount++;
            }

            return cursor;
        }

        public static string GetVariableStateWriteError(TransSyncMetadata.Field[] fields, int failedFieldIndex, int error)
        {
            if (error == WriteVariableStateErrorBegin)
                return "Failed to begin VariableState message.";
            if (error == WriteVariableStateErrorEnd)
                return "Failed to end VariableState message.";
            if (error == WriteVariableStateErrorFieldWrite)
                return "Failed to write TransSync field '" + GetFieldName(fields, failedFieldIndex) + "'.";

            return "Failed to write VariableState message.";
        }

        public static string GetRpcWriteError(int failedArgumentIndex, int error)
        {
            if (error == WriteRpcErrorArgumentCount)
                return "RPC argument count exceeds " + NetworkFrameProtocol.ByteMaxValue + ".";
            if (error == WriteRpcErrorBegin)
                return "Failed to begin RpcCall message.";
            if (error == WriteRpcErrorUnsupportedArgument)
                return "Unsupported RPC argument at index " + failedArgumentIndex + ".";
            if (error == WriteRpcErrorArgumentWrite)
                return "Failed to write RPC argument at index " + failedArgumentIndex + ".";
            if (error == WriteRpcErrorEnd)
                return "Failed to end RpcCall message.";

            return "Failed to write RpcCall message.";
        }

        private static string GetFieldName(TransSyncMetadata.Field[] fields, int index)
        {
            if (fields == null || index < 0 || index >= fields.Length)
                return "unknown";

            TransSyncMetadata.Field field = fields[index];
            if (field == null || field.FieldInfo == null)
                return "unknown";

            return field.FieldInfo.Name;
        }

        public static int WriteRpcCall(byte[] buffer, int offset, ushort networkId, ushort sequence, uint rpcHash, object[] arguments, out int failedArgumentIndex, out int error)
        {
            int argumentCount = arguments != null ? arguments.Length : 0;
            failedArgumentIndex = -1;
            error = WriteRpcErrorNone;

            if (argumentCount > NetworkFrameProtocol.ByteMaxValue)
            {
                error = WriteRpcErrorArgumentCount;
                return -1;
            }

            int messageStartOffset = offset;
            int cursor = BeginRpcCall(buffer, offset, networkId, sequence, rpcHash);
            if (cursor < 0)
            {
                error = WriteRpcErrorBegin;
                return -1;
            }

            for (int i = 0; i < argumentCount; i++)
            {
                object argument = arguments[i];
                if (argument == null || !NetworkValueCodec.TryGetValueType(argument.GetType(), out NetworkValueType valueType))
                {
                    failedArgumentIndex = i;
                    error = WriteRpcErrorUnsupportedArgument;
                    return -1;
                }

                int nextOffset = NetworkValueEntryWriter.WriteRpcArgument(buffer, cursor, (int)valueType, argument);
                if (nextOffset < 0)
                {
                    failedArgumentIndex = i;
                    error = WriteRpcErrorArgumentWrite;
                    return -1;
                }

                cursor = nextOffset;
            }

            if (!EndRpcCall(buffer, messageStartOffset, cursor, argumentCount))
            {
                error = WriteRpcErrorEnd;
                return -1;
            }

            return cursor;
        }
#endif

        public static bool EndNetworkFrame(byte[] buffer, int frameStartOffset, int messageCount)
        {
            return PatchNetworkMessageCount(buffer, frameStartOffset, messageCount);
        }

        public static bool ShouldOpenVariableState(bool messageOpen, int openNetworkId, int networkId)
        {
            if (!messageOpen)
                return true;

            return openNetworkId != networkId;
        }

        private static bool PatchNetworkMessageCount(byte[] buffer, int frameStartOffset, int count)
        {
            return PatchUInt16(buffer, frameStartOffset + NetworkFrameProtocol.NetworkMessageCountOffset, count);
        }

        private static bool PatchVariableCount(byte[] buffer, int messageStartOffset, int count)
        {
            return PatchUInt16(buffer, messageStartOffset + NetworkFrameProtocol.VariableCountOffset, count);
        }

        private static bool PatchRpcArgumentCount(byte[] buffer, int messageStartOffset, int count)
        {
            if (buffer == null)
                return false;
            if (count < 0)
                return false;
            if (count > NetworkFrameProtocol.ByteMaxValue)
                return false;

            int offset = messageStartOffset + NetworkFrameProtocol.RpcArgumentCountOffset;
            if (!CanWrite(buffer, offset, 1))
                return false;

            buffer[offset] = (byte)count;
            return true;
        }

        private static bool PatchUInt16(byte[] buffer, int offset, int value)
        {
            if (buffer == null)
                return false;
            if (value < 0)
                return false;
            if (value > NetworkFrameProtocol.UInt16MaxValue)
                return false;
            if (!CanWrite(buffer, offset, NetworkFrameProtocol.UInt16Bytes))
                return false;

            Binary.WriteUInt16LE(buffer, offset, (ushort)value);
            return true;
        }

        private static bool CanWrite(byte[] buffer, int offset, int byteCount)
        {
            if (buffer == null)
                return false;
            if (offset < 0)
                return false;
            if (byteCount < 0)
                return false;

            if (offset > buffer.Length)
                return false;

            return byteCount <= buffer.Length - offset;
        }

        private static ushort ClampUInt16(int value)
        {
            if (value <= 0)
                return 0;
            if (value >= NetworkFrameProtocol.UInt16MaxValue)
                return NetworkFrameProtocol.UInt16MaxValue;

            return (ushort)value;
        }
    }
}
