using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPNetworkBlendShapesSync : TSMPNetworkBehaviour
    {
        public SkinnedMeshRenderer targetRenderer;
        public int[] blendShapeIndices;
        public string[] blendShapeNames;
        [HideInInspector] public int blendShapeCount;
        [HideInInspector] public int encodedBlendShapeCount;

        [HideInInspector]
        [TransSync("blendshapes.packed")]
#if UDONSHARP
        [FieldChangeCallback(nameof(BlendShapeBytes))]
#endif
        public byte[] blendShapeBytes;

        private const byte BlendShapeVersion = 1;
        private const int BlendShapeHeaderBytes = 2;
        private const int BlendShapeEntryBytes = 3;
        private const int MaxEncodedBlendShapes = 255;

        private bool[] _selectedBlendShapeLookup;
        private int[] _validBlendShapeIndices;
        private int _selectedBlendShapeCount;
        private int _cachedBlendShapeIndexLength = -1;
        private int _cachedBlendShapeIndexHash;
        private int _cachedBlendShapeCount = -2;
        private int[] _lastAppliedBlendShapeValues;
        private float[] _targetBlendShapeValues;
        private bool[] _hasTargetBlendShapeValue;
        private bool _hasContinuousTarget;

        public byte[] BlendShapeBytes
        {
            get => blendShapeBytes;
            set
            {
                blendShapeBytes = value;
            }
        }

        private void Start()
        {
            ResolveRenderer();
            RefreshBlendShapeCount();
        }

#if UDONSHARP
        public override void PostLateUpdate()
        {
            ApplyContinuousBlendShapes();
        }
#else
        private void LateUpdate()
        {
            ApplyContinuousBlendShapes();
        }
#endif

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveRenderer();
            RefreshBlendShapeCount();
            RebuildBlendShapeLookupIfNeeded();

            int count = targetRenderer != null ? _selectedBlendShapeCount : 0;
            int requiredBytes = BlendShapeHeaderBytes + count * BlendShapeEntryBytes;
            if (blendShapeBytes == null || blendShapeBytes.Length != requiredBytes)
                blendShapeBytes = new byte[requiredBytes];

            blendShapeBytes[0] = BlendShapeVersion;
            blendShapeBytes[1] = 0;

            int cursor = BlendShapeHeaderBytes;
            int written = 0;
            SkinnedMeshRenderer renderer = targetRenderer;
            if (renderer != null && _validBlendShapeIndices != null)
            {
                for (int i = 0; i < _selectedBlendShapeCount; i++)
                {
                    int index = _validBlendShapeIndices[i];

                    Binary.WriteUInt16LE(blendShapeBytes, cursor, (ushort)index);
                    cursor += 2;
                    int value = Mathf.RoundToInt(renderer.GetBlendShapeWeight(index));
                    if (value < 0)
                        value = 0;
                    else if (value > 100)
                        value = 100;
                    blendShapeBytes[cursor++] = (byte)value;
                    written++;
                }
            }

            blendShapeBytes[1] = (byte)written;
            encodedBlendShapeCount = written;
        }

        private void ApplyBlendShapes()
        {
            if (!IsTSMPActive() || blendShapeBytes == null || blendShapeBytes.Length < BlendShapeHeaderBytes)
                return;

            if (blendShapeBytes[0] != BlendShapeVersion)
                return;

            ResolveRenderer();
            RefreshBlendShapeCount();
            RebuildBlendShapeLookupIfNeeded();

            if (targetRenderer == null)
                return;

            int count = blendShapeBytes[1];
            int requiredBytes = BlendShapeHeaderBytes + count * BlendShapeEntryBytes;
            if (blendShapeBytes.Length < requiredBytes)
                return;

            int cursor = BlendShapeHeaderBytes;
            bool continuous = receiveInterpolation == ReceiveInterpolationMode.Continuous;
            for (int i = 0; i < count; i++)
            {
                int index = Binary.ReadUInt16LE(blendShapeBytes, cursor);
                cursor += 2;
                int value = blendShapeBytes[cursor++];

                if (!IsSelectedBlendShape(index) || !IsValidBlendShapeIndex(index))
                    continue;

                if (continuous)
                {
                    if (_targetBlendShapeValues != null && _hasTargetBlendShapeValue != null && index < _targetBlendShapeValues.Length)
                    {
                        _targetBlendShapeValues[index] = value;
                        _hasTargetBlendShapeValue[index] = true;
                        _hasContinuousTarget = true;
                    }

                    continue;
                }

                if (_lastAppliedBlendShapeValues != null && index < _lastAppliedBlendShapeValues.Length && _lastAppliedBlendShapeValues[index] == value)
                    continue;

                targetRenderer.SetBlendShapeWeight(index, value);
                if (_lastAppliedBlendShapeValues != null && index < _lastAppliedBlendShapeValues.Length)
                    _lastAppliedBlendShapeValues[index] = value;
            }
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyBlendShapes();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ResolveRenderer()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<SkinnedMeshRenderer>();
        }

        private void RefreshBlendShapeCount()
        {
#if !COMPILER_UDONSHARP
            if (targetRenderer != null && targetRenderer.sharedMesh != null)
                blendShapeCount = targetRenderer.sharedMesh.blendShapeCount;
            else
                blendShapeCount = 0;
#endif
        }

        private bool IsSelectedBlendShape(int index)
        {
            if (index < 0 || _selectedBlendShapeLookup == null || index >= _selectedBlendShapeLookup.Length)
                return false;

            return _selectedBlendShapeLookup[index];
        }

        private void RebuildBlendShapeLookupIfNeeded()
        {
            int indexLength = blendShapeIndices != null ? blendShapeIndices.Length : -1;
            int indexHash = ComputeBlendShapeIndexHash();
            if (_selectedBlendShapeLookup != null && _cachedBlendShapeIndexLength == indexLength && _cachedBlendShapeIndexHash == indexHash && _cachedBlendShapeCount == blendShapeCount)
                return;

            int lookupLength = blendShapeCount > 0 ? blendShapeCount : GetMaxSelectedBlendShapeIndex() + 1;
            if (lookupLength < 1)
                lookupLength = 1;

            if (_selectedBlendShapeLookup == null || _selectedBlendShapeLookup.Length != lookupLength)
                _selectedBlendShapeLookup = new bool[lookupLength];
            if (_lastAppliedBlendShapeValues == null || _lastAppliedBlendShapeValues.Length != lookupLength)
                _lastAppliedBlendShapeValues = ArrayUtil.CreateFilledIntArray(lookupLength, -1);
            if (_targetBlendShapeValues == null || _targetBlendShapeValues.Length != lookupLength)
                _targetBlendShapeValues = new float[lookupLength];
            if (_hasTargetBlendShapeValue == null || _hasTargetBlendShapeValue.Length != lookupLength)
                _hasTargetBlendShapeValue = new bool[lookupLength];
            if (_validBlendShapeIndices == null || _validBlendShapeIndices.Length != MaxEncodedBlendShapes)
                _validBlendShapeIndices = new int[MaxEncodedBlendShapes];

            for (int i = 0; i < _selectedBlendShapeLookup.Length; i++)
                _selectedBlendShapeLookup[i] = false;

            int count = 0;
            if (blendShapeIndices == null)
            {
                _selectedBlendShapeCount = 0;
                _cachedBlendShapeIndexLength = indexLength;
                _cachedBlendShapeIndexHash = indexHash;
                _cachedBlendShapeCount = blendShapeCount;
                return;
            }

            for (int i = 0; i < blendShapeIndices.Length; i++)
            {
                int index = blendShapeIndices[i];
                if (!IsValidBlendShapeIndex(index) || index >= _selectedBlendShapeLookup.Length || _selectedBlendShapeLookup[index])
                    continue;

                _selectedBlendShapeLookup[index] = true;
                _validBlendShapeIndices[count] = index;
                count++;
                if (count >= MaxEncodedBlendShapes)
                    break;
            }

            _selectedBlendShapeCount = count;
            _cachedBlendShapeIndexLength = indexLength;
            _cachedBlendShapeIndexHash = indexHash;
            _cachedBlendShapeCount = blendShapeCount;
        }

        private int ComputeBlendShapeIndexHash()
        {
            if (blendShapeIndices == null)
                return 0;

            int hash = 17;
            for (int i = 0; i < blendShapeIndices.Length; i++)
                hash = hash * 31 + blendShapeIndices[i];
            return hash;
        }

        private int GetMaxSelectedBlendShapeIndex()
        {
            if (blendShapeIndices == null)
                return -1;

            int max = -1;
            for (int i = 0; i < blendShapeIndices.Length; i++)
            {
                if (blendShapeIndices[i] > max)
                    max = blendShapeIndices[i];
            }

            return max;
        }

        private bool IsValidBlendShapeIndex(int index)
        {
            if (index < 0)
                return false;

            if (blendShapeCount <= 0)
                return blendShapeIndices != null && index <= GetMaxSelectedBlendShapeIndex();

            return index < blendShapeCount;
        }

        private void ApplyContinuousBlendShapes()
        {
            if (receiveInterpolation != ReceiveInterpolationMode.Continuous || !_hasContinuousTarget || !IsTSMPActive() || targetRenderer == null || _targetBlendShapeValues == null || _hasTargetBlendShapeValue == null)
                return;

            float step = GetReceiveInterpolationStep();
            bool hasAnyTarget = false;
            int count = _targetBlendShapeValues.Length < _hasTargetBlendShapeValue.Length ? _targetBlendShapeValues.Length : _hasTargetBlendShapeValue.Length;
            for (int i = 0; i < count; i++)
            {
                if (!_hasTargetBlendShapeValue[i])
                    continue;

                float current = targetRenderer.GetBlendShapeWeight(i);
                float targetValue = _targetBlendShapeValues[i];
                if (Mathf.Abs(current - targetValue) <= 0.01f)
                {
                    targetRenderer.SetBlendShapeWeight(i, targetValue);
                    _hasTargetBlendShapeValue[i] = false;
                    if (_lastAppliedBlendShapeValues != null && i < _lastAppliedBlendShapeValues.Length)
                        _lastAppliedBlendShapeValues[i] = Mathf.RoundToInt(targetValue);
                    continue;
                }

                targetRenderer.SetBlendShapeWeight(i, Mathf.Lerp(current, targetValue, step));
                hasAnyTarget = true;
            }

            _hasContinuousTarget = hasAnyTarget;
        }

    }
}
