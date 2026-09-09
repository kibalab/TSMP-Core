using UnityEngine;

#if UDONSHARP || COMPILER_UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public enum CompressionMode
    {
        Off = 0,
        RotOnly = 1,
        Full = 2
    }

    public class TSMPNetworkTransformSync : TSMPNetworkBehaviour
    {
        public Transform target;
        public bool useLocalSpace = true;
        public Rigidbody targetRigidbody;
        public bool syncRigidbody = true;

        public CompressionMode compressionMode = CompressionMode.RotOnly;
        public Vector3 quantizedPositionMin = new Vector3(-32f, -8f, -32f);
        public Vector3 quantizedPositionMax = new Vector3(32f, 24f, 32f);
        public Vector3 quantizedScaleMin = new Vector3(0f, 0f, 0f);
        public Vector3 quantizedScaleMax = new Vector3(16f, 16f, 16f);

        [HideInInspector]
        [TransSync("transform.packed")]
#if UDONSHARP || COMPILER_UDONSHARP
        [FieldChangeCallback(nameof(PackedBytes))]
#endif
        public byte[] packedBytes;

        public byte[] PackedBytes
        {
            get => packedBytes;
            set
            {
                packedBytes = value;
            }
        }

        private const byte PackedVersion = 2;
        private const int PackedHeaderBytes = 2;
        private const int PackedPos12Bytes = 5;
        private const int PackedQuaternion12Bytes = 5;

        private const byte FlagHasPosition = 1 << 0;
        private const byte FlagHasRotation = 1 << 1;
        private const byte FlagHasScale = 1 << 2;
        private const byte FlagPositionQuantized = 1 << 3;
        private const byte FlagRotationQuantized = 1 << 4;
        private const byte FlagScaleQuantized = 1 << 5;
        private const byte FlagHasRigidbodyVelocity = 1 << 6;
        private const byte FlagHasRigidbodyAngularVelocity = 1 << 7;

        private bool _hasContinuousTarget;
        private bool _continuousHasPosition;
        private bool _continuousHasRotation;
        private bool _continuousHasScale;
        private Vector3 _continuousPosition;
        private Quaternion _continuousRotation = Quaternion.identity;
        private Vector3 _continuousScale;
        private Vector3 _continuousRigidbodyVelocity;
        private Vector3 _continuousRigidbodyAngularVelocity;
        private bool _continuousHasRigidbodyVelocity;
        private bool _continuousHasRigidbodyAngularVelocity;

        private void Start()
        {
            if (target == null)
                target = transform;
            ResolveRigidbody();
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public override void PostLateUpdate()
        {
            ApplyContinuousTarget();
        }
#else
        private void LateUpdate()
        {
            ApplyContinuousTarget();
        }
#endif

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            if (target == null)
                target = transform;
            ResolveRigidbody();

            byte flags = ComputeFlags();
            int totalLength = PackedHeaderBytes + ComputeBodyLength(flags);

            if (packedBytes == null || packedBytes.Length != totalLength)
                packedBytes = new byte[totalLength];

            packedBytes[0] = PackedVersion;
            packedBytes[1] = flags;

            int cursor = PackedHeaderBytes;

            if ((flags & FlagHasPosition) != 0)
            {
                Vector3 pos = useLocalSpace ? target.localPosition : target.position;
                if ((flags & FlagPositionQuantized) != 0)
                {
                    WritePackedPos12(pos, quantizedPositionMin, GetSafeRangeMax(quantizedPositionMin, quantizedPositionMax), packedBytes, cursor);
                    cursor += PackedPos12Bytes;
                }
                else
                {
                    cursor = Binary.WriteVector3Float32LE(packedBytes, cursor, pos);
                }
            }

            if ((flags & FlagHasRotation) != 0)
            {
                Quaternion rot = useLocalSpace ? target.localRotation : target.rotation;
                if ((flags & FlagRotationQuantized) != 0)
                {
                    Binary.WritePackedQuaternion12LE(packedBytes, cursor, rot);
                    cursor += PackedQuaternion12Bytes;
                }
                else
                {
                    cursor = Binary.WriteQuaternionFloat32LE(packedBytes, cursor, rot);
                }
            }

            if ((flags & FlagHasScale) != 0)
            {
                Vector3 scale = target.localScale;
                if ((flags & FlagScaleQuantized) != 0)
                {
                    WritePackedPos12(scale, quantizedScaleMin, GetSafeRangeMax(quantizedScaleMin, quantizedScaleMax), packedBytes, cursor);
                    cursor += PackedPos12Bytes;
                }
                else
                {
                    cursor = Binary.WriteVector3Float32LE(packedBytes, cursor, scale);
                }
            }

            if ((flags & FlagHasRigidbodyVelocity) != 0)
            {
                Vector3 velocity = targetRigidbody.velocity;
                cursor = Binary.WriteVector3Float32LE(packedBytes, cursor, velocity);
            }

            if ((flags & FlagHasRigidbodyAngularVelocity) != 0)
            {
                Vector3 angularVelocity = targetRigidbody.angularVelocity;
                cursor = Binary.WriteVector3Float32LE(packedBytes, cursor, angularVelocity);
            }
        }

        private void ApplyPacked()
        {
            if (!IsTSMPActive())
                return;

            if (packedBytes == null || packedBytes.Length < PackedHeaderBytes)
                return;

            byte version = packedBytes[0];
            if (version != 1 && version != PackedVersion)
                return;

            byte flags = packedBytes[1];
            int expected = PackedHeaderBytes + ComputeBodyLength(flags);
            if (packedBytes.Length != expected)
                return;

            if (target == null)
                target = transform;
            ResolveRigidbody();

            int cursor = PackedHeaderBytes;
            bool hasAppliedPosition = false;
            bool hasAppliedRotation = false;
            bool continuous = receiveInterpolation == ReceiveInterpolationMode.Continuous;
            Vector3 appliedPosition = Vector3.zero;
            Quaternion appliedRotation = Quaternion.identity;
            if (continuous)
            {
                _continuousHasPosition = false;
                _continuousHasRotation = false;
                _continuousHasScale = false;
                _continuousHasRigidbodyVelocity = false;
                _continuousHasRigidbodyAngularVelocity = false;
            }

            if ((flags & FlagHasPosition) != 0)
            {
                Vector3 pos;
                if ((flags & FlagPositionQuantized) != 0)
                {
                    pos = ReadPackedPos12(packedBytes, cursor, quantizedPositionMin, GetSafeRangeMax(quantizedPositionMin, quantizedPositionMax));
                    cursor += PackedPos12Bytes;
                }
                else
                {
                    pos = Binary.ReadVector3Float32LE(packedBytes, cursor);
                    cursor += NetworkFrameProtocol.Vector3Bytes;
                }

                hasAppliedPosition = true;
                appliedPosition = GetWorldPosition(pos);
                if (continuous)
                {
                    _continuousHasPosition = true;
                    _continuousPosition = pos;
                }
                else if (!syncRigidbody || targetRigidbody == null)
                {
                    if (useLocalSpace)
                        target.localPosition = pos;
                    else
                        target.position = pos;
                }
            }

            if ((flags & FlagHasRotation) != 0)
            {
                Quaternion rot;
                if ((flags & FlagRotationQuantized) != 0)
                {
                    rot = Binary.ReadPackedQuaternion12LE(packedBytes, cursor);
                    cursor += PackedQuaternion12Bytes;
                }
                else
                {
                    rot = Binary.ReadQuaternionFloat32LE(packedBytes, cursor);
                    cursor += NetworkFrameProtocol.QuaternionBytes;
                }

                hasAppliedRotation = true;
                appliedRotation = GetWorldRotation(rot);
                if (continuous)
                {
                    _continuousHasRotation = true;
                    _continuousRotation = rot;
                }
                else if (!syncRigidbody || targetRigidbody == null)
                {
                    if (useLocalSpace)
                        target.localRotation = rot;
                    else
                        target.rotation = rot;
                }
            }

            if ((flags & FlagHasScale) != 0)
            {
                Vector3 scale;
                if ((flags & FlagScaleQuantized) != 0)
                {
                    scale = ReadPackedPos12(packedBytes, cursor, quantizedScaleMin, GetSafeRangeMax(quantizedScaleMin, quantizedScaleMax));
                    cursor += PackedPos12Bytes;
                }
                else
                {
                    scale = Binary.ReadVector3Float32LE(packedBytes, cursor);
                    cursor += NetworkFrameProtocol.Vector3Bytes;
                }

                if (continuous)
                {
                    _continuousHasScale = true;
                    _continuousScale = scale;
                }
                else
                {
                    target.localScale = scale;
                }
            }

            if (!continuous && syncRigidbody && targetRigidbody != null)
            {
                if (hasAppliedPosition)
                    targetRigidbody.position = appliedPosition;
                if (hasAppliedRotation)
                    targetRigidbody.rotation = appliedRotation;
            }

            if ((flags & FlagHasRigidbodyVelocity) != 0)
            {
                Vector3 velocity = Binary.ReadVector3Float32LE(packedBytes, cursor);
                cursor += NetworkFrameProtocol.Vector3Bytes;

                if (targetRigidbody != null)
                {
                    if (continuous)
                    {
                        _continuousHasRigidbodyVelocity = true;
                        _continuousRigidbodyVelocity = velocity;
                    }
                    else
                    {
                        targetRigidbody.velocity = velocity;
                    }
                }
            }

            if ((flags & FlagHasRigidbodyAngularVelocity) != 0)
            {
                Vector3 angularVelocity = Binary.ReadVector3Float32LE(packedBytes, cursor);
                cursor += NetworkFrameProtocol.Vector3Bytes;

                if (targetRigidbody != null)
                {
                    if (continuous)
                    {
                        _continuousHasRigidbodyAngularVelocity = true;
                        _continuousRigidbodyAngularVelocity = angularVelocity;
                    }
                    else
                    {
                        targetRigidbody.angularVelocity = angularVelocity;
                    }
                }
            }

            if (continuous)
                _hasContinuousTarget = _continuousHasPosition || _continuousHasRotation || _continuousHasScale || _continuousHasRigidbodyVelocity || _continuousHasRigidbodyAngularVelocity;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyPacked();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ApplyContinuousTarget()
        {
            if (receiveInterpolation != ReceiveInterpolationMode.Continuous || !_hasContinuousTarget || !IsTSMPActive())
                return;

            if (target == null)
                target = transform;
            ResolveRigidbody();

            float step = GetReceiveInterpolationStep();
            if (_continuousHasPosition)
            {
                Vector3 worldPosition = GetWorldPosition(_continuousPosition);
                if (syncRigidbody && targetRigidbody != null)
                {
                    targetRigidbody.position = Vector3.Lerp(targetRigidbody.position, worldPosition, step);
                }
                else if (useLocalSpace)
                {
                    target.localPosition = Vector3.Lerp(target.localPosition, _continuousPosition, step);
                }
                else
                {
                    target.position = Vector3.Lerp(target.position, _continuousPosition, step);
                }
            }

            if (_continuousHasRotation)
            {
                Quaternion worldRotation = GetWorldRotation(_continuousRotation);
                if (syncRigidbody && targetRigidbody != null)
                {
                    targetRigidbody.rotation = Quaternion.Slerp(targetRigidbody.rotation, worldRotation, step);
                }
                else if (useLocalSpace)
                {
                    target.localRotation = Quaternion.Slerp(target.localRotation, _continuousRotation, step);
                }
                else
                {
                    target.rotation = Quaternion.Slerp(target.rotation, _continuousRotation, step);
                }
            }

            if (_continuousHasScale)
                target.localScale = Vector3.Lerp(target.localScale, _continuousScale, step);

            if (targetRigidbody != null)
            {
                if (_continuousHasRigidbodyVelocity)
                    targetRigidbody.velocity = Vector3.Lerp(targetRigidbody.velocity, _continuousRigidbodyVelocity, step);
                if (_continuousHasRigidbodyAngularVelocity)
                    targetRigidbody.angularVelocity = Vector3.Lerp(targetRigidbody.angularVelocity, _continuousRigidbodyAngularVelocity, step);
            }
        }

        private byte ComputeFlags()
        {
            byte flags = FlagHasPosition | FlagHasRotation | FlagHasScale;

            if (compressionMode == CompressionMode.Full)
                flags |= FlagPositionQuantized;

            if (compressionMode != CompressionMode.Off)
                flags |= FlagRotationQuantized;

            if (compressionMode == CompressionMode.Full)
                flags |= FlagScaleQuantized;

            if (syncRigidbody && targetRigidbody != null)
            {
                flags |= FlagHasRigidbodyVelocity;
                flags |= FlagHasRigidbodyAngularVelocity;
            }

            return flags;
        }

        private static int ComputeBodyLength(byte flags)
        {
            int len = 0;
            if ((flags & FlagHasPosition) != 0)
                len += (flags & FlagPositionQuantized) != 0 ? PackedPos12Bytes : 12;
            if ((flags & FlagHasRotation) != 0)
                len += (flags & FlagRotationQuantized) != 0 ? PackedQuaternion12Bytes : 16;
            if ((flags & FlagHasScale) != 0)
                len += (flags & FlagScaleQuantized) != 0 ? PackedPos12Bytes : 12;
            if ((flags & FlagHasRigidbodyVelocity) != 0)
                len += 12;
            if ((flags & FlagHasRigidbodyAngularVelocity) != 0)
                len += 12;
            return len;
        }

        private void ResolveRigidbody()
        {
            if (targetRigidbody != null)
                return;

            Transform source = target != null ? target : transform;
            if (source != null)
                targetRigidbody = source.GetComponent<Rigidbody>();
        }

        private Vector3 GetWorldPosition(Vector3 value)
        {
            if (!useLocalSpace)
                return value;

            Transform parent = target != null ? target.parent : null;
            if (parent == null)
                return value;

            return parent.TransformPoint(value);
        }

        private Quaternion GetWorldRotation(Quaternion value)
        {
            if (!useLocalSpace)
                return value;

            Transform parent = target != null ? target.parent : null;
            if (parent == null)
                return value;

            return parent.rotation * value;
        }

        private static Vector3 GetSafeRangeMax(Vector3 min, Vector3 max)
        {
            return new Vector3(
                max.x > min.x ? max.x : min.x + 0.0001f,
                max.y > min.y ? max.y : min.y + 0.0001f,
                max.z > min.z ? max.z : min.z + 0.0001f);
        }

        private static void WritePackedPos12(Vector3 position, Vector3 rangeMin, Vector3 rangeMax, byte[] buffer, int offset)
        {
            uint x = QuantizePos12Component(position.x, rangeMin.x, rangeMax.x);
            uint y = QuantizePos12Component(position.y, rangeMin.y, rangeMax.y);
            uint z = QuantizePos12Component(position.z, rangeMin.z, rangeMax.z);

            buffer[offset] = (byte)(x & 0xFFu);
            buffer[offset + 1] = (byte)(((x >> 8) & 0x0Fu) | ((y & 0x0Fu) << 4));
            buffer[offset + 2] = (byte)((y >> 4) & 0xFFu);
            buffer[offset + 3] = (byte)(z & 0xFFu);
            buffer[offset + 4] = (byte)((z >> 8) & 0x0Fu);
        }

        private static Vector3 ReadPackedPos12(byte[] buffer, int offset, Vector3 rangeMin, Vector3 rangeMax)
        {
            uint x = (uint)buffer[offset] | (((uint)buffer[offset + 1] & 0x0Fu) << 8);
            uint y = ((uint)buffer[offset + 1] >> 4) & 0x0Fu | ((uint)buffer[offset + 2] << 4);
            uint z = (uint)buffer[offset + 3] | (((uint)buffer[offset + 4] & 0x0Fu) << 8);

            return new Vector3(
                DequantizePos12Component(x, rangeMin.x, rangeMax.x),
                DequantizePos12Component(y, rangeMin.y, rangeMax.y),
                DequantizePos12Component(z, rangeMin.z, rangeMax.z));
        }

        private static uint QuantizePos12Component(float value, float min, float max)
        {
            if (max <= min)
                return 0u;

            float normalized = (value - min) / (max - min);
            if (normalized < 0f)
                normalized = 0f;
            else if (normalized > 1f)
                normalized = 1f;

            return (uint)Mathf.RoundToInt(normalized * 4095f);
        }

        private static float DequantizePos12Component(uint value, float min, float max)
        {
            if (value > 4095u)
                value = 4095u;

            float normalized = value / 4095f;
            return Mathf.Lerp(min, max, normalized);
        }

    }
}
