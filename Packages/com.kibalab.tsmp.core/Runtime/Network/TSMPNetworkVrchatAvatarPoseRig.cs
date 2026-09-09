using UnityEngine;

namespace K13A.TSMP.Udon
{
    public class TSMPNetworkVrchatAvatarPoseRig : TSMPBehaviour
    {
        public Animator animator;
        public int playerId = -1;
        public string playerName;
        public bool resolveHumanoidTargets = true;
        public Transform rootTarget;
        public Transform headTarget;
        public Transform leftHandTarget;
        public Transform rightHandTarget;
        public Transform hipsTarget;
        public Transform leftFootTarget;
        public Transform rightFootTarget;
        [HideInInspector] public bool interpolateReceivedPose;
        [HideInInspector] public float receiveInterpolationRate = 24f;

        private Transform[] _humanoidTargets;
        private bool[] _humanoidTargetResolved;
        private Quaternion[] _targetHumanoidRotations;
        private Vector3[] _targetHumanoidPositions;
        private bool[] _hasTargetHumanoidRotation;
        private bool[] _hasTargetHumanoidPosition;
        private int[] _targetHumanoidBoneIds;
        private int _targetHumanoidBoneCount;
        private Quaternion[] _currentHumanoidRotations;
        private Vector3[] _currentHumanoidPositions;
        private bool[] _hasCurrentHumanoidRotation;
        private bool[] _hasCurrentHumanoidPosition;
        private int[] _currentHumanoidBoneIds;
        private int _currentHumanoidBoneCount;
        private int _receivedPoseMode = -1;
        private bool _targetsResolved;
        private bool _hasRootTarget;
        private bool _hasTrackingTarget;
        private Vector3 _targetRootPosition;
        private Quaternion _targetRootRotation = Quaternion.identity;
        private bool _hasCurrentRootPose;
        private Vector3 _currentRootPosition;
        private Quaternion _currentRootRotation = Quaternion.identity;
        private Vector3[] _targetTrackingPositions;
        private Quaternion[] _targetTrackingRotations;
        private bool[] _hasTargetTrackingPoint;
        private Vector3[] _currentTrackingPositions;
        private Quaternion[] _currentTrackingRotations;
        private bool[] _hasCurrentTrackingPoint;
        private const float InterpolationPositionEpsilonSqr = 0.000001f;
        private const float InterpolationRotationDotThreshold = 0.99995f;

        private void Start()
        {
            ResolveTargets();
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public override void PostLateUpdate()
        {
            ApplyReceivedPoseState();
        }
#else
        private void LateUpdate()
        {
            ApplyReceivedPoseState();
        }
#endif

        public void ResolveTargets()
        {
            if (_targetsResolved)
                return;

            if (animator == null)
                animator = GetComponent<Animator>();

            if (rootTarget == null)
                rootTarget = transform;

            if (!resolveHumanoidTargets || animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                _targetsResolved = true;
                return;
            }

            if (headTarget == null)
                headTarget = animator.GetBoneTransform(HumanBodyBones.Head);
            if (leftHandTarget == null)
                leftHandTarget = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (rightHandTarget == null)
                rightHandTarget = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hipsTarget == null)
                hipsTarget = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (leftFootTarget == null)
                leftFootTarget = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (rightFootTarget == null)
                rightFootTarget = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            EnsureHumanoidTargetCache();
            if (_humanoidTargets != null && _humanoidTargetResolved != null)
            {
                int lastBone = (int)HumanBodyBones.LastBone;
                for (int i = 0; i < lastBone; i++)
                {
                    _humanoidTargets[i] = animator.GetBoneTransform((HumanBodyBones)i);
                    _humanoidTargetResolved[i] = true;
                }
            }

            _targetsResolved = true;
        }

        public void SetPlayerInfo(int id, string displayName)
        {
            if (playerId != id)
                ClearReceivedPoseState();

            playerId = id;
            playerName = displayName == null ? string.Empty : displayName;
        }

        public void SetPlayerId(int id)
        {
            if (playerId != id)
                ClearReceivedPoseState();

            playerId = id;
        }

        public void SetReceivedPoseMode(int mode)
        {
            if (_receivedPoseMode == mode)
                return;

            _receivedPoseMode = mode;
            ClearReceivedPoseState();
        }

        public void ClearReceivedPoseState()
        {
            _hasRootTarget = false;
            _hasTrackingTarget = false;
            _hasCurrentRootPose = false;
            _targetHumanoidBoneCount = 0;
            _currentHumanoidBoneCount = 0;

            if (_hasTargetHumanoidRotation != null)
            {
                for (int i = 0; i < _hasTargetHumanoidRotation.Length; i++)
                    _hasTargetHumanoidRotation[i] = false;
            }

            if (_hasTargetHumanoidPosition != null)
            {
                for (int i = 0; i < _hasTargetHumanoidPosition.Length; i++)
                    _hasTargetHumanoidPosition[i] = false;
            }

            if (_hasCurrentHumanoidRotation != null)
            {
                for (int i = 0; i < _hasCurrentHumanoidRotation.Length; i++)
                    _hasCurrentHumanoidRotation[i] = false;
            }

            if (_hasCurrentHumanoidPosition != null)
            {
                for (int i = 0; i < _hasCurrentHumanoidPosition.Length; i++)
                    _hasCurrentHumanoidPosition[i] = false;
            }

            if (_hasTargetTrackingPoint != null)
            {
                for (int i = 0; i < _hasTargetTrackingPoint.Length; i++)
                    _hasTargetTrackingPoint[i] = false;
            }

            if (_hasCurrentTrackingPoint != null)
            {
                for (int i = 0; i < _hasCurrentTrackingPoint.Length; i++)
                    _hasCurrentTrackingPoint[i] = false;
            }
        }

        public void ApplyRootPose(Vector3 position, Quaternion rotation)
        {
            ResolveTargets();

            Transform target = rootTarget;
            if (target == null)
                target = transform;

            if (interpolateReceivedPose)
            {
                if (!_hasCurrentRootPose)
                    StoreCurrentRootPose(target.position, target.rotation);

                _targetRootPosition = position;
                _targetRootRotation = rotation;
                _hasRootTarget = true;
                return;
            }

            StoreCurrentRootPose(position, rotation);
            target.position = position;
            target.rotation = rotation;
        }

        public void ApplyTrackingPoint(int pointId, Vector3 position, Quaternion rotation)
        {
            ResolveTargets();

            Transform target = GetTrackingTarget(pointId);
            if (target == null)
                return;

            if (interpolateReceivedPose)
            {
                EnsureTrackingTargetCache();
                if (_targetTrackingPositions != null && _targetTrackingRotations != null && _hasTargetTrackingPoint != null && pointId >= 0 && pointId < _hasTargetTrackingPoint.Length)
                {
                    if (_currentTrackingPositions != null && _currentTrackingRotations != null && _hasCurrentTrackingPoint != null && !_hasCurrentTrackingPoint[pointId])
                        StoreCurrentTrackingPoint(pointId, target.position, target.rotation);

                    _targetTrackingPositions[pointId] = position;
                    _targetTrackingRotations[pointId] = rotation;
                    _hasTargetTrackingPoint[pointId] = true;
                    _hasTrackingTarget = true;
                }

                return;
            }

            StoreCurrentTrackingPoint(pointId, position, rotation);
            target.position = position;
            target.rotation = rotation;
        }

        public void ApplyHumanoidBone(int boneId, Vector3 position, Quaternion rotation)
        {
            ResolveTargets();

            Transform target = GetHumanoidTarget(boneId);

            if (target == null)
                target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));

            if (target == null)
                return;

            if (interpolateReceivedPose)
            {
                StoreHumanoidPositionTarget(boneId, position);
                StoreHumanoidRotationTarget(boneId, rotation);
                return;
            }

            StoreCurrentHumanoidPosition(boneId, position);
            StoreCurrentHumanoidRotation(boneId, rotation);
            target.position = position;
            target.rotation = rotation;
        }

        public void ApplyHumanoidRoot(Vector3 position)
        {
            ResolveTargets();

            Transform target = hipsTarget;
            if (target == null)
                target = rootTarget;
            if (target == null)
                target = transform;

            if (interpolateReceivedPose)
            {
                if (!_hasCurrentRootPose)
                    StoreCurrentRootPose(target.position, target.rotation);

                _targetRootPosition = position;
                _targetRootRotation = target.rotation;
                _hasRootTarget = true;
                return;
            }

            StoreCurrentRootPose(position, target.rotation);
            target.position = position;
        }

        public void ApplyHumanoidBoneRotation(int boneId, Quaternion rotation)
        {
            ResolveTargets();

            Transform target = GetHumanoidTarget(boneId);
            if (target == null)
                target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));

            if (target == null)
                return;

            if (interpolateReceivedPose)
            {
                StoreHumanoidRotationTarget(boneId, rotation);
                return;
            }

            StoreCurrentHumanoidRotation(boneId, rotation);
            target.rotation = rotation;
        }

        public void ApplyCompactHumanoidBoneRotations(byte[] bytes, int offset, int count)
        {
            ResolveTargets();

            if (bytes == null || count <= 0)
                return;

            int cursor = offset;
            for (int i = 0; i < count; i++)
            {
                if (cursor + 6 > bytes.Length)
                    return;

                int boneId = bytes[cursor++];
                Quaternion rotation = Binary.ReadPackedQuaternion12LE(bytes, cursor);
                cursor += 5;

                Transform target = GetHumanoidTarget(boneId);
                if (target == null)
                    target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));

                if (target != null)
                {
                    if (interpolateReceivedPose)
                        StoreHumanoidRotationTarget(boneId, rotation);
                    else
                    {
                        StoreCurrentHumanoidRotation(boneId, rotation);
                        target.rotation = rotation;
                    }
                }
            }
        }

        private Transform GetHumanoidTarget(int boneId)
        {
            if (boneId < 0 || boneId >= (int)HumanBodyBones.LastBone)
                return null;

            if (_targetsResolved && _humanoidTargets != null && boneId < _humanoidTargets.Length)
                return _humanoidTargets[boneId];

            EnsureHumanoidTargetCache();
            if (_humanoidTargets == null || _humanoidTargetResolved == null)
                return null;

            if (!_humanoidTargetResolved[boneId])
            {
                if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                    _humanoidTargets[boneId] = animator.GetBoneTransform((HumanBodyBones)boneId);
                _humanoidTargetResolved[boneId] = true;
            }

            return _humanoidTargets[boneId];
        }

        private void EnsureHumanoidTargetCache()
        {
            int lastBone = (int)HumanBodyBones.LastBone;
            if (_humanoidTargets == null || _humanoidTargets.Length != lastBone)
                _humanoidTargets = new Transform[lastBone];
            if (_humanoidTargetResolved == null || _humanoidTargetResolved.Length != lastBone)
                _humanoidTargetResolved = new bool[lastBone];
            if (_targetHumanoidRotations == null || _targetHumanoidRotations.Length != lastBone)
                _targetHumanoidRotations = new Quaternion[lastBone];
            if (_targetHumanoidPositions == null || _targetHumanoidPositions.Length != lastBone)
                _targetHumanoidPositions = new Vector3[lastBone];
            if (_hasTargetHumanoidRotation == null || _hasTargetHumanoidRotation.Length != lastBone)
            {
                _hasTargetHumanoidRotation = new bool[lastBone];
                _targetHumanoidBoneCount = 0;
            }
            if (_hasTargetHumanoidPosition == null || _hasTargetHumanoidPosition.Length != lastBone)
            {
                _hasTargetHumanoidPosition = new bool[lastBone];
                _targetHumanoidBoneCount = 0;
            }
            if (_targetHumanoidBoneIds == null || _targetHumanoidBoneIds.Length != lastBone)
            {
                _targetHumanoidBoneIds = new int[lastBone];
                _targetHumanoidBoneCount = 0;
            }
            if (_currentHumanoidRotations == null || _currentHumanoidRotations.Length != lastBone)
                _currentHumanoidRotations = new Quaternion[lastBone];
            if (_currentHumanoidPositions == null || _currentHumanoidPositions.Length != lastBone)
                _currentHumanoidPositions = new Vector3[lastBone];
            if (_hasCurrentHumanoidRotation == null || _hasCurrentHumanoidRotation.Length != lastBone)
            {
                _hasCurrentHumanoidRotation = new bool[lastBone];
                _currentHumanoidBoneCount = 0;
            }
            if (_hasCurrentHumanoidPosition == null || _hasCurrentHumanoidPosition.Length != lastBone)
            {
                _hasCurrentHumanoidPosition = new bool[lastBone];
                _currentHumanoidBoneCount = 0;
            }
            if (_currentHumanoidBoneIds == null || _currentHumanoidBoneIds.Length != lastBone)
            {
                _currentHumanoidBoneIds = new int[lastBone];
                _currentHumanoidBoneCount = 0;
            }
        }

        private Transform GetTrackingTarget(int pointId)
        {
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointOrigin)
                return rootTarget != null ? rootTarget : transform;
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointHead)
                return headTarget;
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointLeftHand)
                return leftHandTarget;
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointRightHand)
                return rightHandTarget;
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointHips)
                return hipsTarget;
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointLeftFoot)
                return leftFootTarget;
            if (pointId == TSMPNetworkVrchatAvatarPoseSync.TrackingPointRightFoot)
                return rightFootTarget;

            return null;
        }

        private static int MapBoneToTrackingPoint(int boneId)
        {
            if (boneId == (int)HumanBodyBones.Head)
                return TSMPNetworkVrchatAvatarPoseSync.TrackingPointHead;
            if (boneId == (int)HumanBodyBones.LeftHand)
                return TSMPNetworkVrchatAvatarPoseSync.TrackingPointLeftHand;
            if (boneId == (int)HumanBodyBones.RightHand)
                return TSMPNetworkVrchatAvatarPoseSync.TrackingPointRightHand;
            if (boneId == (int)HumanBodyBones.Hips)
                return TSMPNetworkVrchatAvatarPoseSync.TrackingPointHips;
            if (boneId == (int)HumanBodyBones.LeftFoot)
                return TSMPNetworkVrchatAvatarPoseSync.TrackingPointLeftFoot;
            if (boneId == (int)HumanBodyBones.RightFoot)
                return TSMPNetworkVrchatAvatarPoseSync.TrackingPointRightFoot;

            return -1;
        }

        private void StoreHumanoidRotationTarget(int boneId, Quaternion rotation)
        {
            if (boneId < 0 || boneId >= (int)HumanBodyBones.LastBone)
                return;

            EnsureHumanoidTargetCache();
            if (_targetHumanoidRotations == null || _hasTargetHumanoidRotation == null || boneId >= _targetHumanoidRotations.Length)
                return;

            if (_hasCurrentHumanoidRotation == null || !_hasCurrentHumanoidRotation[boneId])
            {
                Transform target = GetHumanoidTarget(boneId);
                if (target == null)
                    target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));

                if (target != null)
                    StoreCurrentHumanoidRotation(boneId, target.rotation);
                else
                    StoreCurrentHumanoidRotation(boneId, rotation);
            }

            EnsureTargetHumanoidBoneId(boneId);
            _targetHumanoidRotations[boneId] = rotation;
            _hasTargetHumanoidRotation[boneId] = true;
        }

        private void StoreHumanoidPositionTarget(int boneId, Vector3 position)
        {
            if (boneId < 0 || boneId >= (int)HumanBodyBones.LastBone)
                return;

            EnsureHumanoidTargetCache();
            if (_targetHumanoidPositions == null || _hasTargetHumanoidPosition == null || boneId >= _targetHumanoidPositions.Length)
                return;

            if (_hasCurrentHumanoidPosition == null || !_hasCurrentHumanoidPosition[boneId])
            {
                Transform target = GetHumanoidTarget(boneId);
                if (target == null)
                    target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));

                if (target != null)
                    StoreCurrentHumanoidPosition(boneId, target.position);
                else
                    StoreCurrentHumanoidPosition(boneId, position);
            }

            EnsureTargetHumanoidBoneId(boneId);
            _targetHumanoidPositions[boneId] = position;
            _hasTargetHumanoidPosition[boneId] = true;
        }

        private void EnsureTargetHumanoidBoneId(int boneId)
        {
            if (_targetHumanoidBoneIds == null || _targetHumanoidBoneCount >= _targetHumanoidBoneIds.Length)
                return;

            bool hasRotation = _hasTargetHumanoidRotation != null && boneId >= 0 && boneId < _hasTargetHumanoidRotation.Length && _hasTargetHumanoidRotation[boneId];
            bool hasPosition = _hasTargetHumanoidPosition != null && boneId >= 0 && boneId < _hasTargetHumanoidPosition.Length && _hasTargetHumanoidPosition[boneId];
            if (hasRotation || hasPosition)
                return;

            _targetHumanoidBoneIds[_targetHumanoidBoneCount] = boneId;
            _targetHumanoidBoneCount++;
        }

        private void StoreCurrentRootPose(Vector3 position, Quaternion rotation)
        {
            _currentRootPosition = position;
            _currentRootRotation = rotation;
            _hasCurrentRootPose = true;
        }

        private void StoreCurrentTrackingPoint(int pointId, Vector3 position, Quaternion rotation)
        {
            if (pointId < 0)
                return;

            EnsureTrackingTargetCache();
            if (_currentTrackingPositions == null || _currentTrackingRotations == null || _hasCurrentTrackingPoint == null || pointId >= _hasCurrentTrackingPoint.Length)
                return;

            _currentTrackingPositions[pointId] = position;
            _currentTrackingRotations[pointId] = rotation;
            _hasCurrentTrackingPoint[pointId] = true;
        }

        private void StoreCurrentHumanoidRotation(int boneId, Quaternion rotation)
        {
            if (boneId < 0 || boneId >= (int)HumanBodyBones.LastBone)
                return;

            EnsureHumanoidTargetCache();
            if (_currentHumanoidRotations == null || _hasCurrentHumanoidRotation == null || boneId >= _currentHumanoidRotations.Length)
                return;

            EnsureCurrentHumanoidBoneId(boneId);
            _currentHumanoidRotations[boneId] = rotation;
            _hasCurrentHumanoidRotation[boneId] = true;
        }

        private void StoreCurrentHumanoidPosition(int boneId, Vector3 position)
        {
            if (boneId < 0 || boneId >= (int)HumanBodyBones.LastBone)
                return;

            EnsureHumanoidTargetCache();
            if (_currentHumanoidPositions == null || _hasCurrentHumanoidPosition == null || boneId >= _currentHumanoidPositions.Length)
                return;

            EnsureCurrentHumanoidBoneId(boneId);
            _currentHumanoidPositions[boneId] = position;
            _hasCurrentHumanoidPosition[boneId] = true;
        }

        private void EnsureCurrentHumanoidBoneId(int boneId)
        {
            if (_currentHumanoidBoneIds == null || _currentHumanoidBoneCount >= _currentHumanoidBoneIds.Length)
                return;

            bool hasRotation = _hasCurrentHumanoidRotation != null && boneId >= 0 && boneId < _hasCurrentHumanoidRotation.Length && _hasCurrentHumanoidRotation[boneId];
            bool hasPosition = _hasCurrentHumanoidPosition != null && boneId >= 0 && boneId < _hasCurrentHumanoidPosition.Length && _hasCurrentHumanoidPosition[boneId];
            if (hasRotation || hasPosition)
                return;

            _currentHumanoidBoneIds[_currentHumanoidBoneCount] = boneId;
            _currentHumanoidBoneCount++;
        }

        private void ApplyReceivedPoseState()
        {
            ResolveTargets();

            float step = interpolateReceivedPose ? GetInterpolationStep() : 1f;
            if (_hasRootTarget)
            {
                Transform target = rootTarget;
                if (target == null)
                    target = transform;
                if (!_hasCurrentRootPose)
                    StoreCurrentRootPose(target.position, target.rotation);

                _currentRootPosition = Vector3.Lerp(_currentRootPosition, _targetRootPosition, step);
                _currentRootRotation = Quaternion.Slerp(_currentRootRotation, _targetRootRotation, step);
                if (IsPositionClose(_currentRootPosition, _targetRootPosition) && IsRotationClose(_currentRootRotation, _targetRootRotation))
                {
                    _currentRootPosition = _targetRootPosition;
                    _currentRootRotation = _targetRootRotation;
                    _hasRootTarget = false;
                }
            }

            if (_hasCurrentRootPose)
            {
                Transform target = rootTarget;
                if (target == null)
                    target = transform;
                target.position = _currentRootPosition;
                target.rotation = _currentRootRotation;
            }

            if (_hasTrackingTarget && _hasTargetTrackingPoint != null)
            {
                bool anyTrackingTarget = false;
                int count = _hasTargetTrackingPoint.Length;
                for (int i = 0; i < count; i++)
                {
                    if (!_hasTargetTrackingPoint[i])
                        continue;

                    Transform target = GetTrackingTarget(i);
                    if (target == null)
                    {
                        _hasTargetTrackingPoint[i] = false;
                        continue;
                    }

                    if (_hasCurrentTrackingPoint == null || !_hasCurrentTrackingPoint[i])
                        StoreCurrentTrackingPoint(i, target.position, target.rotation);

                    _currentTrackingPositions[i] = Vector3.Lerp(_currentTrackingPositions[i], _targetTrackingPositions[i], step);
                    _currentTrackingRotations[i] = Quaternion.Slerp(_currentTrackingRotations[i], _targetTrackingRotations[i], step);
                    if (IsPositionClose(_currentTrackingPositions[i], _targetTrackingPositions[i]) && IsRotationClose(_currentTrackingRotations[i], _targetTrackingRotations[i]))
                    {
                        _currentTrackingPositions[i] = _targetTrackingPositions[i];
                        _currentTrackingRotations[i] = _targetTrackingRotations[i];
                        _hasTargetTrackingPoint[i] = false;
                    }
                    else
                    {
                        anyTrackingTarget = true;
                    }
                }

                _hasTrackingTarget = anyTrackingTarget;
            }

            if (_hasCurrentTrackingPoint != null)
            {
                int count = _hasCurrentTrackingPoint.Length;
                for (int i = 0; i < count; i++)
                {
                    if (!_hasCurrentTrackingPoint[i])
                        continue;

                    Transform target = GetTrackingTarget(i);
                    if (target == null)
                        continue;

                    target.position = _currentTrackingPositions[i];
                    target.rotation = _currentTrackingRotations[i];
                }
            }

            if (_targetHumanoidBoneIds == null)
            {
                ApplyHeldHumanoidTransforms();
                return;
            }

            int index = 0;
            while (index < _targetHumanoidBoneCount)
            {
                int boneId = _targetHumanoidBoneIds[index];
                bool hasRotationTarget = _hasTargetHumanoidRotation != null && boneId >= 0 && boneId < _hasTargetHumanoidRotation.Length && _hasTargetHumanoidRotation[boneId];
                bool hasPositionTarget = _hasTargetHumanoidPosition != null && boneId >= 0 && boneId < _hasTargetHumanoidPosition.Length && _hasTargetHumanoidPosition[boneId];
                if (boneId < 0 || (!hasRotationTarget && !hasPositionTarget))
                {
                    RemoveHumanoidTargetAt(index);
                    continue;
                }

                Transform target = GetHumanoidTarget(boneId);
                if (target == null)
                    target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));
                if (target == null)
                {
                    RemoveHumanoidTargetAt(index);
                    continue;
                }

                if (hasPositionTarget)
                {
                    if (_hasCurrentHumanoidPosition == null || !_hasCurrentHumanoidPosition[boneId])
                        StoreCurrentHumanoidPosition(boneId, target.position);

                    _currentHumanoidPositions[boneId] = Vector3.Lerp(_currentHumanoidPositions[boneId], _targetHumanoidPositions[boneId], step);
                    if (IsPositionClose(_currentHumanoidPositions[boneId], _targetHumanoidPositions[boneId]))
                    {
                        _currentHumanoidPositions[boneId] = _targetHumanoidPositions[boneId];
                        _hasTargetHumanoidPosition[boneId] = false;
                        hasPositionTarget = false;
                    }
                }

                if (hasRotationTarget)
                {
                    if (_hasCurrentHumanoidRotation == null || !_hasCurrentHumanoidRotation[boneId])
                        StoreCurrentHumanoidRotation(boneId, target.rotation);

                    _currentHumanoidRotations[boneId] = Quaternion.Slerp(_currentHumanoidRotations[boneId], _targetHumanoidRotations[boneId], step);
                    if (IsRotationClose(_currentHumanoidRotations[boneId], _targetHumanoidRotations[boneId]))
                    {
                        _currentHumanoidRotations[boneId] = _targetHumanoidRotations[boneId];
                        _hasTargetHumanoidRotation[boneId] = false;
                        hasRotationTarget = false;
                    }
                }

                if (!hasPositionTarget && !hasRotationTarget)
                    RemoveHumanoidTargetAt(index);
                else
                    index++;
            }

            ApplyHeldHumanoidTransforms();
        }

        private void ApplyHeldHumanoidTransforms()
        {
            if (_currentHumanoidBoneIds == null)
                return;

            int index = 0;
            while (index < _currentHumanoidBoneCount)
            {
                int boneId = _currentHumanoidBoneIds[index];
                bool hasRotation = _hasCurrentHumanoidRotation != null && boneId >= 0 && boneId < _hasCurrentHumanoidRotation.Length && _hasCurrentHumanoidRotation[boneId];
                bool hasPosition = _hasCurrentHumanoidPosition != null && boneId >= 0 && boneId < _hasCurrentHumanoidPosition.Length && _hasCurrentHumanoidPosition[boneId];
                if (boneId < 0 || (!hasRotation && !hasPosition))
                {
                    RemoveCurrentHumanoidTargetAt(index);
                    continue;
                }

                Transform target = GetHumanoidTarget(boneId);
                if (target == null)
                    target = GetTrackingTarget(MapBoneToTrackingPoint(boneId));
                if (target == null)
                {
                    index++;
                    continue;
                }

                if (hasPosition)
                    target.position = _currentHumanoidPositions[boneId];
                if (hasRotation)
                    target.rotation = _currentHumanoidRotations[boneId];
                index++;
            }
        }

        private void RemoveCurrentHumanoidTargetAt(int index)
        {
            if (_currentHumanoidBoneIds == null || _hasCurrentHumanoidRotation == null || index < 0 || index >= _currentHumanoidBoneCount)
                return;

            int boneId = _currentHumanoidBoneIds[index];
            if (boneId >= 0 && boneId < _hasCurrentHumanoidRotation.Length)
                _hasCurrentHumanoidRotation[boneId] = false;
            if (_hasCurrentHumanoidPosition != null && boneId >= 0 && boneId < _hasCurrentHumanoidPosition.Length)
                _hasCurrentHumanoidPosition[boneId] = false;

            _currentHumanoidBoneCount--;
            if (index < _currentHumanoidBoneCount)
                _currentHumanoidBoneIds[index] = _currentHumanoidBoneIds[_currentHumanoidBoneCount];
            _currentHumanoidBoneIds[_currentHumanoidBoneCount] = 0;
        }

        private void RemoveHumanoidTargetAt(int index)
        {
            if (_targetHumanoidBoneIds == null || _hasTargetHumanoidRotation == null || index < 0 || index >= _targetHumanoidBoneCount)
                return;

            int boneId = _targetHumanoidBoneIds[index];
            if (boneId >= 0 && boneId < _hasTargetHumanoidRotation.Length)
                _hasTargetHumanoidRotation[boneId] = false;
            if (_hasTargetHumanoidPosition != null && boneId >= 0 && boneId < _hasTargetHumanoidPosition.Length)
                _hasTargetHumanoidPosition[boneId] = false;

            _targetHumanoidBoneCount--;
            if (index < _targetHumanoidBoneCount)
                _targetHumanoidBoneIds[index] = _targetHumanoidBoneIds[_targetHumanoidBoneCount];
            _targetHumanoidBoneIds[_targetHumanoidBoneCount] = 0;
        }

        private static bool IsPositionClose(Vector3 current, Vector3 target)
        {
            Vector3 delta = current - target;
            return delta.sqrMagnitude <= InterpolationPositionEpsilonSqr;
        }

        private static bool IsRotationClose(Quaternion current, Quaternion target)
        {
            float dot = current.x * target.x + current.y * target.y + current.z * target.z + current.w * target.w;
            if (dot < 0f)
                dot = -dot;
            return dot >= InterpolationRotationDotThreshold;
        }

        private void EnsureTrackingTargetCache()
        {
            int count = 7;
            if (_targetTrackingPositions == null || _targetTrackingPositions.Length != count)
                _targetTrackingPositions = new Vector3[count];
            if (_targetTrackingRotations == null || _targetTrackingRotations.Length != count)
                _targetTrackingRotations = new Quaternion[count];
            if (_hasTargetTrackingPoint == null || _hasTargetTrackingPoint.Length != count)
                _hasTargetTrackingPoint = new bool[count];
            if (_currentTrackingPositions == null || _currentTrackingPositions.Length != count)
                _currentTrackingPositions = new Vector3[count];
            if (_currentTrackingRotations == null || _currentTrackingRotations.Length != count)
                _currentTrackingRotations = new Quaternion[count];
            if (_hasCurrentTrackingPoint == null || _hasCurrentTrackingPoint.Length != count)
                _hasCurrentTrackingPoint = new bool[count];
        }

        private float GetInterpolationStep()
        {
            float rate = receiveInterpolationRate;
            if (rate <= 0f)
                return 1f;

            float step = rate * Time.deltaTime;
            if (step < 0f)
                return 0f;
            if (step > 1f)
                return 1f;

            return step;
        }

    }

}
