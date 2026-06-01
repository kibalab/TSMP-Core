using System.Collections.Generic;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPSetup))]
    public sealed class TSMPSetupEditor : UnityEditor.Editor
    {
        private static readonly GUIContent[] Tabs =
        {
            new GUIContent("Setup"),
            new GUIContent("Codec"),
            new GUIContent("Frame"),
            new GUIContent("Output"),
            new GUIContent("Auto"),
            new GUIContent("Debug")
        };

        private SerializedProperty _sourceTexture;
        private SerializedProperty _decoderSourceTexture;
        private SerializedProperty _preferEncoderOutputAsDecoderSource;
        private SerializedProperty _width;
        private SerializedProperty _height;
        private SerializedProperty _blockSize;
        private SerializedProperty _sampleSize;
        private SerializedProperty _flipY;
        private SerializedProperty _applyOnValidate;
        private SerializedProperty _encoder;
        private SerializedProperty _decoder;
        private SerializedProperty _configureEncoder;
        private SerializedProperty _configureDecoder;
        private SerializedProperty _driveEncoderInEditor;
        private SerializedProperty _encoderOutput;
        private SerializedProperty _payloadByteTexture;
        private SerializedProperty _extraFrameRenderTextures;
        private SerializedProperty _extraByteRenderTextures;
        private SerializedProperty _resizeFrameRenderTextures;
        private SerializedProperty _resizeByteRenderTextures;
        private SerializedProperty _autoSizeByteTexture;
        private SerializedProperty _roundByteTextureWidthToPowerOfTwo;
        private SerializedProperty _byteTextureSafetyPixels;
        private SerializedProperty _byteTextureWidth;
        private SerializedProperty _byteTextureHeight;
        private SerializedProperty _autoDiscoverCodecs;
        private SerializedProperty _codecPrefabs;
        private SerializedProperty _selectedCodecIndex;
        private SerializedProperty _codecInstanceRoot;
        private SerializedProperty _configureMaterials;
        private SerializedProperty _encoderBlockExpandMaterial;
        private SerializedProperty _layout;

        private int _selectedTab;
        private bool _refreshCodecsOnNextGui;
        private Vector2 _networkIdScroll;

        private void OnEnable()
        {
            _sourceTexture = serializedObject.FindProperty("sourceTexture");
            _decoderSourceTexture = serializedObject.FindProperty("decoderSourceTexture");
            _preferEncoderOutputAsDecoderSource = serializedObject.FindProperty("preferEncoderOutputAsDecoderSource");
            _width = serializedObject.FindProperty("width");
            _height = serializedObject.FindProperty("height");
            _blockSize = serializedObject.FindProperty("blockSize");
            _sampleSize = serializedObject.FindProperty("sampleSize");
            _flipY = serializedObject.FindProperty("flipY");
            _applyOnValidate = serializedObject.FindProperty("applyOnValidate");
            _encoder = serializedObject.FindProperty("encoder");
            _decoder = serializedObject.FindProperty("decoder");
            _configureEncoder = serializedObject.FindProperty("configureEncoder");
            _configureDecoder = serializedObject.FindProperty("configureDecoder");
            _driveEncoderInEditor = serializedObject.FindProperty("driveEncoderInEditor");
            _encoderOutput = serializedObject.FindProperty("encoderOutput");
            _payloadByteTexture = serializedObject.FindProperty("payloadByteTexture");
            _extraFrameRenderTextures = serializedObject.FindProperty("extraFrameRenderTextures");
            _extraByteRenderTextures = serializedObject.FindProperty("extraByteRenderTextures");
            _resizeFrameRenderTextures = serializedObject.FindProperty("resizeFrameRenderTextures");
            _resizeByteRenderTextures = serializedObject.FindProperty("resizeByteRenderTextures");
            _autoSizeByteTexture = serializedObject.FindProperty("autoSizeByteTexture");
            _roundByteTextureWidthToPowerOfTwo = serializedObject.FindProperty("roundByteTextureWidthToPowerOfTwo");
            _byteTextureSafetyPixels = serializedObject.FindProperty("byteTextureSafetyPixels");
            _byteTextureWidth = serializedObject.FindProperty("byteTextureWidth");
            _byteTextureHeight = serializedObject.FindProperty("byteTextureHeight");
            _autoDiscoverCodecs = serializedObject.FindProperty("autoDiscoverCodecs");
            _codecPrefabs = serializedObject.FindProperty("codecPrefabs");
            _selectedCodecIndex = serializedObject.FindProperty("selectedCodecIndex");
            _codecInstanceRoot = serializedObject.FindProperty("codecInstanceRoot");
            _configureMaterials = serializedObject.FindProperty("configureMaterials");
            _encoderBlockExpandMaterial = serializedObject.FindProperty("encoderBlockExpandMaterial");
            _layout = serializedObject.FindProperty("layout");
            _refreshCodecsOnNextGui = true;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (ShouldRefreshCodecs())
            {
                serializedObject.ApplyModifiedProperties();
                ((TSMPSetup)target).RefreshInstalledCodecs();
                serializedObject.Update();
            }

            DrawTabs();
            EditorGUILayout.Space(6f);
            DrawSelectedTab();
            DrawApplyAction();

            serializedObject.ApplyModifiedProperties();
        }

        private bool ShouldRefreshCodecs()
        {
            if (!_refreshCodecsOnNextGui)
                return false;

            _refreshCodecsOnNextGui = false;
            return _autoDiscoverCodecs != null
                && _autoDiscoverCodecs.boolValue
                && !Application.isPlaying
                && target is TSMPSetup;
        }

        private void DrawTabs()
        {
            _selectedTab = Mathf.Clamp(_selectedTab, 0, Tabs.Length - 1);
            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs, GUILayout.Height(25f));
        }

        private void DrawSelectedTab()
        {
            switch (_selectedTab)
            {
                case 0:
                    DrawReferenceTab();
                    break;
                case 1:
                    DrawCodecTab();
                    break;
                case 2:
                    DrawFrameTab();
                    break;
                case 3:
                    DrawRenderTab();
                    break;
                case 4:
                    DrawAutomationTab();
                    break;
                default:
                    DrawDiagnosticsTab();
                    break;
            }
        }

        private void DrawReferenceTab()
        {
            DrawScriptField();
            DrawProperty(_encoder);
            DrawProperty(_configureEncoder);
            DrawProperty(_decoder);
            DrawProperty(_configureDecoder);
            EditorGUILayout.Space(6f);
            DrawProperty(_sourceTexture);
            DrawProperty(_decoderSourceTexture);
            DrawProperty(_preferEncoderOutputAsDecoderSource);
        }

        private void DrawCodecTab()
        {
            DrawCodecPopup();

            TSMPCodec codec = GetSelectedCodec();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Selected Prefab", codec, typeof(TSMPCodec), false);
                EditorGUILayout.IntField("Codec ID", codec != null ? codec.codecId : 0);
                EditorGUILayout.IntField("Symbol Mode", codec != null ? codec.SymbolMode : 0);
                DrawSelectedCodecPackageInfo(codec);
            }

            EditorGUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            DrawProperty(_autoDiscoverCodecs);
            if (EditorGUI.EndChangeCheck() && _autoDiscoverCodecs != null && _autoDiscoverCodecs.boolValue)
                _refreshCodecsOnNextGui = true;

            DrawProperty(_codecPrefabs);
            DrawProperty(_codecInstanceRoot);

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(Application.isPlaying || _autoDiscoverCodecs == null || !_autoDiscoverCodecs.boolValue))
            {
                if (GUILayout.Button("Refresh Codecs", GUILayout.Height(24f)))
                    RefreshCodecs();
            }
        }

        private void DrawFrameTab()
        {
            DrawProperty(_width);
            DrawProperty(_height);
            DrawProperty(_blockSize);
            DrawProperty(_sampleSize);
            DrawProperty(_flipY);
            EditorGUILayout.Space(6f);
            DrawFramePreview();
        }

        private void DrawRenderTab()
        {
            DrawProperty(_encoderOutput);
            DrawReadOnlyText("Output Size", GetTextureSize(_encoderOutput != null ? _encoderOutput.objectReferenceValue as Texture : null));
            DrawProperty(_payloadByteTexture);
            DrawReadOnlyText("Byte Texture Size", GetTextureSize(_payloadByteTexture != null ? _payloadByteTexture.objectReferenceValue as Texture : null));

            EditorGUILayout.Space(6f);
            DrawProperty(_resizeFrameRenderTextures);
            DrawProperty(_resizeByteRenderTextures);
            DrawProperty(_autoSizeByteTexture);

            if (_autoSizeByteTexture != null && _autoSizeByteTexture.boolValue)
            {
                DrawProperty(_byteTextureHeight);
                DrawProperty(_byteTextureSafetyPixels);
                DrawProperty(_roundByteTextureWidthToPowerOfTwo);
                DrawReadOnlyInt("Calculated Byte Width", _byteTextureWidth != null ? _byteTextureWidth.intValue : 0);
            }
            else
            {
                DrawProperty(_byteTextureWidth);
                DrawProperty(_byteTextureHeight);
            }

            EditorGUILayout.Space(6f);
            DrawProperty(_extraFrameRenderTextures);
            DrawProperty(_extraByteRenderTextures);
        }

        private void DrawAutomationTab()
        {
            DrawProperty(_applyOnValidate);
            DrawProperty(_driveEncoderInEditor);
            DrawProperty(_configureMaterials);
            DrawProperty(_encoderBlockExpandMaterial);
        }

        private void DrawDiagnosticsTab()
        {
            DrawCapacityPreview();
            using (new EditorGUI.DisabledScope(true))
                DrawProperty(_layout);

            EditorGUILayout.Space(8f);
            DrawNetworkIdList();

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(_encoder == null || _encoder.objectReferenceValue == null))
            {
                if (GUILayout.Button("Encode Now", GUILayout.Height(24f)))
                    EncodeNow();
            }
        }

        private void DrawApplyAction()
        {
            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Apply Setup", GUILayout.Height(26f)))
                ApplySetup();
        }

        private void DrawSelectedCodecPackageInfo(TSMPCodec codec)
        {
            string author = string.Empty;
            string description = string.Empty;
            SetupCodecDiscovery.TryGetPackageInfo(codec, out author, out description);
            EditorGUILayout.TextField("Author", author);
            EditorGUILayout.TextField("Description", description);
        }

        private void DrawScriptField()
        {
            SerializedProperty script = serializedObject.FindProperty("m_Script");
            if (script == null)
                return;

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(script);
        }

        private void DrawCodecPopup()
        {
            if (_codecPrefabs == null || _selectedCodecIndex == null)
                return;

            int count = _codecPrefabs.arraySize;
            if (count <= 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("Active Codec", 0, new[] { "No codecs installed" });
                return;
            }

            string[] labels = new string[count];
            for (int i = 0; i < count; i++)
                labels[i] = GetCodecLabel(_codecPrefabs.GetArrayElementAtIndex(i).objectReferenceValue as TSMPCodec);

            int selected = Mathf.Clamp(_selectedCodecIndex.intValue, 0, count - 1);
            _selectedCodecIndex.intValue = EditorGUILayout.Popup("Active Codec", selected, labels);
        }

        private void DrawFramePreview()
        {
            FrameLayout layout = FrameLayout.Calculate(
                _width != null ? _width.intValue : 0,
                _height != null ? _height.intValue : 0,
                _blockSize != null ? _blockSize.intValue : 1);

            DrawReadOnlyText("Active Blocks", layout.ActiveWidthBlocks + " x " + layout.ActiveHeightBlocks);
            DrawReadOnlyInt("Payload Start Row", layout.Luma4PayloadStartRow);
        }

        private void DrawCapacityPreview()
        {
            TSMPCodec codec = GetSelectedCodec();
            int width = _width != null ? _width.intValue : 0;
            int height = _height != null ? _height.intValue : 0;
            int blockSize = _blockSize != null ? Mathf.Max(1, _blockSize.intValue) : 1;
            int capacity = codec != null ? codec.GetPayloadCapacityBytes(width, height, blockSize) : 0;
            int byteTexturePixels = Mathf.Max(0, GetInt(_byteTextureWidth) * GetInt(_byteTextureHeight));

            DrawReadOnlyInt("Codec Payload Capacity", capacity);
            DrawReadOnlyInt("Byte Texture Capacity", byteTexturePixels * 4);
        }

        private void DrawNetworkIdList()
        {
            List<NetworkIdRow> rows = BuildNetworkIdRows();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Network IDs", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Resolve IDs", GUILayout.Width(92f), GUILayout.Height(20f)))
                {
                    serializedObject.ApplyModifiedProperties();
                    TransSyncBindingBuilder.ResolveSceneNetworkIds(true);
                    serializedObject.Update();
                    rows = BuildNetworkIdRows();
                }
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Matched Objects", CountNetworkObjects(rows));
                EditorGUILayout.IntField("Network Behaviours", CountNetworkBehaviours(rows));
            }

            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("No TSMP network behaviours in the scene.", EditorStyles.miniLabel);
                return;
            }

            DrawNetworkIdHeader();

            float rowHeight = EditorGUIUtility.singleLineHeight + 2f;
            float maxHeight = rowHeight * Mathf.Min(rows.Count, 10);
            _networkIdScroll = EditorGUILayout.BeginScrollView(_networkIdScroll, GUILayout.MinHeight(rowHeight), GUILayout.MaxHeight(maxHeight + 4f));
            for (int i = 0; i < rows.Count; i++)
                DrawNetworkIdRow(rows[i]);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawNetworkIdHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect idRect;
            Rect objectRect;
            SplitNetworkIdRowRect(rect, out idRect, out objectRect);

            EditorGUI.LabelField(idRect, "ID", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(objectRect, "Object", EditorStyles.miniBoldLabel);
        }

        private static void DrawNetworkIdRow(NetworkIdRow row)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            rect.height = EditorGUIUtility.singleLineHeight;

            Rect idRect;
            Rect objectRect;
            SplitNetworkIdRowRect(rect, out idRect, out objectRect);

            string idText = row.NetworkId != 0 ? row.NetworkId.ToString() : "0";
            EditorGUI.LabelField(idRect, idText);

            using (new EditorGUI.DisabledScope(true))
                EditorGUI.ObjectField(objectRect, GUIContent.none, row.GameObject, typeof(GameObject), true);
        }

        private static void SplitNetworkIdRowRect(Rect rect, out Rect idRect, out Rect objectRect)
        {
            const float IdWidth = 54f;
            const float Gap = 4f;

            idRect = new Rect(rect.x, rect.y, IdWidth, rect.height);
            float objectWidth = Mathf.Max(40f, rect.xMax - idRect.xMax - Gap);
            objectRect = new Rect(idRect.xMax + Gap, rect.y, objectWidth, rect.height);
        }

        private static List<NetworkIdRow> BuildNetworkIdRows()
        {
            TSMPNetworkBehaviour[] behaviours = Object.FindObjectsOfType<TSMPNetworkBehaviour>(true);
            List<NetworkIdRow> rows = new List<NetworkIdRow>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                TSMPNetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                GameObject gameObject = behaviour.gameObject;
                ushort networkId = BindingTable.ResolveNetworkId(behaviour);
                int rowIndex = FindNetworkIdRow(rows, networkId, gameObject);
                if (rowIndex < 0)
                {
                    NetworkIdRow row = new NetworkIdRow();
                    row.NetworkId = networkId;
                    row.GameObject = gameObject;
                    row.Path = GetHierarchyPath(behaviour.transform);
                    row.ComponentCount = 1;
                    rows.Add(row);
                }
                else
                {
                    NetworkIdRow row = rows[rowIndex];
                    row.ComponentCount++;
                    rows[rowIndex] = row;
                }
            }

            rows.Sort(CompareNetworkIdRows);
            return rows;
        }

        private static int FindNetworkIdRow(List<NetworkIdRow> rows, ushort networkId, GameObject gameObject)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                NetworkIdRow row = rows[i];
                if (row.NetworkId == networkId && row.GameObject == gameObject)
                    return i;
            }

            return -1;
        }

        private static int CompareNetworkIdRows(NetworkIdRow left, NetworkIdRow right)
        {
            int idCompare = left.NetworkId.CompareTo(right.NetworkId);
            if (idCompare != 0)
                return idCompare;

            return string.CompareOrdinal(left.Path, right.Path);
        }

        private static int CountNetworkObjects(List<NetworkIdRow> rows)
        {
            return rows != null ? rows.Count : 0;
        }

        private static int CountNetworkBehaviours(List<NetworkIdRow> rows)
        {
            if (rows == null)
                return 0;

            int count = 0;
            for (int i = 0; i < rows.Count; i++)
                count += rows[i].ComponentCount;

            return count;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private void DrawReadOnlyText(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(label, value);
        }

        private void DrawReadOnlyInt(string label, int value)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField(label, value);
        }

        private void DrawProperty(SerializedProperty property)
        {
            if (property != null)
                EditorGUILayout.PropertyField(property, true);
        }

        private void ApplySetup()
        {
            serializedObject.ApplyModifiedProperties();
            TSMPSetup setup = (TSMPSetup)target;
            Undo.RecordObject(setup, "Apply TSMP setup");
            TransSyncBindingBuilder.ResolveSceneNetworkIds(false);
            setup.ApplyNow();
            EditorUtility.SetDirty(setup);
            serializedObject.Update();
        }

        private void EncodeNow()
        {
            serializedObject.ApplyModifiedProperties();
            TSMPSetup setup = (TSMPSetup)target;
            setup.EncodeEncoderNow();
            EditorUtility.SetDirty(setup);
            serializedObject.Update();
        }

        private void RefreshCodecs()
        {
            serializedObject.ApplyModifiedProperties();
            TSMPSetup setup = (TSMPSetup)target;
            Undo.RecordObject(setup, "Refresh TSMP codec list");
            setup.RefreshInstalledCodecs();
            EditorUtility.SetDirty(setup);
            serializedObject.Update();
        }

        private TSMPCodec GetSelectedCodec()
        {
            if (_codecPrefabs == null || _selectedCodecIndex == null || _codecPrefabs.arraySize <= 0)
                return null;

            int index = Mathf.Clamp(_selectedCodecIndex.intValue, 0, _codecPrefabs.arraySize - 1);
            return _codecPrefabs.GetArrayElementAtIndex(index).objectReferenceValue as TSMPCodec;
        }

        private static string GetCodecLabel(TSMPCodec codec)
        {
            if (codec == null)
                return "Missing";

            string label = string.IsNullOrEmpty(codec.displayName) ? codec.name : codec.displayName;
            return label + " (" + codec.codecId + ")";
        }

        private static string GetTextureSize(Texture texture)
        {
            if (texture == null)
                return "Not assigned";

            return texture.width + " x " + texture.height;
        }

        private static int GetInt(SerializedProperty property)
        {
            return property != null ? Mathf.Max(0, property.intValue) : 0;
        }

        private struct NetworkIdRow
        {
            public ushort NetworkId;
            public GameObject GameObject;
            public string Path;
            public int ComponentCount;
        }
    }
}
