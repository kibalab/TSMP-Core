using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

#if !UDONSHARP && !COMPILER_UDONSHARP
using System.Collections.Generic;
#endif

using K13A.TSMP.Udon;

namespace K13A.TSMP
{
#if !UDONSHARP && !COMPILER_UDONSHARP
    [ExecuteAlways]
#endif
    public sealed class TSMPEncoder : TSMPBehaviour
    {
        public const string WidthFieldName = nameof(width);
        public const string HeightFieldName = nameof(height);
        public const string BlockSizeFieldName = nameof(blockSize);
        public const string SampleSizeFieldName = nameof(sampleSize);
        public const string FrameRateFieldName = nameof(frameRate);
        public const string AutoEncodeFieldName = nameof(autoEncode);
        public const string OutputTextureMemberName = nameof(outputTexture);
        public const string OutputFieldName = nameof(output);
        public const string BlockExpandMaterialFieldName = nameof(blockExpandMaterial);
        public const string SelectedCodecFieldName = nameof(selectedCodec);
        public const string SelectedCodecUdonTargetFieldName = "selectedCodecUdonTarget";
        public const string PayloadSymbolModeFieldName = nameof(payloadSymbolMode);
        public const string CodecIdFieldName = nameof(codecId);
        public const string PayloadBytesMemberName = "PayloadBytes";
        public const string PayloadBytesFieldName = nameof(payloadBytes);
        public const string MaxPayloadBytesFieldName = nameof(maxPayloadBytes);
        public const string TargetPayloadBytesFieldName = nameof(targetPayloadBytes);
        public const string EncodeNowMethodName = nameof(EncodeNow);
        public const string BindingTargetsFieldName = nameof(bindingTargets);
        public const string BindingUdonTargetsFieldName = "bindingUdonTargets";
        public const string BindingNetworkIdsFieldName = nameof(bindingNetworkIds);
        public const string BindingVariableHashesFieldName = nameof(bindingVariableHashes);
        public const string BindingValueTypesFieldName = nameof(bindingValueTypes);
        public const string BindingFieldNamesFieldName = nameof(bindingFieldNames);
        public const string BindingDirectionsFieldName = nameof(bindingDirections);

#if !COMPILER_UDONSHARP && !UDONSHARP
        public TSMPNetworkBehaviour[] networkBehaviours;
#endif

        [HideInInspector] public Texture2D outputTexture;
        public RenderTexture output;
        public Material blockExpandMaterial;
        [HideInInspector] public Material luma4EncodeMaterial;
        [HideInInspector] public bool useGpuLuma4 = true;
        [HideInInspector] public bool lastFrameUsedGpuLuma4;
        private Texture2D _gpuUpload;
        private byte[] _gpuUploadBytes;
        private RenderTexture _gpuSymbols;
        [HideInInspector]
        public int width = 640;
        [HideInInspector]
        public int height = 360;
        public int frameRate = 30;
        public int blockSize = 8;
        public int sampleSize;

#if UDONSHARP || COMPILER_UDONSHARP
        public bool autoEncode;
#else
        public bool autoEncode = true;
#endif
        public bool clearAfterEncode = true;
        public bool useBlockSymbolTexture = true;
        public int transRpcRepeatFrames = 4;
        [Min(0f), Tooltip("Resend unchanged TransSync fields after this many seconds. Zero sends only changes. Per-field minimum intervals still apply.")]
        public float transSyncRefreshInterval = 1f;
        public TSMPCodec selectedCodec;
#if UDONSHARP || COMPILER_UDONSHARP
        [HideInInspector] public UdonBehaviour selectedCodecUdonTarget;
#endif
        [HideInInspector] public int payloadSymbolMode;
        public int codecId;
        [Tooltip("Backing payload buffer capacity. This can grow to the raster capacity; use Target Payload Bytes to limit transmitted data.")]
        public int maxPayloadBytes = 4096;
        [Min(0), Tooltip("Maximum network payload bytes per output frame, including its 8-byte network header. Zero uses the available raster capacity. Oversized fields wait; they are not fragmented.")]
        public int targetPayloadBytes;
        public uint streamId = 1;
        public ushort layoutId;

        public bool autoBuildVariablesFromBindings = true;
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
        [HideInInspector] public bool[] bindingSendOnChange;
        [HideInInspector] public float[] bindingMinSendIntervals;
        [HideInInspector] public string[] bindingSentEvents;
        [HideInInspector] public int deferredVariableCount;
        // Current EncodeNow diagnostics. Required bytes include a complete minimal network packet.
        [HideInInspector] public int oversizedVariableCount;
        [HideInInspector] public int lastDeferredVariableNetworkId;
        [HideInInspector] public string lastDeferredVariableName;
        [HideInInspector] public int lastDeferredVariableRequiredBytes;
        [HideInInspector] public bool lastDeferredVariableFitsBudget;
        [HideInInspector] public int blockedRpcNetworkId;
        [HideInInspector] public uint blockedRpcHash;
        [HideInInspector] public int blockedRpcRequiredBytes;
        private bool _isEncoding;
        private bool _targetPayloadBudgetActive;

        [HideInInspector] public int encodedObjectCount;
        [HideInInspector] public int queuedRpcCount;
        [HideInInspector] public int messageCount;
        [HideInInspector] public int variableMessageCount;
        [HideInInspector] public int rpcMessageCount;
        [HideInInspector] public int payloadBytes;
        [HideInInspector] public int usablePayloadBytes;
        [HideInInspector] public int effectivePayloadBudgetBytes;
        [HideInInspector] public int deferredRpcCount;
        [HideInInspector] public int bindingCount;
        [HideInInspector] public int autoVariableCount;
        [HideInInspector] public int lastEncodeStage;
        [HideInInspector] public int symbolTextureWidth;
        [HideInInspector] public int symbolTextureHeight;
        [HideInInspector] public uint frameIndex;
        public bool debugLog = true;
        public int debugErrorLogBudget = 32;
        public string lastError;
        private uint[] _crc32Table;
        private const string LogPrefix = "[TSMP Encoder] ";

#if !UDONSHARP && !COMPILER_UDONSHARP
        public int EncodedObjectCount
        {
            get { return encodedObjectCount; }
        }

        public int QueuedRpcCount
        {
            get { return queuedRpcCount; }
        }

        public int PayloadBytes
        {
            get { return payloadBytes; }
        }

        public uint FrameIndex
        {
            get { return frameIndex; }
        }

        public string LastError
        {
            get { return lastError; }
        }

        private readonly List<TSMPNetworkBehaviour> _networkBehaviours = new List<TSMPNetworkBehaviour>(64);
        private readonly List<EncoderNativeFrameBuilder.QueuedRpc> _queuedRpcs = new List<EncoderNativeFrameBuilder.QueuedRpc>(16);
        private readonly Dictionary<System.Type, TransSyncMetadata.Cache> _bindingCache = new Dictionary<System.Type, TransSyncMetadata.Cache>();
        private readonly EncoderNativeSendState _sendState = new EncoderNativeSendState();
        private double _sendTime;
        private uint _sendStreamId;
        private int _nextTransRpcEventId = 1;
        private Texture2D _stagingTexture;
        private Color32[] _rasterPixels;
        private byte[] _payload;
        private byte[] _encodedPayload;
        private byte[] _header;
        private byte[] _rpcValidationBuffer;
        private double _nextEncodeTime;
        private int _payloadOffset;
        private int _currentMessageStartOffset = -1;
        private int _currentVariableCount;

        private void OnEnable()
        {
            _sendState.Reset();
            EnsureResources();
            ResetTSMPLogBudget(debugErrorLogBudget);
            _nextEncodeTime = 0.0;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
            UnityEditor.EditorApplication.update += EditorUpdate;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
            ReleaseResources();
        }

        private void OnValidate()
        {
            SyncOutputDimensions();
            width = Mathf.Max(blockSize, width);
            height = Mathf.Max(blockSize, height);
            frameRate = Mathf.Clamp(frameRate, 1, 120);
            blockSize = Mathf.Max(1, blockSize);
            sampleSize = FrameHeader.ClampDecodeSampleSize(sampleSize, blockSize);
            maxPayloadBytes = NetworkPayloadBuffer.ClampCapacity(maxPayloadBytes);
            targetPayloadBytes = Mathf.Clamp(targetPayloadBytes, 0, NetworkFrameProtocol.MaximumPayloadBytes);
            transRpcRepeatFrames = Mathf.Clamp(transRpcRepeatFrames, 1, 16);
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            Tick(Time.timeAsDouble);
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (this == null || Application.isPlaying)
                return;

            Tick(UnityEditor.EditorApplication.timeSinceStartup);
        }
#endif

        private void Tick(double now)
        {
            if (!autoEncode)
                return;

            double interval = 1.0 / Mathf.Max(1, frameRate);
            if (_nextEncodeTime <= 0.0)
                _nextEncodeTime = now;

            if (now + 0.0005 < _nextEncodeTime)
                return;

            EncodeNow();
            _nextEncodeTime += interval;
            if (_nextEncodeTime < now - interval)
                _nextEncodeTime = now + interval;
        }

        [ContextMenu("Encode Now")]
        public void EncodeNow()
        {
            if (_isEncoding)
                return;
            _isEncoding = true;
            try { EncodeFrame(); }
            finally { _isEncoding = false; }
        }

        private void EncodeFrame()
        {
            lastError = string.Empty;
            ResetBudgetDiagnostics();

            if (output == null)
            {
                SetLastError("Output RenderTexture is not assigned.");
                return;
            }

            EnsureResources();
            CollectNetworkBehaviours();

            queuedRpcCount = _queuedRpcs.Count;
            if (_networkBehaviours.Count == 0 && queuedRpcCount == 0)
            {
                _sendState.Reset();
                SetLastError("No TSMP data to encode.");
                return;
            }

            encodedObjectCount = _networkBehaviours.Count;

            TSMPCodec codec = ResolveCodec();
            if (codec == null)
            {
                SetLastError("Selected TSMP codec is not assigned.");
                return;
            }

            usablePayloadBytes = codec.GetPayloadCapacityBytes(width, height, blockSize);
            EnsurePayloadBufferCapacity(usablePayloadBytes);
            _targetPayloadBudgetActive = targetPayloadBytes > 0;
            effectivePayloadBudgetBytes = NetworkPayloadBuffer.ResolveWriteLimit(targetPayloadBytes,
                Mathf.Min(maxPayloadBytes, usablePayloadBytes));

            if (!BuildNetworkPayload())
            {
                SetLastError(lastError);
                return;
            }

            autoVariableCount = _sendState.WrittenCount;
            deferredVariableCount = _sendState.DeferredCount;
            ReportDeferredVariables();
            if (messageCount == 0)
            {
                payloadBytes = 0;
                return;
            }

            if (payloadBytes > usablePayloadBytes)
            {
                SetLastError($"Payload does not fit. payload={payloadBytes}, usable={usablePayloadBytes}.");
                return;
            }
            if (payloadBytes > effectivePayloadBudgetBytes)
            {
                SetLastError("Payload exceeds the transmission budget.");
                return;
            }

            BuildHeader();

            if (!FrameHeaderWriter.TryWritePayloadCrc32(_header, 0, _encodedPayload, payloadBytes, _crc32Table))
            {
                SetLastError("Failed to add payload checksum.");
                return;
            }

            lastFrameUsedGpuLuma4 = codec.SupportsGpuLuma4Encoding
                && TryWriteGpuLuma4(_header, _encodedPayload, payloadBytes, codec.GetPayloadStartRow(width, blockSize), 128f / 255f);
            if (!lastFrameUsedGpuLuma4)
            {
                EnsureStagingTexture();
                if (!codec.TryWriteFrameBuffered(_stagingTexture, blockSize, _header, _encodedPayload, ref _rasterPixels, out string writeError))
                {
                    SetLastError(writeError);
                    return;
                }
                Graphics.Blit(_stagingTexture, output);
            }
            _sendState.Commit(_payload, GetSendTime());
            frameIndex++;
            if (rpcMessageCount > 0)
                EncoderNativeFrameBuilder.AdvanceQueuedRpcs(_queuedRpcs);
            queuedRpcCount = _queuedRpcs.Count;
        }

        private TSMPCodec ResolveCodec()
        {
            if (selectedCodec != null)
                return selectedCodec;

            TSMPCodec localCodec = GetComponent<TSMPCodec>();
            if (localCodec != null)
            {
                selectedCodec = localCodec;
                return localCodec;
            }

            return null;
        }

        public bool QueueRpc(ushort networkId, string rpcName, params object[] arguments)
        {
            if (string.IsNullOrEmpty(rpcName))
                return false;

            return QueueRpcHash(networkId, StableHash.Fnv1A32(rpcName), arguments);
        }

        public bool QueueRpcHash(ushort networkId, uint rpcHash, params object[] arguments)
        {
            return QueueNativeRpc(new EncoderNativeFrameBuilder.QueuedRpc
            {
                NetworkId = networkId,
                RpcHash = rpcHash,
                Arguments = arguments,
                RepeatsRemaining = 1
            });
        }

        public bool QueueTransRpc(int networkId, uint rpcHash, string methodName)
        {
            return QueueTransRpc(networkId, rpcHash, methodName, NextTransRpcEventId());
        }

        public bool QueueTransRpc(int networkId, uint rpcHash, string methodName, int eventId)
        {
            if (networkId < 0 || networkId > 65535)
                return false;
            if (string.IsNullOrEmpty(methodName))
                return false;
            int repeats = Mathf.Clamp(transRpcRepeatFrames, 1, 16);
            return QueueNativeRpc(new EncoderNativeFrameBuilder.QueuedRpc
            {
                NetworkId = (ushort)networkId,
                RpcHash = rpcHash,
                Arguments = new object[] { methodName, eventId },
                RepeatsRemaining = repeats
            });
        }

        private bool QueueNativeRpc(EncoderNativeFrameBuilder.QueuedRpc rpc)
        {
            if (_queuedRpcs.Count >= 32)
            {
                SetLastError("RPC queue is full.");
                return false;
            }

            int capacity = NetworkFrameProtocol.MaximumPayloadBytes;
            TSMPCodec codec = ResolveCodec();
            if (output != null && codec != null)
            {
                int configuredCapacity = codec.GetPayloadCapacityBytes(output.width, output.height, blockSize);
                if (configuredCapacity >= NetworkFrameProtocol.NetworkHeaderBytes)
                    capacity = Mathf.Min(capacity, configuredCapacity);
            }
            capacity = NetworkPayloadBuffer.ResolveWriteLimit(targetPayloadBytes, capacity);

            if (!EncoderNativeFrameBuilder.ValidateRpc(rpc, ref _rpcValidationBuffer, capacity, out string error))
            {
                SetLastError("RPC rejected: networkId=" + rpc.NetworkId + ", hash=" + rpc.RpcHash + ". " + error);
                return false;
            }

            _queuedRpcs.Add(rpc);
            queuedRpcCount = _queuedRpcs.Count;
            return true;
        }

        private int NextTransRpcEventId()
        {
            int eventId = _nextTransRpcEventId;
            _nextTransRpcEventId++;
            if (_nextTransRpcEventId <= 0 || _nextTransRpcEventId >= 2147483647)
                _nextTransRpcEventId = 1;

            return eventId;
        }

        private void CollectNetworkBehaviours()
        {
#if !UDONSHARP && !COMPILER_UDONSHARP
            EncoderSourceCollector.Collect(_networkBehaviours, bindingTargets, networkBehaviours);
#else
            EncoderSourceCollector.Collect(_networkBehaviours, bindingTargets, null);
#endif
        }

        private bool BuildNetworkPayload()
        {
            if (_sendStreamId != streamId)
            {
                _sendState.Reset();
                _sendStreamId = streamId;
            }
            _sendTime = GetSendTime();
            string error;
            bool result = EncoderNativeFrameBuilder.BuildNetworkPayload(
                _networkBehaviours,
                _queuedRpcs,
                _bindingCache,
                ref _payload,
                ref _encodedPayload,
                ref _payloadOffset,
                ref _currentMessageStartOffset,
                ref _currentVariableCount,
                frameIndex,
                effectivePayloadBudgetBytes,
                out messageCount,
                out variableMessageCount,
                out rpcMessageCount,
                out payloadBytes,
                out error,
                _sendState,
                _sendTime,
                transSyncRefreshInterval,
                SetLastError,
                _targetPayloadBudgetActive);

            queuedRpcCount = _queuedRpcs.Count;
            deferredRpcCount = _sendState.BlockedRpcRequiredBytes > 0 ? 1 : 0;
            deferredVariableCount = _sendState.DeferredCount;
            oversizedVariableCount = _sendState.OversizedCount;
            lastDeferredVariableNetworkId = _sendState.LastDeferredNetworkId;
            lastDeferredVariableName = _sendState.LastDeferredName;
            lastDeferredVariableRequiredBytes = _sendState.LastDeferredRequiredBytes;
            lastDeferredVariableFitsBudget = _sendState.LastDeferredFitsBudget;
            blockedRpcNetworkId = _sendState.BlockedRpcNetworkId;
            blockedRpcHash = _sendState.BlockedRpcHash;
            blockedRpcRequiredBytes = _sendState.BlockedRpcRequiredBytes;
            if (!result)
                lastError = error;
            return result;
        }

        private void EnsureResources()
        {
            SyncOutputDimensions();
            _crc32Table = Crc32Runtime.EnsureFastTable(_crc32Table);
            if (_header == null || _header.Length != FrameHeader.Size)
                _header = new byte[FrameHeader.Size];
        }

        private void EnsureStagingTexture()
        {
            if (_stagingTexture == null || _stagingTexture.width != width || _stagingTexture.height != height)
            {
                if (_stagingTexture != null)
                    DestroyResource(_stagingTexture);

                _stagingTexture = Luma4Raster.CreateTexture(width, height);
            }
        }

        private void SyncOutputDimensions()
        {
            if (output == null)
                return;

            width = Mathf.Max(1, output.width);
            height = Mathf.Max(1, output.height);
        }

        private void ReleaseResources()
        {
            ReleaseGpuResources();
            _rasterPixels = null;
            if (_stagingTexture != null)
            {
                DestroyResource(_stagingTexture);
                _stagingTexture = null;
            }
        }

        private void BuildHeader()
        {
            TSMPCodec codec = ResolveCodec();
            if (codec == null)
                return;

            _crc32Table = EncoderHeaderRuntime.WriteNativeNetworkHeader(
                _header,
                blockSize,
                width,
                height,
                layoutId,
                streamId,
                frameIndex,
                codec.SymbolMode,
                codec.codecId,
                codec.GetCodecOptionBytes(),
                payloadBytes,
                sampleSize,
                _crc32Table);
        }

        private static void DestroyResource(Object resource)
        {
            if (resource == null)
                return;

            if (Application.isPlaying)
                Destroy(resource);
            else
                DestroyImmediate(resource);
        }

        public bool WriteRawBytesVariable(uint variableHash, byte[] value)
        {
            return WriteNativeVariable(variableHash, NetworkValueType.RawBytes, value);
        }

        private bool WriteNativeVariable(uint variableHash, NetworkValueType valueType, object value)
        {
            if (_currentMessageStartOffset < 0)
                return false;

            int valueStartOffset = _payloadOffset;
            int nextOffset = NetworkValueEntryWriter.WriteVariableValue(_payload, _payloadOffset, variableHash, (int)valueType, value);
            if (nextOffset < 0)
            {
                _payloadOffset = valueStartOffset;
                return false;
            }
            if (nextOffset > GetManualPayloadWriteLimit(_payload.Length))
            {
                lastError = "Manually written field exceeds the transmission budget.";
                return false;
            }

            _payloadOffset = nextOffset;
            _currentVariableCount++;
            return true;
        }

        private void EnsurePayloadBufferCapacity(int requiredBytes)
        {
            if (requiredBytes > maxPayloadBytes)
                maxPayloadBytes = NetworkPayloadBuffer.ClampCapacity(requiredBytes);

            _payload = NetworkPayloadBuffer.EnsureCapacity(_payload, maxPayloadBytes);
        }
#endif

#if UDONSHARP || COMPILER_UDONSHARP
        private byte[] _headerBytes;
        private byte[] _payloadBytes;
        private Color32[] _pixels;
        private Color32[] _basePixels;
        private Color32[] _luma4Colors;
        private bool _usingBlockTexture;
        private bool _frameOpen;
        private bool _pendingRpcWritten;
        private int _payloadOffset;
        private int _messageCount;
        private int _currentMessageStartOffset = -1;
        private int _currentVariableCount;
        private int _currentRpcArgumentCount;
        private int _currentMessageType;
        private float _nextEncodeTime;
        private bool _codecQueryValid;
        private UdonBehaviour _codecQueryTarget;
        private int _cachedCodecId;
        private int _cachedPayloadSymbolMode;
        private int _cachedPayloadStartRow;
        private int _cachedPayloadCapacityBytes;
        private int _cachedCodecOptionByteCount;
        private int _cachedCodecOptionByte0;
        private int _cachedCodecOptionByte1;
        private int _cachedCodecOptionByte2;
        private int _cachedCodecOptionByte3;
        private int _cachedCodecOptionByte4;
        private int[] _codecQueryValues;
        private TSMPCodec _codecQuerySelectedCodecKey;
        private UdonBehaviour _codecQuerySelectedCodecUdonTargetKey;
        private int _codecQueryWidthKey;
        private int _codecQueryHeightKey;
        private int _codecQueryBlockSizeKey;
        private int _codecQueryCodecIdKey;
        private int _codecQueryActiveWidthBlocksKey;
        private int _codecQueryActiveHeightBlocksKey;
        private UdonBehaviour[] _cachedBindingUdonTargets;
        private int[] _beforeEncodeTargetGroups;
        private bool[] _beforeEncodeVisited;
        private int[] _beforeEncodeSendModes;
        private int _cachedBindingTargetCount = -1;
        private const int MaxPendingTransRpcs = 32;
        private int _nextTransRpcEventId = 1;
        private int[] _pendingRpcNetworkIds;
        private uint[] _pendingRpcHashes;
        private string[] _pendingRpcMethodNames;
        private int[] _pendingRpcEventIds;
        private int[] _pendingRpcRepeatsRemaining;
        private int _pendingRpcCount;
        private bool _boundVariableMessageOpen;
        private int _boundVariableOpenNetworkId;
        private int _activeWidthBlocks;
        private int _activeHeightBlocks;
        private bool _basePixelsValid;
        private int _basePixelsWidth;
        private int _basePixelsHeight;
        private int _basePixelsBlockSize;
        private int _basePixelsActiveWidthBlocks;
        private int _basePixelsActiveHeightBlocks;
        private bool _basePixelsUsingBlockTexture;
        private int[] _sendOrder;
        private UdonBehaviour[] _sendTargets;
        private ushort[] _sendNetworkIds;
        private uint[] _sendHashes;
        private int[] _sendTypes;
        private string[] _sendFieldNames;
        private int[] _sendPriorities;
        private bool[] _sendOnChange;
        private float[] _sendIntervals;
        private string[] _sendEvents;
        private byte[][] _sendPrevious;
        private bool[] _sendCompleted;
        private double[] _sendLastTimes;
        private int[] _sendPendingOffsets;
        private int[] _sendPendingLengths;
        private byte[] _sendScratch;
        private int _sendRotation;
        private double _sendTime;
        private uint _sendStreamId;

        private void OnEnable()
        {
            _sendOrder = null;
        }

        private void Start()
        {
            EnsureResources();
            ResetTSMPLogBudget(debugErrorLogBudget);
            _nextEncodeTime = 0f;
        }

        private void Update()
        {
            if (!autoEncode)
                return;

            float now = Time.time;
            float interval = 1f / Mathf.Max(1, frameRate);
            if (_nextEncodeTime <= 0f)
                _nextEncodeTime = now;

            if (now + 0.0005f < _nextEncodeTime)
                return;

            EncodeNow();
            _nextEncodeTime += interval;
            if (_nextEncodeTime < now - interval)
                _nextEncodeTime = now + interval;
        }

        public void BeginFrame()
        {
            EnsureResources();
            BeginFrameInternal();
        }

        private void BeginFrameInternal()
        {
            _payloadOffset = 0;
            _messageCount = 0;
            messageCount = 0;
            variableMessageCount = 0;
            rpcMessageCount = 0;
            payloadBytes = 0;
            lastError = string.Empty;
            _currentMessageStartOffset = -1;
            _currentVariableCount = 0;
            _currentRpcArgumentCount = 0;
            _currentMessageType = 0;
            _payloadOffset = NetworkFrameWriter.BeginNetworkFrame(_payloadBytes, 0, frameIndex);
            if (_payloadOffset < 0)
            {
                _payloadOffset = 0;
                _frameOpen = false;
                Fail("Payload buffer is full.");
                return;
            }

            _frameOpen = true;
        }

        public void ClearFrame()
        {
            ResetFrameBuilder();
            messageCount = 0;
            variableMessageCount = 0;
            rpcMessageCount = 0;
            payloadBytes = 0;
        }

        private void ResetFrameBuilder()
        {
            _frameOpen = false;
            _payloadOffset = 0;
            _messageCount = 0;
            _currentMessageStartOffset = -1;
            _currentVariableCount = 0;
            _currentRpcArgumentCount = 0;
            _currentMessageType = 0;
        }

        public bool BeginVariableState(int networkId)
        {
            EnsureFrameOpen();
            string error;
            int nextPayloadOffset;
            int nextMessageStartOffset;
            int nextVariableCount;
            int nextRpcArgumentCount;
            int nextMessageType;
            if (!EncoderUdonFrameWriter.BeginVariableState(_payloadBytes, _payloadOffset, _currentMessageStartOffset, frameIndex, networkId, out nextPayloadOffset, out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType, out error))
                return Fail(error);
            if (nextPayloadOffset > GetManualPayloadWriteLimit(_payloadBytes.Length))
                return Fail("VariableState header exceeds the transmission budget.");

            _payloadOffset = nextPayloadOffset;
            _currentMessageStartOffset = nextMessageStartOffset;
            _currentVariableCount = nextVariableCount;
            _currentRpcArgumentCount = nextRpcArgumentCount;
            _currentMessageType = nextMessageType;
            return true;
        }

        public bool EndVariableState()
        {
            string error;
            int nextPayloadOffset;
            int nextMessageStartOffset;
            int nextVariableCount;
            int nextRpcArgumentCount;
            int nextMessageType;
            bool messageWritten;
            if (!EncoderUdonFrameWriter.EndVariableState(_payloadBytes, _payloadOffset, _currentMessageStartOffset, _currentMessageType, _currentVariableCount, out nextPayloadOffset, out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType, out messageWritten, out error))
                return Fail(error);

            _payloadOffset = nextPayloadOffset;
            _currentMessageStartOffset = nextMessageStartOffset;
            _currentVariableCount = nextVariableCount;
            _currentRpcArgumentCount = nextRpcArgumentCount;
            _currentMessageType = nextMessageType;
            if (messageWritten)
            {
                _messageCount++;
                variableMessageCount++;
            }

            return true;
        }

        public bool BeginRpcCall(int networkId, uint rpcHash)
        {
            EnsureFrameOpen();
            string error;
            int nextPayloadOffset;
            int nextMessageStartOffset;
            int nextVariableCount;
            int nextRpcArgumentCount;
            int nextMessageType;
            if (!EncoderUdonFrameWriter.BeginRpcCall(_payloadBytes, _payloadOffset, _currentMessageStartOffset, frameIndex, networkId, rpcHash, out nextPayloadOffset, out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType, out error))
                return Fail(error);
            if (nextPayloadOffset > GetManualPayloadWriteLimit(_payloadBytes.Length))
                return Fail("RpcCall header exceeds the transmission budget.");

            _payloadOffset = nextPayloadOffset;
            _currentMessageStartOffset = nextMessageStartOffset;
            _currentVariableCount = nextVariableCount;
            _currentRpcArgumentCount = nextRpcArgumentCount;
            _currentMessageType = nextMessageType;
            return true;
        }

        public bool EndRpcCall()
        {
            string error;
            int nextMessageStartOffset;
            int nextVariableCount;
            int nextRpcArgumentCount;
            int nextMessageType;
            if (!EncoderUdonFrameWriter.EndRpcCall(_payloadBytes, _payloadOffset, _currentMessageStartOffset, _currentMessageType, _currentRpcArgumentCount, out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType, out error))
                return Fail(error);

            _currentMessageStartOffset = nextMessageStartOffset;
            _currentVariableCount = nextVariableCount;
            _currentRpcArgumentCount = nextRpcArgumentCount;
            _currentMessageType = nextMessageType;
            _messageCount++;
            rpcMessageCount++;
            return true;
        }

        public bool WriteStringRpcArgument(string value)
        {
            return WriteRpcArgument(NetworkFrameProtocol.ValueTypeUTF8String, value);
        }

        public bool WriteInt32RpcArgument(int value)
        {
            return WriteRpcArgument(NetworkFrameProtocol.ValueTypeInt32, value);
        }

        public bool QueueTransRpc(int networkId, uint rpcHash, string methodName)
        {
            return QueueTransRpc(networkId, rpcHash, methodName, NextTransRpcEventId());
        }

        public bool QueueTransRpc(int networkId, uint rpcHash, string methodName, int eventId)
        {
            if (networkId < 0 || networkId > 65535)
                return false;
            if (string.IsNullOrEmpty(methodName))
                return false;

            if (targetPayloadBytes > 0)
            {
                int capacity = NetworkFrameProtocol.MaximumPayloadBytes;
                if (output != null && (selectedCodec != null || selectedCodecUdonTarget != null))
                {
                    _codecQueryValid = false;
                    capacity = GetPayloadCapacityBytes();
                }
                int limit = NetworkPayloadBuffer.ResolveWriteLimit(targetPayloadBytes, capacity);
                if (GetTransRpcMessageBytes(methodName) > limit - NetworkFrameProtocol.NetworkHeaderBytes)
                    return Fail("TransRPC does not fit the transmission budget.");
            }

            EnsurePendingRpcQueue();
            if (_pendingRpcCount >= MaxPendingTransRpcs)
                return Fail("TransRPC queue is full.");

            int repeats = transRpcRepeatFrames;
            if (repeats < 1)
                repeats = 1;
            if (repeats > 16)
                repeats = 16;

            int index = _pendingRpcCount;
            _pendingRpcNetworkIds[index] = networkId;
            _pendingRpcHashes[index] = rpcHash;
            _pendingRpcMethodNames[index] = methodName;
            _pendingRpcEventIds[index] = eventId;
            _pendingRpcRepeatsRemaining[index] = repeats;
            _pendingRpcCount++;
            queuedRpcCount = _pendingRpcCount;
            return true;
        }

        private int NextTransRpcEventId()
        {
            int eventId = _nextTransRpcEventId;
            _nextTransRpcEventId++;
            if (_nextTransRpcEventId <= 0 || _nextTransRpcEventId >= 2147483647)
                _nextTransRpcEventId = 1;

            return eventId;
        }

        public void EncodeNow()
        {
            if (_isEncoding)
                return;
            _isEncoding = true;
            EncodeFrame();
            _isEncoding = false;
        }

        private void EncodeFrame()
        {
            lastEncodeStage = 1;
            lastError = string.Empty;
            _sendTime = GetSendTime();
            ResetBudgetDiagnostics();
            _codecQueryValid = false;
            EnsureResources();

            if (output == null)
            {
                AbortEncode("Output RenderTexture is not assigned.");
                return;
            }

            if (_currentMessageStartOffset >= 0)
            {
                AbortEncode("Cannot encode while a TSMP message is open.");
                return;
            }

            usablePayloadBytes = GetPayloadCapacityBytes();
            EnsurePayloadBufferCapacity(usablePayloadBytes);
            _targetPayloadBudgetActive = targetPayloadBytes > 0;
            effectivePayloadBudgetBytes = NetworkPayloadBuffer.ResolveWriteLimit(targetPayloadBytes,
                Mathf.Min(usablePayloadBytes, _payloadBytes.Length));

            if (!_frameOpen)
                BeginFrameInternal();
            if (!_frameOpen)
                return;

            if (!WritePendingRpcCalls())
            {
                AbortEncode(lastError);
                return;
            }

            if (autoBuildVariablesFromBindings)
            {
                lastEncodeStage = 2;
                if (!WriteBoundVariableStates())
                {
                    AbortEncode(lastError);
                    return;
                }
            }

            ReportDeferredVariables();
            if (_messageCount <= 0)
            {
                ClearFrame();
                if (bindingCount == 0)
                    SetLastError("No TSMP data to encode.");
                return;
            }

            lastEncodeStage = 3;
            if (!NetworkFrameWriter.EndNetworkFrame(_payloadBytes, 0, _messageCount))
            {
                AbortEncode("Network frame has too many messages.");
                return;
            }

            payloadBytes = _payloadOffset;
            messageCount = _messageCount;

            if (payloadBytes > usablePayloadBytes)
            {
                AbortEncode("Payload does not fit. payload=" + payloadBytes + ", usable=" + usablePayloadBytes + ".");
                return;
            }
            if (payloadBytes > effectivePayloadBudgetBytes)
            {
                AbortEncode("Payload exceeds the transmission budget.");
                return;
            }

            BuildHeader();

            if (!FrameHeaderWriter.TryWritePayloadCrc32(_headerBytes, 0, _payloadBytes, payloadBytes, _crc32Table))
            {
                AbortEncode("Failed to add payload checksum.");
                return;
            }

            lastEncodeStage = 4;
            if (!WriteFrameTexture())
            {
                AbortEncode(lastError);
                return;
            }

            lastEncodeStage = 5;
            BlitEncodedTexture();
            CommitBoundVariables();

            lastEncodeStage = 6;
            frameIndex++;
            if (_pendingRpcWritten)
                AdvancePendingRpcQueue();

            if (clearAfterEncode)
                ResetFrameBuilder();
        }

        private bool WritePendingRpcCalls()
        {
            queuedRpcCount = _pendingRpcCount;
            _pendingRpcWritten = false;
            if (_pendingRpcCount <= 0)
                return true;

            if (_targetPayloadBudgetActive)
            {
                int messageBytes = GetTransRpcMessageBytes(_pendingRpcMethodNames[0]);
                if (messageBytes > effectivePayloadBudgetBytes - _payloadOffset)
                {
                    deferredRpcCount = 1;
                    blockedRpcNetworkId = _pendingRpcNetworkIds[0];
                    blockedRpcHash = _pendingRpcHashes[0];
                    blockedRpcRequiredBytes = NetworkFrameProtocol.NetworkHeaderBytes + messageBytes;
                    return true;
                }
            }
            _pendingRpcWritten = WritePendingRpcCall(0);
            return _pendingRpcWritten;
        }

        private bool WritePendingRpcCall(int index)
        {
            if (!BeginRpcCall(_pendingRpcNetworkIds[index], _pendingRpcHashes[index]))
                return false;
            if (!WriteStringRpcArgument(_pendingRpcMethodNames[index]))
            {
                CancelCurrentMessage();
                return false;
            }
            if (!WriteInt32RpcArgument(_pendingRpcEventIds[index]))
            {
                CancelCurrentMessage();
                return false;
            }

            return EndRpcCall();
        }

        private void AdvancePendingRpcQueue()
        {
            if (_pendingRpcCount <= 0)
            {
                queuedRpcCount = 0;
                return;
            }

            int repeats = _pendingRpcRepeatsRemaining[0] - 1;
            if (repeats > 0)
            {
                _pendingRpcRepeatsRemaining[0] = repeats;
                queuedRpcCount = _pendingRpcCount;
                return;
            }

            ShiftPendingRpcQueue();
            queuedRpcCount = _pendingRpcCount;
        }

        private void ShiftPendingRpcQueue()
        {
            int last = _pendingRpcCount - 1;
            for (int i = 0; i < last; i++)
            {
                int next = i + 1;
                _pendingRpcNetworkIds[i] = _pendingRpcNetworkIds[next];
                _pendingRpcHashes[i] = _pendingRpcHashes[next];
                _pendingRpcMethodNames[i] = _pendingRpcMethodNames[next];
                _pendingRpcEventIds[i] = _pendingRpcEventIds[next];
                _pendingRpcRepeatsRemaining[i] = _pendingRpcRepeatsRemaining[next];
            }

            ClearPendingRpcRange(last, _pendingRpcCount);
            _pendingRpcCount = last;
        }

        private void ClearPendingRpcRange(int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                _pendingRpcNetworkIds[i] = 0;
                _pendingRpcHashes[i] = 0u;
                _pendingRpcMethodNames[i] = string.Empty;
                _pendingRpcEventIds[i] = 0;
                _pendingRpcRepeatsRemaining[i] = 0;
            }
        }

        private void EnsurePendingRpcQueue()
        {
            if (_pendingRpcNetworkIds == null || _pendingRpcNetworkIds.Length != MaxPendingTransRpcs)
                _pendingRpcNetworkIds = new int[MaxPendingTransRpcs];
            if (_pendingRpcHashes == null || _pendingRpcHashes.Length != MaxPendingTransRpcs)
                _pendingRpcHashes = new uint[MaxPendingTransRpcs];
            if (_pendingRpcMethodNames == null || _pendingRpcMethodNames.Length != MaxPendingTransRpcs)
                _pendingRpcMethodNames = new string[MaxPendingTransRpcs];
            if (_pendingRpcEventIds == null || _pendingRpcEventIds.Length != MaxPendingTransRpcs)
                _pendingRpcEventIds = new int[MaxPendingTransRpcs];
            if (_pendingRpcRepeatsRemaining == null || _pendingRpcRepeatsRemaining.Length != MaxPendingTransRpcs)
                _pendingRpcRepeatsRemaining = new int[MaxPendingTransRpcs];
        }

        private bool WriteBoundVariableStates()
        {
            autoVariableCount = 0;
            bindingCount = 0;
            int count = EncoderUdonBindingRuntime.GetWritableBindingCount(bindingTargets, bindingUdonTargets, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
            if (count <= 0)
            {
                _sendOrder = null;
                return true;
            }

            bindingCount = count;
            EnsureBindingTargetCache(count);
            EnsureSendState(count);
            EnsureBeforeEncodeTargetCache(count);
            BeginBoundVariableWrite();
            int limit = effectivePayloadBudgetBytes;
            for (int i = 0; i < count; i++)
            {
                _sendPendingLengths[i] = 0;
                _beforeEncodeVisited[i] = false;
            }

            for (int groupStart = 0; groupStart < count;)
            {
                int groupEnd = groupStart + 1;
                int priority = _sendPriorities[_sendOrder[groupStart]];
                while (groupEnd < count && _sendPriorities[_sendOrder[groupEnd]] == priority)
                    groupEnd++;
                int groupCount = groupEnd - groupStart;
                for (int step = 0; step < groupCount; step++)
                {
                    int index = _sendOrder[groupStart + (step + _sendRotation % groupCount) % groupCount];
                    UdonBehaviour target = _cachedBindingUdonTargets[index];
                    if (!EncoderUdonBindingRuntime.CanWriteBinding(bindingFieldNames, bindingDirections, target, index))
                    {
                        _sendCompleted[index] = false;
                        continue;
                    }
                    if (!TransSyncSendScheduler.IsDue(_sendCompleted[index], _sendLastTimes[index], _sendTime, _sendIntervals[index]))
                        continue;
                    int sendMode = SendBeforeEncodeOnce(target, index);
                    object value = GetProgramVariable(target, bindingFieldNames[index]);
                    int length = NetworkValueEntryWriter.WriteVariableValue(_sendScratch, 0, bindingVariableHashes[index], _sendTypes[index], value);
                    if (length < 0)
                        return Fail("Failed to serialize TransSync field '" + bindingFieldNames[index] + "'.");
                    if (!TransSyncSendScheduler.ShouldSend(_sendCompleted[index], _sendLastTimes[index], _sendTime,
                        TransSyncSendScheduler.ResolveSendOnChange(sendMode, _sendOnChange[index]), transSyncRefreshInterval, _sendPrevious[index], _sendScratch, length))
                        continue;
                    int networkId = bindingNetworkIds[index];
                    bool newMessage = !_boundVariableMessageOpen || _boundVariableOpenNetworkId != networkId;
                    int overhead = newMessage ? NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.VariableStateBodyHeaderBytes : 0;
                    if (length + overhead > limit - _payloadOffset)
                    {
                        deferredVariableCount++;
                        lastDeferredVariableNetworkId = networkId;
                        lastDeferredVariableName = bindingFieldNames[index];
                        lastDeferredVariableRequiredBytes = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes
                            + NetworkFrameProtocol.VariableStateBodyHeaderBytes + length;
                        lastDeferredVariableFitsBudget = lastDeferredVariableRequiredBytes <= limit;
                        if (!lastDeferredVariableFitsBudget)
                            oversizedVariableCount++;
                        continue;
                    }
                    if (!EnsureBoundVariableMessage(networkId))
                        return false;
                    _sendPendingOffsets[index] = _payloadOffset;
                    _sendPendingLengths[index] = length;
                    System.Array.Copy(_sendScratch, 0, _payloadBytes, _payloadOffset, length);
                    _payloadOffset += length;
                    _currentVariableCount++;
                    autoVariableCount++;
                }
                groupStart = groupEnd;
            }
            return EndBoundVariableWrite();
        }

        private void EnsureSendState(int count)
        {
            bool rebuild = _sendOrder == null;
            if (!rebuild)
                rebuild = _sendOrder.Length != count || _sendStreamId != streamId;
            if (!rebuild)
            {
                for (int i = 0; i < count; i++)
                {
                    UdonBehaviour target = _cachedBindingUdonTargets[i];
                    int valueType = bindingValueTypes[i];
                    if ((Object)_sendTargets[i] != (Object)target || _sendNetworkIds[i] != bindingNetworkIds[i]
                        || _sendHashes[i] != bindingVariableHashes[i] || _sendTypes[i] != valueType
                        || _sendFieldNames[i] != bindingFieldNames[i]
                        || _sendPriorities[i] != TransSyncSendScheduler.GetPriority(bindingPriorities, i)
                        || _sendOnChange[i] != TransSyncSendScheduler.GetSendOnChange(bindingSendOnChange, i)
                        || _sendEvents[i] != GetSentEvent(i)
                        || _sendIntervals[i] != TransSyncSendScheduler.GetInterval(bindingMinSendIntervals, i))
                    {
                        rebuild = true;
                        break;
                    }
                }
            }
            if (!rebuild)
                return;

            _sendStreamId = streamId;
            _sendTargets = new UdonBehaviour[count];
            _sendNetworkIds = new ushort[count];
            _sendHashes = new uint[count];
            _sendTypes = new int[count];
            _sendFieldNames = new string[count];
            _sendPriorities = new int[count];
            _sendOnChange = new bool[count];
            _sendIntervals = new float[count];
            _sendEvents = new string[count];
            _sendPrevious = new byte[count][];
            _sendCompleted = new bool[count];
            _sendLastTimes = new double[count];
            _sendPendingOffsets = new int[count];
            _sendPendingLengths = new int[count];
            _beforeEncodeTargetGroups = new int[count];
            if (_sendScratch == null)
                _sendScratch = new byte[TransSyncSendScheduler.ScratchBytes];
            _sendRotation = 0;
            for (int i = 0; i < count; i++)
            {
                _sendTargets[i] = _cachedBindingUdonTargets[i];
                // Stable binding identities share one callback/sendMode cache entry.
                // Build only when send state changes, preserving first-visit order
                // under priority rotation without scanning targets per field.
                _beforeEncodeTargetGroups[i] = i;
                for (int previous = 0; previous < i; previous++)
                {
                    if ((Object)_sendTargets[previous] != (Object)_sendTargets[i])
                        continue;
                    _beforeEncodeTargetGroups[i] = _beforeEncodeTargetGroups[previous];
                    break;
                }
                _sendNetworkIds[i] = bindingNetworkIds[i];
                _sendHashes[i] = bindingVariableHashes[i];
                _sendTypes[i] = bindingValueTypes[i];
                _sendFieldNames[i] = bindingFieldNames[i];
                _sendPriorities[i] = TransSyncSendScheduler.GetPriority(bindingPriorities, i);
                _sendOnChange[i] = TransSyncSendScheduler.GetSendOnChange(bindingSendOnChange, i);
                _sendIntervals[i] = TransSyncSendScheduler.GetInterval(bindingMinSendIntervals, i);
                _sendEvents[i] = GetSentEvent(i);
            }
            _sendOrder = TransSyncSendScheduler.BuildOrder(count, _sendPriorities);
        }

        private string GetSentEvent(int index)
        {
            return bindingSentEvents != null && index < bindingSentEvents.Length ? bindingSentEvents[index] : string.Empty;
        }

        private void CommitBoundVariables()
        {
            if (!autoBuildVariablesFromBindings || _sendOrder == null)
                return;
            double now = GetSendTime();
            for (int i = 0; i < _sendOrder.Length; i++)
            {
                int length = _sendPendingLengths[i];
                if (length <= 0)
                    continue;
                _sendPrevious[i] = TransSyncSendScheduler.Snapshot(_payloadBytes, _sendPendingOffsets[i], length, _sendPrevious[i]);
                _sendCompleted[i] = true;
                _sendLastTimes[i] = now;
                _sendPendingLengths[i] = 0;
                if (!string.IsNullOrEmpty(_sendEvents[i]))
                    SendCustomEvent(_sendTargets[i], _sendEvents[i]);
            }
            _sendRotation = _sendRotation >= 2147483646 ? 0 : _sendRotation + 1;
        }

        private void BeginBoundVariableWrite()
        {
            _boundVariableMessageOpen = false;
            _boundVariableOpenNetworkId = -1;
        }

        private bool EnsureBoundVariableMessage(int networkId)
        {
            if (!NetworkFrameWriter.ShouldOpenVariableState(_boundVariableMessageOpen, _boundVariableOpenNetworkId, networkId))
                return true;

            if (!EndOpenVariableMessage(_boundVariableMessageOpen))
                return false;

            if (!BeginVariableState(networkId))
                return false;

            _boundVariableMessageOpen = true;
            _boundVariableOpenNetworkId = networkId;
            return true;
        }

        private bool EndBoundVariableWrite()
        {
            bool ended = EndOpenVariableMessage(_boundVariableMessageOpen);
            _boundVariableMessageOpen = false;
            _boundVariableOpenNetworkId = -1;
            return ended;
        }

        private bool EndOpenVariableMessage(bool messageOpen)
        {
            if (!messageOpen)
                return true;

            return EndVariableState();
        }

        private void EnsureBeforeEncodeTargetCache(int count)
        {
            if (_beforeEncodeVisited == null || _beforeEncodeVisited.Length < count)
                _beforeEncodeVisited = new bool[count];
            if (_beforeEncodeSendModes == null || _beforeEncodeSendModes.Length < count)
                _beforeEncodeSendModes = new int[count];
        }

        private int SendBeforeEncodeOnce(UdonBehaviour target, int index)
        {
            int group = _beforeEncodeTargetGroups[index];
            if (!_beforeEncodeVisited[group])
            {
                TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.BeforeEncodeEventName);
                _beforeEncodeSendModes[group] = EncoderUdonBindingRuntime.GetSendMode(target);
                _beforeEncodeVisited[group] = true;
            }
            return _beforeEncodeSendModes[group];
        }

        private void EnsureBindingTargetCache(int count)
        {
            _cachedBindingUdonTargets = EncoderUdonBindingRuntime.EnsureBindingTargetCache(bindingTargets, bindingUdonTargets, count, _cachedBindingUdonTargets, _cachedBindingTargetCount, out _cachedBindingTargetCount);
        }

        internal bool WriteVariableValue(uint variableHash, int valueType, object value)
        {
            string error;
            int nextPayloadOffset;
            int nextVariableCount;
            if (!EncoderUdonFrameWriter.WriteVariableValue(_payloadBytes, _payloadOffset, _currentMessageStartOffset, _currentMessageType, _currentVariableCount, variableHash, valueType, value, out nextPayloadOffset, out nextVariableCount, out error))
                return Fail(error);
            if (nextPayloadOffset > GetManualPayloadWriteLimit(_payloadBytes.Length))
                return Fail("Manually written field exceeds the transmission budget.");

            _payloadOffset = nextPayloadOffset;
            _currentVariableCount = nextVariableCount;
            return true;
        }

        internal bool WriteRpcArgument(int valueType, object value)
        {
            string error;
            int nextPayloadOffset;
            int nextRpcArgumentCount;
            if (!EncoderUdonFrameWriter.WriteRpcArgument(_payloadBytes, _payloadOffset, _currentMessageStartOffset, _currentMessageType, _currentRpcArgumentCount, valueType, value, out nextPayloadOffset, out nextRpcArgumentCount, out error))
                return Fail(error);
            if (nextPayloadOffset > GetManualPayloadWriteLimit(_payloadBytes.Length))
                return Fail("RPC argument exceeds the transmission budget.");

            _payloadOffset = nextPayloadOffset;
            _currentRpcArgumentCount = nextRpcArgumentCount;
            return true;
        }

        private void EnsureFrameOpen()
        {
            if (!_frameOpen)
                BeginFrame();
        }

        public void CancelCurrentMessage()
        {
            int nextPayloadOffset;
            int nextMessageStartOffset;
            int nextVariableCount;
            int nextRpcArgumentCount;
            int nextMessageType;
            EncoderUdonFrameWriter.CancelCurrentMessage(_payloadOffset, _currentMessageStartOffset, out nextPayloadOffset, out nextMessageStartOffset, out nextVariableCount, out nextRpcArgumentCount, out nextMessageType);
            _payloadOffset = nextPayloadOffset;
            _currentMessageStartOffset = nextMessageStartOffset;
            _currentVariableCount = nextVariableCount;
            _currentRpcArgumentCount = nextRpcArgumentCount;
            _currentMessageType = nextMessageType;
        }

        private bool WriteFrameTexture()
        {
            int mode = GetPayloadSymbolMode();
            lastFrameUsedGpuLuma4 = mode == (int)K13A.TSMP.SymbolMode.Luma4
                && TryWriteGpuLuma4(_headerBytes, _payloadBytes, payloadBytes, GetPayloadStartRow(), 0f);
            if (lastFrameUsedGpuLuma4)
                return true;

            _pixels = EncoderUdonTextureRuntime.EnsurePixelBuffer(_pixels, symbolTextureWidth, symbolTextureHeight);
            outputTexture = EncoderUdonTextureRuntime.EnsureOutputTexture(outputTexture, symbolTextureWidth, symbolTextureHeight);
            EnsureBasePixels();
            EncoderUdonTextureRuntime.CopyPixelBuffer(_basePixels, _pixels);
            Luma4FrameTextureWriter.WriteHeader(_pixels, width, height, blockSize, _activeWidthBlocks, _activeHeightBlocks, _usingBlockTexture, _headerBytes, _luma4Colors);

            if (mode == (int)K13A.TSMP.SymbolMode.Luma4)
            {
                Luma4FrameTextureWriter.WritePayload(_pixels, width, height, blockSize, _activeWidthBlocks, _activeHeightBlocks, _usingBlockTexture, GetPayloadStartRow(), _payloadBytes, payloadBytes, _luma4Colors);
            }
            else
            {
                if (!WriteCodecPayload())
                    return Fail("Selected TSMP codec failed to write payload.");
            }

            outputTexture.SetPixels32(_pixels);
            outputTexture.Apply();
            return true;
        }

        private void EnsureBasePixels()
        {
            _basePixels = EncoderUdonTextureRuntime.EnsurePixelBuffer(_basePixels, symbolTextureWidth, symbolTextureHeight);
            if (_basePixelsValid
                && _basePixelsWidth == width
                && _basePixelsHeight == height
                && _basePixelsBlockSize == blockSize
                && _basePixelsActiveWidthBlocks == _activeWidthBlocks
                && _basePixelsActiveHeightBlocks == _activeHeightBlocks
                && _basePixelsUsingBlockTexture == _usingBlockTexture)
                return;

            EncoderUdonTextureRuntime.ClearPixelBuffer(_basePixels);
            Luma4FrameTextureWriter.WriteStaticRegions(_basePixels, width, height, blockSize, _activeWidthBlocks, _activeHeightBlocks, _usingBlockTexture);
            _basePixelsWidth = width;
            _basePixelsHeight = height;
            _basePixelsBlockSize = blockSize;
            _basePixelsActiveWidthBlocks = _activeWidthBlocks;
            _basePixelsActiveHeightBlocks = _activeHeightBlocks;
            _basePixelsUsingBlockTexture = _usingBlockTexture;
            _basePixelsValid = true;
        }

        private void BuildHeader()
        {
            NetworkPayloadBuffer.Clear(_headerBytes);
            int mode = GetPayloadSymbolMode();
            _crc32Table = EncoderHeaderRuntime.WriteCachedNetworkHeader(
                _headerBytes,
                mode,
                blockSize,
                _activeWidthBlocks,
                _activeHeightBlocks,
                layoutId,
                streamId,
                frameIndex,
                _cachedCodecId,
                _cachedCodecOptionByteCount,
                GetCachedCodecOptionByte(0),
                GetCachedCodecOptionByte(1),
                GetCachedCodecOptionByte(2),
                GetCachedCodecOptionByte(3),
                GetCachedCodecOptionByte(4),
                payloadBytes,
                sampleSize,
                _crc32Table);
        }

        private void EnsureResources()
        {
            SyncOutputDimensions();
            _crc32Table = Crc32Runtime.EnsureFastTable(_crc32Table);
            blockSize = EncoderUdonTextureRuntime.ClampBlockSize(blockSize);
            width = EncoderUdonTextureRuntime.ClampDimension(width, blockSize);
            height = EncoderUdonTextureRuntime.ClampDimension(height, blockSize);
            frameRate = EncoderUdonTextureRuntime.ClampFrameRate(frameRate);
            sampleSize = FrameHeader.ClampDecodeSampleSize(sampleSize, blockSize);
            maxPayloadBytes = NetworkPayloadBuffer.ClampCapacity(maxPayloadBytes, NetworkFrameProtocol.NetworkHeaderBytes);
            if (transRpcRepeatFrames < 1)
                transRpcRepeatFrames = 1;
            if (transRpcRepeatFrames > 16)
                transRpcRepeatFrames = 16;

            if (_headerBytes == null || _headerBytes.Length != FrameHeader.Size)
                _headerBytes = new byte[FrameHeader.Size];

            _payloadBytes = NetworkPayloadBuffer.EnsureCapacity(_payloadBytes, maxPayloadBytes);
            _luma4Colors = EncoderUdonTextureRuntime.EnsureLuma4Colors(_luma4Colors);

            _activeWidthBlocks = EncoderUdonTextureRuntime.GetActiveBlockCount(width, blockSize);
            _activeHeightBlocks = EncoderUdonTextureRuntime.GetActiveBlockCount(height, blockSize);
            _usingBlockTexture = EncoderUdonTextureRuntime.ShouldUseBlockTexture(useBlockSymbolTexture, blockExpandMaterial);
            int textureWidth = EncoderUdonTextureRuntime.GetSymbolTextureSize(width, _activeWidthBlocks, _usingBlockTexture);
            int textureHeight = EncoderUdonTextureRuntime.GetSymbolTextureSize(height, _activeHeightBlocks, _usingBlockTexture);
            symbolTextureWidth = textureWidth;
            symbolTextureHeight = textureHeight;
        }

        private void SyncOutputDimensions()
        {
            if (output == null)
                return;

            width = Mathf.Max(1, output.width);
            height = Mathf.Max(1, output.height);
        }

        private void BlitEncodedTexture()
        {
            if (lastFrameUsedGpuLuma4)
                return;
            EncoderUdonTextureRuntime.BlitEncodedTexture(outputTexture, output, _usingBlockTexture, blockExpandMaterial, _activeWidthBlocks, _activeHeightBlocks, blockSize);
        }

        private int GetPayloadSymbolMode()
        {
            EnsureCodecQuery();
            return _cachedPayloadSymbolMode;
        }

        private int GetPayloadCapacityBytes()
        {
            EnsureCodecQuery();
            return _cachedPayloadCapacityBytes;
        }

        private int GetPayloadStartRow()
        {
            EnsureCodecQuery();
            return _cachedPayloadStartRow;
        }

        private void EnsureCodecQuery()
        {
            TSMPCodec selected = selectedCodec;
            UdonBehaviour selectedTarget = selectedCodecUdonTarget;
            if (_codecQueryValid
                && _codecQuerySelectedCodecKey == selected
                && _codecQuerySelectedCodecUdonTargetKey == selectedTarget
                && _codecQueryWidthKey == width
                && _codecQueryHeightKey == height
                && _codecQueryBlockSizeKey == blockSize
                && _codecQueryCodecIdKey == codecId
                && _codecQueryActiveWidthBlocksKey == _activeWidthBlocks
                && _codecQueryActiveHeightBlocksKey == _activeHeightBlocks)
                return;

            ResetCodecQueryCache();

            _cachedPayloadCapacityBytes = EncoderCodecRuntime.GetDefaultPayloadCapacityBytes(_activeWidthBlocks, _activeHeightBlocks);
            _codecQueryValues = EncoderCodecRuntime.EnsureQueryValues(_codecQueryValues);

            _codecQueryTarget = CodecBridge.ResolveUdonTarget(selected, selectedTarget);
            if (_codecQueryTarget != null)
            {
                EncoderCodecRuntime.QueryBridge(_codecQueryTarget, width, height, blockSize, codecId, _cachedPayloadCapacityBytes, _codecQueryValues);
                SetCodecQueryCache(_codecQueryValues);
            }
            else
            {
                TSMPCodec directCodec = selected;
                if (directCodec != null)
                {
                    EncoderCodecRuntime.QueryDirect(directCodec, width, height, blockSize, _codecQueryValues);
                    SetCodecQueryCache(_codecQueryValues);
                }
            }

            _codecQuerySelectedCodecKey = selected;
            _codecQuerySelectedCodecUdonTargetKey = selectedTarget;
            _codecQueryWidthKey = width;
            _codecQueryHeightKey = height;
            _codecQueryBlockSizeKey = blockSize;
            _codecQueryCodecIdKey = codecId;
            _codecQueryActiveWidthBlocksKey = _activeWidthBlocks;
            _codecQueryActiveHeightBlocksKey = _activeHeightBlocks;
            _codecQueryValid = true;
        }

        private void ResetCodecQueryCache()
        {
            _codecQueryTarget = null;
            _cachedCodecId = codecId;
            _cachedPayloadSymbolMode = (int)K13A.TSMP.SymbolMode.Luma4;
            _cachedPayloadStartRow = Luma4Raster.PayloadStartRow;
            _cachedPayloadCapacityBytes = 0;
            _cachedCodecOptionByteCount = 0;
            _cachedCodecOptionByte0 = 0;
            _cachedCodecOptionByte1 = 0;
            _cachedCodecOptionByte2 = 0;
            _cachedCodecOptionByte3 = 0;
            _cachedCodecOptionByte4 = 0;
        }

        private void SetCodecQueryCache(int[] values)
        {
            if (values == null || values.Length < EncoderCodecRuntime.QueryValueCount)
                return;

            _cachedCodecId = values[EncoderCodecRuntime.QueryCodecId];
            _cachedPayloadSymbolMode = values[EncoderCodecRuntime.QuerySymbolMode];
            _cachedPayloadStartRow = values[EncoderCodecRuntime.QueryPayloadStartRow];
            _cachedPayloadCapacityBytes = values[EncoderCodecRuntime.QueryPayloadCapacityBytes];
            _cachedCodecOptionByteCount = values[EncoderCodecRuntime.QueryOptionByteCount];
            _cachedCodecOptionByte0 = values[EncoderCodecRuntime.QueryOptionByte0];
            _cachedCodecOptionByte1 = values[EncoderCodecRuntime.QueryOptionByte1];
            _cachedCodecOptionByte2 = values[EncoderCodecRuntime.QueryOptionByte2];
            _cachedCodecOptionByte3 = values[EncoderCodecRuntime.QueryOptionByte3];
            _cachedCodecOptionByte4 = values[EncoderCodecRuntime.QueryOptionByte4];
            payloadSymbolMode = _cachedPayloadSymbolMode;
        }

        private bool WriteCodecPayload()
        {
            EnsureCodecQuery();
            UdonBehaviour codec = _codecQueryTarget;
            if (codec != null)
                return EncoderCodecRuntime.WriteBridgePayload(codec, width, height, blockSize, _pixels, _usingBlockTexture, _payloadBytes, payloadBytes);

            TSMPCodec directCodec = selectedCodec;
            if (directCodec != null)
                return EncoderCodecRuntime.WriteDirectPayload(directCodec, _pixels, _usingBlockTexture, _payloadBytes, payloadBytes, width, height, blockSize);

            SetLastError("Selected TSMP codec Udon target is not assigned.");
            return false;
        }

        private int GetCachedCodecOptionByte(int index)
        {
            if (index == 0)
                return _cachedCodecOptionByte0;
            if (index == 1)
                return _cachedCodecOptionByte1;
            if (index == 2)
                return _cachedCodecOptionByte2;
            if (index == 3)
                return _cachedCodecOptionByte3;
            if (index == 4)
                return _cachedCodecOptionByte4;
            return 0;
        }

        private void EnsurePayloadBufferCapacity(int requiredBytes)
        {
            int required = NetworkPayloadBuffer.ClampCapacity(requiredBytes, NetworkFrameProtocol.NetworkHeaderBytes);

            if (required > maxPayloadBytes)
                maxPayloadBytes = required;

            _payloadBytes = NetworkPayloadBuffer.EnsureCapacity(_payloadBytes, maxPayloadBytes);
        }

        private bool Fail(string error)
        {
            if (error == "Payload buffer is full." && !string.IsNullOrEmpty(lastError))
                return false;

            SetLastError(error);
            return false;
        }

        private void AbortEncode(string error)
        {
            SetLastError(error);
            ClearFrame();
        }

#endif

        private void ResetBudgetDiagnostics()
        {
            deferredVariableCount = 0;
            deferredRpcCount = 0;
            oversizedVariableCount = 0;
            lastDeferredVariableNetworkId = 0;
            lastDeferredVariableName = string.Empty;
            lastDeferredVariableRequiredBytes = 0;
            lastDeferredVariableFitsBudget = false;
            blockedRpcNetworkId = 0;
            blockedRpcHash = 0;
            blockedRpcRequiredBytes = 0;
        }

        private int GetManualPayloadWriteLimit(int bufferLength)
        {
            if (_isEncoding)
                return effectivePayloadBudgetBytes;
            return NetworkPayloadBuffer.ResolveWriteLimit(targetPayloadBytes, bufferLength);
        }

        private int GetTransRpcMessageBytes(string methodName)
        {
            return NetworkFrameProtocol.MessageHeaderBytes + NetworkFrameProtocol.RpcCallBodyHeaderBytes
                + 2 * NetworkFrameProtocol.RpcArgumentHeaderBytes + NetworkFrameProtocol.Int32Bytes
                + Utf8.GetByteCount(methodName, NetworkFrameProtocol.MaximumPayloadBytes + 1);
        }

        private bool TryWriteGpuLuma4(byte[] header, byte[] payload, int count, int startRow, float background)
        {
            if (!useGpuLuma4 || !useBlockSymbolTexture)
                return false;
#if !COMPILER_UDONSHARP
            if (luma4EncodeMaterial == null)
                luma4EncodeMaterial = Resources.Load<Material>("TSMPEncodeLuma4");
#endif
            return GpuLuma4Writer.TryWrite(output, luma4EncodeMaterial, blockSize, startRow, header, payload, count, background,
                ref _gpuUpload, ref _gpuUploadBytes, ref _gpuSymbols);
        }

        private void ReleaseGpuResources()
        {
            GpuLuma4Writer.ReleaseTexture(_gpuUpload);
            DecoderSnapshotRuntime.Release(_gpuSymbols);
            _gpuUpload = null;
            _gpuUploadBytes = null;
            _gpuSymbols = null;
            lastFrameUsedGpuLuma4 = false;
        }

        private void OnDestroy()
        {
            ReleaseGpuResources();
        }

#if UDONSHARP || COMPILER_UDONSHARP
        private void OnDisable()
        {
            ReleaseGpuResources();
        }
#endif

        [ContextMenu("Reset Frame Index")]
        public void ResetFrameIndex()
        {
            frameIndex = 0u;
#if UDONSHARP || COMPILER_UDONSHARP
            ClearFrame();
            _sendOrder = null;
#else
            _sendState.Reset();
#endif
        }

        private double GetSendTime()
        {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
                return UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.realtimeSinceStartupAsDouble;
        }

        private void ReportDeferredVariables()
        {
            if (deferredVariableCount > 0)
                LogTSMPWarning(LogPrefix, "Payload limit deferred " + deferredVariableCount + " TransSync field(s). Increase the target budget/output capacity or reduce field size/send frequency.", debugLog, 1f);
        }

        private void SetLastError(string error)
        {
            lastError = error == null ? string.Empty : error;
            LogTSMPWarning(LogPrefix, lastError, debugLog, 1f);
        }
    }
}
