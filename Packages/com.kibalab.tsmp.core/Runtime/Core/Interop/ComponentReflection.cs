using System.Reflection;
using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class ComponentReflection
    {
        private const BindingFlags PublicInstanceFlags = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags AllInstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string DefaultLogPrefix = "[TSMP]";
        private const string UdonSharpBackingFieldName = "_udonSharpBackingUdonBehaviour";

        public static void SetField(Component target, string fieldName, object value, string logPrefix)
        {
            if (target == null)
                return;

            FieldInfo field = target.GetType().GetField(fieldName, PublicInstanceFlags);
            if (field == null)
            {
                Debug.LogWarning(GetLogPrefix(logPrefix) + " Field '" + fieldName + "' was not found on " + target.GetType().Name + ".", target);
                return;
            }

            try
            {
                field.SetValue(target, value);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(GetLogPrefix(logPrefix) + " Failed to set field '" + fieldName + "' on " + target.GetType().Name + ": " + exception.Message, target);
            }
        }

        public static object GetMemberValue(Component target, string memberName)
        {
            return GetMemberValue(target, memberName, false);
        }

        public static object GetMemberValue(Component target, string memberName, bool includeNonPublic)
        {
            if (target == null)
                return null;
            if (string.IsNullOrEmpty(memberName))
                return null;

            System.Type type = target.GetType();
            BindingFlags flags = GetInstanceFlags(includeNonPublic);

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(target);

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                return property.GetValue(target);

            return null;
        }

        public static void SetMemberValue(Component target, string memberName, object value, bool includeNonPublic)
        {
            if (target == null)
                return;
            if (string.IsNullOrEmpty(memberName))
                return;

            System.Type type = target.GetType();
            BindingFlags flags = GetInstanceFlags(includeNonPublic);

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                property.SetValue(target, value);
        }

        public static int GetIntMember(Component target, string memberName, int defaultValue)
        {
            object value = GetMemberValue(target, memberName);
            if (value is int)
                return (int)value;

            return defaultValue;
        }

        public static bool GetBoolMember(Component target, string memberName, bool defaultValue)
        {
            object value = GetMemberValue(target, memberName);
            if (value is bool)
                return (bool)value;

            return defaultValue;
        }

        public static void InvokeMethod(Component target, string methodName)
        {
            InvokeMethod(target, methodName, false);
        }

        public static void InvokeMethod(Component target, string methodName, bool includeNonPublic)
        {
            if (target == null)
                return;
            if (string.IsNullOrEmpty(methodName))
                return;

            MethodInfo method = target.GetType().GetMethod(methodName, GetInstanceFlags(includeNonPublic));
            if (method != null && method.GetParameters().Length == 0)
                method.Invoke(target, null);
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public static UdonBehaviour GetBackingUdonBehaviour(Component component)
        {
            if (component == null)
                return null;

            System.Type type = component.GetType();
            while (type != null)
            {
                FieldInfo backingField = type.GetField(UdonSharpBackingFieldName, AllInstanceFlags);
                if (backingField != null)
                {
                    UdonBehaviour backing = backingField.GetValue(component) as UdonBehaviour;
                    if (backing != null)
                        return backing;
                }

                type = type.BaseType;
            }

            return component as UdonBehaviour;
        }

#endif

        private static string GetLogPrefix(string logPrefix)
        {
            if (string.IsNullOrEmpty(logPrefix))
                return DefaultLogPrefix;

            return logPrefix;
        }

        private static BindingFlags GetInstanceFlags(bool includeNonPublic)
        {
            if (includeNonPublic)
                return AllInstanceFlags;

            return PublicInstanceFlags;
        }
    }

    public static class UdonProxySyncBridge
    {
#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
        public static System.Action<Component> SyncAction;
        public static System.Func<UdonBehaviour, Component> ResolveProxyAction;
#endif

        public static void Sync(Component component)
        {
#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
            if (component == null || SyncAction == null)
                return;

            SyncAction(component);
#endif
        }

#if UDONSHARP || COMPILER_UDONSHARP
        public static Component ResolveProxy(UdonBehaviour behaviour)
        {
#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
            if (behaviour == null || ResolveProxyAction == null)
                return null;

            return ResolveProxyAction(behaviour);
#else
            return null;
#endif
        }
#endif
    }
}
