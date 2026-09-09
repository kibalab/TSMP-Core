using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public enum HumanoidRootSyncSpace
    {
        Local = 0,
        World = 1
    }

    public class TSMPNetworkHumanoidPoseSync : TSMPNetworkBehaviour
    {
        public Animator animator;

        [HideInInspector] public int[] boneIds = new int[]
        {
            (int)HumanBodyBones.Hips,
            (int)HumanBodyBones.Spine,
            (int)HumanBodyBones.Chest,
            (int)HumanBodyBones.UpperChest,
            (int)HumanBodyBones.Neck,
            (int)HumanBodyBones.Head,
            (int)HumanBodyBones.LeftShoulder,
            (int)HumanBodyBones.LeftUpperArm,
            (int)HumanBodyBones.LeftLowerArm,
            (int)HumanBodyBones.LeftHand,
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
            (int)HumanBodyBones.LeftLittleDistal,
            (int)HumanBodyBones.RightShoulder,
            (int)HumanBodyBones.RightUpperArm,
            (int)HumanBodyBones.RightLowerArm,
            (int)HumanBodyBones.RightHand,
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
            (int)HumanBodyBones.RightLittleDistal,
            (int)HumanBodyBones.LeftUpperLeg,
            (int)HumanBodyBones.LeftLowerLeg,
            (int)HumanBodyBones.LeftFoot,
            (int)HumanBodyBones.LeftToes,
            (int)HumanBodyBones.RightUpperLeg,
            (int)HumanBodyBones.RightLowerLeg,
            (int)HumanBodyBones.RightFoot,
            (int)HumanBodyBones.RightToes
        };

        [HideInInspector] public Transform[] boneTargets;
        public bool autoResolveBones = true;
        public HumanoidRootSyncSpace rootSyncSpace = HumanoidRootSyncSpace.Local;
        public bool includeFingerBones = true;
        [HideInInspector] public int validBoneCount;
        [HideInInspector] public int encodedPoseBytes;
        [HideInInspector] public int encodedRootMotionBytes;

        [HideInInspector]
        [TransSync("humanoid.pose")]
#if UDONSHARP
        [FieldChangeCallback(nameof(PoseBytes))]
#endif
        public byte[] poseBytes;

        private const byte PoseVersion = 4;
        private const int PoseHeaderBytes = 4;
        private const int PoseBoneBytes = 6;
        private const int PoseRootMotionPositionBytes = 12;
        private const int PoseRootMotionRotationBytes = 5;
        private const int PoseRootMotionBytes = PoseRootMotionPositionBytes + PoseRootMotionRotationBytes;
        private const byte PoseFlagLocalRotations = 1 << 0;
        private const byte PoseFlagHasRootMotionPosition = 1 << 1;
        private const byte PoseFlagAnimatorRootSpaceRotations = 1 << 2;
        private const byte PoseFlagRootMotionWorldSpace = 1 << 3;

        private Transform[] _boneTargetsById;
        private bool[] _boneTargetResolvedById;
        private bool[] _boneTargetResolvedByIndex;
        private bool[] _hasBoneId;
        private bool[] _fingerAllowedById;
        private int[] _activeBoneIndices;
        private int[] _activeBoneIds;
        private Transform[] _activeBoneTargets;
        private int _activeBoneCount;
        private int _cachedBoneIdLength = -1;
        private int _cachedBoneIdHash;
        private bool _boneIdHashCacheValid;
        private int _boneIdHashCacheLength = -2;
        private int _boneIdHashCacheValue;
        private bool _cachedIncludeFingerBones;
        private bool _boneCacheValid;
        private bool _resolvedBones;
        private Animator _resolvedAnimator;
        private Transform _rootMotionTarget;
        private bool _fingerBoneIdsPrepared;
        private int _fingerBoneIdsPreparedLength = -1;
        private int _fingerBoneIdsPreparedHash;
        private bool _fingerBoneIdsPreparedInclude;
        private Quaternion[] _targetBoneRotations;
        private bool[] _hasTargetBoneRotation;
        private Quaternion[] _receivedBoneWorldRotations;
        private bool[] _hasReceivedBoneWorldRotation;
        private int[] _boneParentIdsById;
        private int[] _receivedAncestorBoneIdsById;
        private bool _receivedAncestorBoneIdsValid;
        private int _receivedAncestorBoneIdsHash;
        private bool _hasContinuousBoneTargets;
        private bool _hasContinuousRootTarget;
        private bool _continuousRootIsLocal;
        private Vector3 _continuousRootPosition;
        private Quaternion _continuousRootRotation = Quaternion.identity;
        private bool _continuousRootHasRotation;

        public byte[] PoseBytes
        {
            get => poseBytes;
            set
            {
                poseBytes = value;
            }
        }

        private void Start()
        {
            ResolveBones();
        }

#if UDONSHARP
        public override void PostLateUpdate()
        {
            ApplyContinuousPose();
        }
#else
        private void LateUpdate()
        {
            ApplyContinuousPose();
        }
#endif

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            EnsureFingerBoneIds();

            if (autoResolveBones)
                ResolveBones();

            Transform rootTarget = _rootMotionTarget != null ? _rootMotionTarget : ResolveRootMotionTarget();
            bool hasRootMotionPosition = rootTarget != null;
            int count = _activeBoneCount;
            int rootMotionBytes = hasRootMotionPosition ? PoseRootMotionBytes : 0;
            int requiredBytes = PoseHeaderBytes + count * PoseBoneBytes + rootMotionBytes;
            if (poseBytes == null || poseBytes.Length != requiredBytes)
                poseBytes = new byte[requiredBytes];

            poseBytes[0] = PoseVersion;
            byte flags = 0;
            bool useRootSpaceRotations = animator != null;
            Quaternion animatorRootInverseRotation = Quaternion.identity;
            Transform animatorRoot = null;
            if (useRootSpaceRotations)
            {
                animatorRoot = animator.transform;
                animatorRootInverseRotation = Quaternion.Inverse(animatorRoot.rotation);
                flags |= PoseFlagAnimatorRootSpaceRotations;
            }
            else
            {
                flags |= PoseFlagLocalRotations;
            }

            if (hasRootMotionPosition)
                flags |= PoseFlagHasRootMotionPosition;
            if (rootSyncSpace == HumanoidRootSyncSpace.World)
                flags |= PoseFlagRootMotionWorldSpace;
            poseBytes[1] = flags;
            Binary.WriteUInt16LE(poseBytes, 2, (ushort)count);

            int cursor = PoseHeaderBytes;
            validBoneCount = count;
            if (_activeBoneTargets != null && _activeBoneIds != null)
            {
                for (int i = 0; i < _activeBoneCount; i++)
                {
                    Transform bone = _activeBoneTargets[i];
                    if (bone == null)
                        continue;

                    poseBytes[cursor++] = (byte)_activeBoneIds[i];
                    Quaternion rotation;
                    if (useRootSpaceRotations)
                        rotation = animatorRootInverseRotation * bone.rotation;
                    else
                        rotation = bone.localRotation;
                    Binary.WritePackedQuaternion12LE(poseBytes, cursor, rotation);
                    cursor += 5;
                }
            }

            encodedRootMotionBytes = 0;
            if (hasRootMotionPosition)
            {
                Vector3 rootPosition;
                Quaternion rootRotation;
                if (rootSyncSpace == HumanoidRootSyncSpace.World)
                {
                    rootPosition = rootTarget.position;
                    rootRotation = rootTarget.rotation;
                }
                else if (useRootSpaceRotations && animatorRoot != null)
                {
                    Vector3 rootDelta = rootTarget.position - animatorRoot.position;
                    rootPosition = animatorRootInverseRotation * rootDelta;
                    rootRotation = animatorRootInverseRotation * rootTarget.rotation;
                }
                else
                {
                    rootPosition = rootTarget.localPosition;
                    rootRotation = rootTarget.localRotation;
                }

                cursor = Binary.WriteVector3Float32LE(poseBytes, cursor, rootPosition);
                Binary.WritePackedQuaternion12LE(poseBytes, cursor, rootRotation);
                cursor += PoseRootMotionRotationBytes;
                encodedRootMotionBytes = PoseRootMotionBytes;
            }

            encodedPoseBytes = requiredBytes;
        }

        private void ApplyPose()
        {
            if (!IsTSMPActive() || poseBytes == null || poseBytes.Length < PoseHeaderBytes)
                return;

            EnsureFingerBoneIds();

            if (autoResolveBones)
                ResolveBones();

            byte version = poseBytes[0];
            byte flags = poseBytes[1];
            int count = Binary.ReadUInt16LE(poseBytes, 2);
            int rootTailBytes = 0;
            if (version >= 2 && (flags & PoseFlagHasRootMotionPosition) != 0)
                rootTailBytes = PoseRootMotionPositionBytes + (version >= 4 ? PoseRootMotionRotationBytes : 0);
            if (poseBytes.Length < PoseHeaderBytes + rootTailBytes)
                return;

            int maxBoneRecords = (poseBytes.Length - PoseHeaderBytes - rootTailBytes) / PoseBoneBytes;
            if (count < 0 || count > maxBoneRecords)
                return;

            int cursor = PoseHeaderBytes;
            bool localRotations = (flags & PoseFlagLocalRotations) != 0;
            bool animatorRootSpaceRotations = (flags & PoseFlagAnimatorRootSpaceRotations) != 0;
            bool rootMotionWorldSpace = (flags & PoseFlagRootMotionWorldSpace) != 0;
            Transform animatorRoot = null;
            Quaternion animatorRootRotation = Quaternion.identity;
            if (animatorRootSpaceRotations && animator != null)
            {
                animatorRoot = animator.transform;
                animatorRootRotation = animatorRoot.rotation;
            }

            Transform[] targetsById = _boneTargetsById;
            bool continuous = receiveInterpolation == ReceiveInterpolationMode.Continuous;
            if (continuous)
            {
                EnsureLookupArrays();
                targetsById = _boneTargetsById;
                ClearReceivedBoneWorldRotations();
            }

            for (int i = 0; i < count; i++)
            {
                if (cursor + PoseBoneBytes > poseBytes.Length)
                    return;

                int rawBoneId = poseBytes[cursor++];
                if (rawBoneId < 0 || rawBoneId >= (int)HumanBodyBones.LastBone)
                {
                    cursor += 5;
                    continue;
                }

                Quaternion rotation = Binary.ReadPackedQuaternion12LE(poseBytes, cursor);
                cursor += 5;

                Transform target = null;
                if (targetsById != null && rawBoneId < targetsById.Length)
                    target = targetsById[rawBoneId];
                if (target == null)
                    continue;

                if (continuous)
                {
                    if (_targetBoneRotations != null && _hasTargetBoneRotation != null && rawBoneId < _targetBoneRotations.Length)
                    {
                        if (localRotations)
                        {
                            _targetBoneRotations[rawBoneId] = rotation;
                            _hasTargetBoneRotation[rawBoneId] = true;
                            _hasContinuousBoneTargets = true;
                        }
                        else if (_receivedBoneWorldRotations != null && _hasReceivedBoneWorldRotation != null && rawBoneId < _receivedBoneWorldRotations.Length)
                        {
                            if (animatorRootSpaceRotations && animatorRoot != null)
                                _receivedBoneWorldRotations[rawBoneId] = animatorRootRotation * rotation;
                            else
                                _receivedBoneWorldRotations[rawBoneId] = rotation;

                            _hasReceivedBoneWorldRotation[rawBoneId] = true;
                        }
                    }
                }
                else if (animatorRootSpaceRotations && animatorRoot != null)
                {
                    target.rotation = animatorRootRotation * rotation;
                }
                else if (localRotations)
                {
                    target.localRotation = rotation;
                }
                else
                {
                    target.rotation = rotation;
                }
            }

            bool skipContinuousRootBoneRotation = false;
            if (version >= 2 && (flags & PoseFlagHasRootMotionPosition) != 0 && cursor + PoseRootMotionPositionBytes <= poseBytes.Length)
            {
                Vector3 rootPosition = Binary.ReadVector3Float32LE(poseBytes, cursor);
                cursor += PoseRootMotionPositionBytes;

                bool hasRootRotation = version >= 4 && cursor + PoseRootMotionRotationBytes <= poseBytes.Length;
                Quaternion rootRotation = Quaternion.identity;
                if (hasRootRotation)
                {
                    rootRotation = Binary.ReadPackedQuaternion12LE(poseBytes, cursor);
                    cursor += PoseRootMotionRotationBytes;
                }

                Transform target = _rootMotionTarget != null ? _rootMotionTarget : ResolveRootMotionTarget();
                if (target != null && rootMotionWorldSpace)
                {
                    if (continuous)
                    {
                        _continuousRootPosition = rootPosition;
                        _continuousRootRotation = rootRotation;
                        _continuousRootIsLocal = false;
                        _continuousRootHasRotation = hasRootRotation;
                        _hasContinuousRootTarget = true;
                        if (hasRootRotation)
                        {
                            SetReceivedRootWorldRotation(target, rootRotation, false);
                            skipContinuousRootBoneRotation = true;
                        }
                    }
                    else
                    {
                        target.position = rootPosition;
                        if (hasRootRotation)
                            target.rotation = rootRotation;
                    }
                }
                else if (target != null && animatorRootSpaceRotations && animatorRoot != null)
                {
                    Vector3 rootOffset = animatorRoot.rotation * rootPosition;
                    if (continuous)
                    {
                        _continuousRootPosition = animatorRoot.position + rootOffset;
                        _continuousRootRotation = animatorRoot.rotation * rootRotation;
                        _continuousRootIsLocal = false;
                        _continuousRootHasRotation = hasRootRotation;
                        _hasContinuousRootTarget = true;
                        if (hasRootRotation)
                        {
                            SetReceivedRootWorldRotation(target, _continuousRootRotation, false);
                            skipContinuousRootBoneRotation = true;
                        }
                    }
                    else
                    {
                        target.position = animatorRoot.position + rootOffset;
                        if (hasRootRotation)
                            target.rotation = animatorRoot.rotation * rootRotation;
                    }
                }
                else if (target != null)
                {
                    if (continuous)
                    {
                        _continuousRootPosition = rootPosition;
                        _continuousRootRotation = rootRotation;
                        _continuousRootIsLocal = true;
                        _continuousRootHasRotation = hasRootRotation;
                        _hasContinuousRootTarget = true;
                        if (hasRootRotation)
                        {
                            SetReceivedRootWorldRotation(target, rootRotation, true);
                            skipContinuousRootBoneRotation = true;
                        }
                    }
                    else
                    {
                        target.localPosition = rootPosition;
                        if (hasRootRotation)
                            target.localRotation = rootRotation;
                    }
                }
            }

            if (continuous)
                ConvertReceivedWorldRotationsToLocalTargets(skipContinuousRootBoneRotation);
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyPose();
            OnTSMPVariableChanged(lastVariableHash);
        }

        public void ResolveBones()
        {
            EnsureFingerBoneIds();

            if (animator == null)
                animator = GetComponent<Animator>();

            bool cacheShapeValid =
                _resolvedBones
                && _resolvedAnimator == animator
                && _boneCacheValid
                && _cachedBoneIdLength == (boneIds != null ? boneIds.Length : -1)
                && _cachedBoneIdHash == GetBoneIdHash()
                && _cachedIncludeFingerBones == includeFingerBones;

            if (cacheShapeValid)
                return;

            EnsureLookupArrays();

            if (boneIds == null)
                return;

            if (boneTargets == null || boneTargets.Length != boneIds.Length)
            {
                boneTargets = new Transform[boneIds.Length];
                _boneTargetResolvedByIndex = new bool[boneIds.Length];
            }

            if (!CanResolveHumanoidBones())
                return;

            if (!_boneCacheValid || _cachedBoneIdLength != boneIds.Length || _cachedBoneIdHash != GetBoneIdHash() || _cachedIncludeFingerBones != includeFingerBones)
                RebuildBoneLookup();

            for (int i = 0; i < boneIds.Length; i++)
            {
                if (!_boneTargetResolvedByIndex[i] && HumanoidBoneUtil.IsValidHumanBoneId(boneIds[i]))
                {
                    boneTargets[i] = animator.GetBoneTransform((HumanBodyBones)boneIds[i]);
                    _boneTargetResolvedByIndex[i] = true;
                }
            }

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                if (!_boneTargetResolvedById[i])
                {
                    _boneTargetsById[i] = animator.GetBoneTransform((HumanBodyBones)i);
                    _boneTargetResolvedById[i] = true;
                }
            }

            _rootMotionTarget = _boneTargetsById[(int)HumanBodyBones.Hips];
            RebuildBoneParentLookup();
            RebuildActiveBoneIndices();
            _resolvedAnimator = animator;
            _resolvedBones = true;
        }

        private void EnsureFingerBoneIds()
        {
            int currentLength = -1;
            if (boneIds != null)
                currentLength = boneIds.Length;
            int currentHash = GetBoneIdHash();
            if (_fingerBoneIdsPrepared && _fingerBoneIdsPreparedLength == currentLength && _fingerBoneIdsPreparedHash == currentHash && _fingerBoneIdsPreparedInclude == includeFingerBones)
                return;

            if (!includeFingerBones || boneIds == null)
            {
                MarkFingerBoneIdsPrepared();
                return;
            }

            EnsureLookupArrays();
            if (!_boneCacheValid || _cachedBoneIdLength != boneIds.Length || _cachedBoneIdHash != currentHash || _cachedIncludeFingerBones != includeFingerBones)
                RebuildBoneLookup();

            bool includeLeft = HasBoneId((int)HumanBodyBones.LeftHand) && !HasLeftFingerBoneIds();
            bool includeRight = HasBoneId((int)HumanBodyBones.RightHand) && !HasRightFingerBoneIds();
            if (!includeLeft && !includeRight)
            {
                MarkFingerBoneIdsPrepared();
                return;
            }

            int oldLength = boneIds.Length;
            int newLength = oldLength + (includeLeft ? HumanoidBoneUtil.FingerBoneCountPerHand : 0) + (includeRight ? HumanoidBoneUtil.FingerBoneCountPerHand : 0);
            int[] expandedBoneIds = new int[newLength];
            for (int i = 0; i < oldLength; i++)
                expandedBoneIds[i] = boneIds[i];

            int cursor = oldLength;
            if (includeLeft)
                cursor = HumanoidBoneUtil.AppendLeftFingerBoneIds(expandedBoneIds, cursor);
            if (includeRight)
                cursor = HumanoidBoneUtil.AppendRightFingerBoneIds(expandedBoneIds, cursor);

            if (boneTargets != null)
            {
                Transform[] expandedTargets = new Transform[newLength];
                int copyLength = boneTargets.Length < oldLength ? boneTargets.Length : oldLength;
                for (int i = 0; i < copyLength; i++)
                    expandedTargets[i] = boneTargets[i];
                boneTargets = expandedTargets;
            }

            boneIds = expandedBoneIds;
            InvalidateBoneIdHash();
            _boneCacheValid = false;
            _resolvedBones = false;
            _fingerBoneIdsPrepared = false;
        }

        private void MarkFingerBoneIdsPrepared()
        {
            _fingerBoneIdsPrepared = true;
            _fingerBoneIdsPreparedLength = -1;
            if (boneIds != null)
                _fingerBoneIdsPreparedLength = boneIds.Length;
            _fingerBoneIdsPreparedHash = GetBoneIdHash();
            _fingerBoneIdsPreparedInclude = includeFingerBones;
        }

        private bool HasLeftFingerBoneIds()
        {
            if (boneIds == null)
                return false;

            return HumanoidBoneUtil.HasLeftFingerBoneIds(_hasBoneId);
        }

        private bool HasRightFingerBoneIds()
        {
            if (boneIds == null)
                return false;

            return HumanoidBoneUtil.HasRightFingerBoneIds(_hasBoneId);
        }

        private bool HasBoneId(int boneId)
        {
            return HumanoidBoneUtil.HasBoneId(_hasBoneId, boneId);
        }

        private void ClearReceivedBoneWorldRotations()
        {
            if (_hasReceivedBoneWorldRotation == null)
                return;

            int count = _hasReceivedBoneWorldRotation.Length;
            for (int i = 0; i < count; i++)
                _hasReceivedBoneWorldRotation[i] = false;
        }

        private void SetReceivedRootWorldRotation(Transform root, Quaternion rootRotation, bool rootRotationIsLocal)
        {
            int hips = (int)HumanBodyBones.Hips;
            if (root == null || _receivedBoneWorldRotations == null || _hasReceivedBoneWorldRotation == null || hips >= _receivedBoneWorldRotations.Length)
                return;

            Quaternion worldRotation = rootRotation;
            if (rootRotationIsLocal)
            {
                Transform parent = root.parent;
                if (parent != null)
                    worldRotation = parent.rotation * rootRotation;
            }

            _receivedBoneWorldRotations[hips] = worldRotation;
            _hasReceivedBoneWorldRotation[hips] = true;
        }

        private void ConvertReceivedWorldRotationsToLocalTargets(bool skipRootBoneRotation)
        {
            if (_receivedBoneWorldRotations == null || _hasReceivedBoneWorldRotation == null || _targetBoneRotations == null || _hasTargetBoneRotation == null || _boneTargetsById == null)
                return;

            int hips = (int)HumanBodyBones.Hips;
            int count = _hasReceivedBoneWorldRotation.Length;
            EnsureReceivedAncestorBoneIdsCache();
            for (int i = 0; i < count; i++)
            {
                if (!_hasReceivedBoneWorldRotation[i])
                    continue;

                if (skipRootBoneRotation && i == hips)
                {
                    _hasTargetBoneRotation[i] = false;
                    continue;
                }

                Transform target = i < _boneTargetsById.Length ? _boneTargetsById[i] : null;
                if (target == null)
                    continue;

                Transform parent = target.parent;
                Quaternion parentWorldRotation = Quaternion.identity;
                if (parent != null)
                    parentWorldRotation = GetContinuousParentWorldRotation(i, parent);

                _targetBoneRotations[i] = Quaternion.Inverse(parentWorldRotation) * _receivedBoneWorldRotations[i];
                _hasTargetBoneRotation[i] = true;
                _hasContinuousBoneTargets = true;
            }
        }

        private Quaternion GetContinuousParentWorldRotation(int boneId, Transform parent)
        {
            int ancestorBoneId = -1;
            if (_receivedAncestorBoneIdsById != null && boneId >= 0 && boneId < _receivedAncestorBoneIdsById.Length)
                ancestorBoneId = _receivedAncestorBoneIdsById[boneId];

            bool ancestorAvailable = ancestorBoneId >= 0
                                     && _boneTargetsById != null
                                     && _receivedBoneWorldRotations != null
                                     && _hasReceivedBoneWorldRotation != null
                                     && ancestorBoneId < _boneTargetsById.Length
                                     && ancestorBoneId < _receivedBoneWorldRotations.Length
                                     && ancestorBoneId < _hasReceivedBoneWorldRotation.Length
                                     && _hasReceivedBoneWorldRotation[ancestorBoneId];
            if (ancestorAvailable)
            {
                Transform ancestor = _boneTargetsById[ancestorBoneId];
                if (ancestor != null)
                {
                    Quaternion ancestorWorldRotation = _receivedBoneWorldRotations[ancestorBoneId];
                    if (ancestor == parent)
                        return ancestorWorldRotation;

                    return ancestorWorldRotation * Quaternion.Inverse(ancestor.rotation) * parent.rotation;
                }
            }

            return parent.rotation;
        }

        private void EnsureReceivedAncestorBoneIdsCache()
        {
            if (_receivedAncestorBoneIdsById == null || _hasReceivedBoneWorldRotation == null)
                return;

            int hash = ComputeReceivedBoneWorldRotationHash();
            if (_receivedAncestorBoneIdsValid && _receivedAncestorBoneIdsHash == hash)
                return;

            _receivedAncestorBoneIdsHash = hash;
            _receivedAncestorBoneIdsValid = true;

            int count = _receivedAncestorBoneIdsById.Length;
            for (int i = 0; i < count; i++)
                _receivedAncestorBoneIdsById[i] = FindReceivedAncestorBoneId(i);
        }

        private int ComputeReceivedBoneWorldRotationHash()
        {
            if (_hasReceivedBoneWorldRotation == null)
                return 0;

            int hash = 17;
            int count = _hasReceivedBoneWorldRotation.Length;
            hash = hash * 31 + count;
            for (int i = 0; i < count; i++)
            {
                if (_hasReceivedBoneWorldRotation[i])
                    hash = hash * 31 + i;
            }

            return hash;
        }

        private int FindReceivedAncestorBoneId(int boneId)
        {
            if (_boneParentIdsById == null || _hasReceivedBoneWorldRotation == null)
                return -1;

            int lastBone = _boneParentIdsById.Length;
            int parentBoneId = -1;
            if (boneId >= 0 && boneId < lastBone)
                parentBoneId = _boneParentIdsById[boneId];

            int guard = 0;
            while (parentBoneId >= 0 && parentBoneId < lastBone && guard < lastBone)
            {
                if (parentBoneId < _hasReceivedBoneWorldRotation.Length && _hasReceivedBoneWorldRotation[parentBoneId])
                    return parentBoneId;

                parentBoneId = _boneParentIdsById[parentBoneId];
                guard++;
            }

            return -1;
        }

        private void ApplyContinuousPose()
        {
            if (receiveInterpolation != ReceiveInterpolationMode.Continuous || !IsTSMPActive())
                return;

            float step = GetReceiveInterpolationStep();
            bool hasBoneTargets = false;
            if (_hasContinuousBoneTargets && _hasTargetBoneRotation != null && _targetBoneRotations != null && _boneTargetsById != null)
            {
                int count = _hasTargetBoneRotation.Length;
                for (int i = 0; i < count; i++)
                {
                    if (!_hasTargetBoneRotation[i])
                        continue;

                    Transform target = i < _boneTargetsById.Length ? _boneTargetsById[i] : null;
                    if (target == null)
                        continue;

                    Quaternion targetRotation = _targetBoneRotations[i];
                    target.localRotation = Quaternion.Slerp(target.localRotation, targetRotation, step);

                    hasBoneTargets = true;
                }
            }

            _hasContinuousBoneTargets = hasBoneTargets;

            if (!_hasContinuousRootTarget)
                return;

            Transform root = _rootMotionTarget != null ? _rootMotionTarget : ResolveRootMotionTarget();
            if (root == null)
                return;

            if (_continuousRootIsLocal)
            {
                root.localPosition = Vector3.Lerp(root.localPosition, _continuousRootPosition, step);
                if (_continuousRootHasRotation)
                    root.localRotation = Quaternion.Slerp(root.localRotation, _continuousRootRotation, step);
            }
            else
            {
                root.position = Vector3.Lerp(root.position, _continuousRootPosition, step);
                if (_continuousRootHasRotation)
                    root.rotation = Quaternion.Slerp(root.rotation, _continuousRootRotation, step);
            }
        }

        private void EnsureLookupArrays()
        {
            int lastBone = (int)HumanBodyBones.LastBone;
            if (_boneTargetsById == null || _boneTargetsById.Length != lastBone)
            {
                _boneTargetsById = new Transform[lastBone];
                _boneTargetResolvedById = new bool[lastBone];
            }
            if (_boneTargetResolvedById == null || _boneTargetResolvedById.Length != lastBone)
                _boneTargetResolvedById = new bool[lastBone];
            if (_hasBoneId == null || _hasBoneId.Length != lastBone)
                _hasBoneId = new bool[lastBone];
            if (_fingerAllowedById == null || _fingerAllowedById.Length != lastBone)
                _fingerAllowedById = new bool[lastBone];
            if (_targetBoneRotations == null || _targetBoneRotations.Length != lastBone)
                _targetBoneRotations = new Quaternion[lastBone];
            if (_hasTargetBoneRotation == null || _hasTargetBoneRotation.Length != lastBone)
                _hasTargetBoneRotation = new bool[lastBone];
            if (_receivedBoneWorldRotations == null || _receivedBoneWorldRotations.Length != lastBone)
                _receivedBoneWorldRotations = new Quaternion[lastBone];
            if (_hasReceivedBoneWorldRotation == null || _hasReceivedBoneWorldRotation.Length != lastBone)
                _hasReceivedBoneWorldRotation = new bool[lastBone];
            if (_boneParentIdsById == null || _boneParentIdsById.Length != lastBone)
            {
                _boneParentIdsById = new int[lastBone];
                for (int i = 0; i < lastBone; i++)
                    _boneParentIdsById[i] = -1;
            }
            if (_receivedAncestorBoneIdsById == null || _receivedAncestorBoneIdsById.Length != lastBone)
            {
                _receivedAncestorBoneIdsById = new int[lastBone];
                for (int i = 0; i < lastBone; i++)
                    _receivedAncestorBoneIdsById[i] = -1;
                _receivedAncestorBoneIdsValid = false;
            }
            if (boneIds != null && (_activeBoneIndices == null || _activeBoneIndices.Length != boneIds.Length))
                _activeBoneIndices = new int[boneIds.Length];
            if (boneIds != null && (_activeBoneIds == null || _activeBoneIds.Length != boneIds.Length))
                _activeBoneIds = new int[boneIds.Length];
            if (boneIds != null && (_activeBoneTargets == null || _activeBoneTargets.Length != boneIds.Length))
                _activeBoneTargets = new Transform[boneIds.Length];
            if (boneIds != null && (_boneTargetResolvedByIndex == null || _boneTargetResolvedByIndex.Length != boneIds.Length))
                _boneTargetResolvedByIndex = new bool[boneIds.Length];
        }

        private void RebuildBoneLookup()
        {
            EnsureLookupArrays();

            int lastBone = (int)HumanBodyBones.LastBone;
            for (int i = 0; i < lastBone; i++)
            {
                _hasBoneId[i] = false;
                _fingerAllowedById[i] = true;
            }

            if (boneIds != null)
            {
                for (int i = 0; i < boneIds.Length; i++)
                {
                    int boneId = boneIds[i];
                    if (HumanoidBoneUtil.IsValidHumanBoneId(boneId))
                        _hasBoneId[boneId] = true;
                }
            }

            bool hasLeftHand = _hasBoneId[(int)HumanBodyBones.LeftHand];
            bool hasRightHand = _hasBoneId[(int)HumanBodyBones.RightHand];
            for (int i = 0; i < lastBone; i++)
            {
                bool allowed = true;
                if (HumanoidBoneUtil.IsLeftFingerBoneId(i))
                    allowed = includeFingerBones && hasLeftHand;
                else if (HumanoidBoneUtil.IsRightFingerBoneId(i))
                    allowed = includeFingerBones && hasRightHand;

                _fingerAllowedById[i] = allowed;
            }

            _cachedBoneIdLength = -1;
            if (boneIds != null)
                _cachedBoneIdLength = boneIds.Length;
            _cachedBoneIdHash = GetBoneIdHash();
            _cachedIncludeFingerBones = includeFingerBones;
            _boneCacheValid = true;
        }

        private void RebuildBoneParentLookup()
        {
            if (_boneParentIdsById == null || _boneTargetsById == null)
                return;

            int lastBone = _boneParentIdsById.Length;
            for (int i = 0; i < lastBone; i++)
                _boneParentIdsById[i] = -1;

            for (int i = 0; i < lastBone; i++)
            {
                Transform target = i < _boneTargetsById.Length ? _boneTargetsById[i] : null;
                if (target == null)
                    continue;

                Transform parent = target.parent;
                while (parent != null)
                {
                    int parentBoneId = FindBoneIdByTransform(parent);
                    if (parentBoneId >= 0)
                    {
                        _boneParentIdsById[i] = parentBoneId;
                        break;
                    }

                    parent = parent.parent;
                }
            }

            _receivedAncestorBoneIdsValid = false;
        }

        private int FindBoneIdByTransform(Transform target)
        {
            if (target == null || _boneTargetsById == null)
                return -1;

            int count = _boneTargetsById.Length;
            for (int i = 0; i < count; i++)
            {
                if (_boneTargetsById[i] == target)
                    return i;
            }

            return -1;
        }

        private int GetBoneIdHash()
        {
            int length = boneIds != null ? boneIds.Length : -1;
            if (_boneIdHashCacheValid && _boneIdHashCacheLength == length)
                return _boneIdHashCacheValue;

            _boneIdHashCacheLength = length;
            _boneIdHashCacheValue = ComputeBoneIdHash();
            _boneIdHashCacheValid = true;
            return _boneIdHashCacheValue;
        }

        private void InvalidateBoneIdHash()
        {
            _boneIdHashCacheValid = false;
            _boneIdHashCacheLength = -2;
            _boneIdHashCacheValue = 0;
        }

        private int ComputeBoneIdHash()
        {
            if (boneIds == null)
                return 0;

            int hash = 17;
            for (int i = 0; i < boneIds.Length; i++)
                hash = hash * 31 + boneIds[i];
            return hash;
        }

        private void RebuildActiveBoneIndices()
        {
            if (boneIds == null || boneTargets == null)
            {
                _activeBoneCount = 0;
                return;
            }

            EnsureLookupArrays();
            int count = 0;
            for (int i = 0; i < boneIds.Length; i++)
            {
                int boneId = boneIds[i];
                if (!HumanoidBoneUtil.IsValidHumanBoneId(boneId) || !_fingerAllowedById[boneId])
                    continue;

                Transform target = boneTargets[i];
                if (target == null && _boneTargetsById != null)
                    target = _boneTargetsById[boneId];

                if (target == null)
                    continue;

                _activeBoneIndices[count] = i;
                _activeBoneIds[count] = boneId;
                _activeBoneTargets[count] = target;
                count++;
            }

            _activeBoneCount = count;
            validBoneCount = count;
        }

        private bool CanResolveHumanoidBones()
        {
            if (animator == null)
                return false;

            Avatar avatar = animator.avatar;
            if (avatar == null)
                return false;

            return avatar.isHuman;
        }

        private Transform ResolveRootMotionTarget()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            if (!CanResolveHumanoidBones())
                return null;

            return animator.GetBoneTransform(HumanBodyBones.Hips);
        }

    }
}
