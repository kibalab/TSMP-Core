#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// Recording-only localhost bridge. It carries complete TSMP frame pixels and a
// separate camera preview for presentation. No decoded state/pose is transported.
public sealed class ShowcasePipe : IDisposable
{
    public const int PreviewWidth = 480, PreviewHeight = 480;
    public const int StreamWidth = 640, StreamHeight = 360;
    public const int PreviewBytes = PreviewWidth * PreviewHeight * 4;
    public const int StreamBytes = StreamWidth * StreamHeight * 4;
    const int Magic = 0x504D5354, Version = 1, HeaderBytes = 24;

    sealed class Frame
    {
        public readonly byte[] preview = new byte[PreviewBytes];
        public readonly byte[] stream = new byte[StreamBytes];
        public int index;
        public bool hasStream;
    }

    readonly bool server;
    readonly int port, processId;
    readonly object gate = new object(), socketGate = new object();
    readonly AutoResetEvent available = new AutoResetEvent(false);
    readonly ManualResetEvent cancelled = new ManualResetEvent(false);
    readonly Thread worker;
    Frame producer = new Frame(), consumer = new Frame();
    bool ready;
    volatile bool stopping, connected;
    volatile string error;
    volatile int remoteProcessId;
    int networkFrames, streamFrames, replacedFrames;
    TcpListener listener;
    TcpClient socket;

    public bool Connected { get { return connected; } }
    public string Error { get { return error ?? string.Empty; } }
    public int RemoteProcessId { get { return remoteProcessId; } }
    // Includes preview-only packets; StreamFrames counts nonempty TSMP images.
    public int NetworkFrames { get { return Volatile.Read(ref networkFrames); } }
    public int StreamFrames { get { return Volatile.Read(ref streamFrames); } }
    // Latest-buffer replacement is observer-side coalescing, not a loss verdict.
    public int ReplacedFrames { get { return Volatile.Read(ref replacedFrames); } }

    public ShowcasePipe(bool server, int port)
    {
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        this.server = server; this.port = port;
        processId = Process.GetCurrentProcess().Id;
        worker = new Thread(Run) { IsBackground = true, Name = server ? "TSMP showcase send" : "TSMP showcase receive" };
        worker.Start();
    }

    // Input arrays are copied before return. The sender can safely reuse them.
    // At most one unsent packet is retained while the other buffer is in IO.
    public void Publish(byte[] previewRgba, byte[] streamRgba, int encodedFrame)
    {
        if (!server) throw new InvalidOperationException("Only the showcase sender publishes.");
        if (previewRgba == null || previewRgba.Length != PreviewBytes) throw new ArgumentException("Expected a 480x480 RGBA32 preview.", nameof(previewRgba));
        if (streamRgba != null && streamRgba.Length != StreamBytes) throw new ArgumentException("Expected a 640x360 RGBA32 TSMP image or null.", nameof(streamRgba));
        if (encodedFrame < 0) throw new ArgumentOutOfRangeException(nameof(encodedFrame));
        lock (gate)
        {
            if (stopping) return;
            if (ready) Interlocked.Increment(ref replacedFrames);
            Buffer.BlockCopy(previewRgba, 0, producer.preview, 0, PreviewBytes);
            producer.hasStream = streamRgba != null;
            if (producer.hasStream) Buffer.BlockCopy(streamRgba, 0, producer.stream, 0, StreamBytes);
            producer.index = encodedFrame;
            ready = true;
            available.Set();
        }
    }

    // Invoke on Unity's main thread. Buffers remain stable for the callback;
    // callers must copy/upload inside it and must not retain the array references.
    public bool TryConsume(Action<byte[], byte[], int> upload)
    {
        if (server) throw new InvalidOperationException("Only the showcase receiver consumes.");
        if (upload == null) throw new ArgumentNullException(nameof(upload));
        lock (gate)
        {
            if (!ready || stopping) return false;
            ready = false;
            upload(consumer.preview, consumer.hasStream ? consumer.stream : null, consumer.index);
            return true;
        }
    }

    void Run()
    {
        try
        {
            using (TcpClient client = server ? Accept() : Connect())
            {
                if (client == null || stopping) return;
                lock (socketGate)
                {
                    if (stopping) return;
                    socket = client;
                }
                client.NoDelay = true;
                client.SendBufferSize = 1024 * 1024;
                client.ReceiveBufferSize = 1024 * 1024;
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] header = new byte[HeaderBytes];
                    if (server)
                    {
                        client.ReceiveTimeout = 10000;
                        ReadExact(stream, header, 12);
                        if (GetInt(header, 0) != Magic || GetInt(header, 4) != Version || GetInt(header, 8) <= 0) throw new InvalidDataException("Invalid showcase receiver handshake.");
                        remoteProcessId = GetInt(header, 8);
                        client.ReceiveTimeout = 0;
                        connected = true;
                        SendFrames(stream, header);
                    }
                    else
                    {
                        PutInt(header, 0, Magic); PutInt(header, 4, Version); PutInt(header, 8, processId);
                        stream.Write(header, 0, 12);
                        connected = true;
                        ReceiveFrames(stream, header);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (!stopping) error = exception.GetType().Name + ": " + exception.Message;
        }
        finally
        {
            connected = false;
            lock (socketGate) socket = null;
            if (listener != null) listener.Stop();
        }
    }

    TcpClient Accept()
    {
        var pending = new TcpListener(IPAddress.Loopback, port);
        lock (socketGate)
        {
            if (stopping) return null;
            listener = pending;
            pending.Start(1);
        }
        return pending.AcceptTcpClient();
    }

    TcpClient Connect()
    {
        var deadline = Stopwatch.StartNew();
        while (!stopping && deadline.Elapsed.TotalSeconds < 30)
        {
            var candidate = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                lock (socketGate)
                {
                    if (stopping) { candidate.Close(); return null; }
                    socket = candidate;
                }
                candidate.Connect(IPAddress.Loopback, port);
                return candidate;
            }
            catch (SocketException)
            {
                candidate.Close();
                lock (socketGate) if (ReferenceEquals(socket, candidate)) socket = null;
                if (cancelled.WaitOne(250)) return null;
            }
        }
        if (!stopping) throw new TimeoutException("Local showcase sender was not available within 30 seconds.");
        return null;
    }

    void SendFrames(NetworkStream stream, byte[] header)
    {
        while (!stopping)
        {
            available.WaitOne();
            Frame sending;
            lock (gate)
            {
                if (stopping) return;
                if (!ready) continue;
                Frame swap = consumer; consumer = producer; producer = swap;
                sending = consumer; ready = false;
            }
            PutInt(header, 0, Magic); PutInt(header, 4, Version); PutInt(header, 8, processId);
            PutInt(header, 12, sending.index); PutInt(header, 16, PreviewBytes); PutInt(header, 20, sending.hasStream ? StreamBytes : 0);
            stream.Write(header, 0, HeaderBytes);
            stream.Write(sending.preview, 0, PreviewBytes);
            if (sending.hasStream) { stream.Write(sending.stream, 0, StreamBytes); Interlocked.Increment(ref streamFrames); }
            Interlocked.Increment(ref networkFrames);
        }
    }

    void ReceiveFrames(NetworkStream stream, byte[] header)
    {
        while (!stopping)
        {
            ReadExact(stream, header, HeaderBytes);
            int senderPid = GetInt(header, 8), frame = GetInt(header, 12), previewLength = GetInt(header, 16), streamLength = GetInt(header, 20);
            if (GetInt(header, 0) != Magic || GetInt(header, 4) != Version || senderPid <= 0 || frame < 0 || previewLength != PreviewBytes || (streamLength != 0 && streamLength != StreamBytes))
                throw new InvalidDataException("Invalid or oversized showcase frame header.");
            if (remoteProcessId != 0 && remoteProcessId != senderPid) throw new InvalidDataException("Showcase sender identity changed mid-connection.");
            ReadExact(stream, producer.preview, previewLength);
            producer.hasStream = streamLength != 0;
            if (producer.hasStream) ReadExact(stream, producer.stream, streamLength);
            producer.index = frame;
            lock (gate)
            {
                if (stopping) return;
                if (ready) Interlocked.Increment(ref replacedFrames);
                Frame swap = consumer; consumer = producer; producer = swap;
                ready = true; remoteProcessId = senderPid;
            }
            Interlocked.Increment(ref networkFrames);
            if (streamLength != 0) Interlocked.Increment(ref streamFrames);
        }
    }

    void ReadExact(NetworkStream stream, byte[] destination, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            if (stopping) throw new OperationCanceledException();
            int read = stream.Read(destination, offset, count - offset);
            if (read == 0) throw new EndOfStreamException("The other showcase process closed the frame connection.");
            offset += read;
        }
    }

    static void PutInt(byte[] bytes, int offset, int value)
    {
        unchecked { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); bytes[offset + 2] = (byte)(value >> 16); bytes[offset + 3] = (byte)(value >> 24); }
    }
    static int GetInt(byte[] bytes, int offset)
    {
        return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (stopping) return;
            stopping = true;
            ready = false;
            cancelled.Set(); available.Set();
        }
        lock (socketGate)
        {
            if (socket != null) socket.Close();
            if (listener != null) listener.Stop();
        }
        // A blocked read/write/accept is released by closing only our own sockets.
        // Keep wait handles alive if the OS has not released the worker yet.
        if (Thread.CurrentThread != worker && worker.Join(2000)) { available.Dispose(); cancelled.Dispose(); }
        connected = false;
    }
}
#endif
