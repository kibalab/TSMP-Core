using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.SDKBase;
using VRC.SDK3.Rendering;
#endif

#if !COMPILER_UDONSHARP
using UnityEngine.Rendering;
#endif

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon.Common.Interfaces;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPDecoder : TSMPBehaviour
    {
        public const string SourceTextureFieldName = nameof(sourceTexture);
        public const string PayloadByteTextureFieldName = nameof(payloadByteTexture);
        public const string CodecHandlersFieldName = nameof(codecHandlers);
        public const string BindingTargetsFieldName = nameof(bindingTargets);
        public const string BindingUdonTargetsFieldName = "bindingUdonTargets";
        public const string BindingNetworkIdsFieldName = nameof(bindingNetworkIds);
        public const string BindingVariableHashesFieldName = nameof(bindingVariableHashes);
        public const string BindingValueTypesFieldName = nameof(bindingValueTypes);
        public const string BindingFieldNamesFieldName = nameof(bindingFieldNames);
        public const string BindingDirectionsFieldName = nameof(bindingDirections);
        public const string BindingPrioritiesFieldName = nameof(bindingPriorities);
        public const string SourceWidthFieldName = nameof(sourceWidth);
        public const string SourceHeightFieldName = nameof(sourceHeight);
        public const string BlockSizeFieldName = nameof(blockSize);
        public const string SampleSizeFieldName = nameof(sampleSize);
        public const string FlipYFieldName = nameof(flipY);

        public Texture sourceTexture;
        public RenderTexture payloadByteTexture;

        public TSMPCodec[] codecHandlers;

        [HideInInspector] public Component[] bindingTargets;

#if UDONSHARP || COMPILER_UDONSHARP
        [HideInInspector] public UdonBehaviour[] bindingUdonTargets;
#endif
        [HideInInspector] public ushort[] bindingNetworkIds;
        [HideInInspector] public uint[] bindingVariableHashes;
        [HideInInspector] public byte[] bindingValueTypes;
        [HideInInspector] public string[] bindingFieldNames;
        [HideInInspector] public int[] bindingDirections;
        [HideInInspector] public int[] bindingPriorities;

        public bool applyEveryFrame = true;
        [Tooltip("Check all binding settings on each received frame. Disable only for fixed bindings; call _InvalidateBindingCache after changing bindings at runtime.")]
        public bool validateBindingCacheOnReceive = true;
        public bool skipDuplicateFrames = true;
        [Min(1)] public int frameWindowSize = 256;

        [HideInInspector]
        public int sourceWidth = 640;

        [HideInInspector]
        public int sourceHeight = 360;
        public int blockSize = 8;
        public int sampleSize;
        public bool flipY = true;

        public bool useHeaderPayloadLayout = true;
        public int payloadBytesOverride;
        public int decodeSafetyMode;
        public bool usePredictedReadback = true;
        public bool useCombinedByteOutput = true;
        public bool overlapReadbacks = true;
        [HideInInspector] public int pendingDecodeCount;
        [HideInInspector] public int skippedBusyDecodeCount;
        [HideInInspector] public Material readbackPackMaterial;
        [HideInInspector] public int predictedReadbackCount;
        [HideInInspector] public int predictionFallbackCount;
        [HideInInspector] public int combinedByteOutputCount;

        public bool debugLog = true;
        public int debugErrorLogBudget = 32;
        public string lastError;
        [HideInInspector] public bool readbackInFlight;
        [HideInInspector] public bool lastFrameValid;
        [HideInInspector] public bool lastHeaderValid;
        [HideInInspector] public bool lastHeaderFlipY;
        [HideInInspector] public uint lastStreamId;
        [HideInInspector] public uint lastFrameIndex;
        [HideInInspector] public int skippedDuplicateFrameCount;
        [HideInInspector] public int skippedOutOfOrderFrameCount;
        [HideInInspector] public int lastSymbolMode;
        [HideInInspector] public int lastHeaderRow;
        [HideInInspector] public int lastHeaderSource;
        [HideInInspector] public int lastPayloadStartRow;
        [HideInInspector] public int lastPayloadAvailableBytes;
        [HideInInspector] public int lastPayloadSizeFromHeader;
        [HideInInspector] public int lastByteTextureCapacityBytes;
        [HideInInspector] public int lastRequestedByteCount;
        [HideInInspector] public int lastReadbackWidth;
        [HideInInspector] public int lastReadbackHeight;
        [HideInInspector] public int lastReadbackPixelCount;
        [HideInInspector] public int lastAppliedValueType;
        [HideInInspector] public int lastAppliedValueLength;
        [HideInInspector] public int lastNetworkMessageCount;
        [HideInInspector] public int lastAppliedVariableCount;
        [HideInInspector] public int lastRpcCallCount;
        [HideInInspector] public ushort lastRpcNetworkId;
        [HideInInspector] public uint lastRpcHash;
        [HideInInspector] public int lastRpcArgumentCount;
        [HideInInspector] public int lastRpcEventId;
        [HideInInspector] public string lastRpcMethodName;
        [HideInInspector] public int skippedDuplicateRpcCount;
        [HideInInspector] public byte[] lastRpcArgumentTypes;
        [HideInInspector] public int[] lastRpcArgumentOffsets;
        [HideInInspector] public int[] lastRpcArgumentLengths;

        private const int DecodeSafetyHeaderOnly = 1;
        private const int DecodeSafetyPayloadReadbackOnly = 2;
        private const int DecodeSafetyParseOnly = 3;
        private const string LogPrefix = "[TSMP] ";

        private byte[] _readbackBytes;
        private RenderTexture _decodeSourceTexture;
        private bool _decodeSuspended;
        private bool _processingReadbacks;
        private int _slotHead;
        private int _activeSlot = -1;
        private int[] _slotStates = new int[2];
        private bool[] _slotDiscarded = new bool[2];
        private string[] _slotErrors = new string[2];
        private Texture _requestSource;
        private RenderTexture _requestByteTexture;
        private bool _requestUseHeaderLayout;
        private int _requestSafetyMode;
        private Texture[] _slotSources = new Texture[2];
        private RenderTexture[] _slotSnapshots = new RenderTexture[2];
        private RenderTexture[] _slotHeaderTextures = new RenderTexture[2];
        private int[] _slotSnapshotSourceFormats = new int[2];
        private RenderTexture[] _slotCombinedTextures = new RenderTexture[2];
        private RenderTexture[] _slotByteTextures = new RenderTexture[2];
        private byte[][] _slotReadbackBytes = new byte[2][];
        private byte[][] _slotHeaders = new byte[2][];
        private byte[][] _slotPayloads = new byte[2][];
        private byte[][] _slotOptions = new byte[2][];
        private byte[][] _slotPredictedHeaders = new byte[2][];
        private int[] _slotPredictedBytes = new int[2];
        private int[] _slotPredictedBlocks = new int[2];
        private int[] _slotPredictedSamples = new int[2];
        private int[] _slotPredictedStarts = new int[2];
#if COMPILER_UDONSHARP
        private VRCAsyncGPUReadbackRequest[] _slotRequests = new VRCAsyncGPUReadbackRequest[2];
#endif
        private int[] _slotWidths = new int[2];
        private int[] _slotHeights = new int[2];
        private int[] _slotBlockSizes = new int[2];
        private int[] _slotSampleSizes = new int[2];
        private int[] _slotActiveWidths = new int[2];
        private int[] _slotPayloadCounts = new int[2];
        private int[] _slotPayloadStarts = new int[2];
        private int[] _slotSymbolModes = new int[2];
        private int[] _slotCodecIds = new int[2];
        private int[] _slotPayloadTypes = new int[2];
        private int[] _slotHeaderRows = new int[2];
        private bool[] _slotFlipYs = new bool[2];
        private bool[] _slotInterleaved = new bool[2];
        private int[] _slotStages = new int[2];
        private int[] _slotReadWidths = new int[2];
        private int[] _slotReadHeights = new int[2];
        private int[] _slotReadCounts = new int[2];
        private int[] _slotRequestedCounts = new int[2];
        private int[] _slotCapacities = new int[2];
        private uint[] _slotStreamIds = new uint[2];
        private uint[] _slotFrameIndices = new uint[2];
        private int[] _slotHeaderPayloadCounts = new int[2];
        private int[] _slotPayloadRows = new int[2];
        private int[] _slotLastSymbols = new int[2];
        private bool[] _slotHeaderValid = new bool[2];
        private bool[] _slotHeaderFlipYs = new bool[2];
        private int[] _slotLastHeaderRows = new int[2];
        private int[] _slotHeaderSources = new int[2];
        private bool[] _slotHeaderLayouts = new bool[2];
        private int[] _slotSafetyModes = new int[2];
        private byte[] _headerBytes;
        private byte[] _payloadBytes;
        private int _payloadDataBytes;
        private int _activeWidthBlocks;
        private int _payloadStartBlock;
        private int _payloadSymbolMode;
        private int _payloadCodecId;
        private byte[] _codecOptionBytes;
        private int _expectedPixels;
        private int _currentHeaderRow;
        private bool _decodeFlipY;
        private int _decodeStage;
        private bool _payloadInterleaved;
        private int _payloadType;
        private bool[][] _boolValueArrays;
        private int[][] _intValueArrays;
        private float[][] _floatValueArrays;
        private Vector2[][] _vector2ValueArrays;
        private Vector3[][] _vector3ValueArrays;
        private Quaternion[][] _quaternionValueArrays;
        private string[][] _stringValueArrays;
        private bool _hasAppliedFrame;
        private uint _lastAppliedStreamId;
        private uint _lastAppliedFrameIndex;
#if UDONSHARP || COMPILER_UDONSHARP
        private UdonBehaviour[] _cachedBindingUdonTargets;
#endif
        private Component[] _cachedBindingComponentTargets;
        private string[] _cachedBindingFieldNames;
        private ushort[] _bindingLookupNetworkIds;
        private uint[] _bindingLookupVariableHashes;
        private int[] _bindingLookupBindingIndices;
        private int _bindingLookupCount;
        private int _bindingLookupSignature;
        private byte[][] _rawByteValueArrays;
        private uint[] _crc32Table;
        private int _cachedBindingTargetCount = -1;
        private bool _lastValidateBindingCacheOnReceive = true;
        private ushort[] _bindingTableSource;
        private const int RpcDedupCacheSize = 32;
        private uint[] _recentRpcStreamIds;
        private ushort[] _recentRpcNetworkIds;
        private uint[] _recentRpcHashes;
        private int[] _recentRpcEventIds;
        private int _recentRpcWriteIndex;
        private bool _predictionValid;
        private byte[] _predictionHeader;
        private Texture _predictionSource;
        private int _predictionWidth;
        private int _predictionHeight;
        private int _predictionBlockSize;
        private int _predictionSampleSize;
        private int _predictionActiveWidthBlocks;
        private int _predictionStartBlock;
        private bool _predictionFlipY;
        private int _predictionByteCount;
        private RenderTexture _headerByteTexture;
        private RenderTexture _combinedByteTexture;
        private Material _readbackPackInstance;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        private void OnValidate()
        {
            if (readbackPackMaterial == null)
                readbackPackMaterial = Resources.Load<Material>("TSMPReadbackPack");
        }
#endif

        private void Start()
        {
            ResetDecodeDiagnostics();
            InitializeBuffers();
            _crc32Table = Crc32Runtime.EnsureTable(_crc32Table);
        }

        private void Update()
        {
            ProcessReadyReadbacks();
            if (!applyEveryFrame)
                return;

            DecodeNow();
        }

        private void OnEnable()
        {
            _decodeSuspended = false;
#if !COMPILER_UDONSHARP
            if (readbackPackMaterial == null)
                readbackPackMaterial = Resources.Load<Material>("TSMPReadbackPack");
#endif
        }

        private void OnDisable()
        {
            _decodeSuspended = true;
            _predictionValid = false;
            for (int i = 0; i < 2; i++)
            {
                _slotDiscarded[i] = true;
                if (_slotStates[i] == 0 && !_processingReadbacks)
                    ReleaseSlotTextures(i);
            }
            lastFrameValid = false;
            lastHeaderValid = false;
            if (pendingDecodeCount == 0)
                ReleasePredictionResources();
        }

        private void OnDestroy()
        {
            _decodeSuspended = true;
            for (int i = 0; i < 2; i++)
                ReleaseSlotTextures(i);
            ReleasePredictionResources();
        }

        private void ReleasePredictionResources()
        {
            _predictionValid = false;
            _headerByteTexture = null;
            _combinedByteTexture = null;
#if !COMPILER_UDONSHARP
            if (_readbackPackInstance != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_readbackPackInstance);
                else
#endif
                    Destroy(_readbackPackInstance);
            }
#endif
            _readbackPackInstance = null;
        }

        public void ResetDecodeDiagnostics()
        {
            ResetTSMPLogBudget(debugErrorLogBudget);
            predictedReadbackCount = 0;
            predictionFallbackCount = 0;
            combinedByteOutputCount = 0;
            skippedBusyDecodeCount = 0;
        }

        public void DecodeNow()
        {
            if (_decodeSuspended || _processingReadbacks)
                return;
            if (pendingDecodeCount >= (overlapReadbacks ? 2 : 1))
            {
                skippedBusyDecodeCount++;
                return;
            }

            lastError = string.Empty;

            if (!ValidateSetup())
                return;

            SyncSourceDimensions();
            _activeSlot = (_slotHead + pendingDecodeCount) % 2;
            LoadSlotBuffers(_activeSlot);
            _slotDiscarded[_activeSlot] = false;
            _slotErrors[_activeSlot] = null;
            _requestSource = sourceTexture;
            _requestUseHeaderLayout = useHeaderPayloadLayout;
            _requestSafetyMode = decodeSafetyMode;
            _requestByteTexture = _activeSlot == 0 ? payloadByteTexture :
                DecoderPredictionRuntime.EnsureTexture(_slotByteTextures[_activeSlot], payloadByteTexture.width, payloadByteTexture.height);
            pendingDecodeCount++;
            if (_requestByteTexture == null)
            {
                lastFrameValid = false;
                lastError = "Failed to create a slot byte texture.";
                LogDecodeError(lastError);
                CancelCapture();
                return;
            }
            InitializeBuffers();
            PreparePredictionOptions();
            _currentHeaderRow = 2;
            _decodeFlipY = flipY;
            int snapshotSourceFormat;
            _decodeSourceTexture = DecoderSnapshotRuntime.CaptureMatchingFormat(sourceTexture, _decodeSourceTexture,
                _slotSnapshotSourceFormats[_activeSlot], out snapshotSourceFormat, out lastError);
            _slotSnapshotSourceFormats[_activeSlot] = snapshotSourceFormat;
            if (_decodeSourceTexture == null)
            {
                lastFrameValid = false;
                lastHeaderValid = false;
                LogDecodeError(lastError);
                CancelCapture();
                return;
            }

            if (!TryRequestPredictedReadback())
            {
                _predictionValid = false;
                RequestHeaderCopy();
            }
            if (_slotStates[_activeSlot] == 0)
                CancelCapture();
            _activeSlot = -1;
        }

#if COMPILER_UDONSHARP
        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
        {
            int slot = -1;
            for (int i = 0; i < 2; i++)
            {
                if (request.Equals(_slotRequests[i]))
                    slot = i;
            }
            if (slot < 0 || _slotStates[slot] != 1)
                return;
            if (!_slotDiscarded[slot])
            {
                if (request.hasError)
                    _slotErrors[slot] = "Async GPU readback failed.";
                else if (!request.TryGetData(_slotReadbackBytes[slot]))
                    _slotErrors[slot] = "TryGetData failed.";
            }
            _slotStates[slot] = 2;
            _slotRequests[slot] = null;
            ProcessReadyReadbacks();
        }
#else
        private void OnSlotZeroReadback(AsyncGPUReadbackRequest request)
        {
            ReceiveNativeReadback(request, 0);
        }

        private void OnSlotOneReadback(AsyncGPUReadbackRequest request)
        {
            ReceiveNativeReadback(request, 1);
        }

        private void ReceiveNativeReadback(AsyncGPUReadbackRequest request, int slot)
        {
            if (this == null || _slotStates[slot] != 1)
                return;
            if (!_slotDiscarded[slot])
            {
                if (request.hasError)
                    _slotErrors[slot] = "Async GPU readback failed.";
                else
                {
                    var bytes = request.GetData<byte>();
                    int count = _slotReadCounts[slot] * 4;
                    if (bytes.Length < count)
                        _slotErrors[slot] = "Async GPU readback data is smaller than requested.";
                    else
                        Unity.Collections.NativeArray<byte>.Copy(bytes, 0, _slotReadbackBytes[slot], 0, count);
                }
            }
            _slotStates[slot] = 2;
            ProcessReadyReadbacks();
        }
#endif

        private void ProcessReadyReadbacks()
        {
            if (_processingReadbacks)
                return;
            _processingReadbacks = true;
            while (pendingDecodeCount > 0 && _slotStates[_slotHead] == 2)
            {
                int slot = _slotHead;
                _activeSlot = slot;
                LoadSlotContext(slot);
                _slotStates[slot] = 0;
                if (!_slotDiscarded[slot] && !_decodeSuspended)
                {
                    if (!string.IsNullOrEmpty(_slotErrors[slot]))
                    {
                        lastFrameValid = false;
                        lastError = _slotErrors[slot];
                        LogDecodeError(lastError);
                    }
                    else
                        CompleteReadbackStage();
                }
                if (_slotStates[slot] == 1)
                    break;
                StoreSlotBuffers(slot);
                if (_slotDiscarded[slot] || _decodeSuspended)
                    ReleaseSlotTextures(slot);
                pendingDecodeCount--;
                _slotHead = (_slotHead + 1) % 2;
            }
            _activeSlot = -1;
            _processingReadbacks = false;
            readbackInFlight = pendingDecodeCount > 0;
            if (_decodeSuspended && !readbackInFlight)
                ReleasePredictionResources();
        }

        private void LoadSlotBuffers(int slot)
        {
            _requestSource = _slotSources[slot];
            _decodeSourceTexture = _slotSnapshots[slot];
            _headerByteTexture = _slotHeaderTextures[slot];
            _combinedByteTexture = _slotCombinedTextures[slot];
            _requestByteTexture = _slotByteTextures[slot];
            _readbackBytes = _slotReadbackBytes[slot];
            _headerBytes = _slotHeaders[slot];
            _payloadBytes = _slotPayloads[slot];
            _codecOptionBytes = _slotOptions[slot];
        }

        private void StoreSlotBuffers(int slot)
        {
            _slotSources[slot] = _requestSource;
            _slotSnapshots[slot] = _decodeSourceTexture;
            _slotHeaderTextures[slot] = _headerByteTexture;
            _slotCombinedTextures[slot] = _combinedByteTexture;
            _slotByteTextures[slot] = _requestByteTexture;
            _slotReadbackBytes[slot] = _readbackBytes;
            _slotHeaders[slot] = _headerBytes;
            _slotPayloads[slot] = _payloadBytes;
            _slotOptions[slot] = _codecOptionBytes;
        }

        private void StoreSlotContext(int slot)
        {
            StoreSlotBuffers(slot);
            _slotWidths[slot] = sourceWidth;
            _slotHeights[slot] = sourceHeight;
            _slotBlockSizes[slot] = blockSize;
            _slotSampleSizes[slot] = sampleSize;
            _slotActiveWidths[slot] = _activeWidthBlocks;
            _slotPayloadCounts[slot] = _payloadDataBytes;
            _slotPayloadStarts[slot] = _payloadStartBlock;
            _slotSymbolModes[slot] = _payloadSymbolMode;
            _slotCodecIds[slot] = _payloadCodecId;
            _slotPayloadTypes[slot] = _payloadType;
            _slotHeaderRows[slot] = _currentHeaderRow;
            _slotFlipYs[slot] = _decodeFlipY;
            _slotInterleaved[slot] = _payloadInterleaved;
            _slotStages[slot] = _decodeStage;
            _slotReadWidths[slot] = lastReadbackWidth;
            _slotReadHeights[slot] = lastReadbackHeight;
            _slotReadCounts[slot] = lastReadbackPixelCount;
            _slotRequestedCounts[slot] = lastRequestedByteCount;
            _slotCapacities[slot] = lastByteTextureCapacityBytes;
            _slotStreamIds[slot] = lastStreamId;
            _slotFrameIndices[slot] = lastFrameIndex;
            _slotHeaderPayloadCounts[slot] = lastPayloadSizeFromHeader;
            _slotPayloadRows[slot] = lastPayloadStartRow;
            _slotLastSymbols[slot] = lastSymbolMode;
            _slotHeaderValid[slot] = lastHeaderValid;
            _slotHeaderFlipYs[slot] = lastHeaderFlipY;
            _slotLastHeaderRows[slot] = lastHeaderRow;
            _slotHeaderSources[slot] = lastHeaderSource;
            _slotHeaderLayouts[slot] = _requestUseHeaderLayout;
            _slotSafetyModes[slot] = _requestSafetyMode;
            if (_decodeStage == 3)
            {
                _slotPredictedHeaders[slot] = DecoderReadbackRuntime.EnsureByteBuffer(_slotPredictedHeaders[slot], FrameHeader.Size);
                for (int i = 0; i < FrameHeader.Size; i++)
                    _slotPredictedHeaders[slot][i] = _predictionHeader[i];
                _slotPredictedBytes[slot] = _predictionByteCount;
                _slotPredictedBlocks[slot] = _predictionBlockSize;
                _slotPredictedSamples[slot] = _predictionSampleSize;
                _slotPredictedStarts[slot] = _predictionStartBlock;
            }
        }

        private void LoadSlotContext(int slot)
        {
            LoadSlotBuffers(slot);
            sourceWidth = _slotWidths[slot];
            sourceHeight = _slotHeights[slot];
            blockSize = _slotBlockSizes[slot];
            sampleSize = _slotSampleSizes[slot];
            _activeWidthBlocks = _slotActiveWidths[slot];
            _payloadDataBytes = _slotPayloadCounts[slot];
            _payloadStartBlock = _slotPayloadStarts[slot];
            _payloadSymbolMode = _slotSymbolModes[slot];
            _payloadCodecId = _slotCodecIds[slot];
            _payloadType = _slotPayloadTypes[slot];
            _currentHeaderRow = _slotHeaderRows[slot];
            _decodeFlipY = _slotFlipYs[slot];
            _payloadInterleaved = _slotInterleaved[slot];
            _decodeStage = _slotStages[slot];
            lastReadbackWidth = _slotReadWidths[slot];
            lastReadbackHeight = _slotReadHeights[slot];
            lastReadbackPixelCount = _slotReadCounts[slot];
            lastRequestedByteCount = _slotRequestedCounts[slot];
            lastByteTextureCapacityBytes = _slotCapacities[slot];
            lastStreamId = _slotStreamIds[slot];
            lastFrameIndex = _slotFrameIndices[slot];
            lastPayloadSizeFromHeader = _slotHeaderPayloadCounts[slot];
            lastPayloadStartRow = _slotPayloadRows[slot];
            lastSymbolMode = _slotLastSymbols[slot];
            lastHeaderValid = _slotHeaderValid[slot];
            lastHeaderFlipY = _slotHeaderFlipYs[slot];
            lastHeaderRow = _slotLastHeaderRows[slot];
            lastHeaderSource = _slotHeaderSources[slot];
            _requestUseHeaderLayout = _slotHeaderLayouts[slot];
            _requestSafetyMode = _slotSafetyModes[slot];
        }

        private void ReleaseSlotTextures(int slot)
        {
            DecoderSnapshotRuntime.Release(_slotSnapshots[slot]);
            DecoderSnapshotRuntime.Release(_slotHeaderTextures[slot]);
            DecoderSnapshotRuntime.Release(_slotCombinedTextures[slot]);
            if (slot != 0)
                DecoderSnapshotRuntime.Release(_slotByteTextures[slot]);
            _slotSnapshots[slot] = null;
            _slotHeaderTextures[slot] = null;
            _slotCombinedTextures[slot] = null;
            _slotByteTextures[slot] = null;
        }

        private void CancelCapture()
        {
            StoreSlotBuffers(_activeSlot);
            _slotStates[_activeSlot] = 0;
            pendingDecodeCount--;
            readbackInFlight = pendingDecodeCount > 0;
            _activeSlot = -1;
        }

        private void PreparePredictionOptions()
        {
            if (!_predictionValid)
                return;
            ushort ignoredBlock;
            ushort ignoredWidth;
            ushort ignoredType;
            ushort ignoredSize;
            int ignoredSample;
            uint ignoredStream;
            uint ignoredFrame;
            DecoderHeaderRuntime.ReadHeaderFields(_predictionHeader, _codecOptionBytes,
                out ignoredBlock, out ignoredWidth, out ignoredType, out ignoredSize, out ignoredSample,
                out _payloadSymbolMode, out _payloadCodecId, out ignoredStream, out ignoredFrame);
        }

        private void CompleteReadbackStage()
        {
            if (_decodeStage == 3)
            {
                CompletePredictedReadback();
                return;
            }

            if (_decodeStage == 1)
            {
                if (!CopyBytesFromReadback(_headerBytes, FrameHeader.Size))
                    return;

                if (!ReadHeader())
                    return;

                if (ShouldSkipFrame())
                    return;

                if (_requestSafetyMode == DecodeSafetyHeaderOnly)
                {
                    lastFrameValid = true;
                    lastError = "Decode safety mode: header only.";
                    return;
                }

                RequestPayloadReadback();
                return;
            }

            if (!CopyBytesFromReadback(_payloadBytes, _payloadDataBytes))
                return;

            CompletePayload();
        }

        private void CompletePayload()
        {
            CachePrediction();

            if (_requestSafetyMode == DecodeSafetyPayloadReadbackOnly)
            {
                lastFrameValid = true;
                lastError = "Decode safety mode: payload readback only.";
                return;
            }

            if (!ApplyNetworkFrame())
                return;
            _hasAppliedFrame = true;
            _lastAppliedStreamId = lastStreamId;
            _lastAppliedFrameIndex = lastFrameIndex;

            lastFrameValid = true;
        }

        private bool ValidateSetup()
        {
            return DecoderReadbackRuntime.ValidateSetup(sourceTexture, payloadByteTexture, codecHandlers, out lastError);
        }

        private void SyncSourceDimensions()
        {
            if (sourceTexture == null)
                return;

            sourceWidth = Mathf.Max(1, sourceTexture.width);
            sourceHeight = Mathf.Max(1, sourceTexture.height);
        }

        private void InitializeBuffers()
        {
            if (payloadByteTexture == null)
            {
                lastByteTextureCapacityBytes = 0;
                return;
            }

            _payloadDataBytes = 4096;
            if (payloadBytesOverride > 0)
                _payloadDataBytes = payloadBytesOverride;

            _activeWidthBlocks = blockSize > 0 ? sourceWidth / blockSize : 0;
            if (_activeWidthBlocks <= 0)
                lastError = "Invalid layout: sourceWidth=" + sourceWidth + " blockSize=" + blockSize + " yields zero active blocks.";
            _payloadStartBlock = GetPayloadDataStartRow() * _activeWidthBlocks;
            _expectedPixels = ByteTextureReader.GetRequiredPixelCount(_payloadDataBytes);
            lastByteTextureCapacityBytes = ByteTextureReader.GetCapacityBytes(payloadByteTexture.width, payloadByteTexture.height);

            _headerBytes = DecoderReadbackRuntime.EnsureByteBuffer(_headerBytes, FrameHeader.Size);
            _codecOptionBytes = DecoderReadbackRuntime.EnsureCodecOptionBuffer(_codecOptionBytes);
        }

        private void RequestHeaderCopy()
        {
            int startBlock = _currentHeaderRow * _activeWidthBlocks;
            lastRequestedByteCount = FrameHeader.Size;
            _decodeStage = 1;
            RunByteDecodePass(startBlock, FrameHeader.Size, 0);
            RequestByteReadback(FrameHeader.Size);
        }

        private void RequestPayloadReadback()
        {
            _payloadBytes = DecoderReadbackRuntime.EnsureByteBuffer(_payloadBytes, _payloadDataBytes);
            int symbolMode = _payloadSymbolMode;
            TSMPCodec handler = PrepareDecodeHandler(_payloadCodecId, _payloadDataBytes);
            if (handler == null || handler.selectedDecodeMaterial == null)
            {
                lastFrameValid = false;
                lastError = "Byte decode codec handler/material is not assigned. codecId=" + _payloadCodecId + " symbolMode=" + symbolMode;
                LogDecodeError(lastError);
                return;
            }

            _payloadStartBlock = handler.payloadStartRow * _activeWidthBlocks;
            lastPayloadStartRow = handler.payloadStartRow;
            lastRequestedByteCount = _payloadDataBytes;
            _decodeStage = 2;
            RunByteDecodePass(_payloadStartBlock, _payloadDataBytes, symbolMode);
            RequestByteReadback(_payloadDataBytes);
        }

        private void RequestByteReadback(int byteCount)
        {
            RequestTextureReadback(_requestByteTexture, byteCount);
        }

        private void RequestTextureReadback(RenderTexture texture, int byteCount)
        {
            int pixelCount;
            int readWidth;
            int readHeight;
            int readbackPixelCount;
            if (!DecoderReadbackRuntime.TryGetReadbackRegion(texture, byteCount, out pixelCount, out readWidth, out readHeight, out readbackPixelCount, out lastError))
            {
                lastFrameValid = false;
                LogDecodeError(lastError);
                return;
            }

            _readbackBytes = DecoderReadbackRuntime.EnsureByteBuffer(_readbackBytes, readbackPixelCount * 4);

            lastReadbackWidth = readWidth;
            lastReadbackHeight = readHeight;
            lastReadbackPixelCount = readbackPixelCount;

            IssueByteReadback(texture, readWidth, readHeight);
        }

        private void IssueByteReadback(RenderTexture texture, int readWidth, int readHeight)
        {
            StoreSlotContext(_activeSlot);
            _slotStates[_activeSlot] = 1;
            readbackInFlight = true;
#if COMPILER_UDONSHARP
            _slotRequests[_activeSlot] = VRCAsyncGPUReadback.Request(texture, 0, 0, readWidth, 0, readHeight, 0, 1, (IUdonEventReceiver)this);
#else
            if (_activeSlot == 0)
                AsyncGPUReadback.Request(texture, 0, OnSlotZeroReadback);
            else
                AsyncGPUReadback.Request(texture, 0, OnSlotOneReadback);
#endif
        }

        private void RunByteDecodePass(int startBlock, int byteCount, int symbolMode)
        {
            RunByteDecodeTo(startBlock, byteCount, symbolMode, _requestByteTexture, false);
        }

        private bool RunByteDecodeTo(int startBlock, int byteCount, int symbolMode, RenderTexture destination, bool combined)
        {
            int codecId = _payloadCodecId;
            if (symbolMode == 0)
                codecId = 0;

            TSMPCodec handler = PrepareDecodeHandler(codecId, byteCount);
            Material material = handler != null ? handler.selectedDecodeMaterial : null;
            if (material == null)
                return false;

            Texture decodeSource = _decodeSourceTexture;
            material.SetTexture(ShaderProperties.MainTex, decodeSource);
            material.SetFloat(ShaderProperties.BlockSize, blockSize);
            material.SetFloat(ShaderProperties.SampleSize, sampleSize);
            material.SetFloat(ShaderProperties.StartBlock, startBlock);
            material.SetFloat(ShaderProperties.ByteCount, byteCount);
            material.SetFloat(ShaderProperties.ActiveWidthBlocks, _activeWidthBlocks);
            material.SetFloat(ShaderProperties.SourceWidth, sourceWidth);
            material.SetFloat(ShaderProperties.SourceHeight, sourceHeight);
            material.SetFloat(ShaderProperties.OutputWidth, destination.width);
            material.SetFloat(ShaderProperties.OutputHeight, destination.height);
            material.SetFloat(ShaderProperties.FlipY, _decodeFlipY ? 1f : 0f);
            if (material.HasProperty("_TSMPHeaderPixels"))
            {
                material.SetFloat("_TSMPHeaderPixels", combined ? FrameHeader.Size / 4 : 0);
                material.SetTexture("_TSMPHeaderTex", combined ? _headerByteTexture : null);
            }

            handler.PrepareDecode(decodeSource, material);
            GraphicsBridge.Blit(decodeSource, destination, material);
            return true;
        }

        private bool TryRequestPredictedReadback()
        {
            if (!usePredictedReadback || !_requestUseHeaderLayout || _requestSafetyMode != 0)
            {
                _predictionValid = false;
                return false;
            }
            if (!_predictionValid || readbackPackMaterial == null)
                return false;
            if (_predictionSource != sourceTexture || _predictionWidth != sourceWidth || _predictionHeight != sourceHeight ||
                _predictionBlockSize != blockSize || _predictionSampleSize != sampleSize || _predictionFlipY != _decodeFlipY ||
                _predictionActiveWidthBlocks != _activeWidthBlocks ||
                _predictionByteCount > lastByteTextureCapacityBytes)
            {
                _predictionValid = false;
                return false;
            }

            int pixelCount = ByteTextureReader.GetRequiredPixelCount(FrameHeader.Size + _predictionByteCount);
            int width = Mathf.Min(256, pixelCount);
            int height = (pixelCount + width - 1) / width;
            _headerByteTexture = DecoderPredictionRuntime.EnsureTexture(_headerByteTexture, FrameHeader.Size / 4, 1);
            _combinedByteTexture = DecoderPredictionRuntime.EnsureTexture(_combinedByteTexture, width, height);
            if (_headerByteTexture == null || _combinedByteTexture == null)
                return false;
            if (_readbackPackInstance == null)
#if COMPILER_UDONSHARP
                _readbackPackInstance = readbackPackMaterial;
#else
                _readbackPackInstance = new Material(readbackPackMaterial);
#endif

            _decodeStage = 1;
            if (!RunByteDecodeTo(_currentHeaderRow * _activeWidthBlocks, FrameHeader.Size, 0, _headerByteTexture, false))
                return false;
            _decodeStage = 2;
            TSMPCodec handler = PrepareDecodeHandler(_payloadCodecId, _predictionByteCount);
            if (handler == null)
                return false;
            _predictionStartBlock = handler.payloadStartRow * _activeWidthBlocks;
            Material payloadMaterial = handler.selectedDecodeMaterial;
            bool combined = useCombinedByteOutput && payloadMaterial != null &&
                            payloadMaterial.HasProperty("_TSMPHeaderPixels") && payloadMaterial.HasProperty("_TSMPHeaderTex");
            if (combined)
            {
                if (!RunByteDecodeTo(_predictionStartBlock, _predictionByteCount, _payloadSymbolMode, _combinedByteTexture, true))
                    return false;
                combinedByteOutputCount++;
            }
            else
            {
                if (!RunByteDecodeTo(_predictionStartBlock, _predictionByteCount, _payloadSymbolMode, _requestByteTexture, false))
                    return false;
                _readbackPackInstance.SetTexture("_HeaderTex", _headerByteTexture);
                _readbackPackInstance.SetFloat("_OutputWidth", width);
                _readbackPackInstance.SetFloat("_OutputHeight", height);
                _readbackPackInstance.SetFloat("_PayloadWidth", _requestByteTexture.width);
                _readbackPackInstance.SetFloat("_HeaderPixels", FrameHeader.Size / 4);
                _readbackPackInstance.SetFloat("_PayloadPixels", ByteTextureReader.GetRequiredPixelCount(_predictionByteCount));
                GraphicsBridge.Blit(_requestByteTexture, _combinedByteTexture, _readbackPackInstance);
            }
            _decodeStage = 3;
            lastRequestedByteCount = FrameHeader.Size + _predictionByteCount;
            RequestTextureReadback(_combinedByteTexture, lastRequestedByteCount);
            return true;
        }

        private void CompletePredictedReadback()
        {
            if (!CopyBytesFromReadback(_headerBytes, FrameHeader.Size))
                return;
            bool matches = DecoderPredictionRuntime.HeadersMatch(_slotPredictedHeaders[_activeSlot], _headerBytes);
            _predictionValid = false;
            _decodeStage = 1;
            if (!ReadHeader())
                return;
            if (ShouldSkipFrame())
            {
                CachePrediction();
                return;
            }

            _decodeStage = 2;
            TSMPCodec handler = PrepareDecodeHandler(_payloadCodecId, _payloadDataBytes);
            if (!matches || handler == null || !_requestUseHeaderLayout || _requestSafetyMode != 0 ||
                _payloadDataBytes != _slotPredictedBytes[_activeSlot] || blockSize != _slotPredictedBlocks[_activeSlot] || sampleSize != _slotPredictedSamples[_activeSlot] ||
                handler.payloadStartRow * _activeWidthBlocks != _slotPredictedStarts[_activeSlot])
            {
                predictionFallbackCount++;
                if (_requestSafetyMode == DecodeSafetyHeaderOnly)
                {
                    lastFrameValid = true;
                    lastError = "Decode safety mode: header only.";
                    return;
                }
                RequestPayloadReadback();
                return;
            }

            if (!DecoderReadbackRuntime.TryCopyRawBytes(_readbackBytes, lastReadbackPixelCount * 4, FrameHeader.Size,
                    _payloadBytes, _payloadDataBytes, out _expectedPixels, out lastError))
            {
                FailHeaderRead("Predicted readback is smaller than the validated payload.");
                return;
            }
            predictedReadbackCount++;
            CompletePayload();
        }

        private void CachePrediction()
        {
            if (!_requestUseHeaderLayout)
                return;
            _predictionHeader = DecoderReadbackRuntime.EnsureByteBuffer(_predictionHeader, FrameHeader.Size);
            for (int i = 0; i < FrameHeader.Size; i++)
                _predictionHeader[i] = _headerBytes[i];
            _predictionSource = _requestSource;
            _predictionWidth = sourceWidth;
            _predictionHeight = sourceHeight;
            _predictionBlockSize = blockSize;
            _predictionSampleSize = sampleSize;
            _predictionActiveWidthBlocks = _activeWidthBlocks;
            _predictionFlipY = _decodeFlipY;
            _predictionByteCount = _payloadDataBytes;
            _predictionValid = _predictionByteCount > 0 && _predictionByteCount <= lastByteTextureCapacityBytes;
        }

        private TSMPCodec PrepareDecodeHandler(int codecId, int byteCount)
        {
            return CodecBridge.PrepareDecodeHandler(codecHandlers, codecId, _codecOptionBytes, _activeWidthBlocks, _decodeStage, _payloadInterleaved, byteCount);
        }

        private bool CopyBytesFromReadback(byte[] destination, int byteCount)
        {
            if (!DecoderReadbackRuntime.TryCopyRawBytes(_readbackBytes, lastReadbackPixelCount * 4, 0,
                    destination, byteCount, out _expectedPixels, out lastError))
            {
                lastFrameValid = false;
                LogDecodeError(lastError);
                return false;
            }

            return true;
        }

        private bool ReadHeader()
        {
            _crc32Table = Crc32Runtime.EnsureTable(_crc32Table);
            int headerStatus;
            uint decodedMagic;
            ushort decodedHeaderSize;
            int versionMajor;
            uint expectedCrc;
            uint actualCrc;
            if (!FrameHeaderReader.TryValidate(_headerBytes, 0, _crc32Table, out headerStatus, out decodedMagic, out decodedHeaderSize, out versionMajor, out expectedCrc, out actualCrc))
            {
                if (headerStatus == FrameHeaderReader.StatusMagicMismatch)
                    return FailHeaderRead("Header magic mismatch. magic=" + decodedMagic + " row=" + _currentHeaderRow + " startBlock=" + (_currentHeaderRow * _activeWidthBlocks) + " source=" + sourceWidth + "x" + sourceHeight + " blockSize=" + blockSize + " flipY=" + _decodeFlipY);
                if (headerStatus == FrameHeaderReader.StatusHeaderSizeMismatch)
                    return FailHeaderRead("Header size mismatch. expected=" + FrameHeader.Size + " actual=" + decodedHeaderSize);
                if (headerStatus == FrameHeaderReader.StatusVersionMismatch)
                    return FailHeaderRead("Unsupported TSMP header major version. version=" + versionMajor);
                if (headerStatus == FrameHeaderReader.StatusCrcMismatch)
                {
                    lastHeaderValid = false;
                    lastFrameValid = false;
                    lastError = "Header CRC mismatch. expected=" + expectedCrc + " actual=" + actualCrc + " frame discarded.";
                    LogHeaderCrcWarning(lastError);
                    return false;
                }

                return FailHeaderRead("Header parse failed.");
            }

            lastHeaderValid = true;
            lastHeaderSource = 0;
            lastHeaderRow = _currentHeaderRow;
            lastHeaderFlipY = _decodeFlipY;

            ushort headerBlockSize;
            ushort headerActiveWidthBlocks;
            ushort payloadType;
            ushort payloadSize;
            int headerSampleSize;
            uint streamId;
            uint decodedFrameIndex;
            _codecOptionBytes = DecoderReadbackRuntime.EnsureCodecOptionBuffer(_codecOptionBytes);
            DecoderHeaderRuntime.ReadHeaderFields(_headerBytes, _codecOptionBytes, out headerBlockSize, out headerActiveWidthBlocks, out payloadType, out payloadSize, out headerSampleSize, out _payloadSymbolMode, out _payloadCodecId, out streamId, out decodedFrameIndex);

            TSMPCodec headerCodecHandler = PrepareDecodeHandler(_payloadCodecId, payloadSize);
            if (headerCodecHandler == null)
            {
                lastFrameValid = false;
                lastError = "Unsupported codec id. codecId=" + _payloadCodecId;
                LogDecodeError(lastError);
                return false;
            }

            _payloadType = payloadType;
            _payloadInterleaved = false;
            lastSymbolMode = _payloadSymbolMode;
            lastStreamId = streamId;
            lastFrameIndex = decodedFrameIndex;
            lastPayloadSizeFromHeader = payloadSize;

            if (_payloadType != NetworkFrameProtocol.PayloadTypeNetworkFrame)
            {
                lastFrameValid = false;
                lastError = "Unsupported payload type. expected NetworkFrame=" + NetworkFrameProtocol.PayloadTypeNetworkFrame + " actual=" + _payloadType;
                LogDecodeError(lastError);
                return false;
            }

            DecoderHeaderRuntime.ResolvePayloadLayout(_requestUseHeaderLayout, sourceWidth, headerBlockSize, headerActiveWidthBlocks, headerSampleSize, payloadSize, headerCodecHandler.payloadStartRow, blockSize, sampleSize, _activeWidthBlocks, _payloadDataBytes, _payloadBytes, out blockSize, out sampleSize, out _activeWidthBlocks, out _payloadDataBytes, out _payloadBytes, out _payloadStartBlock, out lastPayloadStartRow);

            if (_payloadDataBytes < NetworkFrameProtocol.NetworkHeaderBytes)
                return FailHeaderRead("Payload is too small for NetworkFrame.");

            return true;
        }

        private bool ShouldSkipFrame()
        {
            if (!skipDuplicateFrames || !_hasAppliedFrame || lastStreamId != _lastAppliedStreamId)
                return false;

            if (lastFrameIndex == _lastAppliedFrameIndex)
            {
                skippedDuplicateFrameCount++;
                lastFrameValid = true;
                lastError = "Duplicate frame skipped.";
                return true;
            }

            long distance = (long)lastFrameIndex - (long)_lastAppliedFrameIndex;
            if (distance < 0L)
                distance += 4294967296L;
            if (distance < 2147483648L)
                return false;

            if (lastFrameIndex == 0u && (long)_lastAppliedFrameIndex >= (long)Mathf.Max(1, frameWindowSize))
                return false;

            skippedOutOfOrderFrameCount++;
            lastFrameValid = true;
            lastError = "Out-of-order frame skipped.";
            return true;
        }

        private int GetPayloadDataStartRow()
        {
            TSMPCodec handler = PrepareDecodeHandler(_payloadCodecId, _payloadDataBytes);
            return handler != null ? handler.payloadStartRow : Luma4Raster.PayloadStartRow;
        }

        private bool FailHeaderRead(string error)
        {
            lastHeaderValid = false;
            lastFrameValid = false;
            lastError = error;
            LogDecodeError(lastError);
            return false;
        }

        private void LogDecodeError(string error)
        {
            LogTSMPError(LogPrefix, error, debugLog);
        }

        private void LogHeaderCrcWarning(string error)
        {
            LogTSMPRateLimitedWarning(LogPrefix, error, 1f);
        }

        private bool ApplyNetworkFrame()
        {
            lastAppliedVariableCount = 0;
            lastNetworkMessageCount = 0;
            lastRpcCallCount = 0;
            lastRpcMethodName = string.Empty;
            skippedDuplicateRpcCount = 0;
            lastPayloadAvailableBytes = _payloadBytes != null ? Mathf.Clamp(_payloadDataBytes, 0, _payloadBytes.Length) : 0;

            if (_payloadBytes == null || _payloadDataBytes < NetworkFrameProtocol.NetworkHeaderBytes)
            {
                lastFrameValid = false;
                lastError = "Payload is too small for NetworkFrame.";
                LogDecodeError(lastError);
                return false;
            }

            if (_payloadDataBytes > _payloadBytes.Length)
                return FailNetworkFrame("Payload length exceeds buffer capacity.");

            int cursor;
            int messageCount;
            if (!NetworkFrameReader.TryReadNetworkFrameHeader(_payloadBytes, out messageCount, out cursor))
                return FailNetworkFrame("NetworkFrame header is malformed.");

            lastNetworkMessageCount = messageCount;
            if (_requestSafetyMode != DecodeSafetyParseOnly)
                EnsureBindingTargetCache(GetBindingTargetCount());

            for (int i = 0; i < messageCount; i++)
            {
                ushort networkId;
                int messageType;
                int bodyStart;
                int bodyEnd;
                int nextMessageOffset;
                if (!NetworkFrameReader.TryReadMessageHeader(_payloadBytes, cursor, _payloadDataBytes, out networkId, out messageType, out bodyStart, out bodyEnd, out nextMessageOffset))
                    return FailNetworkFrame("NetworkFrame message is malformed.");

                if (NetworkFrameReader.IsVariableStateMessage(messageType))
                {
                    if (!ApplyVariableState(networkId, bodyStart, bodyEnd))
                        return false;
                }
                else if (NetworkFrameReader.IsRpcCallMessage(messageType))
                {
                    if (_requestSafetyMode != DecodeSafetyParseOnly && !ReadRpcCall(networkId, bodyStart, bodyEnd))
                        return false;
                }

                cursor = nextMessageOffset;
            }

            if (cursor != _payloadDataBytes)
                return FailNetworkFrame("NetworkFrame has trailing bytes.");

            return true;
        }

        private bool ReadRpcCall(ushort networkId, int bodyStart, int bodyEnd)
        {
            uint rpcHash;
            int argumentCount;
            int eventId;
            string methodName;
            string error;
            if (!DecoderRpcRuntime.TryReadRpcCall(
                    _payloadBytes,
                    bodyStart,
                    bodyEnd,
                    lastRpcArgumentTypes,
                    lastRpcArgumentOffsets,
                    lastRpcArgumentLengths,
                    out lastRpcArgumentTypes,
                    out lastRpcArgumentOffsets,
                    out lastRpcArgumentLengths,
                    out rpcHash,
                    out argumentCount,
                    out methodName,
                    out eventId,
                    out error))
                return FailNetworkFrame(error);

            int visibleArgumentCount = argumentCount;
            if (!string.IsNullOrEmpty(methodName) && visibleArgumentCount > 0)
                visibleArgumentCount--;
            if (eventId > 0 && visibleArgumentCount > 0)
                visibleArgumentCount--;

            lastRpcNetworkId = networkId;
            lastRpcHash = rpcHash;
            lastRpcArgumentCount = visibleArgumentCount;
            lastRpcEventId = eventId;
            lastRpcMethodName = methodName;

            if (IsDuplicateRpcEvent(networkId, rpcHash, eventId))
            {
                skippedDuplicateRpcCount++;
                return true;
            }

            lastRpcCallCount++;
            DispatchRpc(networkId, rpcHash, visibleArgumentCount, methodName);
            return true;
        }

        private bool IsDuplicateRpcEvent(ushort networkId, uint rpcHash, int eventId)
        {
            if (eventId <= 0)
                return false;

            EnsureRpcDedupCache();
            for (int i = 0; i < RpcDedupCacheSize; i++)
            {
                if (_recentRpcEventIds[i] != eventId)
                    continue;
                if (_recentRpcStreamIds[i] != lastStreamId)
                    continue;
                if (_recentRpcNetworkIds[i] != networkId)
                    continue;
                if (_recentRpcHashes[i] != rpcHash)
                    continue;

                return true;
            }

            _recentRpcStreamIds[_recentRpcWriteIndex] = lastStreamId;
            _recentRpcNetworkIds[_recentRpcWriteIndex] = networkId;
            _recentRpcHashes[_recentRpcWriteIndex] = rpcHash;
            _recentRpcEventIds[_recentRpcWriteIndex] = eventId;
            _recentRpcWriteIndex++;
            if (_recentRpcWriteIndex >= RpcDedupCacheSize)
                _recentRpcWriteIndex = 0;

            return false;
        }

        private void EnsureRpcDedupCache()
        {
            if (_recentRpcStreamIds == null || _recentRpcStreamIds.Length != RpcDedupCacheSize)
                _recentRpcStreamIds = new uint[RpcDedupCacheSize];
            if (_recentRpcNetworkIds == null || _recentRpcNetworkIds.Length != RpcDedupCacheSize)
                _recentRpcNetworkIds = new ushort[RpcDedupCacheSize];
            if (_recentRpcHashes == null || _recentRpcHashes.Length != RpcDedupCacheSize)
                _recentRpcHashes = new uint[RpcDedupCacheSize];
            if (_recentRpcEventIds == null || _recentRpcEventIds.Length != RpcDedupCacheSize)
                _recentRpcEventIds = new int[RpcDedupCacheSize];
        }

        private void DispatchRpc(ushort networkId, uint rpcHash, int argumentCount, string methodName)
        {
            int targetCount = GetBindingTargetCount();
            if (targetCount <= 0)
                return;

#if UDONSHARP || COMPILER_UDONSHARP
            DecoderRpcDispatcher.Dispatch(_cachedBindingUdonTargets, targetCount, bindingNetworkIds, networkId, rpcHash, argumentCount, methodName);
#else
            DecoderRpcDispatcher.Dispatch(_cachedBindingComponentTargets, targetCount, bindingNetworkIds, networkId, rpcHash, argumentCount, methodName);
#endif
        }

        private bool ApplyVariableState(ushort networkId, int bodyStart, int bodyEnd)
        {
            int variableCount;
            int cursor;
            if (!NetworkFrameReader.TryReadVariableStateHeader(_payloadBytes, bodyStart, bodyEnd, out variableCount, out cursor))
                return FailNetworkFrame("VariableState body is too small.");

            for (int i = 0; i < variableCount; i++)
            {
                uint variableHash;
                int valueType;
                int valueOffset;
                int valueLength;
                int nextEntryOffset;
                if (!NetworkFrameReader.TryReadVariableEntry(_payloadBytes, cursor, bodyEnd, out variableHash, out valueType, out valueOffset, out valueLength, out nextEntryOffset))
                    return FailNetworkFrame("VariableState value is malformed.");

                if (_requestSafetyMode != DecodeSafetyParseOnly)
                    ApplyVariableValue(networkId, variableHash, valueType, valueOffset, valueLength);
                cursor = nextEntryOffset;
            }

            if (cursor != bodyEnd)
                return FailNetworkFrame("VariableState body has trailing bytes.");

            return true;
        }

        private void ApplyVariableValue(ushort networkId, uint variableHash, int valueType, int valueOffset, int valueLength)
        {
            int bindingCount = GetReadableBindingCount();
            if (bindingCount <= 0)
                return;

            int rejectedValueTypeCount;
#if UDONSHARP || COMPILER_UDONSHARP
            int appliedCount = DecoderVariableRuntime.ApplyVariableValue(
                _payloadBytes,
                networkId,
                variableHash,
                valueType,
                valueOffset,
                valueLength,
                bindingCount,
                _cachedBindingUdonTargets,
                bindingNetworkIds,
                bindingVariableHashes,
                bindingValueTypes,
                bindingFieldNames,
                _bindingLookupNetworkIds,
                _bindingLookupVariableHashes,
                _bindingLookupBindingIndices,
                _bindingLookupCount,
                _rawByteValueArrays,
                _boolValueArrays,
                _intValueArrays,
                _floatValueArrays,
                _vector2ValueArrays,
                _vector3ValueArrays,
                _quaternionValueArrays,
                _stringValueArrays,
                out rejectedValueTypeCount);
#else
            int appliedCount = DecoderVariableRuntime.ApplyVariableValue(
                _payloadBytes,
                networkId,
                variableHash,
                valueType,
                valueOffset,
                valueLength,
                bindingCount,
                _cachedBindingComponentTargets,
                bindingNetworkIds,
                bindingVariableHashes,
                bindingValueTypes,
                bindingFieldNames,
                _bindingLookupNetworkIds,
                _bindingLookupVariableHashes,
                _bindingLookupBindingIndices,
                _bindingLookupCount,
                _rawByteValueArrays,
                _boolValueArrays,
                _intValueArrays,
                _floatValueArrays,
                _vector2ValueArrays,
                _vector3ValueArrays,
                _quaternionValueArrays,
                _stringValueArrays,
                out rejectedValueTypeCount);
#endif

            if (rejectedValueTypeCount > 0)
            {
                lastError = "Skipped " + rejectedValueTypeCount + " TSMP variable value(s) with mismatched value type.";
                LogTSMPWarning(LogPrefix, lastError, debugLog, 1f);
            }

            if (appliedCount <= 0)
                return;

            lastAppliedValueType = valueType;
            lastAppliedValueLength = valueLength;
            lastAppliedVariableCount += appliedCount;
        }

        private int GetReadableBindingCount()
        {
#if UDONSHARP || COMPILER_UDONSHARP
            return BindingTable.GetWritableBindingCount(bindingTargets, bindingUdonTargets, true, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
#else
            return BindingTable.GetWritableBindingCount(bindingTargets, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
#endif
        }

        private int GetBindingTargetCount()
        {
#if UDONSHARP || COMPILER_UDONSHARP
            return BindingTable.GetTargetCount(bindingTargets, bindingUdonTargets, true);
#else
            return BindingTable.GetTargetCount(bindingTargets);
#endif
        }

        // Local-only helper. Rebuild lazily before the next applied network frame.
        public void _InvalidateBindingCache()
        {
            _cachedBindingTargetCount = -1;
            _bindingLookupCount = -1;
            _bindingLookupSignature = -1;
        }

        private void EnsureBindingTargetCache(int targetCount)
        {
            if (targetCount < 0)
                targetCount = 0;

            if (_lastValidateBindingCacheOnReceive != validateBindingCacheOnReceive || _bindingTableSource != bindingNetworkIds)
            {
                _InvalidateBindingCache();
                _lastValidateBindingCacheOnReceive = validateBindingCacheOnReceive;
                _bindingTableSource = bindingNetworkIds;
            }

            bool cacheValid = true;
            if (_cachedBindingTargetCount != targetCount)
                cacheValid = false;
#if UDONSHARP || COMPILER_UDONSHARP
            if (!BindingTable.IsUdonTargetCacheValid(_cachedBindingUdonTargets, targetCount))
                cacheValid = false;
            if (validateBindingCacheOnReceive && !BindingTable.MatchesUdonTargets(_cachedBindingUdonTargets, bindingTargets, bindingUdonTargets, targetCount))
                cacheValid = false;
#else
            if (!BindingTable.IsComponentTargetCacheValid(_cachedBindingComponentTargets, targetCount))
                cacheValid = false;
            if (validateBindingCacheOnReceive && !BindingTable.MatchesComponentTargets(_cachedBindingComponentTargets, bindingTargets, targetCount))
                cacheValid = false;
#endif
            bool valueCacheValid = DecoderBindingRuntime.IsArrayValueCacheValid(
                targetCount, _rawByteValueArrays, _boolValueArrays, _intValueArrays, _floatValueArrays,
                _vector2ValueArrays, _vector3ValueArrays, _quaternionValueArrays, _stringValueArrays);
            if (!valueCacheValid)
                cacheValid = false;
            if (_cachedBindingFieldNames == null || _cachedBindingFieldNames.Length != targetCount)
                cacheValid = false;
            for (int i = 0; validateBindingCacheOnReceive && cacheValid && i < targetCount; i++)
            {
                string fieldName = bindingFieldNames != null && i < bindingFieldNames.Length ? bindingFieldNames[i] : null;
                if (_cachedBindingFieldNames[i] != fieldName)
                    cacheValid = false;
            }

            if (cacheValid)
            {
                int lookupCount = BindingTable.ClampNetworkHashCount(targetCount, bindingNetworkIds, bindingVariableHashes);
                if (validateBindingCacheOnReceive || !BindingLookup.IsCacheValid(
                    _bindingLookupNetworkIds, _bindingLookupVariableHashes, _bindingLookupBindingIndices,
                    _bindingLookupCount, _bindingLookupSignature, lookupCount, lookupCount))
                    EnsureBindingLookupCache(targetCount);
                return;
            }

            _cachedBindingTargetCount = targetCount;
            _rawByteValueArrays = new byte[targetCount][];
            _boolValueArrays = new bool[targetCount][];
            _intValueArrays = new int[targetCount][];
            _floatValueArrays = new float[targetCount][];
            _vector2ValueArrays = new Vector2[targetCount][];
            _vector3ValueArrays = new Vector3[targetCount][];
            _quaternionValueArrays = new Quaternion[targetCount][];
            _stringValueArrays = new string[targetCount][];
            _cachedBindingFieldNames = new string[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                _cachedBindingFieldNames[i] = bindingFieldNames != null && i < bindingFieldNames.Length ? bindingFieldNames[i] : null;
            }
#if UDONSHARP || COMPILER_UDONSHARP
            _cachedBindingUdonTargets = BindingTable.BuildUdonTargetCache(bindingTargets, bindingUdonTargets, targetCount);
#else
            _cachedBindingComponentTargets = BindingTable.BuildComponentTargetCache(bindingTargets, targetCount);
#endif

            EnsureBindingLookupCache(targetCount);
        }

        private void EnsureBindingLookupCache(int targetCount)
        {
            DecoderBindingRuntime.EnsureBindingLookup(
                targetCount,
                bindingNetworkIds,
                bindingVariableHashes,
                _bindingLookupNetworkIds,
                _bindingLookupVariableHashes,
                _bindingLookupBindingIndices,
                _bindingLookupCount,
                _bindingLookupSignature,
                out _bindingLookupNetworkIds,
                out _bindingLookupVariableHashes,
                out _bindingLookupBindingIndices,
                out _bindingLookupCount,
                out _bindingLookupSignature);
        }

        private bool FailNetworkFrame(string error)
        {
            lastFrameValid = false;
            lastError = error;
            LogDecodeError(lastError);
            return false;
        }

    }
}
