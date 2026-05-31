using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace K13A.TSMP
{
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class TSMPBehaviour : UdonSharpBehaviour
#else
    public abstract class TSMPBehaviour : MonoBehaviour
#endif
    {
        private int _tsmpDebugLogRemaining;
        private float _tsmpLastWarningLogTime = -1000f;

#if UDONSHARP
        public static void SetProgramVariable(UdonBehaviour target, string fieldName, object value)
        {
            if (target == null)
                return;
            if (string.IsNullOrEmpty(fieldName))
                return;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
            {
                Component proxy = UdonProxySyncBridge.ResolveProxy(target);
                if (proxy != null)
                {
                    ComponentReflection.SetMemberValue(proxy, fieldName, value, true);
                    return;
                }
            }
#endif
            target.SetProgramVariable(fieldName, value);
        }

        public static object GetProgramVariable(UdonBehaviour target, string fieldName)
        {
            if (target == null)
                return null;
            if (string.IsNullOrEmpty(fieldName))
                return null;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
            {
                Component proxy = UdonProxySyncBridge.ResolveProxy(target);
                if (proxy != null)
                    return ComponentReflection.GetMemberValue(proxy, fieldName, true);
            }
#endif
            return target.GetProgramVariable(fieldName);
        }

        public static void SendCustomEvent(UdonBehaviour target, string methodName)
        {
            if (target == null)
                return;
            if (string.IsNullOrEmpty(methodName))
                return;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
            {
                Component proxy = UdonProxySyncBridge.ResolveProxy(target);
                if (proxy != null)
                {
                    ComponentReflection.InvokeMethod(proxy, methodName, true);
                    return;
                }
            }
#endif
            target.SendCustomEvent(methodName);
        }
#else
        public static void SetProgramVariable(Component target, string fieldName, object value)
        {
            ComponentReflection.SetMemberValue(target, fieldName, value, true);
        }

        public static object GetProgramVariable(Component target, string fieldName)
        {
            return ComponentReflection.GetMemberValue(target, fieldName, true);
        }

        public static void SendCustomEvent(Component target, string methodName)
        {
            ComponentReflection.InvokeMethod(target, methodName, true);
        }
#endif

        protected void ResetTSMPLogBudget(int budget)
        {
            _tsmpDebugLogRemaining = budget > 0 ? budget : 0;
            _tsmpLastWarningLogTime = -1000f;
        }

        protected void LogTSMPError(string prefix, string error, bool enabled)
        {
            if (!CanLogTSMPMessage(error, enabled))
                return;

            _tsmpDebugLogRemaining--;
            Debug.LogError(prefix + error);
        }

        protected void LogTSMPWarning(string prefix, string error, bool enabled, float intervalSeconds)
        {
            if (!CanLogTSMPMessage(error, enabled))
                return;

            float now = Time.realtimeSinceStartup;
            if (intervalSeconds > 0f && now - _tsmpLastWarningLogTime < intervalSeconds)
                return;

            _tsmpLastWarningLogTime = now;
            _tsmpDebugLogRemaining--;
            Debug.LogWarning(prefix + error);
        }

        protected void LogTSMPRateLimitedWarning(string prefix, string error, float intervalSeconds)
        {
            if (error == null || error.Length == 0)
                return;

            float now = Time.realtimeSinceStartup;
            if (intervalSeconds > 0f && now - _tsmpLastWarningLogTime < intervalSeconds)
                return;

            _tsmpLastWarningLogTime = now;
            Debug.LogWarning(prefix + error);
        }

        private bool CanLogTSMPMessage(string error, bool enabled)
        {
            if (!enabled)
                return false;
            if (error == null || error.Length == 0)
                return false;

            return _tsmpDebugLogRemaining > 0;
        }
    }
}
