using UnityEngine;
using UnityEngine.UI;

namespace K13A.TSMP.Udon
{
    [AddComponentMenu("TSMP/Debug/Canvas Stats")]
    public class TSMPDebugCanvas : TSMPBehaviour
    {
        public TSMPEncoder encoder;
        public TSMPDecoder decoder;
        public Text targetText;
        public float updateInterval = 0.25f;
        public float sampleWindowSeconds = 1f;
        public bool includeHeaderBytes = true;
        public bool showEncoder = true;
        public bool showDecoder = true;
        public bool showLastError = true;

        private float _nextTextUpdateTime;
        private float _txWindowStartTime;
        private float _rxWindowStartTime;
        private int _txWindowBytes;
        private int _rxWindowBytes;
        private float _txBitsPerSecond;
        private float _rxBitsPerSecond;
        private bool _hasTxFrame;
        private bool _hasRxFrame;
        private uint _lastTxFrameIndex;
        private uint _lastRxFrameIndex;
        private uint _lastTxStreamId;
        private uint _lastRxStreamId;
        private int _rxExpectedFrames;
        private int _rxLostFrames;

        private void Start()
        {
            EnsureTextTarget();
            ResetCounters();
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            NormalizeSettings();
            RefreshEncoderCounters(now);
            RefreshDecoderCounters(now);
            FlushRateWindows(now);

            if (now < _nextTextUpdateTime)
                return;

            _nextTextUpdateTime = now + updateInterval;
            WriteText();
        }

        public void ResetCounters()
        {
            float now = Time.realtimeSinceStartup;
            _nextTextUpdateTime = 0f;
            _txWindowStartTime = now;
            _rxWindowStartTime = now;
            _txWindowBytes = 0;
            _rxWindowBytes = 0;
            _txBitsPerSecond = 0f;
            _rxBitsPerSecond = 0f;
            _hasTxFrame = false;
            _hasRxFrame = false;
            _lastTxFrameIndex = 0;
            _lastRxFrameIndex = 0;
            _lastTxStreamId = 0;
            _lastRxStreamId = 0;
            _rxExpectedFrames = 0;
            _rxLostFrames = 0;
        }

        private void RefreshEncoderCounters(float now)
        {
            if (!showEncoder || encoder == null)
                return;

            uint streamId = encoder.streamId;
            uint frameIndex = encoder.frameIndex;
            int frameDelta = 1;
            if (_hasTxFrame)
            {
                if (streamId != _lastTxStreamId)
                    frameDelta = 1;
                else
                    frameDelta = GetForwardFrameDelta(frameIndex, _lastTxFrameIndex);
            }

            if (_hasTxFrame && frameDelta <= 0)
                return;

            int frameBytes = encoder.payloadBytes;
            if (includeHeaderBytes)
                frameBytes += FrameHeader.Size;
            if (frameBytes < 0)
                frameBytes = 0;

            if (frameDelta > 120)
                frameDelta = 1;

            _txWindowBytes += frameBytes * frameDelta;
            _lastTxFrameIndex = frameIndex;
            _lastTxStreamId = streamId;
            _hasTxFrame = true;

            FlushTxRateWindow(now);
        }

        private void RefreshDecoderCounters(float now)
        {
            if (!showDecoder || decoder == null)
                return;
            if (!decoder.lastFrameValid || !decoder.lastHeaderValid)
                return;

            uint streamId = decoder.lastStreamId;
            uint frameIndex = decoder.lastFrameIndex;
            int frameDelta = 1;
            bool streamChanged = _hasRxFrame && streamId != _lastRxStreamId;
            if (_hasRxFrame && !streamChanged)
                frameDelta = GetForwardFrameDelta(frameIndex, _lastRxFrameIndex);

            if (_hasRxFrame && frameDelta <= 0)
                return;

            int frameBytes = decoder.lastPayloadSizeFromHeader;
            if (includeHeaderBytes)
                frameBytes += FrameHeader.Size;
            if (frameBytes < 0)
                frameBytes = 0;

            _rxWindowBytes += frameBytes;

            if (!_hasRxFrame || streamChanged)
            {
                _rxExpectedFrames++;
            }
            else
            {
                _rxExpectedFrames += frameDelta;
                if (frameDelta > 1)
                    _rxLostFrames += frameDelta - 1;
            }

            _lastRxFrameIndex = frameIndex;
            _lastRxStreamId = streamId;
            _hasRxFrame = true;

            FlushRxRateWindow(now);
        }

        private void FlushRateWindows(float now)
        {
            FlushTxRateWindow(now);
            FlushRxRateWindow(now);
        }

        private void FlushTxRateWindow(float now)
        {
            if (_txWindowStartTime <= 0f)
                _txWindowStartTime = now;

            float elapsed = now - _txWindowStartTime;
            if (elapsed < sampleWindowSeconds)
                return;

            _txBitsPerSecond = (_txWindowBytes * 8f) / elapsed;
            _txWindowBytes = 0;
            _txWindowStartTime = now;
        }

        private void FlushRxRateWindow(float now)
        {
            if (_rxWindowStartTime <= 0f)
                _rxWindowStartTime = now;

            float elapsed = now - _rxWindowStartTime;
            if (elapsed < sampleWindowSeconds)
                return;

            _rxBitsPerSecond = (_rxWindowBytes * 8f) / elapsed;
            _rxWindowBytes = 0;
            _rxWindowStartTime = now;
        }

        private void WriteText()
        {
            EnsureTextTarget();
            if (targetText == null)
                return;

            string text = "TSMP Debug\n";

            if (showEncoder)
                text += BuildEncoderText();
            if (showDecoder)
                text += BuildDecoderText();

            targetText.text = text;
        }

        private string BuildEncoderText()
        {
            if (encoder == null)
                return "TX: not assigned\n";

            string text = "TX: " + FormatBitRate(_txBitsPerSecond);
            text += " frame=" + encoder.frameIndex;
            text += " stream=" + encoder.streamId;
            text += "\n";
            text += "  payload=" + encoder.payloadBytes + "/" + encoder.usablePayloadBytes;
            text += " msg=" + encoder.messageCount;
            text += " var=" + encoder.variableMessageCount;
            text += " rpc=" + encoder.rpcMessageCount;
            text += "\n";
            text += "  codec=" + encoder.codecId;
            text += " symbol=" + encoder.payloadSymbolMode;
            text += " block=" + encoder.blockSize;
            text += " size=" + encoder.width + "x" + encoder.height;
            text += "\n";
            if (showLastError && !string.IsNullOrEmpty(encoder.lastError))
                text += "  error=" + encoder.lastError + "\n";

            return text;
        }

        private string BuildDecoderText()
        {
            if (decoder == null)
                return "RX: not assigned\n";

            string text = "RX: " + FormatBitRate(_rxBitsPerSecond);
            text += " loss=" + FormatLossPercent();
            text += " frame=" + decoder.lastFrameIndex;
            text += " stream=" + decoder.lastStreamId;
            text += "\n";
            text += "  valid=" + BoolText(decoder.lastFrameValid);
            text += " header=" + BoolText(decoder.lastHeaderValid);
            text += " inFlight=" + BoolText(decoder.readbackInFlight);
            text += "\n";
            text += "  payload=" + decoder.lastPayloadSizeFromHeader;
            text += " available=" + decoder.lastPayloadAvailableBytes;
            text += " byteCap=" + decoder.lastByteTextureCapacityBytes;
            text += "\n";
            text += "  symbol=" + decoder.lastSymbolMode;
            text += " headerRow=" + decoder.lastHeaderRow;
            text += " payloadRow=" + decoder.lastPayloadStartRow;
            text += " flipY=" + BoolText(decoder.lastHeaderFlipY);
            text += "\n";
            text += "  msg=" + decoder.lastNetworkMessageCount;
            text += " var=" + decoder.lastAppliedVariableCount;
            text += " rpc=" + decoder.lastRpcCallCount;
            text += " dupFrame=" + decoder.skippedDuplicateFrameCount;
            text += " dupRpc=" + decoder.skippedDuplicateRpcCount;
            text += "\n";
            if (showLastError && !string.IsNullOrEmpty(decoder.lastError))
                text += "  error=" + decoder.lastError + "\n";

            return text;
        }

        private int GetForwardFrameDelta(uint current, uint previous)
        {
            if (current == previous)
                return 0;
            if (current < previous)
                return 1;

            uint delta = current - previous;
            if (delta > 1000000u)
                return 1;

            return (int)delta;
        }

        private string FormatLossPercent()
        {
            if (_rxExpectedFrames <= 0)
                return "0.0%";

            float percent = (_rxLostFrames * 100f) / _rxExpectedFrames;
            return FormatOneDecimal(percent) + "%";
        }

        private string FormatBitRate(float bitsPerSecond)
        {
            if (bitsPerSecond >= 1000000000f)
                return FormatOneDecimal(bitsPerSecond / 1000000000f) + " Gbps";
            if (bitsPerSecond >= 1000000f)
                return FormatOneDecimal(bitsPerSecond / 1000000f) + " Mbps";
            if (bitsPerSecond >= 1000f)
                return FormatOneDecimal(bitsPerSecond / 1000f) + " Kbps";

            return Mathf.RoundToInt(bitsPerSecond).ToString() + " bps";
        }

        private string FormatOneDecimal(float value)
        {
            int scaled = Mathf.RoundToInt(value * 10f);
            int whole = scaled / 10;
            int fraction = scaled - whole * 10;
            if (fraction < 0)
                fraction = -fraction;

            return whole + "." + fraction;
        }

        private string BoolText(bool value)
        {
            return value ? "yes" : "no";
        }

        private void NormalizeSettings()
        {
            if (updateInterval < 0.05f)
                updateInterval = 0.05f;
            if (sampleWindowSeconds < 0.1f)
                sampleWindowSeconds = 0.1f;
        }

        private void EnsureTextTarget()
        {
            if (targetText != null)
                return;

            targetText = GetComponent<Text>();
        }

#if !COMPILER_UDONSHARP
        private void Reset()
        {
            EnsureTextTarget();
        }

        private void OnValidate()
        {
            NormalizeSettings();
            EnsureTextTarget();
        }
#endif
    }
}
