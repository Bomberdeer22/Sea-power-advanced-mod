using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SeaPowerAdvanced;

/// <summary>
/// Reflection helpers.
///
/// The game's hatch and animation members are not part of any documented API, so this mod talks
/// to them by name instead of binding to them at compile time. That keeps the DLL loadable across
/// game updates: if a member disappears we log it and fall back to the next strategy instead of
/// throwing a <see cref="TypeLoadException"/> at startup.
///
/// Everything here swallows reflection errors on purpose.
/// </summary>
internal static class R
{
    private const BindingFlags Any =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    private static readonly ConcurrentDictionary<string, MemberInfo?> MemberCache = new();

    internal static Type? TypeOf(object? instance) => instance?.GetType();

    // ---------------------------------------------------------------- members

    private static MemberInfo? Find(Type type, string name)
    {
        string key = type.FullName + "|" + name;
        if (MemberCache.TryGetValue(key, out MemberInfo? cached)) return cached;

        MemberInfo? found = null;
        for (Type? t = type; t != null && found == null; t = t.BaseType)
        {
            found = (MemberInfo?)t.GetField(name, Any) ?? t.GetProperty(name, Any);
        }

        MemberCache[key] = found;
        return found;
    }

    /// <summary>Reads a field or property by name. Returns null when missing or unreadable.</summary>
    internal static object? Get(object? instance, string name)
    {
        if (instance == null) return null;
        try
        {
            return Find(instance.GetType(), name) switch
            {
                FieldInfo field => field.GetValue(field.IsStatic ? null : instance),
                PropertyInfo property when property.GetIndexParameters().Length == 0
                    => property.GetValue(property.GetGetMethod(true)?.IsStatic == true ? null : instance, null),
                _ => null
            };
        }
        catch (Exception) { return null; }
    }

    /// <summary>Writes a field or property by name. Returns false when missing or unreadable.</summary>
    internal static bool TrySet(object? instance, string name, object? value)
    {
        if (instance == null) return false;
        try
        {
            switch (Find(instance.GetType(), name))
            {
                case FieldInfo field:
                    field.SetValue(field.IsStatic ? null : instance, Coerce(value, field.FieldType));
                    return true;
                case PropertyInfo property when property.CanWrite && property.GetIndexParameters().Length == 0:
                    property.SetValue(instance, Coerce(value, property.PropertyType), null);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception) { return false; }
    }

    internal static MethodInfo? Method(Type type, string name, int parameterCount)
    {
        for (Type? t = type; t != null; t = t.BaseType)
        {
            foreach (MethodInfo method in t.GetMethods(Any))
            {
                if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
            }
        }
        return null;
    }

    /// <summary>Calls a method by name. Returns false when missing or when it threw.</summary>
    internal static bool TryCall(object? instance, string name, params object?[] arguments)
    {
        if (instance == null) return false;
        try
        {
            MethodInfo? method = Method(instance.GetType(), name, arguments.Length);
            if (method == null) return false;
            method.Invoke(method.IsStatic ? null : instance, arguments);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------- collections

    /// <summary>Reads any enumerable (array, List, ObservableCollection, ...) into a list.</summary>
    internal static List<object> Items(object? instance)
    {
        var result = new List<object>();
        if (instance is string || instance is not IEnumerable sequence) return result;
        try
        {
            foreach (object? item in sequence)
            {
                if (item != null) result.Add(item);
            }
        }
        catch (Exception) { }
        return result;
    }

    /// <summary>Reads a string-keyed dictionary into a list of pairs.</summary>
    internal static List<KeyValuePair<string, object?>> DictionaryEntries(object? instance)
    {
        var result = new List<KeyValuePair<string, object?>>();
        if (instance is not IDictionary dictionary) return result;
        try
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key) result.Add(new KeyValuePair<string, object?>(key, entry.Value));
            }
        }
        catch (Exception) { }
        return result;
    }

    // ------------------------------------------------------------------ text

    private static object? Coerce(object? value, Type target)
    {
        if (value == null) return null;
        if (target.IsInstanceOfType(value)) return value;
        if (target == typeof(bool)) return System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        if (target == typeof(float)) return System.Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (target == typeof(double)) return System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (target == typeof(int)) return System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    /// <summary>Short one-line rendering of any value, safe for logs.</summary>
    internal static string Format(object? value)
    {
        switch (value)
        {
            case null: return "null";
            case string text: return "\"" + text + "\"";
            case bool flag: return flag ? "true" : "false";
            case UnityEngine.Object unity: return unity.name + " (" + unity.GetType().Name + ")";
            case IEnumerable sequence: return value.GetType().Name + "[" + Items(value).Count + "]";
            default: return value.ToString() ?? value.GetType().Name;
        }
    }

    /// <summary>
    /// Dumps the readable instance fields of an object (declared at every level of the hierarchy).
    /// This is what makes the diagnostic files useful: it shows the real member names of the
    /// game's own container and animation objects.
    /// </summary>
    internal static string Describe(object? instance, int maxFields = 24)
    {
        if (instance == null) return "null";

        var text = new StringBuilder();
        int written = 0;
        for (Type? type = instance.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            FieldInfo[] fields;
            try { fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
            catch (Exception) { continue; }

            foreach (FieldInfo field in fields)
            {
                if (written++ >= maxFields) return text.Append("...").ToString();
                object? value;
                try { value = field.GetValue(instance); }
                catch (Exception) { value = "<error>"; }
                text.Append(field.Name).Append('=').Append(Format(value)).Append("; ");
            }
        }
        return text.ToString();
    }

    /// <summary>Lower-case alphanumerics, used to match animation names against system names.</summary>
    internal static string Token(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var token = new StringBuilder(text!.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) token.Append(char.ToLowerInvariant(c));
        }
        return token.ToString();
    }
}
