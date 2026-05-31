using UnityEngine;
using VRC.Udon;

#if !UDONSHARP
using System.Collections.Generic;
#endif

using K13A.TSMP.Udon;

namespace K13A.TSMP
{
#if !UDONSHARP
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
        public const string SelectedCodecUdonTargetFieldName = nameof(selectedCodecUdonTarget);
        public const string PayloadSymbolModeFieldName = nameof(payloadSymbolMode);
        public const string CodecIdFieldName = nameof(codecId);
        public const string PayloadBytesMemberName = "PayloadBytes";
        public const string PayloadBytesFieldName = nameof(payloadBytes);
        public const string MaxPayloadBytesFieldName = nameof(maxPayloadBytes);
        public const string EncodeNowMethodName = nameof(EncodeNow);
        public const string BindingTargetsFieldName = nameof(bindingTargets);
        public const string BindingUdonTargetsFieldName = nameof(bindingUdonTargets);
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
        [HideInInspector]
        public int width = 640;
        [HideInInspector]
        public int height = 360;
        public int frameRate = 30;
        public int blockSize = 8;
        public int sampleSize;

#if UDONSHARP
        public bool autoEncode;
#else
        public bool autoEncode = true;
#endif
        public bool clearAfterEncode = true;
        public bool useBlockSymbolTexture = true;
        public int transRpcRepeatFrames = 4;
        public TSMPCodec selectedCodec;
        [HideInInspector] public UdonBehaviour selectedCodecUdonTarget;
        [HideInInspector] public int payloadSymbolMode;
        public int codecId;
        public int maxPayloadBytes = 4096;
        public uint streamId = 1;
        public ushort layoutId;

        public bool autoBuildVariablesFromBindings = true;
        [HideInInspector] public Component[] bindingTargets;
        [HideInInspector] public UdonBehaviour[] bindingUdonTargets;
        [HideInInspector] public ushort[] bindingNetworkIds;
        [HideInInspector] public uint[] bindingVariableHashes;
        [HideInInspector] public byte[] bindingValueTypes;
        [HideInInspector] public string[] bindingFieldNames;
        [HideInInspector] public int[] bindingDirections;

        [HideInInspector] public int encodedObjectCount;
        [HideInInspector] public int queuedRpcCount;
        [HideInInspector] public int messageCount;
        [HideInInspector] public int variableMessageCount;
        [HideInInspector] public int rpcMessageCount;
        [HideInInspector] public int payloadBytes;
        [HideInInspector] public int usablePayloadBytes;
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

#if !UDONSHARP
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
        private int _nextTransRpcEventId = 1;
        private Texture2D _stagingTexture;
        private byte[] _payload;
        private byte[] _encodedPayload;
        private byte[] _header;
        private double _nextEncodeTime;
        private int _payloadOffset;
        private int _currentMessageStartOffset = -1;
        private int _currentVariableCount;

        private void OnEnable()
        {
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
            lastError = string.Empty;

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

            if (!BuildNetworkPayload())
            {
                SetLastError(lastError);
                return;
            }

            if (payloadBytes > usablePayloadBytes)
            {
                SetLastError($"Payload does not fit. payload={payloadBytes}, usable={usablePayloadBytes}.");
                return;
            }

            BuildHeader();

            bool writeOk = codec.TryWriteFrame(_stagingTexture, blockSize, _header, _encodedPayload, out string writeError);

            if (!writeOk)
            {
                SetLastError(writeError);
                return;
            }

            Graphics.Blit(_stagingTexture, output);
            frameIndex++;
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
            if (_queuedRpcs.Count >= 32)
                return false;

            _queuedRpcs.Add(new EncoderNativeFrameBuilder.QueuedRpc
            {
                NetworkId = networkId,
                RpcHash = rpcHash,
                Arguments = arguments,
                RepeatsRemaining = 1
            });
            return true;
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
            if (_queuedRpcs.Count >= 32)
                return false;

            int repeats = Mathf.Clamp(transRpcRepeatFrames, 1, 16);
            _queuedRpcs.Add(new EncoderNativeFrameBuilder.QueuedRpc
            {
                NetworkId = (ushort)networkId,
                RpcHash = rpcHash,
                Arguments = new object[] { methodName, eventId },
                RepeatsRemaining = repeats
            });
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
#if !UDONSHARP
            EncoderSourceCollector.Collect(_networkBehaviours, bindingTargets, networkBehaviours);
#else
            EncoderSourceCollector.Collect(_networkBehaviours, bindingTargets, null);
#endif
        }

        private bool BuildNetworkPayload()
        {
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
                maxPayloadBytes,
                out messageCount,
                out variableMessageCount,
                out rpcMessageCount,
                out payloadBytes,
                out error);

            lastError = error;
            return result;
        }

        private void EnsureResources()
        {
            SyncOutputDimensions();
            if (_stagingTexture == null || _stagingTexture.width != width || _stagingTexture.height != height)
            {
                if (_stagingTexture != null)
                    DestroyResource(_stagingTexture);

                _stagingTexture = Luma4Raster.CreateTexture(width, height);
            }

            if (_header == null || _header.Length != FrameHeader.Size)
                _header = new byte[FrameHeader.Size];
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

#if UDONSHARP
        private byte[] _headerBytes;
        private byte[] _payloadBytes;
        private Color32[] _pixels;
        private Color32[] _basePixels;
        private Color32[] _luma4Colors;
        private bool _usingBlockTexture;
        private bool _frameOpen;
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
        private UdonBehaviour[] _beforeEncodeTargets;
        private int _beforeEncodeTargetCount;
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

        private void Start()
        {
            EnsureResources();
            _crc32Table = Crc32Runtime.EnsureTable(_crc32Table);
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
            _frameOpen = false;
            _payloadOffset = 0;
            _messageCount = 0;
            messageCount = 0;
            variableMessageCount = 0;
            rpcMessageCount = 0;
            payloadBytes = 0;
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
            lastEncodeStage = 1;
            lastError = string.Empty;
            EnsureResources();

            if (_currentMessageStartOffset >= 0)
            {
                AbortEncode("Cannot encode while a TSMP message is open.");
                return;
            }

            usablePayloadBytes = GetPayloadCapacityBytes();
            EnsurePayloadBufferCapacity(usablePayloadBytes);

            if (!_frameOpen)
                BeginFrameInternal();
            if (!_frameOpen)
                return;

            if (autoBuildVariablesFromBindings)
            {
                lastEncodeStage = 2;
                if (!WriteBoundVariableStates())
                {
                    AbortEncode(lastError);
                    return;
                }
            }

            if (!WritePendingRpcCalls())
            {
                AbortEncode(lastError);
                return;
            }

            if (_messageCount <= 0)
            {
                AbortEncode("No TSMP data to encode.");
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

            BuildHeader();

            lastEncodeStage = 4;
            if (!WriteFrameTexture())
            {
                AbortEncode(lastError);
                return;
            }

            lastEncodeStage = 5;
            BlitEncodedTexture();

            lastEncodeStage = 6;
            frameIndex++;
            AdvancePendingRpcQueue();

            if (clearAfterEncode)
                ClearFrame();
        }

        private bool WritePendingRpcCalls()
        {
            queuedRpcCount = _pendingRpcCount;
            if (_pendingRpcCount <= 0)
                return true;

            return WritePendingRpcCall(0);
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
                return true;

            bindingCount = count;
            EnsureBindingTargetCache(count);
            EnsureBeforeEncodeTargetCache(count);
            _beforeEncodeTargetCount = 0;
            BeginBoundVariableWrite();

            for (int i = 0; i < count; i++)
            {
                UdonBehaviour target = _cachedBindingUdonTargets[i];
                if (!EncoderUdonBindingRuntime.CanWriteBinding(bindingFieldNames, bindingDirections, target, i))
                    continue;

                SendBeforeEncodeOnce(target);

                if (!EnsureBoundVariableMessage(bindingNetworkIds[i]))
                    return false;

                object value = GetProgramVariable(target, bindingFieldNames[i]);
                if (!WriteVariableValue(bindingVariableHashes[i], bindingValueTypes[i], value))
                {
                    int payloadCapacity = 0;
                    if (_payloadBytes != null)
                        payloadCapacity = _payloadBytes.Length;
                    lastError = "Failed to write TransSync field '" + BindingTable.GetFieldName(bindingFieldNames, i) + "'. networkId=" + bindingNetworkIds[i] + " valueType=" + bindingValueTypes[i] + " offset=" + _payloadOffset + " capacity=" + payloadCapacity + ".";
                    CancelCurrentMessage();
                    return false;
                }

                autoVariableCount++;
            }

            if (!EndBoundVariableWrite())
                return false;

            return true;
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
            _beforeEncodeTargets = EncoderUdonBindingRuntime.EnsureBeforeEncodeTargetCache(_beforeEncodeTargets, count);
        }

        private void SendBeforeEncodeOnce(UdonBehaviour target)
        {
            _beforeEncodeTargetCount = EncoderUdonBindingRuntime.SendBeforeEncodeOnce(target, _beforeEncodeTargets, _beforeEncodeTargetCount);
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

            _pixels = EncoderUdonTextureRuntime.EnsurePixelBuffer(_pixels, textureWidth, textureHeight);
            outputTexture = EncoderUdonTextureRuntime.EnsureOutputTexture(outputTexture, textureWidth, textureHeight);
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
            EncoderUdonTextureRuntime.BlitEncodedTexture(outputTexture, output, _usingBlockTexture, blockExpandMaterial, _activeWidthBlocks, _activeHeightBlocks);
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

        [ContextMenu("Reset Frame Index")]
        public void ResetFrameIndex()
        {
            frameIndex = 0u;
#if UDONSHARP
            ClearFrame();
#endif
        }

        private void SetLastError(string error)
        {
            lastError = error == null ? string.Empty : error;
            LogTSMPWarning(LogPrefix, lastError, debugLog, 1f);
        }
    }
}
