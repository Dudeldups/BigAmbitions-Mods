using System;
using System.Reflection;
using Dialogs;
using UnityEngine;

namespace CustomNPCAPI
{
    internal static class CustomNpcReflection
    {
        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type _dialogUiType;
        private static MethodInfo _showDialogMethod;
        private static UnityEngine.Object _dialogUiInstance;

        internal static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var exact = assembly.GetType(typeName, false);
                if (exact != null) return exact;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types; }
                if (types == null) continue;
                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (type.FullName == typeName || type.Name == typeName || (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false))
                        return type;
                }
            }
            return null;
        }

        internal static bool SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(memberName, InstanceFlags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, ConvertMemberValue(value, property.PropertyType));
                    return true;
                }

                var field = type.GetField(memberName, InstanceFlags);
                if (field == null)
                    continue;

                field.SetValue(instance, ConvertMemberValue(value, field.FieldType));
                return true;
            }

            return false;
        }

        private static object ConvertMemberValue(object value, Type targetType)
        {
            if (targetType == null)
                return value;
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            if (targetType.IsInstanceOfType(value))
                return value;
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, Convert.ToInt32(value));
            return Convert.ChangeType(value, targetType);
        }

        internal static bool TryInvokeParameterlessMethod(object instance, string methodName)
        {
            if (instance == null) return false;
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethod(methodName, InstanceFlags, null, Type.EmptyTypes, null);
                if (method == null) continue;
                method.Invoke(instance, null); return true;
            }
            return false;
        }

        internal static bool TryOpenDialog(CallDialogType dialogType)
        {
            _dialogUiType = _dialogUiType ?? FindType("UI.Dialog.DialogUI");
            if (_dialogUiType == null) return false;
            if (_dialogUiInstance == null)
                _dialogUiInstance = Resources.FindObjectsOfTypeAll(_dialogUiType).Length > 0 ? Resources.FindObjectsOfTypeAll(_dialogUiType)[0] : null;
            if (_dialogUiInstance == null) return false;
            if (_showDialogMethod == null)
            {
                foreach (var method in _dialogUiType.GetMethods(InstanceFlags))
                {
                    if (method.Name != "ShowDialog") continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length == 5 && parameters[0].ParameterType == typeof(CallDialogType)) { _showDialogMethod = method; break; }
                }
            }
            if (_showDialogMethod == null) return false;
            _showDialogMethod.Invoke(_dialogUiInstance, new object[] { dialogType, null, null, null, null });
            return true;
        }
    }
}
