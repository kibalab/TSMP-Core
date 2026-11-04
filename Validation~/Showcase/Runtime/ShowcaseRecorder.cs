#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

// Records an explicit application render target, or this application's framebuffer.
// It does not read another window or desktop content.
public sealed class ShowcaseRecorder : IDisposable
{
    readonly int width, height;
    readonly Texture2D capture;
    readonly byte[] pixels;
    readonly Process process;
    readonly Stream input;
    readonly StringBuilder stderr = new StringBuilder();
    readonly object stderrLock = new object();
    bool disposed;
    string error = "";

    public int Frames { get; private set; }
    public long FirstCaptureTicks { get; private set; }
    public long LastCaptureTicks { get; private set; }
    public int? ExitCode { get; private set; }
    public string Error => error;
    public string StderrTail { get { lock (stderrLock) return stderr.ToString(); } }

    public ShowcaseRecorder(string ffmpeg, string output, int width, int height, int fps)
    {
        if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0 || fps < 1 || fps > 120)
            throw new ArgumentOutOfRangeException("Use even positive dimensions and a supported frame rate");
        this.width = width; this.height = height;
        string destination = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(destination));
        capture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
        pixels = new byte[checked(width * height * 3)];
        process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = "-hide_banner -loglevel warning -y -use_wallclock_as_timestamps 1 -f rawvideo -pixel_format rgb24 -video_size " + width + "x" + height +
                " -framerate " + fps + " -i pipe:0 -an -vf vflip -r " + fps +
                " -fps_mode cfr -c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p -movflags +faststart \"" + destination.Replace("\"", "\\\"") + "\"",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardError = true
        }};
        process.ErrorDataReceived += ReceiveError;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("FFmpeg did not start");
            process.BeginErrorReadLine(); input = process.StandardInput.BaseStream;
        }
        catch
        {
            process.Dispose(); Object.Destroy(capture); throw;
        }
    }

    void ReceiveError(object sender, DataReceivedEventArgs args)
    {
        if (args.Data == null) return;
        lock (stderrLock)
        {
            stderr.AppendLine(args.Data);
            if (stderr.Length > 16000) stderr.Remove(0, stderr.Length - 16000);
        }
    }

    public void Capture(RenderTexture source = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ShowcaseRecorder));
        try
        {
            int sourceWidth = source != null ? source.width : Screen.width;
            int sourceHeight = source != null ? source.height : Screen.height;
            if (sourceWidth != width || sourceHeight != height)
                throw new InvalidOperationException("Capture size changed: expected " + width + "x" + height + ", got " + sourceWidth + "x" + sourceHeight);
            if (source != null && !source.IsCreated()) throw new InvalidOperationException("Recording render target is not created");
            if (process.HasExited) throw new InvalidOperationException("FFmpeg exited before capture finished: " + process.ExitCode + "\n" + StderrTail);
            long stamp = Stopwatch.GetTimestamp();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                capture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            }
            finally { RenderTexture.active = previous; }
            NativeArray<byte>.Copy(capture.GetRawTextureData<byte>(), pixels);
            input.Write(pixels, 0, pixels.Length);
            if (Frames == 0) FirstCaptureTicks = stamp;
            LastCaptureTicks = stamp; Frames++;
        }
        catch (Exception failure)
        {
            error = failure.ToString(); throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try
        {
            try { input.Dispose(); } catch (Exception failure) { if (error.Length == 0) error = failure.ToString(); }
            if (!process.WaitForExit(30000))
            {
                error = "FFmpeg did not finalize within 30 seconds";
                if (!process.HasExited) process.Kill(); process.WaitForExit(5000);
            }
            if (process.HasExited)
            {
                // Never add an unbounded stderr-drain wait to shutdown.
                ExitCode = process.ExitCode;
                if (ExitCode != 0 && error.Length == 0) error = "FFmpeg exit " + ExitCode + "\n" + StderrTail;
            }
        }
        finally
        {
            process.ErrorDataReceived -= ReceiveError; process.Dispose();
            if (capture != null) Object.Destroy(capture);
        }
    }
}
#endif
