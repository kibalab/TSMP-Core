#if !UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Offline, finite video roundtrip. This measures byte accuracy, not live delivery or latency.
public static class VideoTransportValidation
{
    const int Frames = 12;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static IEnumerator work;
    static readonly List<Object> Owned = new List<Object>();
    static readonly List<string> Rows = new List<string>();
    static readonly List<string> Details = new List<string>();
    static string root;
    static bool profileFailed;

    public static void Run()
    {
        if (typeof(TSMPBehaviour).BaseType != typeof(MonoBehaviour))
            throw new InvalidOperationException("VideoTransport requires a dedicated SDK-free project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("TSMP.VideoTransport", true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.VideoTransport", false)) return;
        SessionState.SetBool("TSMP.VideoTransport", false);
        root = Environment.GetEnvironmentVariable("TSMP_VIDEO_RESULTS");
        Rows.Clear(); Details.Clear(); Owned.Clear(); profileFailed = false;
        Rows.Add("profile,expectedFrame,actualFrame,expectedBytes,actualBytes,headerValid,frameValid,expectedFrameMatch,byteMismatches,status");
        Details.Add("Offline Luma4 / 1280x720 / block8 / sample4; 12 changing frames per profile.");
        Details.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; ColorSpace=" + QualitySettings.activeColorSpace);
        Details.Add("Frame payload lengths 32/2495/5375 exercise small, Full16-size and Fingers16-size byte loads; these are synthetic bytes, not captured avatars.");
        Details.Add("Each payload is a structurally valid NetworkFrame with one raw-byte field. Decode mode is parse-only; no receiver state or RPC is applied.");
        work = Validate();
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        foreach (Object item in Owned)
        {
            if (item is RenderTexture texture) texture.Release();
            if (item != null) Object.DestroyImmediate(item);
        }
        string status = error != null || profileFailed ? "FAIL" : "PASS";
        if (error != null) Details.Add(error.ToString());
        File.WriteAllLines(Path.Combine(root, "frames.csv"), Rows);
        File.WriteAllLines(Path.Combine(root, "result.txt"), new[] { status }.Concat(Details));
        EditorApplication.Exit(status == "PASS" ? 0 : 1);
    }

    static T Own<T>(T item) where T : Object { Owned.Add(item); return item; }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    static RenderTexture Target(int width, int height)
    {
        var texture = Own(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        texture.filterMode = FilterMode.Point;
        Check(texture.Create(), "RenderTexture creation");
        return texture;
    }

    static byte[] Payload(int frame)
    {
        int length = new[] { 32, 2495, 5375 }[(frame - 1) % 3];
        var bytes = new byte[length];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, (uint)frame);
        int messageStart = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, (ushort)frame);
        int overhead = NetworkFrameProtocol.NetworkHeaderBytes + NetworkFrameProtocol.MessageHeaderBytes
            + NetworkFrameProtocol.VariableStateBodyHeaderBytes + NetworkFrameProtocol.VariableValueHeaderBytes;
        byte[] value = Enumerable.Range(0, length - overhead).Select(i => (byte)(frame * 29 + i * 73 + (i >> 8))).ToArray();
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, 100, NetworkFrameProtocol.ValueTypeRawBytes, value);
        Check(cursor == length && NetworkFrameWriter.EndVariableState(bytes, messageStart, cursor, 1)
            && NetworkFrameWriter.EndNetworkFrame(bytes, 0, 1), "NetworkFrame fixture construction");
        return bytes;
    }

    static IEnumerator ProcessFile(string arguments, string log)
    {
        string ffmpeg = Environment.GetEnvironmentVariable("TSMP_VIDEO_FFMPEG");
        using (var process = Process.Start(new ProcessStartInfo(ffmpeg, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        }))
        {
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            double deadline = EditorApplication.timeSinceStartup + 120;
            while (!process.HasExited && EditorApplication.timeSinceStartup < deadline) yield return null;
            if (!process.HasExited) { process.Kill(); throw new TimeoutException("FFmpeg timed out: " + log); }
            File.WriteAllText(log, arguments + "\n" + output.Result + "\n" + errors.Result);
            Check(process.ExitCode == 0, "FFmpeg exit " + process.ExitCode + ": " + log);
        }
    }

    static IEnumerator Validate()
    {
        Application.runInBackground = true;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab");
        Check(prefab != null, "Luma4 prefab missing");
        var codec = Own(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        // Clone materials so validation never mutates package assets.
        var materialCopies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (!materialCopies.TryGetValue(original, out Material copy))
                materialCopies[original] = copy = Own(new Material(original));
            return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials)
                field.SetValue(codec, materials.Select(clone).ToArray());
        }
        var image = Own(new Texture2D(1280, 720, TextureFormat.RGBA32, false, true));
        image.filterMode = FilterMode.Point;
        string input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        for (int frame = 1; frame <= Frames; frame++)
        {
            byte[] payload = Payload(frame);
            var header = FrameHeader.CreateDefault();
            header.BlockSize = 8; header.ActiveWidthBlocks = 160; header.ActiveHeightBlocks = 90;
            header.CodecId = codec.codecId; header.SymbolMode = (byte)codec.SymbolMode;
            header.PayloadSize = (ushort)payload.Length; header.FrameIndex = (uint)frame; header.DecodeSampleSize = 4;
            var bytes = new byte[FrameHeader.Size];
            header.WriteTo(bytes, 0);
            Check(codec.TryWriteFrame(image, 8, bytes, payload, out string error), error);
            File.WriteAllBytes(Path.Combine(input, "frame-" + frame.ToString("000") + ".png"), image.EncodeToPNG());
        }
        var decoder = Own(new GameObject("Offline video decoder")).AddComponent<TSMPDecoder>();
        decoder.applyEveryFrame = false; decoder.skipDuplicateFrames = false; decoder.decodeSafetyMode = 3;
        decoder.debugLog = false; decoder.codecHandlers = new[] { codec }; decoder.sampleSize = 4;
        decoder.payloadByteTexture = Target(2048, 1);
        var source = Target(1280, 720);
        decoder.sourceTexture = source;
        yield return null;
        foreach (string profile in new[] { "png-control", "ffv1-rgb", "h264-crf18", "h264-crf28" })
        {
            string decoded = input;
            if (profile != "png-control")
            {
                string directory = Path.Combine(root, profile);
                Directory.CreateDirectory(directory);
                string video = Path.Combine(directory, "encoded.mkv");
                string encoder = profile == "ffv1-rgb" ? "-c:v ffv1 -pix_fmt bgr0" :
                    "-c:v libx264 -preset veryfast -crf " + (profile.EndsWith("18") ? "18" : "28") + " -pix_fmt yuv420p";
                var encode = ProcessFile("-y -hide_banner -loglevel error -framerate 30 -i \"" + Path.Combine(input, "frame-%03d.png") + "\" -frames:v " + Frames + " " + encoder + " \"" + video + "\"", Path.Combine(directory, "encode.log"));
                while (encode.MoveNext()) yield return encode.Current;
                decoded = Path.Combine(directory, "decoded"); Directory.CreateDirectory(decoded);
                var decode = ProcessFile("-y -hide_banner -loglevel error -i \"" + video + "\" -frames:v " + Frames + " \"" + Path.Combine(decoded, "frame-%03d.png") + "\"", Path.Combine(directory, "decode.log"));
                while (decode.MoveNext()) yield return decode.Current;
            }
            Check(Directory.GetFiles(decoded, "frame-*.png").Length == Frames, "Unexpected decoded frame count: " + profile);
            int accepted = 0, rejected = 0, corrupt = 0, wrongFrame = 0;
            for (int frame = 1; frame <= Frames; frame++)
            {
                Check(image.LoadImage(File.ReadAllBytes(Path.Combine(decoded, "frame-" + frame.ToString("000") + ".png"))), "PNG decode");
                image.filterMode = FilterMode.Point;
                Graphics.Blit(image, source);
                decoder.DecodeNow();
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (decoder.readbackInFlight && EditorApplication.timeSinceStartup < deadline) yield return null;
                Check(!decoder.readbackInFlight, "GPU callback timeout; profile=" + profile + "; frame=" + frame);
                byte[] expected = Payload(frame);
                byte[] actual = (byte[])typeof(TSMPDecoder).GetField("_payloadBytes", Private).GetValue(decoder);
                int validLength = (int)typeof(TSMPDecoder).GetField("_payloadDataBytes", Private).GetValue(decoder);
                bool valid = decoder.lastHeaderValid && decoder.lastFrameValid;
                bool expectedFrame = decoder.lastFrameIndex == frame;
                int mismatches = valid ? Math.Abs(expected.Length - validLength) : -1;
                if (valid) for (int i = 0; i < Math.Min(expected.Length, validLength); i++)
                    if (actual[i] != expected[i]) mismatches++;
                string status = !valid ? "REJECTED" : !expectedFrame ? "WRONG_FRAME" : mismatches == 0 ? "EXACT" : "CORRUPT";
                if (!valid) rejected++; else if (!expectedFrame) wrongFrame++; else if (mismatches != 0) corrupt++; else accepted++;
                Rows.Add(profile + "," + frame + "," + decoder.lastFrameIndex + "," + expected.Length + "," + validLength + "," + decoder.lastHeaderValid + "," + decoder.lastFrameValid + "," + expectedFrame + "," + mismatches + "," + status);
                if (!valid) Details.Add(profile + " frame " + frame + ": " + decoder.lastError);
            }
            bool pass = accepted == Frames;
            profileFailed |= !pass;
            Details.Add((pass ? "PASS " : "FAIL ") + profile + ": exact=" + accepted + "; rejected=" + rejected + "; corrupt=" + corrupt + "; wrongFrame=" + wrongFrame);
        }
        Details.Add("No live pacing, network, OBS/Spout, Udon VM, RPC application, avatar capture or real-client performance is certified by this offline measurement.");
    }
}
#endif
