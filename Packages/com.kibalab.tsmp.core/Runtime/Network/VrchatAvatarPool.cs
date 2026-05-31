using UnityEngine;
using VRC.SDKBase;

namespace K13A.TSMP.Udon
{
    public static class VrchatAvatarPool
    {
        public static int ResolveCachedSlot(int[] recordPlayerIds, int[] recordSlots, int[] slotPlayerIds, int recordIndex, int playerId, int maxPlayers)
        {
            if (recordIndex < 0)
                return -1;
            if (recordPlayerIds == null || recordSlots == null)
                return -1;
            if (recordIndex >= recordPlayerIds.Length || recordIndex >= recordSlots.Length)
                return -1;
            if (recordPlayerIds[recordIndex] != playerId)
                return -1;

            int slot = recordSlots[recordIndex];
            if (!IsSlotAssignedToPlayer(slotPlayerIds, slot, playerId, maxPlayers))
                return -1;

            return slot;
        }

        public static int FindSlotByPlayerId(int[] slotPlayerIds, int maxPlayers, int playerId)
        {
            if (slotPlayerIds == null)
                return -1;

            int count = maxPlayers < slotPlayerIds.Length ? maxPlayers : slotPlayerIds.Length;
            for (int i = 0; i < count; i++)
            {
                if (slotPlayerIds[i] == playerId)
                    return i;
            }

            return -1;
        }

        public static bool IsSlotAssignedToPlayer(int[] slotPlayerIds, int slot, int playerId, int maxPlayers)
        {
            if (slotPlayerIds == null)
                return false;
            if (slot < 0 || slot >= maxPlayers || slot >= slotPlayerIds.Length)
                return false;

            return slotPlayerIds[slot] == playerId;
        }

        public static void CacheSlot(int[] recordPlayerIds, int[] recordSlots, int recordIndex, int playerId, int slot)
        {
            if (recordPlayerIds == null || recordSlots == null)
                return;
            if (recordIndex < 0 || recordIndex >= recordPlayerIds.Length || recordIndex >= recordSlots.Length)
                return;

            recordPlayerIds[recordIndex] = playerId;
            recordSlots[recordIndex] = slot;
        }

        public static void InvalidateSlotCache(int[] recordPlayerIds, int[] recordSlots)
        {
            ArrayUtil.FillIntArray(recordPlayerIds, -1);
            ArrayUtil.FillIntArray(recordSlots, -1);
        }

        public static void InvalidatePlayerSequenceCache(int[] playerIds, int[] sequences)
        {
            ArrayUtil.FillIntArray(playerIds, -1);
            ArrayUtil.FillIntArray(sequences, -1);
        }

        public static bool ShouldRetireSlot(int[] slotPlayerIds, float[] slotLastSeen, int slot, float now, float staleSeconds)
        {
            if (slotPlayerIds == null || slotLastSeen == null)
                return false;
            if (slot < 0 || slot >= slotPlayerIds.Length || slot >= slotLastSeen.Length)
                return false;
            if (slotPlayerIds[slot] < 0)
                return false;

            return now - slotLastSeen[slot] > staleSeconds;
        }

        public static GameObject[] ResizeGameObjectArray(GameObject[] source, int length)
        {
            GameObject[] result = new GameObject[length];
            if (source != null)
            {
                int count = source.Length < length ? source.Length : length;
                for (int i = 0; i < count; i++)
                    result[i] = source[i];
            }

            return result;
        }

        public static TSMPNetworkVrchatAvatarPoseRig[] ResizeRigArray(TSMPNetworkVrchatAvatarPoseRig[] source, int length)
        {
            TSMPNetworkVrchatAvatarPoseRig[] result = new TSMPNetworkVrchatAvatarPoseRig[length];
            if (source != null)
            {
                int count = source.Length < length ? source.Length : length;
                for (int i = 0; i < count; i++)
                    result[i] = source[i];
            }

            return result;
        }
    }

    public static class VrchatAvatarPlayerCache
    {
        public static int GetPlayerId(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
                return -1;

            return player.playerId;
        }

        public static string GetPlayerDisplayName(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
                return string.Empty;

            string value = player.displayName;
            return value == null ? string.Empty : value;
        }

        public static bool IsEncodablePlayer(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
                return false;

            int playerId = GetPlayerId(player);
            return playerId >= 0 && playerId <= NetworkFrameProtocol.UInt16MaxValue;
        }

        public static int GetClampedVrchatPlayerCount(int maxPlayers)
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (count < 0)
                count = 0;
            if (count > maxPlayers)
                count = maxPlayers;

            return count;
        }

        public static void Clear(VRCPlayerApi[] players)
        {
            if (players == null)
                return;

            for (int i = 0; i < players.Length; i++)
                players[i] = null;
        }

        public static int FindIndex(VRCPlayerApi[] players, int count, int playerId)
        {
            if (players == null)
                return -1;

            int limit = count < players.Length ? count : players.Length;
            for (int i = 0; i < limit; i++)
            {
                if (GetPlayerId(players[i]) == playerId)
                    return i;
            }

            return -1;
        }

        public static int FindInsertIndex(VRCPlayerApi[] players, int count, int playerId)
        {
            if (players == null)
                return 0;

            int limit = count < players.Length ? count : players.Length;
            for (int i = 0; i < limit; i++)
            {
                if (GetSortablePlayerId(players[i]) > playerId)
                    return i;
            }

            return limit;
        }

        public static int Insert(VRCPlayerApi[] players, int count, int index, VRCPlayerApi player, int maxPlayers)
        {
            if (players == null)
                return count;
            if (count >= maxPlayers || count >= players.Length)
                return count;

            int insertIndex = index;
            if (insertIndex < 0)
                insertIndex = 0;
            if (insertIndex > count)
                insertIndex = count;

            for (int i = count; i > insertIndex; i--)
                players[i] = players[i - 1];

            players[insertIndex] = player;
            return count + 1;
        }

        public static int RemoveAt(VRCPlayerApi[] players, int count, int index)
        {
            if (players == null)
                return count;
            if (index < 0 || index >= count)
                return count;

            int nextCount = count - 1;
            for (int i = index; i < nextCount; i++)
                players[i] = players[i + 1];

            if (nextCount < 0)
                nextCount = 0;
            if (nextCount < players.Length)
                players[nextCount] = null;

            return nextCount;
        }

        public static int Compact(VRCPlayerApi[] players, int count)
        {
            if (players == null || count <= 0)
                return count;

            int limit = count < players.Length ? count : players.Length;
            int write = 0;
            for (int read = 0; read < limit; read++)
            {
                VRCPlayerApi player = players[read];
                if (!IsEncodablePlayer(player))
                    continue;

                if (write != read)
                    players[write] = player;
                write++;
            }

            for (int i = write; i < limit; i++)
                players[i] = null;

            return write;
        }

        public static void SortById(VRCPlayerApi[] players, int count)
        {
            if (players == null || count <= 1)
                return;

            int limit = count < players.Length ? count : players.Length;
            for (int i = 0; i < limit - 1; i++)
            {
                int best = i;
                int bestId = GetSortablePlayerId(players[i]);
                for (int j = i + 1; j < limit; j++)
                {
                    int id = GetSortablePlayerId(players[j]);
                    if (id < bestId)
                    {
                        best = j;
                        bestId = id;
                    }
                }

                if (best == i)
                    continue;

                VRCPlayerApi player = players[i];
                players[i] = players[best];
                players[best] = player;
            }
        }

        private static int GetSortablePlayerId(VRCPlayerApi player)
        {
            int playerId = GetPlayerId(player);
            return playerId >= 0 ? playerId : 2147483647;
        }
    }
}
