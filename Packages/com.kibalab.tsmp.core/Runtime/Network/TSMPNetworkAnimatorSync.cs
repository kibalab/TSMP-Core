using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public enum AnimatorParameterType : byte
    {
        Float = 1,
        Int = 2,
        Bool = 3
    }

    public class TSMPNetworkAnimatorSync : TSMPNetworkBehaviour
    {
        public Animator animator;
        public string[] parameterNames;
        public byte[] parameterTypes;
        public int[] layerIndices;
        public float layerFadeDuration;
        public float normalizedTimeApplyThreshold = 0.02f;
        [HideInInspector] public int encodedParameterCount;
        [HideInInspector] public int encodedLayerCount;
        [HideInInspector] public int encodedAnimatorBytes;

        [HideInInspector]
        [TransSync("animator.packed")]
#if UDONSHARP
        [FieldChangeCallback(nameof(AnimatorBytes))]
#endif
        public byte[] animatorBytes;

        private const byte PacketVersion = 1;
        private const int HeaderBytes = 5;
        private const byte FlagHasParameters = 1 << 0;
        private const byte FlagHasLayers = 1 << 1;
        private const int ParameterHeaderBytes = 5;
        private const int BoolValueBytes = 1;
        private const int IntValueBytes = 4;
        private const int FloatValueBytes = 4;
        private const int LayerBytes = 13;

        private int[] _parameterHashes;
        private int _parameterHashLength = -1;

        public byte[] AnimatorBytes
        {
            get => animatorBytes;
            set
            {
                animatorBytes = value;
            }
        }

        private void Start()
        {
            ResolveAnimator();
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveAnimator();
            if (animator == null)
                return;

            EnsureParameterHashes();

            int parameterCount = GetValidParameterCount();
            int layerCount = GetValidLayerCount();
            byte flags = 0;
            if (parameterCount > 0)
                flags |= FlagHasParameters;
            if (layerCount > 0)
                flags |= FlagHasLayers;

            int requiredBytes = HeaderBytes + ComputeParameterBytes() + layerCount * LayerBytes;
            if (animatorBytes == null || animatorBytes.Length != requiredBytes)
                animatorBytes = new byte[requiredBytes];

            animatorBytes[0] = PacketVersion;
            animatorBytes[1] = flags;
            animatorBytes[2] = (byte)(parameterCount & 0xFF);
            animatorBytes[3] = (byte)(layerCount & 0xFF);
            animatorBytes[4] = 0;

            int cursor = HeaderBytes;
            int writtenParameters = 0;
            if (parameterNames != null && parameterTypes != null)
            {
                int count = parameterNames.Length < parameterTypes.Length ? parameterNames.Length : parameterTypes.Length;
                for (int i = 0; i < count; i++)
                {
                    if (!IsValidParameter(i))
                        continue;

                    int hash = _parameterHashes[i];
                    byte type = parameterTypes[i];
                    Binary.WriteInt32LE(animatorBytes, cursor, hash);
                    cursor += 4;
                    animatorBytes[cursor++] = type;

                    if (type == (byte)AnimatorParameterType.Bool)
                    {
                        animatorBytes[cursor++] = animator.GetBool(parameterNames[i]) ? (byte)1 : (byte)0;
                    }
                    else if (type == (byte)AnimatorParameterType.Int)
                    {
                        Binary.WriteInt32LE(animatorBytes, cursor, animator.GetInteger(parameterNames[i]));
                        cursor += 4;
                    }
                    else
                    {
                        cursor = Binary.WriteFloat32LE(animatorBytes, cursor, animator.GetFloat(parameterNames[i]));
                    }

                    writtenParameters++;
                }
            }

            int writtenLayers = 0;
            if (layerIndices != null)
            {
                for (int i = 0; i < layerIndices.Length; i++)
                {
                    int layer = layerIndices[i];
                    if (layer < 0 || layer >= animator.layerCount)
                        continue;

                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
                    animatorBytes[cursor++] = (byte)(layer & 0xFF);
                    Binary.WriteInt32LE(animatorBytes, cursor, state.fullPathHash);
                    cursor += 4;
                    cursor = Binary.WriteFloat32LE(animatorBytes, cursor, state.normalizedTime);
                    cursor = Binary.WriteFloat32LE(animatorBytes, cursor, animator.GetLayerWeight(layer));
                    writtenLayers++;
                }
            }

            animatorBytes[2] = (byte)(writtenParameters & 0xFF);
            animatorBytes[3] = (byte)(writtenLayers & 0xFF);
            encodedParameterCount = writtenParameters;
            encodedLayerCount = writtenLayers;
            encodedAnimatorBytes = cursor;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyAnimatorBytes();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ApplyAnimatorBytes()
        {
            if (!IsTSMPActive() || animatorBytes == null || animatorBytes.Length < HeaderBytes)
                return;

            ResolveAnimator();
            if (animator == null || animatorBytes[0] != PacketVersion)
                return;

            EnsureParameterHashes();

            byte flags = animatorBytes[1];
            int parameterCount = animatorBytes[2];
            int layerCount = animatorBytes[3];
            int cursor = HeaderBytes;

            if ((flags & FlagHasParameters) != 0)
            {
                for (int i = 0; i < parameterCount; i++)
                {
                    if (cursor + ParameterHeaderBytes > animatorBytes.Length)
                        return;

                    int hash = Binary.ReadInt32LE(animatorBytes, cursor);
                    cursor += 4;
                    byte type = animatorBytes[cursor++];

                    if (type == (byte)AnimatorParameterType.Bool)
                    {
                        if (cursor + BoolValueBytes > animatorBytes.Length)
                            return;
                        int index = FindParameterIndex(hash, (byte)AnimatorParameterType.Bool);
                        bool value = animatorBytes[cursor++] != 0;
                        if (index >= 0)
                            animator.SetBool(parameterNames[index], value);
                    }
                    else if (type == (byte)AnimatorParameterType.Int)
                    {
                        if (cursor + IntValueBytes > animatorBytes.Length)
                            return;
                        int index = FindParameterIndex(hash, (byte)AnimatorParameterType.Int);
                        int value = Binary.ReadInt32LE(animatorBytes, cursor);
                        cursor += 4;
                        if (index >= 0)
                            animator.SetInteger(parameterNames[index], value);
                    }
                    else if (type == (byte)AnimatorParameterType.Float)
                    {
                        if (cursor + FloatValueBytes > animatorBytes.Length)
                            return;
                        int index = FindParameterIndex(hash, (byte)AnimatorParameterType.Float);
                        float value = Binary.ReadFloat32LE(animatorBytes, cursor);
                        cursor += 4;
                        if (index >= 0)
                            animator.SetFloat(parameterNames[index], value);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            if ((flags & FlagHasLayers) != 0)
            {
                for (int i = 0; i < layerCount; i++)
                {
                    if (cursor + LayerBytes > animatorBytes.Length)
                        return;

                    int layer = animatorBytes[cursor++];
                    int stateHash = Binary.ReadInt32LE(animatorBytes, cursor);
                    cursor += 4;
                    float normalizedTime = Binary.ReadFloat32LE(animatorBytes, cursor);
                    cursor += 4;
                    float weight = Binary.ReadFloat32LE(animatorBytes, cursor);
                    cursor += 4;

                    if (layer < 0 || layer >= animator.layerCount)
                        continue;

                    animator.SetLayerWeight(layer, weight);
                    AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
                    float currentTime = current.normalizedTime;
                    float delta = Mathf.Abs((currentTime - Mathf.Floor(currentTime)) - (normalizedTime - Mathf.Floor(normalizedTime)));
                    if (current.fullPathHash != stateHash || delta > normalizedTimeApplyThreshold)
                    {
                        if (layerFadeDuration > 0f)
                            animator.CrossFade(stateHash, layerFadeDuration, layer, normalizedTime);
                        else
                            animator.Play(stateHash, layer, normalizedTime);
                    }
                }
            }
        }

        private void ResolveAnimator()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void EnsureParameterHashes()
        {
            int length = parameterNames != null ? parameterNames.Length : 0;
            if (_parameterHashes != null && _parameterHashLength == length)
                return;

            _parameterHashes = new int[length];
            for (int i = 0; i < length; i++)
                _parameterHashes[i] = string.IsNullOrEmpty(parameterNames[i]) ? 0 : Animator.StringToHash(parameterNames[i]);
            _parameterHashLength = length;
        }

        private int GetValidParameterCount()
        {
            if (parameterNames == null || parameterTypes == null)
                return 0;

            int count = parameterNames.Length < parameterTypes.Length ? parameterNames.Length : parameterTypes.Length;
            if (count > 255)
                count = 255;

            int valid = 0;
            for (int i = 0; i < count; i++)
            {
                if (IsValidParameter(i))
                    valid++;
            }

            return valid;
        }

        private int GetValidLayerCount()
        {
            if (layerIndices == null || animator == null)
                return 0;

            int max = layerIndices.Length;
            if (max > 255)
                max = 255;

            int valid = 0;
            for (int i = 0; i < max; i++)
            {
                int layer = layerIndices[i];
                if (layer >= 0 && layer < animator.layerCount)
                    valid++;
            }

            return valid;
        }

        private int ComputeParameterBytes()
        {
            if (parameterNames == null || parameterTypes == null)
                return 0;

            int count = parameterNames.Length < parameterTypes.Length ? parameterNames.Length : parameterTypes.Length;
            if (count > 255)
                count = 255;

            int bytes = 0;
            for (int i = 0; i < count; i++)
            {
                if (!IsValidParameter(i))
                    continue;

                byte type = parameterTypes[i];
                bytes += ParameterHeaderBytes;
                if (type == (byte)AnimatorParameterType.Bool)
                    bytes += BoolValueBytes;
                else
                    bytes += 4;
            }

            return bytes;
        }

        private bool IsValidParameter(int index)
        {
            if (parameterNames == null || parameterTypes == null || index < 0 || index >= parameterNames.Length || index >= parameterTypes.Length)
                return false;

            if (string.IsNullOrEmpty(parameterNames[index]))
                return false;

            byte type = parameterTypes[index];
            return type == (byte)AnimatorParameterType.Bool || type == (byte)AnimatorParameterType.Int || type == (byte)AnimatorParameterType.Float;
        }

        private int FindParameterIndex(int hash, byte type)
        {
            if (_parameterHashes == null || parameterTypes == null)
                return -1;

            int count = _parameterHashes.Length < parameterTypes.Length ? _parameterHashes.Length : parameterTypes.Length;
            for (int i = 0; i < count; i++)
            {
                if (_parameterHashes[i] == hash && parameterTypes[i] == type)
                    return i;
            }

            return -1;
        }

    }
}
