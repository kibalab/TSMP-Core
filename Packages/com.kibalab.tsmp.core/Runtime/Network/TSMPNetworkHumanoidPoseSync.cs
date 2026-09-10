using UnityEngine;

#if UDONSHARP || COMPILER_UDONSHARP
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
#if UDONSHARP || COMPILER_UDONSHARP
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
        private bool[] _targetBoneRotationIsLocal;
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

#if UDONSHARP || COMPILER_UDONSHARP
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
                    if (_targetBoneRotations != null && _hasTargetBoneRotation != null && _targetBoneRotationIsLocal != null && rawBoneId < _targetBoneRotations.Length)
                    {
                        if (animatorRootSpaceRotations && animatorRoot != null)
                        {
                            _targetBoneRotations[rawBoneId] = animatorRootRotation * rotation;
                            _targetBoneRotationIsLocal[rawBoneId] = false;
                        }
                        else if (localRotations)
                        {
                            _targetBoneRotations[rawBoneId] = rotation;
                            _targetBoneRotationIsLocal[rawBoneId] = true;
                        }
                        else
                        {
                            _targetBoneRotations[rawBoneId] = rotation;
                            _targetBoneRotationIsLocal[rawBoneId] = false;
                        }

                        _hasTargetBoneRotation[rawBoneId] = true;
                        _hasContinuousBoneTargets = true;
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
                    }
                    else
                    {
                        target.localPosition = rootPosition;
                        if (hasRootRotation)
                            target.localRotation = rootRotation;
                    }
                }
            }
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

        private void ApplyContinuousPose()
        {
            if (receiveInterpolation != ReceiveInterpolationMode.Continuous || !IsTSMPActive())
                return;

            float step = GetReceiveInterpolationStep();
            bool hasBoneTargets = false;
            if (_hasContinuousBoneTargets && _hasTargetBoneRotation != null && _targetBoneRotations != null && _targetBoneRotationIsLocal != null && _boneTargetsById != null)
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
                    if (_targetBoneRotationIsLocal[i])
                        target.localRotation = Quaternion.Slerp(target.localRotation, targetRotation, step);
                    else
                        target.rotation = Quaternion.Slerp(target.rotation, targetRotation, step);

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
            if (_targetBoneRotationIsLocal == null || _targetBoneRotationIsLocal.Length != lastBone)
                _targetBoneRotationIsLocal = new bool[lastBone];
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
