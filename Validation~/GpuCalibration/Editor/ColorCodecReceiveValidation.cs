using System;
using System.Linq;
using K13A.TSMP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class ColorCodecReceiveValidation
{
    private const int Columns = 81;
    private const int Block = 8;

    public static void Run()
    {
        int cases = 0;
        foreach (string name in new[] { "Color256", "RGB16", "RGB20" })
        {
            string root = "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Runtime/Codec_" + name + ".prefab");
            var instance = Object.Instantiate(prefab);
            var codec = instance.GetComponent<TSMPCodec>();
            var prep = new Material(codec.calibrationMaterial);
            try
            {
                codec.calibrationMaterial = prep;
                for (int i = 0; i < codec.DebugMaterialCount; ++i)
                    Compile(codec.GetDebugMaterial(i));
                for (int i = 0; i < codec.DecodeMaterialCount; ++i)
                {
                    var original = codec.GetDecodeMaterial(i);
                    var decode = new Material(original);
                    if (codec is TSMPCodecColor256 color && i == 2) color.robustRefineByteDecodeMaterial = decode;
                    if (codec is TSMPCodecRGB16 rgb)
                    {
                        if (i == 1) rgb.refine444ByteDecodeMaterial = decode;
                        if (i == 3) rgb.variableRefineByteDecodeMaterial = decode;
                    }
                    Debug.Log("Checking " + decode.shader.name);
                    try
                    {
                        cases += TestMode(codec, i, decode, false);
                        if (codec is TSMPCodecRGB16 && i >= 2 || codec is TSMPCodecColor256)
                            cases += TestMode(codec, i, decode, true);
                    }
                    finally
                    {
                        Object.DestroyImmediate(decode);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(prep);
            }
        }
        Debug.Log($"Color codec receive PASS: {cases} GPU round trips on {SystemInfo.graphicsDeviceName}");
    }

    private static void AddCheckerboard(Texture2D texture, int payloadRow)
    {
        var pixels = texture.GetRawTextureData<Color32>();
        int height = texture.height - payloadRow * Block;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < texture.width; x++)
        {
            int i = y * texture.width + x;
            var pixel = pixels[i];
            int delta = ((x + y) % 2 == 0) ? 32 : -32;
            if (pixel.r >= 32 && pixel.r <= 223) pixel.r = (byte)(pixel.r + delta);
            if (pixel.g >= 32 && pixel.g <= 223) pixel.g = (byte)(pixel.g + delta);
            if (pixel.b >= 32 && pixel.b <= 223) pixel.b = (byte)(pixel.b + delta);
            pixels[i] = pixel;
        }
        texture.Apply(false, false);
    }

    private static void Compile(Material material)
    {
        ShaderUtil.CompilePass(material, 0, true);
        if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
            throw new InvalidOperationException("Shader compilation failed: " + material.shader.name);
    }

    private static int TestMode(TSMPCodec codec, int mode, Material material, bool alternate)
    {
        var rgb = codec as TSMPCodecRGB16;
        var rgb20 = codec as TSMPCodecRGB20;
        var color = codec as TSMPCodecColor256;
        bool interleave = color != null && alternate;
        if (rgb != null)
        {
            rgb.rBits = alternate ? 1 : mode < 2 ? 4 : 5;
            rgb.gBits = alternate ? 1 : mode < 2 ? 4 : 6;
            rgb.bBits = alternate ? 1 : 4;
            material.SetFloat("_RBits", rgb.rBits);
            material.SetFloat("_GBits", rgb.gBits);
            material.SetFloat("_BBits", rgb.bBits);
        }
        var source = new Texture2D(Columns * Block, 256, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        var output = RenderTexture.GetTemporary(42, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        int cases = 0;
        try
        {
            material.SetFloat("_SourceWidth", source.width);
            material.SetFloat("_SourceHeight", source.height);
            material.SetFloat("_OutputWidth", output.width);
            material.SetFloat("_OutputHeight", 1);
            material.SetFloat("_FlipY", 1);
            material.SetFloat("_BlockSize", Block);
            material.SetFloat("_ActiveWidthBlocks", Columns);
            material.SetFloat("_StartBlock", Columns * codec.GetPayloadStartRow(source.width, Block));
            string calibration = rgb != null ? "_Rgb16CalibrationStartBlock" :
                rgb20 != null ? "_Rgb20CalibrationStartBlock" : "_ColorCalibrationStartBlock";
            material.SetFloat(calibration, Columns * Luma4Raster.PayloadStartRow);
            if (color != null) material.SetFloat("_InterleaveMode", interleave ? 1 : 0);
            foreach (int length in new[] { Columns * 2, Columns * 2 + 1 })
            {
                var expected = new byte[output.width * 4];
                var logical = new byte[length];
                new System.Random(710 + length).NextBytes(logical);
                Array.Copy(logical, expected, length);
                int rows = (length + Columns - 1) / Columns;
                byte[] physical = logical;
                if (interleave)
                {
                    physical = new byte[rows * Columns];
                    for (int i = 0; i < length; i++)
                        physical[(i % rows) * Columns + i / rows] = logical[i];
                }
                if (!codec.TryWriteFrame(source, Block, new byte[56], physical, out var error))
                    throw new InvalidOperationException(error);
                material.SetFloat("_ByteCount", length);
                foreach (int sample in new[] { 1, 3, 4 })
                {
                    if (sample == 4)
                        AddCheckerboard(source, codec.GetPayloadStartRow(source.width, Block));
                    foreach (bool fast in new[] { false, true })
                    for (int policy = 0; policy < (color != null ? 1 : 2); policy++)
                    {
                        bool prefer = policy != 0;
                        if (rgb != null) { rgb.useBilinear4 = fast; rgb.preferCalibrationLut = prefer; }
                        if (rgb20 != null) { rgb20.useBilinear4 = fast; rgb20.preferCalibrationLut = prefer; }
                        if (color != null) color.useBilinear4 = fast;
                        material.SetFloat("_SampleSize", sample);
                        codec.PrepareDecode(source, material);
                        bool lut = color != null ? mode == 2 : (rgb20 != null || mode % 2 == 0) && (prefer || sample > 1);
                        if (material.IsKeywordEnabled("TSMP_CALIBRATION_LUT") != lut)
                            throw new InvalidOperationException("Unexpected LUT choice: " + material.shader.name);
                        Compile(material);
                        Graphics.Blit(source, output, material);
                        var read = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32);
                        read.WaitForCompletion();
                        if (read.hasError || !read.GetData<byte>().ToArray().SequenceEqual(expected))
                            throw new InvalidOperationException($"{material.shader.name}: length={length} alternate={alternate} samples={sample} fast={fast} preferLut={prefer}");
                        cases++;
                    }
                }
            }
            return cases;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(output);
            Object.DestroyImmediate(source);
        }
    }
}
