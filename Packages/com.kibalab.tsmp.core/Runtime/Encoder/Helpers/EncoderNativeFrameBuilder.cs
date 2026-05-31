#if !COMPILER_UDONSHARP
using System.Collections.Generic;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public static class EncoderNativeFrameBuilder
    {
        public struct QueuedRpc
        {
            public ushort NetworkId;
            public uint RpcHash;
            public object[] Arguments;
            public int RepeatsRemaining;
        }

        public static bool BuildNetworkPayload(
            List<TSMPNetworkBehaviour> behaviours,
            List<QueuedRpc> queuedRpcs,
            Dictionary<System.Type, TransSyncMetadata.Cache> bindingCache,
            ref byte[] payload,
            ref byte[] encodedPayload,
            ref int payloadOffset,
            ref int currentMessageStartOffset,
            ref int currentVariableCount,
            uint frameIndex,
            int maxPayloadBytes,
            out int networkMessageCount,
            out int variableMessageCount,
            out int rpcMessageCount,
            out int payloadBytes,
            out string error)
        {
            networkMessageCount = 0;
            variableMessageCount = 0;
            rpcMessageCount = 0;
            payloadBytes = 0;
            error = string.Empty;

            payload = NetworkPayloadBuffer.EnsureCapacity(payload, maxPayloadBytes);
            NetworkPayloadBuffer.Clear(payload);

            payloadOffset = NetworkFrameWriter.BeginNetworkFrame(payload, 0, frameIndex);
            if (payloadOffset < 0)
            {
                error = "Network frame buffer is too small.";
                return false;
            }

            ushort sequence = unchecked((ushort)frameIndex);
            if (!WriteVariableStateMessages(behaviours, bindingCache, payload, ref payloadOffset, ref currentMessageStartOffset, ref currentVariableCount, sequence, out networkMessageCount, out variableMessageCount, out error))
                return false;

            if (!WriteRpcMessages(queuedRpcs, payload, ref payloadOffset, sequence, ref networkMessageCount, out rpcMessageCount, out error))
                return false;

            AdvanceQueuedRpcs(queuedRpcs);

            if (!NetworkFrameWriter.EndNetworkFrame(payload, 0, networkMessageCount))
            {
                error = "Failed to finish network frame.";
                return false;
            }

            payloadBytes = payloadOffset;
            encodedPayload = NetworkPayloadBuffer.CopyPrefix(payload, payloadBytes, encodedPayload);
            return true;
        }

        private static bool WriteVariableStateMessages(
            List<TSMPNetworkBehaviour> behaviours,
            Dictionary<System.Type, TransSyncMetadata.Cache> bindingCache,
            byte[] payload,
            ref int payloadOffset,
            ref int currentMessageStartOffset,
            ref int currentVariableCount,
            ushort sequence,
            out int networkMessageCount,
            out int variableMessageCount,
            out string error)
        {
            networkMessageCount = 0;
            variableMessageCount = 0;
            error = string.Empty;

            if (behaviours == null)
                return true;

            for (int i = 0; i < behaviours.Count; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                ushort networkId = BindingTable.ResolveNetworkId(behaviour);
                if (!BeginVariableState(payload, ref payloadOffset, ref currentMessageStartOffset, ref currentVariableCount, networkId, sequence))
                {
                    error = "Failed to begin VariableState message.";
                    return false;
                }

                InvokeBeforeEncode(behaviour, bindingCache);

                int writtenVariableCount;
                int failedFieldIndex;
                int writeError;
                TransSyncMetadata.Field[] fields = GetTransSyncFields(behaviour, bindingCache);
                int nextOffset = NetworkFrameWriter.WriteVariableEntries(payload, payloadOffset, behaviour, fields, out writtenVariableCount, out failedFieldIndex, out writeError);
                if (nextOffset < 0)
                {
                    CancelMessage(ref payloadOffset, ref currentMessageStartOffset, ref currentVariableCount);
                    error = NetworkFrameWriter.GetVariableStateWriteError(fields, failedFieldIndex, writeError);
                    return false;
                }

                payloadOffset = nextOffset;
                currentVariableCount += writtenVariableCount;
                if (currentVariableCount == 0)
                {
                    CancelMessage(ref payloadOffset, ref currentMessageStartOffset, ref currentVariableCount);
                    continue;
                }

                if (!EndVariableState(payload, ref currentMessageStartOffset, ref currentVariableCount, payloadOffset))
                {
                    error = "Failed to end VariableState message.";
                    return false;
                }

                networkMessageCount++;
                variableMessageCount++;
            }

            return true;
        }

        private static bool WriteRpcMessages(
            List<QueuedRpc> queuedRpcs,
            byte[] payload,
            ref int payloadOffset,
            ushort sequence,
            ref int networkMessageCount,
            out int rpcMessageCount,
            out string error)
        {
            rpcMessageCount = 0;
            error = string.Empty;

            if (queuedRpcs == null)
                return true;

            if (queuedRpcs.Count <= 0)
                return true;

            if (!WriteRpc(payload, ref payloadOffset, queuedRpcs[0], sequence, out error))
                return false;

            networkMessageCount++;
            rpcMessageCount++;

            return true;
        }

        private static void AdvanceQueuedRpcs(List<QueuedRpc> queuedRpcs)
        {
            if (queuedRpcs == null)
                return;
            if (queuedRpcs.Count <= 0)
                return;

            QueuedRpc rpc = queuedRpcs[0];
            rpc.RepeatsRemaining--;
            if (rpc.RepeatsRemaining > 0)
            {
                queuedRpcs[0] = rpc;
                return;
            }

            queuedRpcs.RemoveAt(0);
        }

        private static bool WriteRpc(byte[] payload, ref int payloadOffset, QueuedRpc rpc, ushort sequence, out string error)
        {
            int failedArgumentIndex;
            int writeError;
            int nextOffset = NetworkFrameWriter.WriteRpcCall(payload, payloadOffset, rpc.NetworkId, sequence, rpc.RpcHash, rpc.Arguments, out failedArgumentIndex, out writeError);
            if (nextOffset < 0)
            {
                error = NetworkFrameWriter.GetRpcWriteError(failedArgumentIndex, writeError);
                return false;
            }

            payloadOffset = nextOffset;
            error = string.Empty;
            return true;
        }

        private static bool BeginVariableState(byte[] payload, ref int payloadOffset, ref int currentMessageStartOffset, ref int currentVariableCount, ushort networkId, ushort sequence)
        {
            int nextOffset = NetworkFrameWriter.BeginVariableState(payload, payloadOffset, networkId, sequence);
            if (nextOffset < 0)
                return false;

            currentMessageStartOffset = payloadOffset;
            currentVariableCount = 0;
            payloadOffset = nextOffset;
            return true;
        }

        private static bool EndVariableState(byte[] payload, ref int currentMessageStartOffset, ref int currentVariableCount, int payloadOffset)
        {
            if (currentMessageStartOffset < 0)
                return false;

            if (!NetworkFrameWriter.EndVariableState(payload, currentMessageStartOffset, payloadOffset, currentVariableCount))
                return false;

            currentMessageStartOffset = -1;
            currentVariableCount = 0;
            return true;
        }

        private static void CancelMessage(ref int payloadOffset, ref int currentMessageStartOffset, ref int currentVariableCount)
        {
            if (currentMessageStartOffset >= 0)
                payloadOffset = currentMessageStartOffset;

            currentMessageStartOffset = -1;
            currentVariableCount = 0;
        }

        private static void InvokeBeforeEncode(TSMPNetworkBehaviour behaviour, Dictionary<System.Type, TransSyncMetadata.Cache> bindingCache)
        {
            if (behaviour == null)
                return;

            System.Reflection.MethodInfo method = TransSyncMetadata.GetOrCreate(bindingCache, behaviour.GetType()).BeforeEncodeMethod;
            if (method != null)
                method.Invoke(behaviour, null);
        }

        private static TransSyncMetadata.Field[] GetTransSyncFields(TSMPNetworkBehaviour behaviour, Dictionary<System.Type, TransSyncMetadata.Cache> bindingCache)
        {
            return TransSyncMetadata.GetOrCreate(bindingCache, behaviour.GetType()).Fields;
        }
    }
}
#endif
