#if !COMPILER_UDONSHARP
using System.Collections.Generic;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public sealed class EncoderNativeSendState
    {
        private sealed class Entry
        {
            public TSMPNetworkBehaviour Target;
            public TransSyncMetadata.Field Field;
            public ushort NetworkId;
            public byte[] Previous;
            public bool Sent;
            public double LastSent;
            public int PendingOffset;
            public int PendingLength;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int[] _order;
        private int[] _priorities;
        private readonly byte[] _scratch = new byte[TransSyncSendScheduler.ScratchBytes];
        private int _rotation;
        public int WrittenCount { get; private set; }
        public int DeferredCount { get; private set; }
        public int OversizedCount { get; private set; }
        public int LastDeferredNetworkId { get; private set; }
        public string LastDeferredName { get; private set; }
        public int LastDeferredRequiredBytes { get; private set; }
        public bool LastDeferredFitsBudget { get; private set; }
        public int BlockedRpcNetworkId { get; private set; }
        public uint BlockedRpcHash { get; private set; }
        public int BlockedRpcRequiredBytes { get; private set; }

        public void ResetDiagnostics()
        {
            DeferredCount = 0;
            OversizedCount = 0;
            LastDeferredNetworkId = 0;
            LastDeferredName = string.Empty;
            LastDeferredRequiredBytes = 0;
            LastDeferredFitsBudget = false;
            BlockedRpcNetworkId = 0;
            BlockedRpcHash = 0;
            BlockedRpcRequiredBytes = 0;
        }

        public void RecordBlockedRpc(ushort networkId, uint hash, int requiredBytes)
        {
            BlockedRpcNetworkId = networkId;
            BlockedRpcHash = hash;
            BlockedRpcRequiredBytes = requiredBytes;
        }

        public void Reset()
        {
            _entries.Clear();
            _order = null;
            _rotation = 0;
        }

        private void Prepare(List<TSMPNetworkBehaviour> behaviours, Dictionary<System.Type, TransSyncMetadata.Cache> cache, bool capture)
        {
            int index = 0;
            bool changed = false;
            if (behaviours != null)
            {
                foreach (TSMPNetworkBehaviour target in behaviours)
                {
                    if (target == null)
                        continue;
                    TransSyncMetadata.Cache metadata = TransSyncMetadata.GetOrCreate(cache, target.GetType());
                    if (capture && metadata.BeforeEncodeMethod != null)
                        metadata.BeforeEncodeMethod.Invoke(target, null);
                    ushort networkId = BindingTable.ResolveNetworkId(target);
                    foreach (TransSyncMetadata.Field field in metadata.Fields)
                    {
                        if (!TransSyncMetadata.CanSend(field))
                            continue;
                        Entry entry = index < _entries.Count ? _entries[index] : null;
                        if (entry == null || entry.Target != target || entry.Field != field || entry.NetworkId != networkId)
                        {
                            entry = new Entry { Target = target, Field = field, NetworkId = networkId };
                            if (index < _entries.Count)
                                _entries[index] = entry;
                            else
                                _entries.Add(entry);
                            changed = true;
                        }
                        entry.PendingLength = 0;
                        index++;
                    }
                }
            }
            if (index < _entries.Count)
            {
                _entries.RemoveRange(index, _entries.Count - index);
                changed = true;
            }
            if (_order == null || changed)
            {
                _priorities = new int[_entries.Count];
                for (int i = 0; i < _entries.Count; i++)
                    _priorities[i] = _entries[i].Field.Sync.Priority;
                _order = TransSyncSendScheduler.BuildOrder(_entries.Count, _priorities);
            }
        }

        public bool Write(List<TSMPNetworkBehaviour> behaviours, Dictionary<System.Type, TransSyncMetadata.Cache> cache,
            byte[] payload, int limit, ushort sequence, double now, float refreshInterval,
            ref int offset, out int messageCount, out string error, bool capture = true)
        {
            WrittenCount = 0;
            DeferredCount = 0;
            OversizedCount = 0;
            LastDeferredNetworkId = 0;
            LastDeferredName = string.Empty;
            LastDeferredRequiredBytes = 0;
            LastDeferredFitsBudget = false;
            messageCount = 0;
            error = string.Empty;
            Prepare(behaviours, cache, capture);
            int messageStart = -1;
            int openNetworkId = -1;
            int variableCount = 0;
            for (int groupStart = 0; groupStart < _order.Length;)
            {
                int groupEnd = groupStart + 1;
                int priority = _priorities[_order[groupStart]];
                while (groupEnd < _order.Length && _priorities[_order[groupEnd]] == priority)
                    groupEnd++;
                int groupCount = groupEnd - groupStart;
                for (int step = 0; step < groupCount; step++)
                {
                    Entry entry = _entries[_order[groupStart + (step + _rotation % groupCount) % groupCount]];
                    if (!entry.Target.IsTSMPActive() || !TransSyncMetadata.IsFieldEnabled(entry.Target, entry.Field))
                    {
                        entry.Sent = false;
                        continue;
                    }
                    if (!TransSyncSendScheduler.IsDue(entry.Sent, entry.LastSent, now, entry.Field.Sync.MinSendInterval))
                        continue;
                    object value = entry.Field.FieldInfo.GetValue(entry.Target);
                    int length = NetworkValueEntryWriter.WriteVariableValue(_scratch, 0, entry.Field.VariableHash, (int)entry.Field.ValueType, value);
                    if (length < 0)
                    {
                        error = "Failed to serialize TransSync field '" + entry.Field.FieldInfo.Name + "'.";
                        return false;
                    }
                    bool sendOnChange = TransSyncSendScheduler.ResolveSendOnChange((int)entry.Target.sendMode, entry.Field.Sync.SendOnChange);
                    if (!TransSyncSendScheduler.ShouldSend(entry.Sent, entry.LastSent, now, sendOnChange, refreshInterval, entry.Previous, _scratch, length))
                        continue;
                    bool newMessage = messageStart < 0 || openNetworkId != entry.NetworkId;
                    int overhead = newMessage ? NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.VariableStateBodyHeaderBytes : 0;
                    if (length + overhead > limit - offset)
                    {
                        DeferredCount++;
                        LastDeferredNetworkId = entry.NetworkId;
                        LastDeferredName = entry.Field.FieldInfo.Name;
                        LastDeferredRequiredBytes = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes
                            + NetworkFrameProtocol.VariableStateBodyHeaderBytes + length;
                        LastDeferredFitsBudget = LastDeferredRequiredBytes <= limit;
                        if (!LastDeferredFitsBudget)
                            OversizedCount++;
                        continue;
                    }
                    if (newMessage)
                    {
                        if (messageStart >= 0)
                            NetworkFrameWriter.EndVariableState(payload, messageStart, offset, variableCount);
                        messageStart = offset;
                        offset = NetworkFrameWriter.BeginVariableState(payload, offset, entry.NetworkId, sequence);
                        openNetworkId = entry.NetworkId;
                        variableCount = 0;
                        messageCount++;
                    }
                    entry.PendingOffset = offset;
                    entry.PendingLength = length;
                    System.Array.Copy(_scratch, 0, payload, offset, length);
                    offset += length;
                    variableCount++;
                    WrittenCount++;
                }
                groupStart = groupEnd;
            }
            if (messageStart >= 0)
                NetworkFrameWriter.EndVariableState(payload, messageStart, offset, variableCount);
            return true;
        }

        public void Commit(byte[] payload, double now)
        {
            foreach (Entry entry in _entries)
            {
                if (entry.PendingLength <= 0)
                    continue;
                entry.Previous = TransSyncSendScheduler.Snapshot(payload, entry.PendingOffset, entry.PendingLength, entry.Previous);
                entry.LastSent = now;
                entry.Sent = true;
                entry.PendingLength = 0;
                if (!string.IsNullOrEmpty(entry.Field.Sync.SentEvent))
                {
                    try { ComponentReflection.InvokeMethod(entry.Target, entry.Field.Sync.SentEvent); }
                    catch (System.Exception exception) { UnityEngine.Debug.LogException(exception, entry.Target); }
                }
            }
            _rotation = _rotation >= int.MaxValue - 1 ? 0 : _rotation + 1;
        }
    }
}
#endif
