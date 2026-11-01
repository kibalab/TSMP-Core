#if UNITY_EDITOR && !COMPILER_UDONSHARP && !UDONSHARP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using K13A.TSMP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class Rgb16PreparationValidation
{
    static readonly List<string> Rows = new List<string>();
    static readonly List<string> Notes = new List<string>();
    static readonly List<Object> Owned = new List<Object>();
    static string Root => Environment.GetEnvironmentVariable("TSMP_RGB16_PREPARATION_RESULTS");
    static int checks;
    static void Check(bool value, string text) { checks++; if (!value) throw new InvalidOperationException(text); }
    static string Row(params object[] values) => string.Join(",", values.Select(value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\""));
    static string H(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    static double Ms(long delta) => delta * 1000.0 / Stopwatch.Frequency;
    static T Keep<T>(T item) where T : Object { Owned.Add(item); return item; }
    static void Save() { File.WriteAllLines(Path.Combine(Root, "samples.csv"), Rows); File.WriteAllLines(Path.Combine(Root, "notes.txt"), Notes); }
    static TSMPCodec Codec()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec.rgb16/Runtime/Codec_RGB16.prefab");
        Check(prefab != null, "RGB16 prefab missing");
        var codec = Keep(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> copy = original =>
        {
            if (original == null) return null;
            if (!copies.TryGetValue(original, out Material clone)) { clone = Keep(new Material(original)); copies.Add(original, clone); }
            return clone;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, copy((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials) field.SetValue(codec, materials.Select(copy).ToArray());
        }
        codec.codecOptionBytes = new byte[] { 4, 4, 4, 1, 2 };
        codec.activeWidthBlocks = 160; codec.calibrationStartBlock = 800; codec.byteCount = 5375; codec.ApplyDecodeOptions();
        Check(codec.selectedDecodeMaterial == codec.GetDecodeMaterial(1), "Default 4/4/4 Refine must select fixed Refine material");
        Check(codec.payloadStartRow == 6 && codec.DecodeMaterialCount == 4, "Unexpected RGB16 layout/materials");
        Notes.Add("ActiveMaterial=" + codec.selectedDecodeMaterial.name + "; Shader=" + codec.selectedDecodeMaterial.shader.name);
        return codec;
    }
    static RenderTexture Target(int width, int height)
    {
        var texture = Keep(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        Check(texture.Create(), "RenderTexture creation failed"); return texture;
    }
    static void Configure(TSMPCodec codec, Material material, int mode, RenderTexture source, RenderTexture output)
    {
        material.SetFloat("_BlockSize", 8); material.SetFloat("_SampleSize", 4);
        material.SetFloat("_StartBlock", codec.payloadStartRow * 160); material.SetFloat("_ByteCount", 5375);
        material.SetFloat("_ActiveWidthBlocks", 160); material.SetFloat("_SourceWidth", source.width); material.SetFloat("_SourceHeight", source.height);
        material.SetFloat("_OutputWidth", output.width); material.SetFloat("_OutputHeight", output.height); material.SetFloat("_FlipY", 1);
        if (material.HasProperty("_Rgb16CalibrationStartBlock")) material.SetFloat("_Rgb16CalibrationStartBlock", 800);
        if (material.HasProperty("_RBits")) material.SetFloat("_RBits", 4);
        if (material.HasProperty("_GBits")) material.SetFloat("_GBits", 4);
        if (material.HasProperty("_BBits")) material.SetFloat("_BBits", 4);
        if (material.HasProperty("_RefineRadius")) material.SetFloat("_RefineRadius", 2);
        if (material.HasProperty("_RecoveryMode")) material.SetFloat("_RecoveryMode", mode);
    }
    static bool Flush(RenderTexture output, Texture2D pixel, byte[] expected)
    {
        var previous = RenderTexture.active;
        try { RenderTexture.active = output; pixel.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0); }
        finally { RenderTexture.active = previous; }
        var bytes = pixel.GetRawTextureData<byte>();
        for (int i = 0; i < expected.Length; i++) if (bytes[i] != expected[i]) return false;
        return true;
    }
    static void Draw(TSMPCodec codec, int variant, int mode, int round, string group, bool flush,
        RenderTexture source, RenderTexture output, Texture2D pixel, byte[] expected)
    {
        var material = codec.GetDecodeMaterial(variant); Check(material != null && material.shader.isSupported, "Missing/unsupported material");
        Configure(codec, material, mode, source, output);
        // Flush source upload before measurement in Run. Configuration and output
        // inspection are outside the three timed operations below.
        long start = Stopwatch.GetTimestamp(); codec.PrepareDecode(source, material); long prepared = Stopwatch.GetTimestamp();
        Graphics.Blit(source, output, material); long submitted = Stopwatch.GetTimestamp();
        bool exact = false; long ended = submitted;
        if (flush) { exact = Flush(output, pixel, expected); ended = Stopwatch.GetTimestamp(); }
        Rows.Add(Row(group, round, variant, mode, material.name, material.shader.name,
            material.HasProperty("_RecoveryMode") ? material.GetFloat("_RecoveryMode") : -1,
            material.IsKeywordEnabled("TSMP_CALIBRATION_LUT"), Ms(prepared - start), Ms(submitted - prepared),
            flush ? Ms(ended - submitted) : -1, Ms(ended - start), flush, flush ? (exact ? "EXACT" : "DIFFERS") : "NOT_READ"));
        if (group == "active-fixed" && mode == 0) Check(exact, "Active ordinary 4/4/4 payload differs from saved original");
    }
    static void Active(TSMPCodec codec, RenderTexture source, RenderTexture output, Texture2D pixel, byte[] expected)
    {
        // Ordinary mode zero is first: this is the actual default material, not
        // preparation of every potential codec material.
        for (int mode = 0; mode <= 2; mode++) for (int round = 0; round < 2; round++)
            Draw(codec, 1, mode, round, "active-fixed", true, source, output, pixel, expected);
        Save();
    }
    static void Broad(TSMPCodec codec, RenderTexture source, RenderTexture output, Texture2D pixel, byte[] expected)
    {
        for (int round = 0; round < 2; round++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int variant = 0; variant < 4; variant++) for (int mode = 0; mode <= 2; mode++)
                Draw(codec, variant, mode, round, "broad-final-flush", variant == 3 && mode == 2, source, output, pixel, expected);
            Notes.Add("BroadRound=" + round + "; HostWallMsIncludingInstrumentation=" + Ms(Stopwatch.GetTimestamp() - start).ToString("F4", CultureInfo.InvariantCulture));
            Save();
        }
        // This diagnostic changes synchronization deliberately so that deferred
        // GPU/compiler cost can be associated with a material/mode.
        for (int round = 0; round < 2; round++) for (int variant = 0; variant < 4; variant++) for (int mode = 0; mode <= 2; mode++)
            Draw(codec, variant, mode, round, "broad-each-flush", true, source, output, pixel, expected);
        Save();
    }
    public static void Run()
    {
        Exception failure = null;
        try
        {
            Check(!string.IsNullOrWhiteSpace(Root), "Results root required"); Directory.CreateDirectory(Root);
            Check(!File.Exists(Path.Combine(Root, "result.txt")), "Use fresh results");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Rows.Add("group,round,variant,requestedMode,material,shader,actualMode,calibrationLut,prepareMs,blitSubmitMs,flushAndCompareMs,totalMs,flushed,payload");
            Notes.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType + "; ColorSpace=" + QualitySettings.activeColorSpace);
            Notes.Add("Fresh editor process with existing Unity/driver caches intact. Not a first-install/cold-disk-cache or actual VRChat measurement.");
            Notes.Add("Prepare/Blit are host wall time and may include synchronous shader/driver work; flush includes queued GPU wait plus 8KB ReadPixels and full original-byte comparison. No GPU execution time inferred.");
            string input = Environment.GetEnvironmentVariable("TSMP_RGB16_PREPARATION_INPUT"), payloadPath = Environment.GetEnvironmentVariable("TSMP_RGB16_PREPARATION_PAYLOAD");
            byte[] imageBytes = File.ReadAllBytes(input), expected = File.ReadAllBytes(payloadPath);
            Check(expected.Length == 5375, "Unexpected payload length");
            Notes.Add("Input=" + input + "; SHA256=" + H(imageBytes)); Notes.Add("Payload=" + payloadPath + "; SHA256=" + H(expected));
            var image = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true)); Check(image.LoadImage(imageBytes), "Invalid PNG");
            Check(image.width == 1280 && image.height == 720, "Unexpected source geometry");
            image.filterMode = FilterMode.Point; image.wrapMode = TextureWrapMode.Clamp;
            var source = Target(image.width, image.height); var output = Target(2048, 1);
            var pixel = Keep(new Texture2D(2048, 1, TextureFormat.RGBA32, false, true));
            Graphics.Blit(image, source); var prior = RenderTexture.active;
            var sourcePixel = Keep(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            try { RenderTexture.active = source; sourcePixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); } finally { RenderTexture.active = prior; }
            var codec = Codec(); string order = Environment.GetEnvironmentVariable("TSMP_RGB16_PREPARATION_ORDER"); Notes.Add("Order=" + order);
            if (order == "broad-first") { Broad(codec, source, output, pixel, expected); Active(codec, source, output, pixel, expected); }
            else { Check(order == "fixed-first", "Invalid order"); Active(codec, source, output, pixel, expected); Broad(codec, source, output, pixel, expected); }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Notes.Add("Assertions=" + checks); if (failure != null) Notes.Add(failure.ToString()); Save();
            File.WriteAllText(Path.Combine(Root, "result.txt"), (failure == null ? "PASS" : "FAIL") + "\nAssertions=" + checks + "\n" + (failure == null ? "" : failure.ToString()));
            EditorApplication.Exit(failure == null ? 0 : 1);
        }
    }
}
#endif
