using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif
using Object = UnityEngine.Object;

public static class EditorPresentationValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> Results = new List<string>();

    public static void Run()
    {
        var owned = new List<Object>();
        var materials = new List<Material>();
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var owner = new GameObject("Presentation validation");
            var sender = new GameObject("Transform sender");
            owned.Add(owner);
            owned.Add(sender);
#if UDONSHARP
            var encoder = owner.AddUdonSharpComponent<TSMPEncoder>();
            var sync = sender.AddUdonSharpComponent<TSMPNetworkTransformSync>();
#else
            var encoder = owner.AddComponent<TSMPEncoder>();
            var sync = sender.AddComponent<TSMPNetworkTransformSync>();
#endif
            sync.target = sender.transform;
            sync.networkId = 401;
            var setup = owner.AddComponent<TSMPSetup>();
            setup.applyOnValidate = false;
            setup.configureEncoder = false;
            setup.configureDecoder = false;
            setup.configureMaterials = false;
            setup.driveEncoderInEditor = false;
            setup.encoder = encoder;
            encoder.autoEncode = false;
            encoder.debugLog = false;
            encoder.transSyncRefreshInterval = 0;
            encoder.output = Target(643, 363);
            owned.Add(encoder.output);
            encoder.blockSize = 8;
            encoder.blockExpandMaterial = new Material(Shader.Find("Hidden/TSMP/Encoder Block Expand"));
            materials.Add(encoder.blockExpandMaterial);
            K13A.TSMP.Editor.TransSyncBindingBuilder.RebuildSceneBindings();

            var expected = Target(643, 363);
            owned.Add(expected);
            foreach (string name in new[] { "Luma4", "RGB16", "RGB20", "Color256", "Luma4" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab");
                if (prefab == null)
                {
                    Check(name != "Luma4", "Required Luma4 prefab is missing");
                    Results.Add("SKIP optional codec not installed: " + name);
                    continue;
                }
                var codec = InstantiateCodec(prefab.GetComponent<TSMPCodec>(), materials);
                owned.Add(codec.gameObject);
                encoder.selectedCodec = codec;
#if UDONSHARP
                encoder.selectedCodecUdonTarget = UdonSharpEditorUtility.GetBackingUdonBehaviour(codec);
#endif
                foreach (bool blocks in new[] { true, false })
                foreach (bool automatic in new[] { false, true })
                {
                    encoder.useBlockSymbolTexture = blocks;
                    sender.transform.position += Vector3.right;
                    uint frame = encoder.frameIndex;
                    long blits = RecordEncode(setup, encoder, automatic);
                    Results.Add(name + " blocks=" + blocks + " automatic=" + automatic + ": Graphics.Blit count=" + blits);
                    Check(blits == 1, "Expected one presentation Blit");
                    Check(encoder.frameIndex == frame + 1, "Encode " + name + ": " + encoder.lastError);
                    RenderExpected(encoder, expected);
                    Color32[] actual = Read(encoder.output);
                    Check(actual.SequenceEqual(Read(expected)), "Presentation mismatch " + name + " automatic=" + automatic);
#if UDONSHARP
                    Check((bool)typeof(TSMPEncoder).GetField("_usingBlockTexture", Private).GetValue(encoder) == blocks, "Requested output mode not exercised");
                    if (blocks)
                        Check(actual.Where((pixel, i) => i % 643 >= 640).All(pixel => pixel.r == 0 && pixel.g == 0 && pixel.b == 0), "Right remainder border is not black");
#endif
                    Results.Add("PASS " + name + " blocks=" + blocks + " automatic=" + automatic + ": exactly one frame; GPU pixels match encoder presentation including remainder borders");
                }
            }
#if !UDONSHARP
            encoder.outputTexture = Texture2D.blackTexture;
#endif
            foreach (bool automatic in new[] { false, true })
            {
                var previous = RenderTexture.active;
                RenderTexture.active = encoder.output;
                GL.Clear(false, true, Color.magenta);
                RenderTexture.active = previous;
                Color32[] sentinel = Read(encoder.output);
                uint frame = encoder.frameIndex;
                Check(RecordEncode(setup, encoder, automatic) == 0, "Skipped frame must not Blit");
                Check(encoder.frameIndex == frame && sentinel.SequenceEqual(Read(encoder.output)), "No-data frame overwrote output, automatic=" + automatic);
                int validBlockSize = encoder.blockSize;
                encoder.blockSize = 400;
                sender.transform.position += Vector3.right;
                Check(RecordEncode(setup, encoder, automatic) == 0, "Failed encode must not Blit");
                Check(encoder.frameIndex == frame && sentinel.SequenceEqual(Read(encoder.output)), "Failed encode overwrote output, automatic=" + automatic);
                encoder.blockSize = validBlockSize;
                encoder.EncodeNow();
                Results.Add("PASS skipped/failed encode leaves output untouched, automatic=" + automatic);
            }
            File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), new[] { "PASS", "Unity=" + Application.unityVersion }.Concat(Results));
        }
        catch (Exception error)
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "FAIL\n" + string.Join("\n", Results) + "\n" + error);
            throw;
        }
        finally
        {
            RenderTexture.active = null;
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            foreach (var material in materials) if (material != null) Object.DestroyImmediate(material);
        }
    }

    static long RecordEncode(TSMPSetup setup, TSMPEncoder encoder, bool automatic)
    {
        using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Graphics.Blit", 1,
            ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
        {
            Check(recorder.Valid, "Graphics.Blit recorder unavailable");
            Encode(setup, encoder, automatic);
            recorder.Stop();
            return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
        }
    }

    static void Encode(TSMPSetup setup, TSMPEncoder encoder, bool automatic)
    {
        if (!automatic) { setup.EncodeEncoderNow(); return; }
        encoder.autoEncode = true;
#if UDONSHARP
        setup.driveEncoderInEditor = true;
        typeof(TSMPSetup).GetField("_nextEditorEncodeTime", Private).SetValue(setup, 0.0);
        typeof(TSMPSetup).GetMethod("EditorUpdate", Private).Invoke(setup, null);
#else
        typeof(TSMPEncoder).GetField("_nextEncodeTime", Private).SetValue(encoder, 0.0);
        typeof(TSMPEncoder).GetMethod("EditorUpdate", Private).Invoke(encoder, null);
#endif
        encoder.autoEncode = false;
        setup.driveEncoderInEditor = false;
    }

    static void RenderExpected(TSMPEncoder encoder, RenderTexture target)
    {
#if UDONSHARP
        bool blocks = (bool)typeof(TSMPEncoder).GetField("_usingBlockTexture", Private).GetValue(encoder);
        EncoderUdonTextureRuntime.BlitEncodedTexture(encoder.outputTexture, target, blocks, encoder.blockExpandMaterial, target.width / encoder.blockSize, target.height / encoder.blockSize, encoder.blockSize);
#else
        Graphics.Blit((Texture)typeof(TSMPEncoder).GetField("_stagingTexture", Private).GetValue(encoder), target);
#endif
    }

    static RenderTexture Target(int width, int height)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.Create();
        return rt;
    }

    // Keep this EditorEncoding harness independent of the optional all-codec
    // snapshot suite. Never assign frame properties to package material assets.
    static TSMPCodec InstantiateCodec(TSMPCodec prefab, List<Material> owned)
    {
        Check(prefab != null, "Codec prefab has no TSMPCodec component");
        var codec = Object.Instantiate(prefab.gameObject).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            if (copies.TryGetValue(original, out Material copy)) return copy;
            copy = new Material(original) { hideFlags = HideFlags.DontSave };
            copies.Add(original, copy);
            owned.Add(copy);
            return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material))
                field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials)
                field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return codec;
    }

    static Color32[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        var pixels = texture.GetPixels32();
        Object.DestroyImmediate(texture);
        RenderTexture.active = previous;
        return pixels;
    }

    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
