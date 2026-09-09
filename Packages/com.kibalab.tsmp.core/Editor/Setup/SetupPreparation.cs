using System;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UDONSHARP
using UdonSharp;
using UdonSharpEditor;
#endif

namespace K13A.TSMP.Editor
{
    [InitializeOnLoad]
    public sealed class SetupPreparation : IProcessSceneWithReport, IPreprocessBuildWithReport
    {
        private static bool _queued;
        public int callbackOrder => -10000;

        static SetupPreparation()
        {
            SetupInstantiation.PrepareAction = Prepare;
            SetupInstantiation.InstantiateCodecAction = InstantiateCodec;
            EditorApplication.hierarchyChanged += QueueAll;
            EditorApplication.projectChanged += QueueAll;
            Undo.undoRedoPerformed += QueueAll;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            QueueAll();
        }

        private static void QueueAll()
        {
            if (_queued || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            _queued = true;
            EditorApplication.delayCall += PrepareQueued;
        }

        private static void PrepareQueued()
        {
            _queued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueAll();
                return;
            }
            PrepareAll();
        }

        public static void PrepareAll()
        {
            if (EditorApplication.isPlaying)
                return;
            foreach (TSMPSetup setup in UnityEngine.Object.FindObjectsOfType<TSMPSetup>(true))
                if (setup.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(setup))
                    setup.ApplyNow();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                PrepareAll();
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            PrepareScene(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (TSMPSetup setup in root.GetComponentsInChildren<TSMPSetup>(true))
                    if (setup.resources != null)
                        AssetDatabase.SaveAssetIfDirty(setup.resources);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            PrepareAll();
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            PrepareScene(scene);
        }

        private static void PrepareScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (TSMPSetup setup in root.GetComponentsInChildren<TSMPSetup>(true))
                    setup.ApplyNow();
        }

        private static void Prepare(TSMPSetup setup)
        {
            Undo.RecordObject(setup, "Prepare TSMP controller");
            if (setup.configureEncoder && setup.encoder == null)
            {
                setup.encoder = FindOwned<TSMPEncoder>(setup);
                if (setup.encoder == null)
                {
                    var encoder = (TSMPEncoder)AddBehaviour(GetChild(setup, "Encoder"), typeof(TSMPEncoder));
                    encoder.autoEncode = true;
                    setup.encoder = encoder;
                }
            }
            if (setup.configureDecoder && setup.decoder == null)
            {
                setup.decoder = FindOwned<TSMPDecoder>(setup);
                if (setup.decoder == null)
                    setup.decoder = AddBehaviour(GetChild(setup, "Decoder"), typeof(TSMPDecoder));
            }
            SetupResourceEditor.Prepare(setup);
            PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
        }

        private static T FindOwned<T>(TSMPSetup setup) where T : Component
        {
            foreach (T component in setup.GetComponentsInChildren<T>(true))
                if (component.GetComponentInParent<TSMPSetup>(true) == setup)
                    return component;
            return null;
        }

        private static GameObject GetChild(TSMPSetup setup, string name)
        {
            Transform existing = setup.transform.Find(name);
            if (existing != null)
                return existing.gameObject;
            var child = new GameObject(name);
            child.transform.SetParent(setup.transform, false);
            Undo.RegisterCreatedObjectUndo(child, "Create TSMP " + name);
            return child;
        }

        internal static Component AddBehaviour(GameObject target, Type type)
        {
#if UDONSHARP
            return UdonSharpUndo.AddComponent(target, type);
#else
            return Undo.AddComponent(target, type);
#endif
        }

        private static TSMPCodec InstantiateCodec(TSMPCodec source, Transform parent, TSMPSetup setup)
        {
            var root = UnityEngine.Object.Instantiate(source.gameObject, parent);
            root.name = source.name + " (Runtime)";
            try
            {
#if UDONSHARP
                CodecInstancePreparation.Prepare(root);
#endif
                SetupResourceEditor.PrepareCodec(setup, root);
                Undo.RegisterCreatedObjectUndo(root, "Create TSMP codec");
                return root.GetComponent<TSMPCodec>();
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }
    }

#if UDONSHARP
    public sealed class SetupWorldBuildPreparation : VRC.SDKBase.Editor.BuildPipeline.IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -10000;

        public bool OnBuildRequested(VRC.SDKBase.Editor.BuildPipeline.VRCSDKRequestedBuildType requestedBuildType)
        {
            SetupPreparation.PrepareAll();
            return true;
        }
    }
#endif
}
