using UnityEngine;

#if UDONSHARP || COMPILER_UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPNetworkBlendShapesSync : TSMPNetworkBehaviour
    {
        public SkinnedMeshRenderer targetRenderer;
        public int[] blendShapeIndices;
        public string[] blendShapeNames;
        [Tooltip("Validate every selected BlendShape index on receive and interpolation. Disable for fixed selections; call _InvalidateBlendShapeCache after editing the existing index array in place. Renderer, mesh and array replacement are still detected. Encoding always validates.")]
        public bool validateBlendShapeCacheOnReceive = true;
        [HideInInspector] public int blendShapeCount;
        [HideInInspector] public int encodedBlendShapeCount;

        [HideInInspector]
        [TransSync("blendshapes.packed")]
#if UDONSHARP || COMPILER_UDONSHARP
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
        private int[] _cachedBlendShapeIndices;
        private int _cachedBlendShapeIndexLength = -1;
        private int _cachedBlendShapeIndexHash;
        private int _cachedBlendShapeCount = -2;
        private float[] _targetBlendShapeValues;
        private bool[] _hasTargetBlendShapeValue;
        private bool _hasContinuousTarget;
        private SkinnedMeshRenderer _cachedRenderer;
        private Mesh _cachedMesh;
        private bool _snapNextReceive;

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

        private void OnDisable()
        {
            ClearContinuousTargets();
        }

#if UDONSHARP || COMPILER_UDONSHARP
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

        public override void ResetReceiveState()
        {
            _hasContinuousTarget = false;
            if (_targetBlendShapeValues != null)
            {
                int count = _targetBlendShapeValues.Length;
                for (int i = 0; i < count; i++)
                    _targetBlendShapeValues[i] = 0f;
            }

            if (_hasTargetBlendShapeValue != null)
            {
                int count = _hasTargetBlendShapeValue.Length;
                for (int i = 0; i < count; i++)
                    _hasTargetBlendShapeValue[i] = false;
            }

            _snapNextReceive = true;
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveRenderer();
            RefreshBlendShapeCount();
            RebuildBlendShapeLookupIfNeeded(true);

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
            RebuildBlendShapeLookupIfNeeded(false);

            if (targetRenderer == null)
                return;

            int count = blendShapeBytes[1];
            int requiredBytes = BlendShapeHeaderBytes + count * BlendShapeEntryBytes;
            if (blendShapeBytes.Length < requiredBytes)
                return;

            int cursor = BlendShapeHeaderBytes;
            bool appliedAny = false;
            bool continuous = receiveInterpolation == ReceiveInterpolationMode.Continuous && !_snapNextReceive;
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
                        appliedAny = true;
                    }

                    continue;
                }

                if (targetRenderer.GetBlendShapeWeight(index) == (float)value)
                {
                    appliedAny = true;
                    continue;
                }

                targetRenderer.SetBlendShapeWeight(index, value);
                appliedAny = true;
            }

            if (_snapNextReceive && appliedAny)
                _snapNextReceive = false;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation != ReceiveInterpolationMode.Continuous)
                ClearContinuousTargets();
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
            Mesh mesh = targetRenderer != null ? targetRenderer.sharedMesh : null;
            int count = mesh != null ? mesh.blendShapeCount : 0;
            if ((Object)_cachedRenderer != (Object)targetRenderer || (Object)_cachedMesh != (Object)mesh || blendShapeCount != count)
            {
                ClearContinuousTargets();
                _cachedRenderer = targetRenderer;
                _cachedMesh = mesh;
                _cachedBlendShapeCount = -2;
            }
            blendShapeCount = count;
        }

        private bool IsSelectedBlendShape(int index)
        {
            if (index < 0 || _selectedBlendShapeLookup == null || index >= _selectedBlendShapeLookup.Length)
                return false;

            return _selectedBlendShapeLookup[index];
        }

        // Local-only invalidation for in-place selection edits while validation is disabled.
        public void _InvalidateBlendShapeCache()
        {
            _cachedBlendShapeCount = -2;
        }

        private void RebuildBlendShapeLookupIfNeeded(bool forceValidation)
        {
            int indexLength = blendShapeIndices != null ? blendShapeIndices.Length : -1;
            if (!forceValidation && !validateBlendShapeCacheOnReceive && _selectedBlendShapeLookup != null &&
                _cachedBlendShapeIndices == blendShapeIndices && _cachedBlendShapeIndexLength == indexLength &&
                _cachedBlendShapeCount == blendShapeCount)
                return;

            _cachedBlendShapeIndices = blendShapeIndices;
            int indexHash = ComputeBlendShapeIndexHash();
            if (_selectedBlendShapeLookup != null && _cachedBlendShapeIndexLength == indexLength && _cachedBlendShapeIndexHash == indexHash && _cachedBlendShapeCount == blendShapeCount)
                return;

            int lookupLength = blendShapeCount;
            if (lookupLength < 1)
                lookupLength = 1;

            if (_selectedBlendShapeLookup == null || _selectedBlendShapeLookup.Length != lookupLength)
                _selectedBlendShapeLookup = new bool[lookupLength];
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
                ClearContinuousTargets();
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
            _hasContinuousTarget = false;
            for (int i = 0; i < _hasTargetBlendShapeValue.Length; i++)
            {
                if (!_selectedBlendShapeLookup[i])
                    _hasTargetBlendShapeValue[i] = false;
                if (_hasTargetBlendShapeValue[i])
                    _hasContinuousTarget = true;
            }
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

        private bool IsValidBlendShapeIndex(int index)
        {
            if (index < 0)
                return false;

            return index < blendShapeCount;
        }

        private void ApplyContinuousBlendShapes()
        {
            if (receiveInterpolation != ReceiveInterpolationMode.Continuous || !IsTSMPActive())
            {
                ClearContinuousTargets();
                return;
            }
            if (!_hasContinuousTarget)
                return;

            ResolveRenderer();
            RefreshBlendShapeCount();
            RebuildBlendShapeLookupIfNeeded(false);
            if (!_hasContinuousTarget || targetRenderer == null)
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
                    continue;
                }

                targetRenderer.SetBlendShapeWeight(i, Mathf.Lerp(current, targetValue, step));
                hasAnyTarget = true;
            }

            _hasContinuousTarget = hasAnyTarget;
        }

        private void ClearContinuousTargets()
        {
            if (_hasTargetBlendShapeValue != null && _hasContinuousTarget)
            {
                for (int i = 0; i < _hasTargetBlendShapeValue.Length; i++)
                    _hasTargetBlendShapeValue[i] = false;
            }
            _hasContinuousTarget = false;
        }

    }
}
