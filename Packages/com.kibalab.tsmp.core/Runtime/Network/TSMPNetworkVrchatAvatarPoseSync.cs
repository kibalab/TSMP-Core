using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.SDKBase;
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public enum VrchatAvatarPoseMode
    {
        None = 0,
        TrackingPoints = 1,
        HumanoidBones = 2
    }

    public class TSMPNetworkVrchatAvatarPoseSync : TSMPNetworkBehaviour
    {
        public VrchatAvatarPoseMode poseMode = VrchatAvatarPoseMode.HumanoidBones;
        public bool includeFingerBones;
        public int maxPlayers = 16;
        public GameObject avatarPrefab;
        public Transform avatarPoolRoot;
        [HideInInspector] public bool compactHumanoidPose = true;
        [HideInInspector] public bool syncDisplayNames;
        [HideInInspector] public int maxDisplayNameBytes;
        [HideInInspector] public int humanoidRecordBudgetPerFrame;
        [HideInInspector] public int humanoidRootPoseRecordCount = 6;
        [HideInInspector] public int rootPoseSampleBudgetPerFrame = 16;
        [HideInInspector] public float rootPositionThreshold = 0.0025f;
        [HideInInspector] public float rootRotationThresholdDegrees = 0.5f;
        [HideInInspector] public int rootPoseKeyframeInterval = 30;
        [HideInInspector] public int playerKeepAliveInterval = 30;
        [HideInInspector] public GameObject[] avatarPool;
        [HideInInspector] public TSMPNetworkVrchatAvatarPoseRig[] avatarRigs;
        [HideInInspector] public float staleAvatarSeconds = 3f;
        [HideInInspector] public int encodedPlayerCount;
        [HideInInspector] public int encodedPoseRecordCount;
        [HideInInspector] public int encodedRootPoseCount;
        [HideInInspector] public int skippedRootPoseCount;
        [HideInInspector] public int sampledRootPoseCount;
        [HideInInspector] public int deferredRootPoseSampleCount;
        [HideInInspector] public int suppressedRootOnlyPoseCount;
        [HideInInspector] public int skippedPlayerEntryCount;
        [HideInInspector] public int keepAlivePlayerEntryCount;
        [HideInInspector] public int encodedPoseBytes;
        [HideInInspector] public int decodedPlayerCount;
        [HideInInspector] public int lastAvatarPoseVersion;
        [HideInInspector] public int lastAvatarPoseError;
        [HideInInspector] public int failedAvatarSlotCount;
        [HideInInspector] public int activeAvatarCount;
        [HideInInspector] public int poolSize;

        [HideInInspector]
#if UDONSHARP || COMPILER_UDONSHARP
        [TransSync("vrchat.avatar_pose")]
        [FieldChangeCallback(nameof(AvatarPoseBytes))]
#else
        [TransSync("vrchat.avatar_pose", Direction = NetworkSyncDirection.ReceiveOnly)]
#endif
        public byte[] avatarPoseBytes;

        private const byte PoseVersionLegacy = 1;
        private const byte PoseVersionCompact = 2;
        private const byte PoseVersionRootDelta = 4;
        private const byte PoseVersionCurrent = 4;
        private const byte PoseFlagIncludeFingerBones = 1 << 0;
        private const byte PoseFlagCompactHumanoid = 1 << 1;
        private const byte PoseFlagHasDisplayNames = 1 << 2;
        private const byte PlayerFlagHasRootPose = 1 << 0;
        private const int HeaderBytes = 6;
        private const int LegacyPlayerHeaderBytes = 3;
        private const int PlayerHeaderBytes = 5;
        private const int PlayerHeaderBytesV3 = 4;
        private const int RootPoseBytes = 17;
        private const int PoseRecordBytes = 18;
        private const int CompactHumanoidRootBytes = 12;
        private const int CompactHumanoidRecordBytes = 6;
        private const int MaxPlayers = 80;
        private const int MaxNameBytes = 64;

        public const int TrackingPointOrigin = 0;
        public const int TrackingPointHead = 1;
        public const int TrackingPointLeftHand = 2;
        public const int TrackingPointRightHand = 3;
        public const int TrackingPointHips = 4;
        public const int TrackingPointLeftFoot = 5;
        public const int TrackingPointRightFoot = 6;

        private int[] _trackingPointIds = new int[]
        {
            TrackingPointHead,
            TrackingPointLeftHand,
            TrackingPointRightHand,
            TrackingPointHips,
            TrackingPointLeftFoot,
            TrackingPointRightFoot
        };

        private int[] _humanoidBoneIds = new int[]
        {
            (int)HumanBodyBones.Hips,
            (int)HumanBodyBones.Head,
            (int)HumanBodyBones.LeftHand,
            (int)HumanBodyBones.RightHand,
            (int)HumanBodyBones.LeftFoot,
            (int)HumanBodyBones.RightFoot,
            (int)HumanBodyBones.Spine,
            (int)HumanBodyBones.Chest,
            (int)HumanBodyBones.UpperChest,
            (int)HumanBodyBones.Neck,
            (int)HumanBodyBones.LeftShoulder,
            (int)HumanBodyBones.LeftUpperArm,
            (int)HumanBodyBones.LeftLowerArm,
            (int)HumanBodyBones.RightShoulder,
            (int)HumanBodyBones.RightUpperArm,
            (int)HumanBodyBones.RightLowerArm,
            (int)HumanBodyBones.LeftUpperLeg,
            (int)HumanBodyBones.LeftLowerLeg,
            (int)HumanBodyBones.LeftToes,
            (int)HumanBodyBones.RightUpperLeg,
            (int)HumanBodyBones.RightLowerLeg,
            (int)HumanBodyBones.RightToes
        };

        private int[] _leftFingerBoneIds = new int[]
        {
            (int)HumanBodyBones.LeftThumbProximal,
            (int)HumanBodyBones.LeftThumbIntermediate,
            (int)HumanBodyBones.LeftThumbDistal,
            (int)HumanBodyBones.LeftIndexProximal,
            (int)HumanBodyBones.LeftIndexIntermediate,
            (int)HumanBodyBones.LeftIndexDistal,
            (int)HumanBodyBones.LeftMiddleProximal,
            (int)HumanBodyBones.LeftMiddleIntermediate,
            (int)HumanBodyBones.LeftMiddleDistal,
            (int)HumanBodyBones.LeftRingProximal,
            (int)HumanBodyBones.LeftRingIntermediate,
            (int)HumanBodyBones.LeftRingDistal,
            (int)HumanBodyBones.LeftLittleProximal,
            (int)HumanBodyBones.LeftLittleIntermediate,
            (int)HumanBodyBones.LeftLittleDistal
        };

        private int[] _rightFingerBoneIds = new int[]
        {
            (int)HumanBodyBones.RightThumbProximal,
            (int)HumanBodyBones.RightThumbIntermediate,
            (int)HumanBodyBones.RightThumbDistal,
            (int)HumanBodyBones.RightIndexProximal,
            (int)HumanBodyBones.RightIndexIntermediate,
            (int)HumanBodyBones.RightIndexDistal,
            (int)HumanBodyBones.RightMiddleProximal,
            (int)HumanBodyBones.RightMiddleIntermediate,
            (int)HumanBodyBones.RightMiddleDistal,
            (int)HumanBodyBones.RightRingProximal,
            (int)HumanBodyBones.RightRingIntermediate,
            (int)HumanBodyBones.RightRingDistal,
            (int)HumanBodyBones.RightLittleProximal,
            (int)HumanBodyBones.RightLittleIntermediate,
            (int)HumanBodyBones.RightLittleDistal
        };

#if UDONSHARP || COMPILER_UDONSHARP
        private VRCPlayerApi[] _players;
#endif
        private int[] _slotPlayerIds;
        private float[] _slotLastSeen;
        private int[] _playerNameByteCounts;
        private int[] _playerRecordCounts;
        private int[] _playerIds;
        private int[] _validPlayerIndices;
        private string[] _playerDisplayNames;
        private Vector3[] _playerRootPositions;
        private Quaternion[] _playerRootRotations;
        private bool[] _playerRootPoseIncluded;
        private bool[] _playerEntryIncluded;
        private int[] _lastRootPosePlayerIds;
        private Vector3[] _lastRootPositions;
        private Quaternion[] _lastRootRotations;
        private int[] _lastRootPoseSequences;
        private int[] _lastPlayerEntryPlayerIds;
        private int[] _lastPlayerEntrySequences;
        private int[] _recordIndexPlayerIds;
        private int[] _recordIndexSlots;
        private int _cachedPlayerCount;
        private bool _playerCacheInitialized;
        private ushort _sequence;
        private float _slotRetireGraceUntil;
        private int _decodedPlayerId;
        private int _decodedRecordCount;
        private int _decodedRecordCursor;
        private int _decodedPlayerBytes;
        private string _decodedPlayerName;
        private bool _decodedHasRootPose;
        private Vector3 _decodedRootPosition;
        private Quaternion _decodedRootRotation;

        public byte[] AvatarPoseBytes
        {
            get => avatarPoseBytes;
            set
            {
                avatarPoseBytes = value;
            }
        }

        private void Start()
        {
            InitializeRuntimeArrays();
#if UDONSHARP || COMPILER_UDONSHARP
            RefreshPlayerCache();
#endif
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public override void PostLateUpdate()
        {
            RetireStaleAvatars();
        }
#else
        private void LateUpdate()
        {
            RetireStaleAvatars();
        }
#endif

#if UDONSHARP || COMPILER_UDONSHARP
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            InitializeRuntimeArrays();
            AddCachedPlayer(player);
            ExtendSlotRetireGrace();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            RemoveCachedPlayer(VrchatAvatarPlayerCache.GetPlayerId(player));
            ExtendSlotRetireGrace();
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            InitializeRuntimeArrays();

            int playerCount = CollectPlayers();
            int totalRecordCount = VrchatAvatarPoseScheduler.GetPoseRecordCount(
                (int)poseMode,
                includeFingerBones,
                _humanoidBoneIds.Length,
                _leftFingerBoneIds.Length,
                _rightFingerBoneIds.Length,
                _trackingPointIds.Length);
            bool compactHumanoid = UseCompactHumanoidPose();
            int recordBytes = compactHumanoid ? CompactHumanoidRecordBytes : PoseRecordBytes;
            ushort sequence = _sequence;

            int validPlayers = CollectValidPlayersForEncode(playerCount, ClampPlayerCount(playerCount), ClampDisplayNameBytes());
            AssignRootPoseSamples(validPlayers, sequence);
            VrchatAvatarPoseScheduler.AssignPoseRecordCounts(
                _playerRecordCounts,
                _validPlayerIndices,
                _playerRootPoseIncluded,
                (int)poseMode,
                totalRecordCount,
                validPlayers,
                sequence,
                humanoidRecordBudgetPerFrame,
                humanoidRootPoseRecordCount);
            int suppressedRootOnly = VrchatAvatarPoseScheduler.SuppressRootOnlyHumanoidEntries(
                _validPlayerIndices,
                _playerRootPoseIncluded,
                _playerRecordCounts,
                (int)poseMode,
                validPlayers,
                totalRecordCount,
                humanoidRootPoseRecordCount);

            int includedPlayers;
            int skippedEntries;
            int keepAliveEntries;
            int requiredBytes = PlanAvatarPoseEntries(validPlayers, recordBytes, sequence, out includedPlayers, out skippedEntries, out keepAliveEntries);

            if (avatarPoseBytes == null || avatarPoseBytes.Length != requiredBytes)
                avatarPoseBytes = new byte[requiredBytes];

            WriteAvatarPoseHeader(includedPlayers, compactHumanoid, sequence);

            int writtenPlayers = 0;
            int writtenRecords = 0;
            int writtenRoots = 0;
            int skippedRoots = 0;
            int cursor = WriteAvatarPoseEntries(validPlayers, totalRecordCount, compactHumanoid, sequence, out writtenPlayers, out writtenRecords, out writtenRoots, out skippedRoots);

            StoreAvatarPoseEncodeCounters(writtenPlayers, writtenRecords, writtenRoots, skippedRoots, suppressedRootOnly, skippedEntries, keepAliveEntries, cursor);
        }

        private int CollectValidPlayersForEncode(int playerCount, int clampedPlayerCount, int displayNameByteLimit)
        {
            int validPlayers = 0;
            for (int i = 0; i < playerCount; i++)
            {
                if (validPlayers >= clampedPlayerCount)
                    break;

                VRCPlayerApi player = _players[i];
                if (!VrchatAvatarPlayerCache.IsEncodablePlayer(player))
                    continue;

                int playerId = VrchatAvatarPlayerCache.GetPlayerId(player);
                string displayName = string.Empty;
                int nameBytes = 0;
                if (syncDisplayNames)
                {
                    displayName = VrchatAvatarPlayerCache.GetPlayerDisplayName(player);
                    nameBytes = Utf8.GetByteCount(displayName, displayNameByteLimit);
                }

                _playerIds[i] = playerId;
                _playerNameByteCounts[i] = nameBytes;
                _playerDisplayNames[i] = displayName;
                _validPlayerIndices[validPlayers] = i;
                validPlayers++;
            }

            return validPlayers;
        }

        private int PlanAvatarPoseEntries(int validPlayers, int recordBytes, ushort sequence, out int includedPlayers, out int skippedEntries, out int keepAliveEntries)
        {
            int requiredBytes = HeaderBytes;
            includedPlayers = 0;
            skippedEntries = 0;
            keepAliveEntries = 0;

            for (int i = 0; i < validPlayers; i++)
            {
                int playerIndex = _validPlayerIndices[i];
                int recordCount = _playerRecordCounts[playerIndex];
                bool hasData = HasPlayerEntryData(playerIndex, recordCount, _playerNameByteCounts[playerIndex]);
                bool keepAlive = false;
                if (!hasData)
                    keepAlive = ShouldSendPlayerKeepAlive(playerIndex, _playerIds[playerIndex], sequence);

                bool includeEntry = hasData;
                if (!includeEntry)
                    includeEntry = keepAlive;

                _playerEntryIncluded[playerIndex] = includeEntry;
                if (!includeEntry)
                {
                    skippedEntries++;
                    continue;
                }

                includedPlayers++;
                if (keepAlive)
                    keepAliveEntries++;
                requiredBytes += PlayerHeaderBytes + _playerNameByteCounts[playerIndex] + recordCount * recordBytes;
                if (_playerRootPoseIncluded[playerIndex])
                    requiredBytes += RootPoseBytes;
            }

            return requiredBytes;
        }

        private void WriteAvatarPoseHeader(int includedPlayers, bool compactHumanoid, ushort sequence)
        {
            byte flags = VrchatAvatarPosePacket.BuildHeaderFlags(includeFingerBones, compactHumanoid, syncDisplayNames);
            VrchatAvatarPosePacket.WriteHeader(avatarPoseBytes, PoseVersionCurrent, (int)poseMode, includedPlayers, flags, sequence);
            _sequence++;
        }

        private int WriteAvatarPoseEntries(int validPlayers, int totalRecordCount, bool compactHumanoid, ushort sequence, out int writtenPlayers, out int writtenRecords, out int writtenRoots, out int skippedRoots)
        {
            int cursor = HeaderBytes;
            writtenPlayers = 0;
            writtenRecords = 0;
            writtenRoots = 0;
            skippedRoots = 0;

            for (int i = 0; i < validPlayers; i++)
            {
                int playerIndex = _validPlayerIndices[i];
                if (!_playerEntryIncluded[playerIndex])
                    continue;

                VRCPlayerApi player = _players[playerIndex];
                int playerId = _playerIds[playerIndex];
                int nameBytes = syncDisplayNames ? _playerNameByteCounts[playerIndex] : 0;
                int recordCount = _playerRecordCounts[playerIndex];
                bool writeRootPose = _playerRootPoseIncluded[playerIndex];
                cursor = VrchatAvatarPosePacket.WritePlayerHeader(avatarPoseBytes, cursor, playerId, recordCount, nameBytes, writeRootPose);

                Vector3 rootPosition = _playerRootPositions[playerIndex];
                Quaternion rootRotation = _playerRootRotations[playerIndex];
                if (writeRootPose)
                {
                    cursor = VrchatAvatarPosePacket.WriteRootPose(avatarPoseBytes, cursor, rootPosition, rootRotation);
                    MarkRootPoseWritten(playerIndex, playerId, rootPosition, rootRotation, sequence);
                    writtenRoots++;
                }
                else
                {
                    skippedRoots++;
                }

                if (nameBytes > 0)
                    cursor = Utf8.WriteString(avatarPoseBytes, cursor, _playerDisplayNames[playerIndex], nameBytes);

                if (poseMode == VrchatAvatarPoseMode.HumanoidBones)
                {
                    Quaternion fallbackRotation = rootRotation;
                    Vector3 fallbackRoot = rootPosition;
                    int recordStart = writeRootPose ? 0 : VrchatAvatarPoseScheduler.GetHumanoidRecordStart(playerId, sequence, totalRecordCount, recordCount);
                    if (compactHumanoid)
                        cursor = WriteCompactHumanoidPoseRecords(player, avatarPoseBytes, cursor, fallbackRotation, recordStart, recordCount, totalRecordCount);
                    else
                        cursor = WriteHumanoidPoseRecords(player, avatarPoseBytes, cursor, fallbackRoot, fallbackRotation, recordStart, recordCount, totalRecordCount);
                }
                else if (poseMode == VrchatAvatarPoseMode.TrackingPoints)
                {
                    cursor = WriteTrackingPointRecords(player, avatarPoseBytes, cursor);
                }

                writtenRecords += recordCount;
                writtenPlayers++;
                MarkPlayerEntryWritten(playerIndex, playerId, sequence);
            }

            return cursor;
        }

        private void StoreAvatarPoseEncodeCounters(int writtenPlayers, int writtenRecords, int writtenRoots, int skippedRoots, int suppressedRootOnly, int skippedEntries, int keepAliveEntries, int cursor)
        {
            encodedPlayerCount = writtenPlayers;
            encodedPoseRecordCount = writtenRecords;
            encodedRootPoseCount = writtenRoots;
            skippedRootPoseCount = skippedRoots;
            suppressedRootOnlyPoseCount = suppressedRootOnly;
            skippedPlayerEntryCount = skippedEntries;
            keepAlivePlayerEntryCount = keepAliveEntries;
            encodedPoseBytes = cursor;
        }

#endif

        private void ApplyAvatarPose()
        {
            if (!IsTSMPActive() || avatarPoseBytes == null || avatarPoseBytes.Length < HeaderBytes)
                return;

            InitializeRuntimeArrays();

            int version = avatarPoseBytes[0];
            lastAvatarPoseVersion = version;
            lastAvatarPoseError = 0;
            failedAvatarSlotCount = 0;
            decodedPlayerCount = 0;
            if (version == 3 || version == PoseVersionCurrent)
            {
                ApplyCurrentAvatarPose();
                return;
            }

            if (version == PoseVersionLegacy || version == PoseVersionCompact)
                ApplyLegacyAvatarPose(version);
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
            {
                ClearAvatarRigPoseState();
                return;
            }

            ApplyAvatarPose();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ApplyCurrentAvatarPose()
        {
            int version = avatarPoseBytes[0];
            int mode = avatarPoseBytes[1];
            int flags = avatarPoseBytes[3];
            bool compactHumanoid = mode == (int)VrchatAvatarPoseMode.HumanoidBones && (flags & PoseFlagCompactHumanoid) != 0;
            bool hasNames = (flags & PoseFlagHasDisplayNames) != 0;
            bool hasPlayerFlags = version >= PoseVersionRootDelta;
            int playerCount = avatarPoseBytes[2];
            if (playerCount < 0 || playerCount > MaxPlayers)
            {
                lastAvatarPoseError = 1;
                return;
            }

            int cursor = HeaderBytes;
            for (int i = 0; i < playerCount; i++)
            {
                if (!TryReadCurrentPlayerPacket(cursor, hasPlayerFlags, hasNames, compactHumanoid))
                    return;

                int slot = EnsureAvatarSlot(_decodedPlayerId, i);
                if (slot < 0)
                {
                    failedAvatarSlotCount++;
                    cursor = _decodedRecordCursor + _decodedPlayerBytes;
                    continue;
                }

                ApplyCurrentPlayerToRig(slot, _decodedPlayerId, _decodedPlayerName, hasNames, mode, _decodedHasRootPose, _decodedRootPosition, _decodedRootRotation, _decodedRecordCount, _decodedRecordCursor, compactHumanoid);

                cursor = _decodedRecordCursor + _decodedPlayerBytes;
                decodedPlayerCount++;
            }
        }

        private bool TryReadCurrentPlayerPacket(int cursor, bool hasPlayerFlags, bool hasNames, bool compactHumanoid)
        {
            int playerHeaderBytes = hasPlayerFlags ? PlayerHeaderBytes : PlayerHeaderBytesV3;
            int nameLength;
            int error;
            if (!VrchatAvatarPosePacket.TryReadPlayerHeader(
                    avatarPoseBytes,
                    cursor,
                    playerHeaderBytes,
                    hasPlayerFlags,
                    out _decodedPlayerId,
                    out _decodedRecordCount,
                    out nameLength,
                    out _decodedHasRootPose,
                    out cursor,
                    out error))
            {
                lastAvatarPoseError = error;
                return false;
            }

            _decodedRootPosition = Vector3.zero;
            _decodedRootRotation = Quaternion.identity;
            if (_decodedHasRootPose)
            {
                if (!VrchatAvatarPosePacket.TryReadRootPose(avatarPoseBytes, cursor, RootPoseBytes, out _decodedRootPosition, out _decodedRootRotation, out cursor))
                {
                    lastAvatarPoseError = 2;
                    return false;
                }
            }

            if (nameLength < 0)
            {
                lastAvatarPoseError = 3;
                return false;
            }

            if (cursor + nameLength > avatarPoseBytes.Length)
            {
                lastAvatarPoseError = 3;
                return false;
            }

            _decodedPlayerName = null;
            if (hasNames)
            {
                if (nameLength > 0)
                    _decodedPlayerName = Utf8.ReadString(avatarPoseBytes, cursor, nameLength);
            }
            cursor += nameLength;

            int recordBytes = compactHumanoid ? CompactHumanoidRecordBytes : PoseRecordBytes;
            _decodedPlayerBytes = _decodedRecordCount * recordBytes;
            if (_decodedRecordCount < 0)
            {
                lastAvatarPoseError = 4;
                return false;
            }

            if (cursor + _decodedPlayerBytes > avatarPoseBytes.Length)
            {
                lastAvatarPoseError = 4;
                return false;
            }

            _decodedRecordCursor = cursor;
            return true;
        }

        private void ApplyCurrentPlayerToRig(int slot, int playerId, string playerName, bool hasNames, int mode, bool hasRootPose, Vector3 rootPosition, Quaternion rootRotation, int recordCount, int cursor, bool compactHumanoid)
        {
            _slotLastSeen[slot] = Time.time;
            TSMPNetworkVrchatAvatarPoseRig rig = GetAvatarRig(slot);
            if (rig == null)
            {
                lastAvatarPoseError = 7;
                return;
            }

            ConfigureRigReceiveMode(rig);
            if (hasNames)
                rig.SetPlayerInfo(playerId, playerName);
            else
                rig.SetPlayerId(playerId);
            rig.SetReceivedPoseMode(mode);
            if (hasRootPose)
                rig.ApplyRootPose(rootPosition, rootRotation);
            ApplyPlayerRecords(rig, mode, recordCount, cursor, compactHumanoid);
        }

        private void ApplyLegacyAvatarPose(int version)
        {
            int mode = avatarPoseBytes[1];
            int flags = avatarPoseBytes[3];
            bool compactHumanoid = version >= PoseVersionCompact && mode == (int)VrchatAvatarPoseMode.HumanoidBones && (flags & PoseFlagCompactHumanoid) != 0;
            int playerCount = avatarPoseBytes[2];
            if (playerCount < 0 || playerCount > MaxPlayers)
            {
                lastAvatarPoseError = 1;
                return;
            }

            int cursor = HeaderBytes;
            for (int i = 0; i < playerCount; i++)
            {
                if (!TryReadLegacyPlayerPacket(cursor, compactHumanoid))
                    return;

                int slot = EnsureAvatarSlot(_decodedPlayerId, i);
                if (slot < 0)
                {
                    failedAvatarSlotCount++;
                    cursor = _decodedRecordCursor + _decodedPlayerBytes;
                    continue;
                }

                ApplyLegacyPlayerToRig(slot, mode, _decodedRecordCount, _decodedRecordCursor, compactHumanoid);
                cursor = _decodedRecordCursor + _decodedPlayerBytes;
                decodedPlayerCount++;
            }
        }

        private bool TryReadLegacyPlayerPacket(int cursor, bool compactHumanoid)
        {
            if (!VrchatAvatarPosePacket.TryReadLegacyPlayerHeader(
                    avatarPoseBytes,
                    cursor,
                    LegacyPlayerHeaderBytes,
                    out _decodedPlayerId,
                    out _decodedRecordCount,
                    out cursor))
            {
                lastAvatarPoseError = 2;
                return false;
            }

            if (_decodedRecordCount < 0)
            {
                lastAvatarPoseError = 4;
                return false;
            }

            if (compactHumanoid)
                _decodedPlayerBytes = CompactHumanoidRootBytes + _decodedRecordCount * CompactHumanoidRecordBytes;
            else
                _decodedPlayerBytes = _decodedRecordCount * PoseRecordBytes;

            if (cursor + _decodedPlayerBytes > avatarPoseBytes.Length)
            {
                lastAvatarPoseError = 4;
                return false;
            }

            _decodedRecordCursor = cursor;
            return true;
        }

        private void ApplyLegacyPlayerToRig(int slot, int mode, int recordCount, int cursor, bool compactHumanoid)
        {
            _slotLastSeen[slot] = Time.time;
            TSMPNetworkVrchatAvatarPoseRig rig = GetAvatarRig(slot);
            if (rig == null)
            {
                lastAvatarPoseError = 7;
                return;
            }

            ConfigureRigReceiveMode(rig);
            rig.SetReceivedPoseMode(mode);
            ApplyLegacyPlayerRecords(rig, mode, recordCount, cursor, compactHumanoid);
        }

#if UDONSHARP || COMPILER_UDONSHARP
        private int WriteTrackingPointRecords(VRCPlayerApi player, byte[] buffer, int cursor)
        {
            for (int i = 0; i < _trackingPointIds.Length; i++)
            {
                int pointId = _trackingPointIds[i];
                buffer[cursor++] = (byte)pointId;
                Vector3 position = GetTrackingPointPosition(player, pointId);
                Quaternion rotation = GetTrackingPointRotation(player, pointId);
                cursor = Binary.WriteVector3Float32LE(buffer, cursor, position);
                Binary.WritePackedQuaternion12LE(buffer, cursor, rotation);
                cursor += 5;
            }

            return cursor;
        }

#endif

        private void ConfigureRigReceiveMode(TSMPNetworkVrchatAvatarPoseRig rig)
        {
            if (rig == null)
                return;

            rig.interpolateReceivedPose = receiveInterpolation == ReceiveInterpolationMode.Continuous;
            rig.receiveInterpolationRate = continuousInterpolationRate;
        }

        private void ClearAvatarRigPoseState()
        {
            if (avatarRigs == null)
                return;

            for (int i = 0; i < avatarRigs.Length; i++)
            {
                TSMPNetworkVrchatAvatarPoseRig rig = avatarRigs[i];
                if (rig != null)
                {
                    rig.interpolateReceivedPose = false;
                    rig.ClearReceivedPoseState();
                }
            }
        }

#if UDONSHARP || COMPILER_UDONSHARP
        private int WriteHumanoidPoseRecords(VRCPlayerApi player, byte[] buffer, int cursor, Vector3 fallbackRoot, Quaternion fallbackRotation, int start, int count, int total)
        {
            for (int i = 0; i < count; i++)
            {
                int recordIndex = start + i;
                if (recordIndex >= total)
                    recordIndex -= total;

                int boneId = VrchatAvatarPoseScheduler.GetHumanoidRecordBoneId(
                    recordIndex,
                    _humanoidBoneIds,
                    includeFingerBones,
                    _leftFingerBoneIds,
                    _rightFingerBoneIds,
                    (int)HumanBodyBones.Hips);
                cursor = WriteHumanoidPoseRecord(player, boneId, buffer, cursor, fallbackRoot, fallbackRotation);
            }

            return cursor;
        }

        private int WriteCompactHumanoidPoseRecords(VRCPlayerApi player, byte[] buffer, int cursor, Quaternion fallbackRotation, int start, int count, int total)
        {
            for (int i = 0; i < count; i++)
            {
                int recordIndex = start + i;
                if (recordIndex >= total)
                    recordIndex -= total;

                int boneId = VrchatAvatarPoseScheduler.GetHumanoidRecordBoneId(
                    recordIndex,
                    _humanoidBoneIds,
                    includeFingerBones,
                    _leftFingerBoneIds,
                    _rightFingerBoneIds,
                    (int)HumanBodyBones.Hips);
                cursor = WriteCompactHumanoidPoseRecord(player, boneId, buffer, cursor, fallbackRotation);
            }

            return cursor;
        }

        private int WriteHumanoidPoseRecord(VRCPlayerApi player, int boneId, byte[] buffer, int cursor, Vector3 fallbackRoot, Quaternion fallbackRotation)
        {
            buffer[cursor++] = (byte)boneId;
            Vector3 position = GetBonePositionWithFallback(player, boneId, fallbackRoot);
            Quaternion rotation = GetBoneRotationWithFallback(player, boneId, fallbackRotation);
            cursor = Binary.WriteVector3Float32LE(buffer, cursor, position);
            Binary.WritePackedQuaternion12LE(buffer, cursor, rotation);
            return cursor + 5;
        }

        private int WriteCompactHumanoidPoseRecord(VRCPlayerApi player, int boneId, byte[] buffer, int cursor, Quaternion fallbackRotation)
        {
            buffer[cursor++] = (byte)boneId;
            Quaternion rotation = GetBoneRotationWithFallback(player, boneId, fallbackRotation);
            Binary.WritePackedQuaternion12LE(buffer, cursor, rotation);
            return cursor + 5;
        }

#endif

        private void ApplyPlayerRecords(TSMPNetworkVrchatAvatarPoseRig rig, int mode, int recordCount, int cursor, bool compactHumanoid)
        {
            if (rig == null)
                return;

            if (compactHumanoid && mode == (int)VrchatAvatarPoseMode.HumanoidBones)
            {
                rig.ApplyCompactHumanoidBoneRotations(avatarPoseBytes, cursor, recordCount);
                return;
            }

            for (int i = 0; i < recordCount; i++)
            {
                if (compactHumanoid)
                {
                    if (cursor + CompactHumanoidRecordBytes > avatarPoseBytes.Length)
                        return;

                    int id = avatarPoseBytes[cursor++];
                    Quaternion rotation = Binary.ReadPackedQuaternion12LE(avatarPoseBytes, cursor);
                    cursor += 5;
                    rig.ApplyHumanoidBoneRotation(id, rotation);
                }
                else
                {
                    if (cursor + PoseRecordBytes > avatarPoseBytes.Length)
                        return;

                    int id = avatarPoseBytes[cursor++];
                    Vector3 position = Binary.ReadVector3Float32LE(avatarPoseBytes, cursor);
                    cursor += 12;
                    Quaternion rotation = Binary.ReadPackedQuaternion12LE(avatarPoseBytes, cursor);
                    cursor += 5;

                    if (mode == (int)VrchatAvatarPoseMode.HumanoidBones)
                        rig.ApplyHumanoidBone(id, position, rotation);
                    else if (mode == (int)VrchatAvatarPoseMode.TrackingPoints)
                        rig.ApplyTrackingPoint(id, position, rotation);
                }
            }
        }

        private void ApplyLegacyPlayerRecords(TSMPNetworkVrchatAvatarPoseRig rig, int mode, int recordCount, int cursor, bool compactHumanoid)
        {
            if (rig == null)
                return;

            if (compactHumanoid)
            {
                if (cursor + CompactHumanoidRootBytes > avatarPoseBytes.Length)
                    return;

                Vector3 rootPosition = Binary.ReadVector3Float32LE(avatarPoseBytes, cursor);
                cursor += CompactHumanoidRootBytes;
                rig.ApplyHumanoidRoot(rootPosition);
                rig.ApplyCompactHumanoidBoneRotations(avatarPoseBytes, cursor, recordCount);

                return;
            }

            ApplyPlayerRecords(rig, mode, recordCount, cursor, false);
        }

#if UDONSHARP || COMPILER_UDONSHARP
        private bool UseCompactHumanoidPose()
        {
            return compactHumanoidPose && poseMode == VrchatAvatarPoseMode.HumanoidBones;
        }

        private bool HasPlayerEntryData(int playerIndex, int recordCount, int nameBytes)
        {
            if (playerIndex >= 0 && _playerRootPoseIncluded != null && playerIndex < _playerRootPoseIncluded.Length && _playerRootPoseIncluded[playerIndex])
                return true;

            return recordCount > 0 || nameBytes > 0;
        }

        private bool ShouldSendPlayerKeepAlive(int playerIndex, int playerId, int sequence)
        {
            if (_lastPlayerEntryPlayerIds == null || _lastPlayerEntrySequences == null)
                return true;

            if (playerIndex < 0 || playerIndex >= _lastPlayerEntryPlayerIds.Length)
                return true;

            if (_lastPlayerEntryPlayerIds[playerIndex] != playerId)
                return true;

            return VrchatAvatarPoseScheduler.ShouldSendKeyframe(
                _lastPlayerEntryPlayerIds[playerIndex],
                _lastPlayerEntrySequences[playerIndex],
                playerId,
                sequence,
                playerKeepAliveInterval);
        }

        private void MarkPlayerEntryWritten(int playerIndex, int playerId, int sequence)
        {
            if (_lastPlayerEntryPlayerIds == null || _lastPlayerEntrySequences == null)
                return;

            if (playerIndex < 0 || playerIndex >= _lastPlayerEntryPlayerIds.Length)
                return;

            _lastPlayerEntryPlayerIds[playerIndex] = playerId;
            _lastPlayerEntrySequences[playerIndex] = sequence;
        }

        private void AssignRootPoseSamples(int playerCount, int sequence)
        {
            sampledRootPoseCount = 0;
            deferredRootPoseSampleCount = 0;

            if (_validPlayerIndices == null || _playerRootPoseIncluded == null)
                return;

            for (int i = 0; i < playerCount; i++)
            {
                int playerIndex = _validPlayerIndices[i];
                _playerRootPoseIncluded[playerIndex] = false;
            }

            if (playerCount <= 0)
                return;

            int budget = rootPoseSampleBudgetPerFrame;
            if (budget < 1)
                budget = 1;

            int sampled = 0;
            for (int i = 0; i < playerCount; i++)
            {
                int playerIndex = _validPlayerIndices[i];
                int playerId = _playerIds[playerIndex];
                if (HasRootPoseCache(playerIndex, playerId))
                    continue;

                SampleRootPose(playerIndex, playerId, sequence);
                sampled++;
            }

            int remaining = budget - sampled;
            if (remaining < 0)
                remaining = 0;

            int start = sequence % playerCount;
            for (int i = 0; i < playerCount && remaining > 0; i++)
            {
                int order = start + i;
                if (order >= playerCount)
                    order -= playerCount;

                int playerIndex = _validPlayerIndices[order];
                int playerId = _playerIds[playerIndex];
                if (!HasRootPoseCache(playerIndex, playerId))
                    continue;

                SampleRootPose(playerIndex, playerId, sequence);
                sampled++;
                remaining--;
            }

            for (int i = 0; i < playerCount; i++)
            {
                int playerIndex = _validPlayerIndices[i];
                if (_playerRootPoseIncluded[playerIndex])
                    continue;

                int playerId = _playerIds[playerIndex];
                if (HasRootPoseCache(playerIndex, playerId))
                    UseCachedRootPose(playerIndex);
                else
                    deferredRootPoseSampleCount++;
            }

            sampledRootPoseCount = sampled;
        }

        private void SampleRootPose(int playerIndex, int playerId, int sequence)
        {
            if (_players == null || playerIndex < 0 || playerIndex >= _players.Length)
                return;

            Vector3 rootPosition;
            Quaternion rootRotation;
            GetPlayerRootPose(_players[playerIndex], out rootPosition, out rootRotation);
            _playerRootPositions[playerIndex] = rootPosition;
            _playerRootRotations[playerIndex] = rootRotation;
            _playerRootPoseIncluded[playerIndex] = ShouldWriteRootPose(playerIndex, playerId, rootPosition, rootRotation, sequence);
        }

        private bool HasRootPoseCache(int playerIndex, int playerId)
        {
            if (_lastRootPosePlayerIds == null || _lastRootPoseSequences == null)
                return false;

            if (playerIndex < 0 || playerIndex >= _lastRootPosePlayerIds.Length)
                return false;

            return _lastRootPosePlayerIds[playerIndex] == playerId && _lastRootPoseSequences[playerIndex] >= 0;
        }

        private void UseCachedRootPose(int playerIndex)
        {
            if (_lastRootPositions == null || _lastRootRotations == null)
                return;

            if (playerIndex < 0 || playerIndex >= _lastRootPositions.Length)
                return;

            _playerRootPositions[playerIndex] = _lastRootPositions[playerIndex];
            _playerRootRotations[playerIndex] = _lastRootRotations[playerIndex];
        }

        private static void GetPlayerRootPose(VRCPlayerApi player, out Vector3 position, out Quaternion rotation)
        {
            if (!Utilities.IsValid(player))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }

            VRCPlayerApi.TrackingData origin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            position = origin.position;
            rotation = origin.rotation;
        }

        private bool ShouldWriteRootPose(int playerIndex, int playerId, Vector3 position, Quaternion rotation, int sequence)
        {
            if (_lastRootPosePlayerIds == null || _lastRootPositions == null || _lastRootRotations == null || _lastRootPoseSequences == null)
                return true;

            if (playerIndex < 0 || playerIndex >= _lastRootPosePlayerIds.Length)
                return true;

            return VrchatAvatarPoseScheduler.ShouldWriteRootPose(
                _lastRootPosePlayerIds[playerIndex],
                _lastRootPoseSequences[playerIndex],
                playerId,
                sequence,
                rootPoseKeyframeInterval,
                _lastRootPositions[playerIndex],
                _lastRootRotations[playerIndex],
                position,
                rotation,
                rootPositionThreshold,
                rootRotationThresholdDegrees);
        }

        private void MarkRootPoseWritten(int playerIndex, int playerId, Vector3 position, Quaternion rotation, int sequence)
        {
            if (_lastRootPosePlayerIds == null || _lastRootPositions == null || _lastRootRotations == null || _lastRootPoseSequences == null)
                return;

            if (playerIndex < 0 || playerIndex >= _lastRootPosePlayerIds.Length)
                return;

            _lastRootPosePlayerIds[playerIndex] = playerId;
            _lastRootPositions[playerIndex] = position;
            _lastRootRotations[playerIndex] = rotation;
            _lastRootPoseSequences[playerIndex] = sequence;
        }

        private Vector3 GetTrackingPointPosition(VRCPlayerApi player, int pointId)
        {
            if (!Utilities.IsValid(player))
                return Vector3.zero;

            if (pointId == TrackingPointOrigin)
                return player.GetPosition();
            if (pointId == TrackingPointHead)
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (pointId == TrackingPointLeftHand)
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            if (pointId == TrackingPointRightHand)
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            if (pointId == TrackingPointHips)
                return GetBonePositionWithFallback(player, (int)HumanBodyBones.Hips, player.GetPosition());
            if (pointId == TrackingPointLeftFoot)
                return GetBonePositionWithFallback(player, (int)HumanBodyBones.LeftFoot, player.GetPosition());
            if (pointId == TrackingPointRightFoot)
                return GetBonePositionWithFallback(player, (int)HumanBodyBones.RightFoot, player.GetPosition());

            return player.GetPosition();
        }

        private Quaternion GetTrackingPointRotation(VRCPlayerApi player, int pointId)
        {
            if (!Utilities.IsValid(player))
                return Quaternion.identity;

            Quaternion originRotation = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin).rotation;
            if (pointId == TrackingPointOrigin)
                return originRotation;
            if (pointId == TrackingPointHead)
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
            if (pointId == TrackingPointLeftHand)
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation;
            if (pointId == TrackingPointRightHand)
                return player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation;
            if (pointId == TrackingPointHips)
                return GetBoneRotationWithFallback(player, (int)HumanBodyBones.Hips, originRotation);
            if (pointId == TrackingPointLeftFoot)
                return GetBoneRotationWithFallback(player, (int)HumanBodyBones.LeftFoot, originRotation);
            if (pointId == TrackingPointRightFoot)
                return GetBoneRotationWithFallback(player, (int)HumanBodyBones.RightFoot, originRotation);

            return originRotation;
        }

        private Vector3 GetBonePositionWithFallback(VRCPlayerApi player, int boneId, Vector3 fallbackRoot)
        {
            if (!Utilities.IsValid(player))
                return fallbackRoot;

            Vector3 position = player.GetBonePosition((HumanBodyBones)boneId);
            if (position != Vector3.zero)
                return position;

            float height = GetFallbackHeight(boneId);
            return fallbackRoot + Vector3.up * height;
        }

        private Quaternion GetBoneRotationWithFallback(VRCPlayerApi player, int boneId, Quaternion fallbackRotation)
        {
            if (!Utilities.IsValid(player))
                return fallbackRotation;

            Quaternion rotation = player.GetBoneRotation((HumanBodyBones)boneId);
            if (rotation.x != 0f || rotation.y != 0f || rotation.z != 0f || rotation.w != 0f)
                return rotation;

            return fallbackRotation;
        }

        private static float GetFallbackHeight(int boneId)
        {
            if (boneId == (int)HumanBodyBones.Head)
                return 1.7f;
            if (boneId == (int)HumanBodyBones.Neck)
                return 1.55f;
            if (boneId == (int)HumanBodyBones.Chest || boneId == (int)HumanBodyBones.UpperChest)
                return 1.35f;
            if (boneId == (int)HumanBodyBones.Hips)
                return 1.0f;
            if (boneId == (int)HumanBodyBones.LeftFoot || boneId == (int)HumanBodyBones.RightFoot || boneId == (int)HumanBodyBones.LeftToes || boneId == (int)HumanBodyBones.RightToes)
                return 0.05f;
            if (boneId == (int)HumanBodyBones.LeftUpperLeg || boneId == (int)HumanBodyBones.RightUpperLeg)
                return 0.7f;
            if (boneId == (int)HumanBodyBones.LeftLowerLeg || boneId == (int)HumanBodyBones.RightLowerLeg)
                return 0.35f;
            if (boneId == (int)HumanBodyBones.LeftHand || boneId == (int)HumanBodyBones.RightHand)
                return 1.1f;
            if (HumanoidBoneUtil.IsFingerBoneId(boneId))
                return 1.1f;

            return 1.2f;
        }

#endif

        private void InitializeRuntimeArrays()
        {
            compactHumanoidPose = true;
            syncDisplayNames = false;
            maxDisplayNameBytes = 0;
            humanoidRecordBudgetPerFrame = 0;
            humanoidRootPoseRecordCount = 6;
            rootPoseSampleBudgetPerFrame = 16;
            rootPositionThreshold = 0.0025f;
            rootRotationThresholdDegrees = 0.5f;
            rootPoseKeyframeInterval = 30;
            playerKeepAliveInterval = 30;
            staleAvatarSeconds = 3f;

            int capacity = maxPlayers;
            if (capacity < 1)
                capacity = 1;
            if (capacity > MaxPlayers)
                capacity = MaxPlayers;
            maxPlayers = capacity;

            if (maxDisplayNameBytes < 0)
                maxDisplayNameBytes = 0;
            if (maxDisplayNameBytes > MaxNameBytes)
                maxDisplayNameBytes = MaxNameBytes;
            if (humanoidRecordBudgetPerFrame < 0)
                humanoidRecordBudgetPerFrame = 0;
            if (humanoidRootPoseRecordCount < 1)
                humanoidRootPoseRecordCount = 1;
            if (rootPoseSampleBudgetPerFrame < 1)
                rootPoseSampleBudgetPerFrame = 1;
            if (rootPositionThreshold < 0f)
                rootPositionThreshold = 0f;
            if (rootRotationThresholdDegrees < 0f)
                rootRotationThresholdDegrees = 0f;
            if (rootPoseKeyframeInterval < 1)
                rootPoseKeyframeInterval = 1;
            if (playerKeepAliveInterval < 1)
                playerKeepAliveInterval = 1;

#if UDONSHARP || COMPILER_UDONSHARP
            if (_players == null || _players.Length != MaxPlayers)
            {
                _players = new VRCPlayerApi[MaxPlayers];
                _cachedPlayerCount = 0;
                _playerCacheInitialized = false;
            }
#endif
            if (_playerNameByteCounts == null || _playerNameByteCounts.Length != MaxPlayers)
                _playerNameByteCounts = new int[MaxPlayers];
            if (_playerRecordCounts == null || _playerRecordCounts.Length != MaxPlayers)
                _playerRecordCounts = new int[MaxPlayers];
            if (_playerIds == null || _playerIds.Length != MaxPlayers)
                _playerIds = new int[MaxPlayers];
            if (_validPlayerIndices == null || _validPlayerIndices.Length != MaxPlayers)
                _validPlayerIndices = new int[MaxPlayers];
            if (_playerDisplayNames == null || _playerDisplayNames.Length != MaxPlayers)
                _playerDisplayNames = new string[MaxPlayers];
            if (_playerRootPositions == null || _playerRootPositions.Length != MaxPlayers)
                _playerRootPositions = new Vector3[MaxPlayers];
            if (_playerRootRotations == null || _playerRootRotations.Length != MaxPlayers)
                _playerRootRotations = new Quaternion[MaxPlayers];
            if (_playerRootPoseIncluded == null || _playerRootPoseIncluded.Length != MaxPlayers)
                _playerRootPoseIncluded = new bool[MaxPlayers];
            if (_playerEntryIncluded == null || _playerEntryIncluded.Length != MaxPlayers)
                _playerEntryIncluded = new bool[MaxPlayers];
            if (_lastRootPosePlayerIds == null || _lastRootPosePlayerIds.Length != MaxPlayers)
                _lastRootPosePlayerIds = ArrayUtil.CreateFilledIntArray(MaxPlayers, -1);
            if (_lastRootPositions == null || _lastRootPositions.Length != MaxPlayers)
                _lastRootPositions = new Vector3[MaxPlayers];
            if (_lastRootRotations == null || _lastRootRotations.Length != MaxPlayers)
                _lastRootRotations = new Quaternion[MaxPlayers];
            if (_lastRootPoseSequences == null || _lastRootPoseSequences.Length != MaxPlayers)
                _lastRootPoseSequences = ArrayUtil.CreateFilledIntArray(MaxPlayers, -1);
            if (_lastPlayerEntryPlayerIds == null || _lastPlayerEntryPlayerIds.Length != MaxPlayers)
                _lastPlayerEntryPlayerIds = ArrayUtil.CreateFilledIntArray(MaxPlayers, -1);
            if (_lastPlayerEntrySequences == null || _lastPlayerEntrySequences.Length != MaxPlayers)
                _lastPlayerEntrySequences = ArrayUtil.CreateFilledIntArray(MaxPlayers, -1);
            if (_recordIndexPlayerIds == null || _recordIndexPlayerIds.Length != MaxPlayers)
                _recordIndexPlayerIds = ArrayUtil.CreateFilledIntArray(MaxPlayers, -1);
            if (_recordIndexSlots == null || _recordIndexSlots.Length != MaxPlayers)
                _recordIndexSlots = ArrayUtil.CreateFilledIntArray(MaxPlayers, -1);
            if (_slotPlayerIds == null || _slotPlayerIds.Length != capacity)
            {
                _slotPlayerIds = ArrayUtil.ResizeIntArray(_slotPlayerIds, capacity, -1);
                InvalidateSlotCache();
            }
            if (_slotLastSeen == null || _slotLastSeen.Length != capacity)
                _slotLastSeen = ArrayUtil.ResizeFloatArray(_slotLastSeen, capacity);
            if (avatarPool == null || avatarPool.Length != capacity)
                avatarPool = VrchatAvatarPool.ResizeGameObjectArray(avatarPool, capacity);
            if (avatarRigs == null || avatarRigs.Length != capacity)
                avatarRigs = VrchatAvatarPool.ResizeRigArray(avatarRigs, capacity);

            RecountPoolSize();
        }

#if UDONSHARP || COMPILER_UDONSHARP
        private int CollectPlayers()
        {
            if (!_playerCacheInitialized)
                RefreshPlayerCache();

            CompactCachedPlayers();
            return _cachedPlayerCount;
        }

        private void RefreshPlayerCache()
        {
            if (_players == null || _players.Length != MaxPlayers)
                _players = new VRCPlayerApi[MaxPlayers];

            VrchatAvatarPlayerCache.Clear(_players);

            VRCPlayerApi.GetPlayers(_players);
            _cachedPlayerCount = VrchatAvatarPlayerCache.GetClampedVrchatPlayerCount(MaxPlayers);
            VrchatAvatarPlayerCache.SortById(_players, _cachedPlayerCount);
            CompactCachedPlayers();
            InvalidatePlayerMembershipCaches();
            _playerCacheInitialized = true;
        }

        private void AddCachedPlayer(VRCPlayerApi player)
        {
            if (!VrchatAvatarPlayerCache.IsEncodablePlayer(player))
                return;

            if (!_playerCacheInitialized)
            {
                RefreshPlayerCache();
                return;
            }

            int playerId = VrchatAvatarPlayerCache.GetPlayerId(player);
            int existingIndex = VrchatAvatarPlayerCache.FindIndex(_players, _cachedPlayerCount, playerId);
            if (existingIndex >= 0)
            {
                _players[existingIndex] = player;
                return;
            }

            if (_cachedPlayerCount >= MaxPlayers)
                return;

            int insertIndex = VrchatAvatarPlayerCache.FindInsertIndex(_players, _cachedPlayerCount, playerId);
            _cachedPlayerCount = VrchatAvatarPlayerCache.Insert(_players, _cachedPlayerCount, insertIndex, player, MaxPlayers);
            InvalidatePlayerMembershipCaches();
        }

        private void RemoveCachedPlayer(int playerId)
        {
            if (!_playerCacheInitialized || playerId < 0 || _players == null)
                return;

            int index = VrchatAvatarPlayerCache.FindIndex(_players, _cachedPlayerCount, playerId);
            if (index < 0)
                return;

            _cachedPlayerCount = VrchatAvatarPlayerCache.RemoveAt(_players, _cachedPlayerCount, index);
            InvalidatePlayerMembershipCaches();
        }

        private void CompactCachedPlayers()
        {
            int previousCount = _cachedPlayerCount;
            _cachedPlayerCount = VrchatAvatarPlayerCache.Compact(_players, _cachedPlayerCount);
            if (_cachedPlayerCount != previousCount)
                InvalidatePlayerMembershipCaches();
        }

#endif

        private int ClampPlayerCount(int playerCount)
        {
            int count = playerCount;
            if (count > maxPlayers)
                count = maxPlayers;
            if (count > 255)
                count = 255;
            if (count < 0)
                count = 0;

            return count;
        }

        private int EnsureAvatarSlot(int playerId, int recordIndex)
        {
            if (playerId < 0)
                return -1;

            int cachedSlot = ResolveCachedAvatarSlot(playerId, recordIndex);
            if (cachedSlot >= 0)
                return cachedSlot;

            int existingSlot = FindAvatarSlotByPlayerId(playerId);
            if (existingSlot >= 0)
                return ActivateAvatarSlot(recordIndex, playerId, existingSlot);

            return AllocateAvatarSlot(recordIndex, playerId);
        }

        private int ResolveCachedAvatarSlot(int playerId, int recordIndex)
        {
            int cachedSlot = VrchatAvatarPool.ResolveCachedSlot(_recordIndexPlayerIds, _recordIndexSlots, _slotPlayerIds, recordIndex, playerId, maxPlayers);
            if (cachedSlot < 0)
                return -1;

            SetAvatarSlotActive(cachedSlot, true);
            return cachedSlot;
        }

        private int FindAvatarSlotByPlayerId(int playerId)
        {
            return VrchatAvatarPool.FindSlotByPlayerId(_slotPlayerIds, maxPlayers, playerId);
        }

        private int AllocateAvatarSlot(int recordIndex, int playerId)
        {
            for (int i = 0; i < maxPlayers; i++)
            {
                if (_slotPlayerIds[i] >= 0)
                    continue;

                if (!EnsurePoolObject(i))
                    return -1;

                _slotPlayerIds[i] = playerId;
                return ActivateAvatarSlot(recordIndex, playerId, i);
            }

            return -1;
        }

        private int ActivateAvatarSlot(int recordIndex, int playerId, int slot)
        {
            CacheSlot(recordIndex, playerId, slot);
            SetAvatarSlotActive(slot, true);
            return slot;
        }

        private bool IsAvatarSlotAssignedToPlayer(int slot, int playerId)
        {
            return VrchatAvatarPool.IsSlotAssignedToPlayer(_slotPlayerIds, slot, playerId, maxPlayers);
        }

        private void CacheSlot(int recordIndex, int playerId, int slot)
        {
            VrchatAvatarPool.CacheSlot(_recordIndexPlayerIds, _recordIndexSlots, recordIndex, playerId, slot);
        }

        private void InvalidateSlotCache()
        {
            VrchatAvatarPool.InvalidateSlotCache(_recordIndexPlayerIds, _recordIndexSlots);
        }

        private void InvalidatePlayerMembershipCaches()
        {
            InvalidateRootPoseCache();
            InvalidatePlayerEntryCache();
        }

        private void InvalidateRootPoseCache()
        {
            VrchatAvatarPool.InvalidatePlayerSequenceCache(_lastRootPosePlayerIds, _lastRootPoseSequences);
        }

        private void InvalidatePlayerEntryCache()
        {
            VrchatAvatarPool.InvalidatePlayerSequenceCache(_lastPlayerEntryPlayerIds, _lastPlayerEntrySequences);
        }

        private bool EnsurePoolObject(int slot)
        {
            if (slot < 0)
                return false;

            if (slot >= maxPlayers)
                return false;

            if (UseExistingPoolObject(slot))
                return true;

            if (avatarPrefab == null)
            {
                lastAvatarPoseError = 5;
                return false;
            }

            return CreatePoolObject(slot);
        }

        private bool UseExistingPoolObject(int slot)
        {
            if (avatarPool == null)
                return false;

            if (slot >= avatarPool.Length)
                return false;

            GameObject avatar = avatarPool[slot];
            if (avatar == null)
                return false;

            StoreAvatarRig(slot, ResolveAvatarRig(avatar));
            return true;
        }

        private bool CreatePoolObject(int slot)
        {
            GameObject spawned = InstantiateAvatarPrefab();
            if (spawned == null)
            {
                lastAvatarPoseError = 6;
                return false;
            }

            if (avatarPoolRoot != null)
                spawned.transform.SetParent(avatarPoolRoot);

            if (avatarPool != null)
            {
                if (slot < avatarPool.Length)
                    avatarPool[slot] = spawned;
            }

            StoreAvatarRig(slot, ResolveAvatarRig(spawned));

            spawned.SetActive(false);
            RecountPoolSize();
            return true;
        }

        private GameObject InstantiateAvatarPrefab()
        {
            return Instantiate(avatarPrefab);
        }

        private TSMPNetworkVrchatAvatarPoseRig GetAvatarRig(int slot)
        {
            if (slot < 0)
                return null;

            if (slot >= maxPlayers)
                return null;

            if (avatarRigs != null)
            {
                if (slot < avatarRigs.Length)
                {
                    if (avatarRigs[slot] != null)
                        return avatarRigs[slot];
                }
            }

            if (avatarPool == null)
                return null;

            if (slot >= avatarPool.Length)
                return null;

            if (avatarPool[slot] == null)
                return null;

            TSMPNetworkVrchatAvatarPoseRig rig = ResolveAvatarRig(avatarPool[slot]);
            StoreAvatarRig(slot, rig);
            return rig;
        }

        private TSMPNetworkVrchatAvatarPoseRig ResolveAvatarRig(GameObject avatar)
        {
            if (avatar == null)
                return null;

            TSMPNetworkVrchatAvatarPoseRig rig = avatar.GetComponent<TSMPNetworkVrchatAvatarPoseRig>();
#if !COMPILER_UDONSHARP
            if (rig == null)
                rig = avatar.GetComponentInChildren<TSMPNetworkVrchatAvatarPoseRig>();
#endif
            return rig;
        }

        private void StoreAvatarRig(int slot, TSMPNetworkVrchatAvatarPoseRig rig)
        {
            if (avatarRigs == null)
                return;

            if (slot < 0)
                return;

            if (slot >= avatarRigs.Length)
                return;

            avatarRigs[slot] = rig;
        }

        private void SetAvatarSlotActive(int slot, bool active)
        {
            if (avatarPool == null)
                return;

            if (slot < 0)
                return;

            if (slot >= avatarPool.Length)
                return;

            if (avatarPool[slot] == null)
                return;

            if (avatarPool[slot].activeSelf != active)
                avatarPool[slot].SetActive(active);

            RecountActiveAvatars();
        }

        private void RetireStaleAvatars()
        {
            if (_slotPlayerIds == null || _slotLastSeen == null || staleAvatarSeconds <= 0f)
                return;

            float now = Time.time;
            if (now < _slotRetireGraceUntil)
                return;

            for (int i = 0; i < _slotPlayerIds.Length; i++)
            {
                if (VrchatAvatarPool.ShouldRetireSlot(_slotPlayerIds, _slotLastSeen, i, now, staleAvatarSeconds))
                {
                    _slotPlayerIds[i] = -1;
                    _slotLastSeen[i] = 0f;
                    InvalidateSlotCache();
                    SetAvatarSlotActive(i, false);
                }
            }
        }

        private void RecountActiveAvatars()
        {
            int count = 0;
            if (avatarPool != null)
            {
                for (int i = 0; i < avatarPool.Length; i++)
                {
                    if (avatarPool[i] != null && avatarPool[i].activeSelf)
                        count++;
                }
            }

            activeAvatarCount = count;
        }

        private void RecountPoolSize()
        {
            int count = 0;
            if (avatarPool != null)
            {
                for (int i = 0; i < avatarPool.Length; i++)
                {
                    if (avatarPool[i] != null)
                        count++;
                }
            }

            poolSize = count;
        }

        private void ExtendSlotRetireGrace()
        {
            float grace = staleAvatarSeconds;
            if (grace < 5f)
                grace = 5f;

            _slotRetireGraceUntil = Time.time + grace;
        }

        private int ClampDisplayNameBytes()
        {
            int value = maxDisplayNameBytes;
            if (value < 0)
                value = 0;
            if (value > MaxNameBytes)
                value = MaxNameBytes;
            return value;
        }

    }

}
