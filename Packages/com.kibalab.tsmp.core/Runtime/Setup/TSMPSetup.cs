#if !COMPILER_UDONSHARP
using System.Collections.Generic;
using UnityEngine;

namespace K13A.TSMP
{
    [AddComponentMenu("TSMP/TSMP Setup")]
    [ExecuteAlways]
    public sealed class TSMPSetup : MonoBehaviour
    {
        [Header("Layout")]
        public Texture sourceTexture;
        public Texture decoderSourceTexture;
        public bool preferEncoderOutputAsDecoderSource = true;
        public int width = 1280;
        public int height = 720;
        public int blockSize = ProtocolConstants.DefaultBlockSize;
        public int sampleSize;
        public bool flipY = true;
        [HideInInspector] public int payloadSymbolMode;
        public bool applyOnValidate = true;

        [Header("Components")]
        public Component encoder;
        public Component decoder;
        public bool configureEncoder = true;
        public bool configureDecoder = true;
        public bool driveEncoderInEditor = true;

        [Header("RenderTextures")]
        public RenderTexture encoderOutput;
        public RenderTexture payloadByteTexture;
        public RenderTexture[] extraFrameRenderTextures;
        public RenderTexture[] extraByteRenderTextures;
        public bool resizeFrameRenderTextures = true;
        public bool resizeByteRenderTextures = true;
        public bool autoSizeByteTexture = true;
        public bool roundByteTextureWidthToPowerOfTwo = true;
        public int byteTextureSafetyPixels = 16;
        public int byteTextureWidth = 1024;
        public int byteTextureHeight = 1;

        [Header("Codecs")]
        public bool autoDiscoverCodecs = true;
        public TSMPCodec[] codecPrefabs;
        public int selectedCodecIndex;
        public Transform codecInstanceRoot;
        [HideInInspector] public bool instantiateCodecPrefabsAtRuntime = true;
        [SerializeField, HideInInspector] private TSMPCodec[] codecInstances;

        [Header("Materials")]
        public bool configureMaterials = true;
        public Material encoderBlockExpandMaterial;

        [Header("Diagnostics")]
        [SerializeField] private FrameLayout layout;

        public FrameLayout Layout => layout;
#if UNITY_EDITOR
        private bool _deferEditorCodecCleanup;
        private double _nextEditorEncodeTime;
#endif
        private void Reset()
        {
            encoder = GetComponent<TSMPEncoder>();
            decoder = GetComponent<Udon.TSMPDecoder>();
        }

        private void OnEnable()
        {
            ApplyNow();
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
        }

        private void OnValidate()
        {
            ClampValues();
            if (applyOnValidate)
            {
#if UNITY_EDITOR
                _deferEditorCodecCleanup = true;
                try
                {
                    ApplyNow();
                }
                finally
                {
                    _deferEditorCodecCleanup = false;
                }
#else
                ApplyNow();
#endif
            }
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (this == null || Application.isPlaying)
                return;

            if (!driveEncoderInEditor || encoder == null || !SetupApplier.GetEncoderAutoEncode(encoder))
                return;

            double now = UnityEditor.EditorApplication.timeSinceStartup;
            double interval = 1.0 / Mathf.Max(1, SetupApplier.GetEncoderFrameRate(encoder));
            if (now < _nextEditorEncodeTime)
                return;

            _nextEditorEncodeTime = now + interval;
            SetupApplier.InvokeEncode(encoder);
            SetupApplier.BlitEncoderOutputInEditor(encoder);
        }
#endif

        [ContextMenu("Apply TSMP Setup")]
        public void ApplyNow()
        {
            ClampValues();
            RecalculateLayout();
            AutoSizeByteTexture();
            RefreshInstalledCodecs();

            if (resizeFrameRenderTextures)
                ResizeFrameRenderTextures();

            if (resizeByteRenderTextures)
                ResizeByteRenderTextures();

            EnsureCodecInstances();

            if (configureEncoder)
                ApplyEncoder();

            if (configureDecoder)
                ApplyDecoder();

            if (configureMaterials)
                ApplyMaterials();
        }

        private void ClampValues()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            blockSize = Mathf.Max(1, blockSize);
            sampleSize = FrameHeader.ClampDecodeSampleSize(sampleSize, blockSize);
            byteTextureWidth = Mathf.Max(1, byteTextureWidth);
            byteTextureHeight = Mathf.Max(1, byteTextureHeight);
            byteTextureSafetyPixels = Mathf.Max(0, byteTextureSafetyPixels);
        }

        private void RecalculateLayout()
        {
            layout = FrameLayout.Calculate(width, height, blockSize);
        }

        private void ApplyEncoder()
        {
            if (encoder == null)
                return;

            TSMPCodec selectedCodec = GetSelectedCodec();
            Material resolvedBlockExpandMaterial = ResolveEncoderBlockExpandMaterial();
            SetupApplier.ApplyEncoder(encoder, layout, sampleSize, selectedCodec, encoderOutput, resolvedBlockExpandMaterial);
        }

        [ContextMenu("Encode Encoder Now")]
        public void EncodeEncoderNow()
        {
            if (encoder == null)
                return;

            if (configureEncoder)
                ApplyEncoder();

            SetupApplier.InvokeEncode(encoder);
            SetupApplier.BlitEncoderOutputInEditor(encoder);
        }

        private void ApplyDecoder()
        {
            Component resolvedDecoder = ResolveDecoderComponent();
            if (resolvedDecoder == null)
                return;

            if (decoder != resolvedDecoder)
            {
                RecordObject(this, "Resolve TSMP decoder component");
                decoder = resolvedDecoder;
                MarkDirty(this);
            }

            Texture resolvedSourceTexture = ResolveDecoderSourceTexture();
            SetupApplier.ApplyDecoder(resolvedDecoder, resolvedSourceTexture, payloadByteTexture, layout, sampleSize, flipY, BuildCodecRuntimeTable());
        }

        public void RefreshInstalledCodecs()
        {
#if UNITY_EDITOR
            if (!autoDiscoverCodecs || Application.isPlaying)
                return;

            TSMPCodec selectedCodec = GetSelectedCodec();
            ushort selectedCodecId = selectedCodec != null ? selectedCodec.codecId : (ushort)0;
            TSMPCodec[] discovered = SetupCodecDiscovery.DiscoverCodecPrefabs();
            if (discovered == null || discovered.Length == 0)
                return;

            if (SetupCodecDiscovery.AreSameCodecs(codecPrefabs, discovered))
                return;

            RecordObject(this, "Refresh TSMP codec list");
            codecPrefabs = discovered;
            selectedCodecIndex = SetupCodecDiscovery.FindCodecIndex(codecPrefabs, selectedCodecId, selectedCodecIndex);
            MarkDirty(this);
#endif
        }

        private void ApplyMaterials()
        {
#if !COMPILER_UDONSHARP
            Texture resolvedSourceTexture = ResolveDecoderSourceTexture();
            CodecMaterialContext context = new CodecMaterialContext
            {
                SourceTexture = resolvedSourceTexture,
                FrameLayout = layout,
                FlipY = flipY,
                OutputWidth = byteTextureWidth,
                OutputHeight = byteTextureHeight,
                SampleSize = sampleSize,
            };

            SetupApplier.ApplyMaterials(GetActiveCodecs(), context);
#endif
        }

        private Material ResolveEncoderBlockExpandMaterial()
        {
            if (encoderBlockExpandMaterial != null)
                return encoderBlockExpandMaterial;

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("TSMPEncoderBlockExpand t:Material");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                Material material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                {
                    encoderBlockExpandMaterial = material;
                    MarkDirty(this);
                    return material;
                }
            }
#endif

            return null;
        }

        private void ResizeFrameRenderTextures()
        {
            ResizeRenderTexture(encoderOutput, layout.Width, layout.Height);
            ResizeRenderTextures(extraFrameRenderTextures, layout.Width, layout.Height);
        }

        private void ResizeByteRenderTextures()
        {
            ResizeRenderTexture(payloadByteTexture, byteTextureWidth, byteTextureHeight);
            ResizeRenderTextures(extraByteRenderTextures, byteTextureWidth, byteTextureHeight);
        }

        private void AutoSizeByteTexture()
        {
            if (!autoSizeByteTexture)
                return;

            int payloadBytes = EstimatePayloadBytes();
            int byteCount = Mathf.Max(FrameHeader.Size, payloadBytes);
            int requiredPixels = (byteCount + 3) / 4 + byteTextureSafetyPixels;
            int requiredWidth = (requiredPixels + byteTextureHeight - 1) / byteTextureHeight;

            if (roundByteTextureWidthToPowerOfTwo)
                requiredWidth = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredWidth));

            if (requiredWidth > byteTextureWidth)
                byteTextureWidth = requiredWidth;
        }

        private void EnsureCodecInstances()
        {
            if (!instantiateCodecPrefabsAtRuntime)
            {
                CleanupCodecInstances(codecInstanceRoot != null ? codecInstanceRoot : transform);
                return;
            }

            if (codecPrefabs == null || codecPrefabs.Length == 0)
            {
                CleanupCodecInstances(codecInstanceRoot != null ? codecInstanceRoot : transform);
                return;
            }

            Transform parent = codecInstanceRoot != null ? codecInstanceRoot : transform;

            if (codecInstances != null && codecInstances.Length == codecPrefabs.Length)
            {
                bool valid = true;
                for (int i = 0; i < codecInstances.Length; i++)
                {
                    if (codecPrefabs[i] == null && codecInstances[i] != null)
                        valid = false;
                    else if (codecPrefabs[i] != null && codecInstances[i] == null)
                        valid = false;
                    else if (codecPrefabs[i] != null && codecInstances[i] != null && !codecInstances[i].name.StartsWith(codecPrefabs[i].name))
                        valid = false;
                    else if (codecInstances[i] != null && codecInstances[i].transform.parent != parent)
                        valid = false;
                }

                if (valid)
                    return;
            }

            CleanupCodecInstances(parent);
            codecInstances = new TSMPCodec[codecPrefabs.Length];

            for (int i = 0; i < codecPrefabs.Length; i++)
            {
                TSMPCodec prefab = codecPrefabs[i];
                if (prefab == null)
                    continue;

#if UNITY_EDITOR
                TSMPCodec instance = !Application.isPlaying
                    ? (UnityEditor.PrefabUtility.InstantiatePrefab(prefab.gameObject, parent) as GameObject)?.GetComponent<TSMPCodec>()
                    : Instantiate(prefab, parent);
#else
                TSMPCodec instance = Instantiate(prefab, parent);
#endif
                if (instance == null)
                    continue;

                instance.name = prefab.name + " (Runtime)";
                codecInstances[i] = instance;
            }
        }

        private void CleanupCodecInstances(Transform parent)
        {
            if (codecInstances != null)
            {
                for (int i = 0; i < codecInstances.Length; i++)
                {
                    if (codecInstances[i] != null)
                        DestroyCodecInstance(codecInstances[i].gameObject);
                }
            }

            if (parent != null)
            {
                List<GameObject> orphanRuntimeInstances = new List<GameObject>();
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child == null || !child.name.EndsWith(" (Runtime)"))
                        continue;

                    if (child.GetComponent<TSMPCodec>() != null)
                        orphanRuntimeInstances.Add(child.gameObject);
                }

                for (int i = 0; i < orphanRuntimeInstances.Count; i++)
                    DestroyCodecInstance(orphanRuntimeInstances[i]);
            }

            codecInstances = null;
        }

        private void DestroyCodecInstance(GameObject instance)
        {
            if (instance == null)
                return;

            if (Application.isPlaying)
                Destroy(instance);
#if UNITY_EDITOR
            else if (_deferEditorCodecCleanup)
            {
                GameObject pendingDestroy = instance;
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (pendingDestroy == null)
                        return;

                    if (Application.isPlaying)
                        Destroy(pendingDestroy);
                    else
                        UnityEditor.Undo.DestroyObjectImmediate(pendingDestroy);
                };
            }
            else
            {
                UnityEditor.Undo.DestroyObjectImmediate(instance);
            }
#else
            else
                DestroyImmediate(instance);
#endif
        }

        private TSMPCodec[] GetActiveCodecs()
        {
            return CodecRuntimeTable.GetActiveCodecs(codecInstances, codecPrefabs);
        }

        private TSMPCodec[] BuildCodecRuntimeTable()
        {
            return CodecRuntimeTable.Build(GetActiveCodecs());
        }

        private TSMPCodec GetSelectedCodec()
        {
            return CodecRuntimeTable.GetSelected(GetActiveCodecs(), selectedCodecIndex);
        }

        private int EstimatePayloadBytes()
        {
            return SetupApplier.EstimateEncoderPayloadBytes(encoder);
        }

        private void ResizeRenderTextures(RenderTexture[] renderTextures, int targetWidth, int targetHeight)
        {
            if (renderTextures == null)
                return;

            for (int i = 0; i < renderTextures.Length; i++)
                ResizeRenderTexture(renderTextures[i], targetWidth, targetHeight);
        }

        private static void ResizeRenderTexture(RenderTexture renderTexture, int targetWidth, int targetHeight)
        {
            if (renderTexture == null)
                return;

            targetWidth = Mathf.Max(1, targetWidth);
            targetHeight = Mathf.Max(1, targetHeight);

            bool needsResize = renderTexture.width != targetWidth || renderTexture.height != targetHeight;
            bool needsStructuralReset =
                renderTexture.useMipMap ||
                renderTexture.autoGenerateMips ||
                renderTexture.antiAliasing != 1;

            if (needsResize || needsStructuralReset)
            {
                RecordObject(renderTexture, "Resize TSMP RenderTexture");

                if (renderTexture.IsCreated())
                    renderTexture.Release();

                renderTexture.width = targetWidth;
                renderTexture.height = targetHeight;
                renderTexture.useMipMap = false;
                renderTexture.autoGenerateMips = false;
                renderTexture.antiAliasing = 1;
                MarkDirty(renderTexture);
            }

            renderTexture.filterMode = FilterMode.Point;
            renderTexture.wrapMode = TextureWrapMode.Clamp;
        }

        private static Material First(Material[] materials)
        {
            if (materials == null)
                return null;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                    return materials[i];
            }

            return null;
        }

        private Component ResolveDecoderComponent()
        {
            Udon.TSMPDecoder typedDecoder = decoder as Udon.TSMPDecoder;
            if (typedDecoder != null)
                return decoder;

            if (decoder != null)
            {
                Component resolved = decoder.GetComponent<Udon.TSMPDecoder>();
                if (resolved != null)
                    return resolved;
            }

            Component local = GetComponent<Udon.TSMPDecoder>();
            if (local != null)
                return local;

            return null;
        }

        private Texture ResolveDecoderSourceTexture()
        {
            if (decoderSourceTexture != null)
                return decoderSourceTexture;

            if (preferEncoderOutputAsDecoderSource && encoderOutput != null)
                return encoderOutput;

            return sourceTexture;
        }

        private static void RecordObject(UnityEngine.Object target, string name)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && target != null)
                UnityEditor.Undo.RecordObject(target, name);
#endif
        }

        private static void MarkDirty(UnityEngine.Object target)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && target != null)
                UnityEditor.EditorUtility.SetDirty(target);
#endif
        }
    }

}
#endif
