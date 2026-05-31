using System;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace K13A.TSMP
{
    [ExecuteAlways]
    public sealed class TSMPSpoutOutput : MonoBehaviour
    {
        [Header("Source")]
        public Component encoder;
        public Texture sourceTexture;
        public bool preferEncoderOutput = true;

        [Header("Spout")]
        public string spoutName = "TSMP";
        public bool keepAlpha;
        public bool autoConfigure = true;
        public bool forceRunInBackground = true;
#if UNITY_EDITOR
        public bool pumpInEditMode = true;
        public int editModeFrameRate = 30;
#endif

        [Header("Diagnostics")]
        public bool configured;
        public string lastError;

        private const string SpoutSenderTypeName = "Klak.Spout.SpoutSender";
        private const string SpoutResourcesTypeName = "Klak.Spout.SpoutResources";
        private const string SpoutResourcesSearchFilter = "t:SpoutResources";
        private const string SpoutNamePropertyName = "spoutName";
        private const string CaptureMethodPropertyName = "captureMethod";
        private const string CaptureMethodTextureValueName = "Texture";
        private const string SourceTexturePropertyName = "sourceTexture";
        private const string KeepAlphaPropertyName = "keepAlpha";
        private const string SetResourcesMethodName = "SetResources";
        private const string CaptureFrameMethodName = "CaptureFrame";

        private bool _previousRunInBackground;
        private bool _didOverrideRunInBackground;
#if UNITY_EDITOR
        private double _nextEditorPumpTime;
#endif

        private void OnEnable()
        {
            ApplyRunInBackgroundOverride();
            if (autoConfigure)
                ConfigureNow();

#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
            RestoreRunInBackgroundOverride();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            editModeFrameRate = Mathf.Clamp(editModeFrameRate, 1, 120);
#endif
            if (autoConfigure && isActiveAndEnabled)
                ConfigureNow();
        }

        [ContextMenu("Configure Spout Sender")]
        public void ConfigureNow()
        {
            configured = false;
            lastError = string.Empty;

            Texture texture = ResolveSourceTexture();
            if (texture == null)
            {
                lastError = "Source texture is not assigned.";
                return;
            }

            Type senderType = FindType(SpoutSenderTypeName);
            if (senderType == null)
            {
                lastError = "KlakSpout is not installed or not loaded. Install jp.keijiro.klak.spout from the Keijiro scoped registry.";
                return;
            }

            Component sender = GetComponent(senderType);
            if (sender == null)
                sender = gameObject.AddComponent(senderType);

            if (!SetProperty(sender, SpoutNamePropertyName, spoutName))
            {
                lastError = "Failed to set Spout sender name.";
                return;
            }

            if (!SetEnumProperty(sender, CaptureMethodPropertyName, CaptureMethodTextureValueName))
            {
                lastError = "Failed to set Spout capture method.";
                return;
            }

            if (!SetProperty(sender, SourceTexturePropertyName, texture))
            {
                lastError = "Failed to set Spout source texture.";
                return;
            }

            if (!SetProperty(sender, KeepAlphaPropertyName, keepAlpha))
            {
                lastError = "Failed to set Spout alpha mode.";
                return;
            }

#if UNITY_EDITOR
            TryAssignSpoutResources(sender, senderType);
#endif

            configured = true;
        }

        private Texture ResolveSourceTexture()
        {
            if (preferEncoderOutput && encoder != null)
            {
                Texture output = ComponentReflection.GetMemberValue(encoder, TSMPEncoder.OutputFieldName) as Texture;
                if (output != null)
                    return output;
            }

            return sourceTexture;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool SetProperty(Component target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite)
                return false;

            property.SetValue(target, value);
            return true;
        }

        private static bool SetEnumProperty(Component target, string propertyName, string enumValueName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
                return false;

            object value = Enum.Parse(property.PropertyType, enumValueName);
            property.SetValue(target, value);
            return true;
        }

#if UNITY_EDITOR
        private static void TryAssignSpoutResources(Component sender, Type senderType)
        {
            MethodInfo method = senderType.GetMethod(SetResourcesMethodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
                return;

            Type resourcesType = FindType(SpoutResourcesTypeName);
            if (resourcesType == null)
                return;

            string[] guids = AssetDatabase.FindAssets(SpoutResourcesSearchFilter);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, resourcesType);
                if (asset == null)
                    continue;

                method.Invoke(sender, new object[] { asset });
                EditorUtility.SetDirty((UnityEngine.Object)sender);
                return;
            }
        }
#endif

        private void ApplyRunInBackgroundOverride()
        {
            if (!forceRunInBackground || _didOverrideRunInBackground)
                return;

            _previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _didOverrideRunInBackground = true;
        }

        private void RestoreRunInBackgroundOverride()
        {
            if (!_didOverrideRunInBackground)
                return;

            Application.runInBackground = _previousRunInBackground;
            _didOverrideRunInBackground = false;
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (Application.isPlaying || !pumpInEditMode || !isActiveAndEnabled)
                return;

            double now = EditorApplication.timeSinceStartup;
            double interval = 1.0 / Mathf.Max(1, editModeFrameRate);
            if (now < _nextEditorPumpTime)
                return;

            _nextEditorPumpTime = now + interval;

            if (!configured && autoConfigure)
                ConfigureNow();

            PumpSpoutSenderOnce();
        }

        [ContextMenu("Pump Spout Sender Once")]
        public void PumpSpoutSenderOnce()
        {
            Type senderType = FindType(SpoutSenderTypeName);
            if (senderType == null)
            {
                lastError = "KlakSpout is not installed or not loaded.";
                configured = false;
                return;
            }

            Component sender = GetComponent(senderType);
            if (sender == null)
            {
                lastError = "SpoutSender component is not attached.";
                configured = false;
                return;
            }

            MethodInfo captureFrame = senderType.GetMethod(CaptureFrameMethodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (captureFrame == null)
            {
                lastError = "KlakSpout CaptureFrame method was not found.";
                configured = false;
                return;
            }

            try
            {
                captureFrame.Invoke(sender, null);
                configured = true;
                lastError = string.Empty;
            }
            catch (Exception ex)
            {
                lastError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                configured = false;
            }
        }
#endif
    }
}
