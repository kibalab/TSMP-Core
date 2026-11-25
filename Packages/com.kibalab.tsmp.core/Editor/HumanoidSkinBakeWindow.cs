using System;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    public sealed class HumanoidSkinBakeWindow : EditorWindow
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private SkinnedMeshRenderer _mesh;
        [SerializeField] private TSMPNetworkHumanoidPoseSync _sender;
        [SerializeField] private TSMPDecoder _decoder;
        [SerializeField] private Transform[] _nativeBoneRoots = Array.Empty<Transform>();
        private SerializedObject _window;
        private SerializedProperty _roots;
        [SerializeField] private int _networkId = 1;
        [SerializeField] private int _channel;
        [SerializeField] private float _margin = 3f;
        private static readonly string[] Channels = { "Channel 0", "Channel 1", "Channel 2", "Channel 3" };
        private Vector2 _scroll;
        private MeshRenderer _lastBake;
        private SkinnedMeshRenderer _lastNative;
        private Mesh _lastMesh;
        private string _error;

        [MenuItem("TSMP/Bake Humanoid GPU Skin")]
        private static void Open()
        {
            var window = GetWindow<HumanoidSkinBakeWindow>("Humanoid GPU Skin");
            var selected = Selection.activeGameObject;
            if (!selected || EditorUtility.IsPersistent(selected)) return;
            var animator = selected.GetComponentInParent<Animator>();
            if (!animator) return;
            window.SetReceiver(animator);
            var mesh = selected.GetComponent<SkinnedMeshRenderer>();
            if (mesh) window._mesh = mesh;
        }

        internal static void OpenForDecoder(TSMPDecoder decoder)
        {
            var window = GetWindow<HumanoidSkinBakeWindow>("Humanoid GPU Skin");
            window._decoder = decoder;
            window._error = null;
        }

        private void SetReceiver(Animator animator)
        {
            if (_animator == animator) return;
            _animator = animator;
            _nativeBoneRoots = Array.Empty<Transform>();
            _mesh = null;
            if (!animator) return;
            var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 1) _mesh = renderers[0];
        }

        private void OnEnable()
        {
            minSize = new Vector2(320f, 240f);
            _window = new SerializedObject(this);
            _roots = _window.FindProperty(nameof(_nativeBoneRoots));
        }

        private void OnDisable() => _window?.Dispose();

        private void OnGUI()
        {
            using var scroll = new EditorGUILayout.ScrollViewScope(_scroll);
            _scroll = scroll.scrollPosition;
            using var body = new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins);
            EditorGUI.BeginChangeCheck();
            InspectorUI.SectionLabel("Receiver");
            SetReceiver((Animator)EditorGUILayout.ObjectField("Receiver Animator", _animator, typeof(Animator), true));
            _mesh = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer", _mesh, typeof(SkinnedMeshRenderer), true);
            if (!_animator && _mesh)
            {
                var mesh = _mesh;
                SetReceiver(mesh.GetComponentInParent<Animator>());
                _mesh = mesh;
            }
            InspectorUI.SectionLabel("Pose Source");
            _sender = (TSMPNetworkHumanoidPoseSync)EditorGUILayout.ObjectField(
                new GUIContent("Sender (Optional)", "Read the outgoing Network ID from a pose component. Leave empty for a remote sender."),
                _sender, typeof(TSMPNetworkHumanoidPoseSync), true);
            int id = _sender ? BindingTable.ResolveNetworkId(_sender) : _networkId;
            if (_sender) EditorGUILayout.LabelField("Sender Network ID", id.ToString());
            else id = _networkId = EditorGUILayout.IntField("Sender Network ID", _networkId);
            _decoder = (TSMPDecoder)EditorGUILayout.ObjectField(
                new GUIContent("Decoder (Optional)", "Read the shader channel from a decoder. Leave empty to choose the channel manually."),
                _decoder, typeof(TSMPDecoder), true);
            int channel = _decoder ? (int)_decoder.shaderChannel - 1 : _channel;
            if (_decoder) EditorGUILayout.LabelField("Shader Channel", channel >= 0 && channel < Channels.Length ? Channels[channel] : "Off");
            else channel = _channel = EditorGUILayout.Popup("Shader Channel", _channel, Channels);

            InspectorUI.SectionLabel("Mesh Settings");
            _window.Update();
            EditorGUILayout.PropertyField(_roots, new GUIContent("Keep on Skinned Renderer",
                "Bones on the receiving rig whose weighted triangles should stay on the original renderer. Includes descendants."), true);
            _window.ApplyModifiedProperties();
            _margin = EditorGUILayout.FloatField(new GUIContent("Motion Margin", "Extra culling bounds in Animator-local units on each side."), _margin);
            if (EditorGUI.EndChangeCheck()) _error = null;

            string problem = HumanoidSkinBaker.CheckReceiver(_animator, _mesh);
            if (problem == null && (id < 0 || id > ushort.MaxValue || (_sender && id == 0)))
                problem = _sender ? "Assign a Network ID to the sender or its Network Identity." : "Enter a sender ID between 0 and 65535.";
            if (problem == null && (channel < 0 || channel >= Channels.Length)) problem = "Choose Channel 0 to 3 on the selected decoder.";
            if (problem == null && (!(_margin >= 0f) || float.IsInfinity(_margin))) problem = "Motion Margin must be a finite, non-negative value.";
            EditorGUILayout.Space();
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button("Bake GPU Skin...")) Bake(channel, id);
            }
            if (_lastBake)
            {
                InspectorUI.SectionLabel("Last Bake");
                DrawResult("GPU Renderer", _lastBake);
                if (_lastNative) DrawResult("Retained Renderer", _lastNative);
                if (_lastMesh) DrawResult("Baked Mesh", _lastMesh);
            }
        }

        private static void DrawResult(string label, UnityEngine.Object result)
        {
            using var row = new EditorGUILayout.HorizontalScope();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(label, result, result.GetType(), true);
            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                Selection.activeObject = result is Component component ? component.gameObject : result;
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
        }

        private void Bake(int channel, int id)
        {
            var path = EditorUtility.SaveFilePanelInProject("Save GPU Skin", _mesh.name + " Skin", "asset", "Choose a new asset.");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                _lastBake = HumanoidSkinBaker.Bake(_animator, _mesh, channel, id, _margin, path, _nativeBoneRoots);
                _lastNative = _mesh.enabled ? _mesh : null;
                _lastMesh = _lastBake.GetComponent<MeshFilter>().sharedMesh;
                Selection.activeGameObject = _lastBake.gameObject;
                _error = null;
            }
            catch (Exception e) { _error = e.Message; }
        }
    }
}
