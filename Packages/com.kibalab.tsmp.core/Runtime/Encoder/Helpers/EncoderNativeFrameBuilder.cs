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
            out string error,
            EncoderNativeSendState sendState = null,
            double now = 0.0,
            float refreshInterval = 1f,
            System.Action<string> onRpcRejected = null,
            bool deferOverBudgetRpc = false)
        {
            networkMessageCount = 0;
            variableMessageCount = 0;
            rpcMessageCount = 0;
            payloadBytes = 0;
            error = string.Empty;

            if (payload == null || payload.Length < maxPayloadBytes)
                payload = NetworkPayloadBuffer.EnsureCapacity(payload, maxPayloadBytes);
            NetworkPayloadBuffer.Clear(payload);

            payloadOffset = NetworkFrameWriter.BeginNetworkFrame(payload, 0, frameIndex);
            if (payloadOffset < 0 || maxPayloadBytes < NetworkFrameProtocol.NetworkHeaderBytes)
            {
                error = "Network frame buffer is too small.";
                return false;
            }

            ushort sequence = unchecked((ushort)frameIndex);
            WriteRpcMessages(queuedRpcs, payload, maxPayloadBytes, ref payloadOffset, sequence,
                ref networkMessageCount, out rpcMessageCount, onRpcRejected, deferOverBudgetRpc);

            if (!CaptureSources(behaviours, bindingCache, payload, sequence, ref payloadOffset,
                ref currentMessageStartOffset, ref currentVariableCount, out int manualMessages, out error))
                return false;
            if (payloadOffset > maxPayloadBytes)
            {
                error = "Manually written payload exceeds the transmission budget.";
                return false;
            }
            if (sendState == null)
                sendState = new EncoderNativeSendState();
            if (!sendState.Write(behaviours, bindingCache, payload, maxPayloadBytes, sequence, now, refreshInterval,
                ref payloadOffset, out variableMessageCount, out error, false))
                return false;
            variableMessageCount += manualMessages;
            networkMessageCount += variableMessageCount;

            if (!NetworkFrameWriter.EndNetworkFrame(payload, 0, networkMessageCount))
            {
                error = "Failed to finish network frame.";
                return false;
            }

            payloadBytes = payloadOffset;
            encodedPayload = NetworkPayloadBuffer.CopyPrefix(payload, payloadBytes, encodedPayload);
            return true;
        }

        private static bool CaptureSources(List<TSMPNetworkBehaviour> behaviours,
            Dictionary<System.Type, TransSyncMetadata.Cache> cache, byte[] payload, ushort sequence,
            ref int offset, ref int messageStart, ref int variableCount, out int messages, out string error)
        {
            messages = 0;
            error = string.Empty;
            messageStart = -1;
            variableCount = 0;
            if (behaviours == null)
                return true;
            foreach (TSMPNetworkBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;
                var method = TransSyncMetadata.GetOrCreate(cache, behaviour.GetType()).BeforeEncodeMethod;
                if (method == null)
                    continue;
                int start = offset;
                int body = NetworkFrameWriter.BeginVariableState(payload, start, BindingTable.ResolveNetworkId(behaviour), sequence);
                if (body >= 0)
                {
                    messageStart = start;
                    offset = body;
                }
                method.Invoke(behaviour, null);
                if (messageStart >= 0 && variableCount > 0)
                {
                    if (!NetworkFrameWriter.EndVariableState(payload, messageStart, offset, variableCount))
                    {
                        error = "Failed to finish manually written VariableState message.";
                        return false;
                    }
                    messages++;
                }
                else
                {
                    offset = start;
                }
                messageStart = -1;
                variableCount = 0;
            }
            return true;
        }

        public static bool ValidateRpc(QueuedRpc rpc, ref byte[] buffer, int capacity, out string error)
        {
            buffer = NetworkPayloadBuffer.EnsureCapacity(buffer, NetworkFrameProtocol.MaximumPayloadBytes);
            int offset = NetworkFrameProtocol.NetworkHeaderBytes;
            bool overCapacity;
            return WriteRpc(buffer, capacity, ref offset, rpc, 0, out error, out overCapacity);
        }

        private static void WriteRpcMessages(
            List<QueuedRpc> queuedRpcs,
            byte[] payload,
            int capacity,
            ref int payloadOffset,
            ushort sequence,
            ref int networkMessageCount,
            out int rpcMessageCount,
            System.Action<string> onRpcRejected,
            bool deferOverBudgetRpc)
        {
            rpcMessageCount = 0;

            if (queuedRpcs == null)
                return;

            while (queuedRpcs.Count > 0)
            {
                QueuedRpc rpc = queuedRpcs[0];
                bool overCapacity;
                if (WriteRpc(payload, capacity, ref payloadOffset, rpc, sequence, out string error, out overCapacity))
                {
                    networkMessageCount++;
                    rpcMessageCount++;
                    return;
                }

                // A live target reduction must not consume an already queued event.
                // Leave it pending while the state scheduler can still send smaller fields.
                if (deferOverBudgetRpc && overCapacity)
                    return;

                queuedRpcs.RemoveAt(0);
                string diagnostic = "RPC discarded: networkId=" + rpc.NetworkId + ", hash=" + rpc.RpcHash + ". " + error;
                if (onRpcRejected != null)
                    onRpcRejected(diagnostic);
                else
                    UnityEngine.Debug.LogWarning("[TSMP Encoder] " + diagnostic);
            }
        }

        public static void AdvanceQueuedRpcs(List<QueuedRpc> queuedRpcs)
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

        private static bool WriteRpc(byte[] payload, int capacity, ref int payloadOffset, QueuedRpc rpc, ushort sequence, out string error, out bool overCapacity)
        {
            overCapacity = false;
            int failedArgumentIndex;
            int writeError;
            int nextOffset = NetworkFrameWriter.WriteRpcCall(payload, payloadOffset, rpc.NetworkId, sequence, rpc.RpcHash, rpc.Arguments, out failedArgumentIndex, out writeError);
            if (nextOffset < 0)
            {
                error = NetworkFrameWriter.GetRpcWriteError(failedArgumentIndex, writeError);
                return false;
            }

            if (nextOffset > capacity)
            {
                overCapacity = true;
                error = "RPC does not fit. requiredPayloadBytes=" + nextOffset + ", capacity=" + capacity + ".";
                return false;
            }

            payloadOffset = nextOffset;
            error = string.Empty;
            return true;
        }

    }
}
#endif
