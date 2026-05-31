using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace K13A.TSMP
{
    [ExecuteAlways]
    public sealed class TSMPFfmpegRtmpPublisher : MonoBehaviour
    {
        [Header("Source")]
        public Texture sourceTexture;
        public bool useSourceDimensions = true;
        public int width = 640;
        public int height = 360;
        public int frameRate = 30;
        public bool flipVertical;

        [Header("FFmpeg")]
        public string ffmpegPath = "ffmpeg";
        public string rtmpUrl = "rtmp://127.0.0.1:1935/tsmp";
        public bool useServerAndStreamKey;
        public string rtmpServerUrl = "rtmp://127.0.0.1:1935/live";
        public string streamKey = "tsmp";
        public int videoBitrateKbps = 4000;
        public string preset = "veryfast";
        public bool yuv420p = true;
        public string extraArguments = string.Empty;
        public bool checkRtmpTcpBeforeStart = true;
        public int tcpConnectTimeoutMs = 1000;
        public bool repeatLastFrameWhenIdle = true;

        [Header("Lifecycle")]
        public bool autoStart;
        public bool publishInEditMode;
        public bool forceRunInBackground = true;
        public bool logFfmpegOutput;

        [Header("Diagnostics")]
        public bool isPublishing;
        public bool readbackInFlight;
        public int framesSubmitted;
        public int framesWritten;
        public int framesDropped;
        public int lastFfmpegExitCode;
        public string lastFfmpegArguments;
        public string lastError;
        public string lastFfmpegOutput;
        [TextArea(3, 8)] public string ffmpegOutputTail;

        private Process _process;
        private Thread _writerThread;
        private AutoResetEvent _frameEvent;
        private readonly object _frameLock = new object();
        private byte[] _readbackBuffer;
        private byte[] _pendingFrame;
        private byte[] _writerFrame;
        private byte[] _lastFrame;
        private byte[] _flipBuffer;
        private int _pendingLength;
        private int _lastFrameLength;
        private bool _hasPendingFrame;
        private bool _hasLastFrame;
        private bool _stopWriter;
        private double _nextFrameTime;
        private int _activeWidth;
        private int _activeHeight;
        private bool _previousRunInBackground;
        private bool _didOverrideRunInBackground;

        private void OnEnable()
        {
            _frameEvent = new AutoResetEvent(false);
            _nextFrameTime = 0.0;

            if (autoStart && (Application.isPlaying || publishInEditMode))
                StartPublishing();
        }

        private void OnDisable()
        {
            StopPublishing();

            if (_frameEvent != null)
            {
                _frameEvent.Dispose();
                _frameEvent = null;
            }
        }

        private void OnValidate()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            frameRate = Mathf.Clamp(frameRate, 1, 120);
            videoBitrateKbps = Mathf.Max(64, videoBitrateKbps);
            tcpConnectTimeoutMs = Mathf.Clamp(tcpConnectTimeoutMs, 100, 10000);
            if (string.IsNullOrWhiteSpace(preset))
                preset = "veryfast";
        }

        private void Update()
        {
            if (!isPublishing)
                return;

            if (!Application.isPlaying && !publishInEditMode)
                return;

            double now = Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;
            double interval = 1.0 / Mathf.Max(1, frameRate);
            if (now < _nextFrameTime)
                return;

            _nextFrameTime = now + interval;
            RequestFrame();
        }

        [ContextMenu("Start Publishing")]
        public void StartPublishing()
        {
            lastError = string.Empty;

            if (isPublishing)
                return;

            if (!ValidateSetup())
                return;

            ResolveActiveDimensions();
            ResetCounters();

            if (checkRtmpTcpBeforeStart && !CheckRtmpTcpEndpoint())
                return;

            try
            {
                lastFfmpegArguments = BuildFfmpegArguments();
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = lastFfmpegArguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.ErrorDataReceived += OnFfmpegOutput;
                _process.OutputDataReceived += OnFfmpegOutput;

                if (!_process.Start())
                {
                    lastError = "Failed to start FFmpeg process.";
                    CleanupProcess();
                    return;
                }

                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();
                ApplyRunInBackgroundOverride();

                _stopWriter = false;
                _hasPendingFrame = false;
                _hasLastFrame = false;
                _writerThread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "TSMP FFmpeg RTMP Writer"
                };
                _writerThread.Start();

                isPublishing = true;
                _nextFrameTime = 0.0;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                CleanupProcess();
                if (logFfmpegOutput) Debug.LogError("[TSMP] " + lastError);
            }
        }

        [ContextMenu("Stop Publishing")]
        public void StopPublishing()
        {
            readbackInFlight = false;
            isPublishing = false;

            lock (_frameLock)
            {
                _stopWriter = true;
                _hasPendingFrame = false;
            }

            if (_frameEvent != null)
                _frameEvent.Set();

            if (_writerThread != null)
            {
                if (!_writerThread.Join(1000))
                    _writerThread.Interrupt();
                _writerThread = null;
            }

            CleanupProcess();
            RestoreRunInBackgroundOverride();
        }

        private bool ValidateSetup()
        {
            if (sourceTexture == null)
            {
                lastError = "Source texture is not assigned.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                lastError = "FFmpeg path is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(GetEffectiveRtmpUrl()))
            {
                lastError = "RTMP URL is empty.";
                return false;
            }

            return true;
        }

        private void ResolveActiveDimensions()
        {
            if (useSourceDimensions && sourceTexture != null)
            {
                _activeWidth = sourceTexture.width;
                _activeHeight = sourceTexture.height;
            }
            else
            {
                _activeWidth = width;
                _activeHeight = height;
            }
        }

        private void ResetCounters()
        {
            framesSubmitted = 0;
            framesWritten = 0;
            framesDropped = 0;
            lastFfmpegExitCode = 0;
            lastFfmpegArguments = string.Empty;
            lastFfmpegOutput = string.Empty;
            ffmpegOutputTail = string.Empty;
        }

        private void RequestFrame()
        {
            if (readbackInFlight || sourceTexture == null)
                return;

            if (_process == null || _process.HasExited)
            {
                lastError = "FFmpeg process is not running.";
                StopPublishing();
                return;
            }

            readbackInFlight = true;
            AsyncGPUReadback.Request(sourceTexture, 0, TextureFormat.RGBA32, OnFrameReadbackComplete);
        }

        private void OnFrameReadbackComplete(AsyncGPUReadbackRequest request)
        {
            readbackInFlight = false;

            if (!isPublishing)
                return;

            if (request.hasError)
            {
                lastError = "AsyncGPUReadback failed.";
                return;
            }

            NativeArray<byte> data = request.GetData<byte>();
            int byteLength = data.Length;
            EnsureFrameBuffers(byteLength);
            data.CopyTo(_readbackBuffer);

            byte[] source = _readbackBuffer;
            if (flipVertical)
            {
                FlipFrameRows(_readbackBuffer, _flipBuffer, _activeWidth, _activeHeight, 4);
                source = _flipBuffer;
            }

            SubmitFrame(source, byteLength);
        }

        private void EnsureFrameBuffers(int byteLength)
        {
            if (_readbackBuffer == null || _readbackBuffer.Length != byteLength)
                _readbackBuffer = new byte[byteLength];

            if (_pendingFrame == null || _pendingFrame.Length != byteLength)
                _pendingFrame = new byte[byteLength];

            if (_writerFrame == null || _writerFrame.Length != byteLength)
                _writerFrame = new byte[byteLength];

            if (_lastFrame == null || _lastFrame.Length != byteLength)
                _lastFrame = new byte[byteLength];

            if (flipVertical && (_flipBuffer == null || _flipBuffer.Length != byteLength))
                _flipBuffer = new byte[byteLength];
        }

        private void SubmitFrame(byte[] frame, int byteLength)
        {
            lock (_frameLock)
            {
                if (_hasPendingFrame)
                    framesDropped++;

                Buffer.BlockCopy(frame, 0, _pendingFrame, 0, byteLength);
                _pendingLength = byteLength;
                _hasPendingFrame = true;
                framesSubmitted++;
            }

            if (_frameEvent != null)
                _frameEvent.Set();
        }

        private void WriterLoop()
        {
            int waitMs = Mathf.Max(1, Mathf.RoundToInt(1000f / Mathf.Max(1, frameRate)));

            while (!_stopWriter)
            {
                if (_frameEvent != null)
                    _frameEvent.WaitOne(waitMs);

                bool hasFrameToWrite = false;
                int length;

                lock (_frameLock)
                {
                    if (_stopWriter)
                        return;

                    if (!_hasPendingFrame)
                    {
                        if (!repeatLastFrameWhenIdle || !_hasLastFrame)
                            continue;

                        length = _lastFrameLength;
                        Buffer.BlockCopy(_lastFrame, 0, _writerFrame, 0, length);
                        hasFrameToWrite = true;
                    }
                    else
                    {
                        length = _pendingLength;
                        Buffer.BlockCopy(_pendingFrame, 0, _writerFrame, 0, length);
                        Buffer.BlockCopy(_pendingFrame, 0, _lastFrame, 0, length);
                        _lastFrameLength = length;
                        _hasLastFrame = true;
                        _hasPendingFrame = false;
                        hasFrameToWrite = true;
                    }
                }

                if (!hasFrameToWrite)
                    continue;

                try
                {
                    if (_process == null || _process.HasExited)
                        return;

                    Stream stream = _process.StandardInput.BaseStream;
                    stream.Write(_writerFrame, 0, length);
                    framesWritten++;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    return;
                }
            }
        }

        private void ApplyRunInBackgroundOverride()
        {
            if (!forceRunInBackground || _didOverrideRunInBackground)
                return;

            _previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _didOverrideRunInBackground = true;
        }

        private void RestoreRunInBackgroundOverride()
        {
            if (!_didOverrideRunInBackground)
                return;

            Application.runInBackground = _previousRunInBackground;
            _didOverrideRunInBackground = false;
        }

        private string BuildFfmpegArguments()
        {
            int gop = Mathf.Max(1, frameRate);
            int bufferKbps = Mathf.Max(64, videoBitrateKbps / 2);
            string pixelFormat = yuv420p ? "yuv420p" : "yuv444p";
            string extra = string.IsNullOrWhiteSpace(extraArguments) ? string.Empty : " " + extraArguments.Trim();

            return "-hide_banner -loglevel warning " +
                "-f rawvideo -pix_fmt rgba " +
                "-s " + _activeWidth + "x" + _activeHeight + " " +
                "-r " + frameRate + " " +
                "-i pipe:0 " +
                "-an " +
                "-c:v libx264 " +
                "-preset " + preset + " " +
                "-tune zerolatency " +
                "-pix_fmt " + pixelFormat + " " +
                "-b:v " + videoBitrateKbps + "k " +
                "-maxrate " + videoBitrateKbps + "k " +
                "-bufsize " + bufferKbps + "k " +
                "-g " + gop + " " +
                "-keyint_min " + gop + " " +
                "-sc_threshold 0 " +
                "-bf 0" +
                extra + " " +
                "-flvflags no_duration_filesize " +
                "-f flv " +
                QuoteArgument(GetEffectiveRtmpUrl());
        }

        private bool CheckRtmpTcpEndpoint()
        {
            string effectiveUrl = GetEffectiveRtmpUrl();
            if (!Uri.TryCreate(effectiveUrl, UriKind.Absolute, out Uri uri))
            {
                lastError = "RTMP URL is invalid.";
                return false;
            }

            int port = uri.Port > 0 ? uri.Port : 1935;
            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(uri.Host, port, null, null);
                    bool connected = result.AsyncWaitHandle.WaitOne(tcpConnectTimeoutMs);
                    if (!connected)
                    {
                        lastError = "RTMP TCP connect timed out: " + uri.Host + ":" + port;
                        return false;
                    }

                    client.EndConnect(result);
                    return true;
                }
            }
            catch (Exception ex)
            {
                lastError = "RTMP TCP connect failed: " + uri.Host + ":" + port + " (" + ex.Message + ")";
                return false;
            }
        }

        private string GetEffectiveRtmpUrl()
        {
            if (!useServerAndStreamKey)
                return rtmpUrl;

            string server = string.IsNullOrWhiteSpace(rtmpServerUrl) ? string.Empty : rtmpServerUrl.Trim();
            string key = string.IsNullOrWhiteSpace(streamKey) ? string.Empty : streamKey.Trim();

            if (string.IsNullOrEmpty(server))
                return string.Empty;

            if (string.IsNullOrEmpty(key))
                return server;

            return server.TrimEnd('/') + "/" + key.TrimStart('/');
        }

        private void OnFfmpegOutput(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrEmpty(args.Data))
                return;

            lastFfmpegOutput = args.Data;
            if (string.IsNullOrEmpty(ffmpegOutputTail))
                ffmpegOutputTail = args.Data;
            else
                ffmpegOutputTail += "\n" + args.Data;

            const int maxTailChars = 4096;
            if (ffmpegOutputTail.Length > maxTailChars)
                ffmpegOutputTail = ffmpegOutputTail.Substring(ffmpegOutputTail.Length - maxTailChars);

            if (logFfmpegOutput)
                Debug.Log("[TSMP] " + args.Data);
        }

        private void CleanupProcess()
        {
            if (_process == null)
                return;

            try
            {
                _process.ErrorDataReceived -= OnFfmpegOutput;
                _process.OutputDataReceived -= OnFfmpegOutput;

                if (!_process.HasExited)
                {
                    try
                    {
                        _process.StandardInput.Close();
                    }
                    catch
                    {
                    }

                    if (!_process.WaitForExit(1000))
                        _process.Kill();
                }

                if (_process.HasExited)
                    lastFfmpegExitCode = _process.ExitCode;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        private static void FlipFrameRows(byte[] source, byte[] destination, int width, int height, int bytesPerPixel)
        {
            int rowBytes = width * bytesPerPixel;
            for (int y = 0; y < height; y++)
            {
                int sourceOffset = y * rowBytes;
                int destinationOffset = (height - 1 - y) * rowBytes;
                Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, rowBytes);
            }
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
