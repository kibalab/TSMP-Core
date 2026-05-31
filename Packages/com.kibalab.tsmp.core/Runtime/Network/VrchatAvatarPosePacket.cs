using UnityEngine;

namespace K13A.TSMP.Udon
{
    public static class VrchatAvatarPosePacket
    {
        private const byte PoseFlagIncludeFingerBones = 1 << 0;
        private const byte PoseFlagCompactHumanoid = 1 << 1;
        private const byte PoseFlagHasDisplayNames = 1 << 2;
        private const byte PlayerFlagHasRootPose = 1 << 0;
        private const int RootPositionBytes = 12;
        private const int RootRotationBytes = 5;

        public static byte BuildHeaderFlags(bool includeFingerBones, bool compactHumanoid, bool hasDisplayNames)
        {
            byte flags = 0;
            if (includeFingerBones)
                flags |= PoseFlagIncludeFingerBones;
            if (compactHumanoid)
                flags |= PoseFlagCompactHumanoid;
            if (hasDisplayNames)
                flags |= PoseFlagHasDisplayNames;

            return flags;
        }

        public static void WriteHeader(byte[] bytes, int version, int mode, int playerCount, byte flags, ushort sequence)
        {
            bytes[0] = (byte)version;
            bytes[1] = (byte)mode;
            bytes[2] = (byte)playerCount;
            bytes[3] = flags;
            Binary.WriteUInt16LE(bytes, 4, sequence);
        }

        public static int WritePlayerHeader(byte[] bytes, int cursor, int playerId, int recordCount, int nameBytes, bool hasRootPose)
        {
            Binary.WriteUInt16LE(bytes, cursor, (ushort)playerId);
            cursor += 2;
            bytes[cursor++] = (byte)recordCount;
            bytes[cursor++] = (byte)nameBytes;
            bytes[cursor++] = hasRootPose ? PlayerFlagHasRootPose : (byte)0;
            return cursor;
        }

        public static int WriteRootPose(byte[] bytes, int cursor, Vector3 position, Quaternion rotation)
        {
            cursor = Binary.WriteVector3Float32LE(bytes, cursor, position);
            Binary.WritePackedQuaternion12LE(bytes, cursor, rotation);
            return cursor + RootRotationBytes;
        }

        public static bool TryReadPlayerHeader(
            byte[] bytes,
            int cursor,
            int headerBytes,
            bool hasPlayerFlags,
            out int playerId,
            out int recordCount,
            out int nameLength,
            out bool hasRootPose,
            out int nextCursor,
            out int error)
        {
            playerId = 0;
            recordCount = 0;
            nameLength = 0;
            hasRootPose = false;
            nextCursor = cursor;
            error = 0;

            if (bytes == null || cursor + headerBytes > bytes.Length)
            {
                error = 2;
                return false;
            }

            playerId = Binary.ReadUInt16LE(bytes, cursor);
            cursor += 2;
            recordCount = bytes[cursor++];
            nameLength = bytes[cursor++];
            int playerFlags = PlayerFlagHasRootPose;
            if (hasPlayerFlags)
                playerFlags = bytes[cursor++];

            hasRootPose = (playerFlags & PlayerFlagHasRootPose) != 0;
            nextCursor = cursor;
            return true;
        }

        public static bool TryReadLegacyPlayerHeader(byte[] bytes, int cursor, int headerBytes, out int playerId, out int recordCount, out int nextCursor)
        {
            playerId = 0;
            recordCount = 0;
            nextCursor = cursor;

            if (bytes == null || cursor + headerBytes > bytes.Length)
                return false;

            playerId = Binary.ReadUInt16LE(bytes, cursor);
            cursor += 2;
            recordCount = bytes[cursor++];
            nextCursor = cursor;
            return true;
        }

        public static bool TryReadRootPose(byte[] bytes, int cursor, int rootPoseBytes, out Vector3 position, out Quaternion rotation, out int nextCursor)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            nextCursor = cursor;

            if (bytes == null || cursor + rootPoseBytes > bytes.Length)
                return false;

            position = Binary.ReadVector3Float32LE(bytes, cursor);
            cursor += RootPositionBytes;
            rotation = Binary.ReadPackedQuaternion12LE(bytes, cursor);
            nextCursor = cursor + RootRotationBytes;
            return true;
        }
    }
}
