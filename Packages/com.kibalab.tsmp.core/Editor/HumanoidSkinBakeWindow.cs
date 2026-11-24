using System;
using UnityEditor;
using UnityEngine;

namespace K13A.TSMP.Editor
{
    public sealed class HumanoidSkinBakeWindow : EditorWindow
    {
        private Animator _animator;
        private SkinnedMeshRenderer _mesh;
        [SerializeField] private Transform[] _nativeBoneRoots = Array.Empty<Transform>();
        private SerializedObject _window;
        private SerializedProperty _roots;
        private int _networkId = 1;
        private int _channel;
        private float _margin = 3f;
        private string _error;

        [MenuItem("TSMP/Bake Humanoid GPU Skin")]
        private static void Open()
        {
            var window = GetWindow<HumanoidSkinBakeWindow>("Humanoid GPU Skin");
            if (!Selection.activeGameObject) return;
            window._mesh = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
            window._animator = Selection.activeGameObject.GetComponentInParent<Animator>();
        }

        private void OnEnable()
        {
            _window = new SerializedObject(this);
            _roots = _window.FindProperty(nameof(_nativeBoneRoots));
        }

        private void OnDisable() => _window?.Dispose();

        private void OnGUI()
        {
            _animator = (Animator)EditorGUILayout.ObjectField("Receiver Animator", _animator, typeof(Animator), true);
            _mesh = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Mesh", _mesh, typeof(SkinnedMeshRenderer), true);
            _window.Update();
            EditorGUILayout.PropertyField(_roots, new GUIContent("Keep on Skinned Renderer",
                "Bones on the receiving rig whose weighted triangles should stay on the original renderer. Includes descendants."), true);
            _window.ApplyModifiedProperties();
            _networkId = EditorGUILayout.IntField("Sender Network ID", _networkId);
            _channel = EditorGUILayout.IntSlider("Decoder Channel", _channel, 0, 3);
            _margin = EditorGUILayout.FloatField(new GUIContent("Motion Margin", "Extra culling bounds in Animator-local units on each side."), _margin);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            using (new EditorGUI.DisabledScope(!_animator || !_mesh || Application.isPlaying))
            {
                if (!GUILayout.Button("Bake GPU Skin")) return;
                var path = EditorUtility.SaveFilePanelInProject("Save GPU Skin", _mesh.name + " Skin", "asset", "Choose a new asset.");
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    Selection.activeGameObject = HumanoidSkinBaker.Bake(_animator, _mesh, _channel, _networkId, _margin, path,
                        _nativeBoneRoots).gameObject;
                    _error = null;
                }
                catch (Exception e) { _error = e.Message; }
            }
        }
    }
}
