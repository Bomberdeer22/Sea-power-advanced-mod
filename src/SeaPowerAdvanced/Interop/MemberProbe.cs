using System;
using System.Collections.Generic;
using System.Reflection;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Finds members of the game's types at runtime, by name, with signature checks.
///
/// Why reflection instead of direct calls: the readiness report lists names, not signatures, and a
/// wrong guess is a compile error that costs a round trip through the user's machine. Reflection
/// moves that risk from "will not build" to "drops the capability and logs it", which is a far
/// cheaper failure. It also means one DLL works across game versions that rename members.
///
/// The danger of name matching is that it finds the wrong thing — the analysis tool proved that by
/// matching CEC inside sourceConstructFlags. So every lookup here takes an explicit signature it
/// will accept, and anything that does not match is recorded as missing rather than used.
/// </summary>
internal static class MemberProbe
{
    private const BindingFlags Any =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Every member of the type and its base types, ignoring object's own members.</summary>
    internal static List<MemberInfo> All(Type type)
    {
        var result = new List<MemberInfo>();
        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            try { result.AddRange(current.GetMembers(Any | BindingFlags.DeclaredOnly)); }
            catch (Exception) { }
        }
        return result;
    }

    /// <summary>A method whose name matches one of the candidates and whose parameters fit.</summary>
    internal static MethodInfo? Method(Type type, Type[]? parameters, params string[] candidates)
    {
        foreach (MemberInfo member in All(type))
        {
            if (member is not MethodInfo method) continue;
            if (!Matches(method.Name, candidates)) continue;
            if (parameters == null) return method;

            ParameterInfo[] actual = method.GetParameters();
            if (actual.Length != parameters.Length) continue;

            bool fits = true;
            for (int i = 0; i < actual.Length && fits; i++) fits = actual[i].ParameterType == parameters[i];
            if (fits) return method;
        }
        return null;
    }

    /// <summary>A field or property whose name matches one of the candidates.</summary>
    internal static MemberInfo? Value(Type type, params string[] candidates)
    {
        foreach (MemberInfo member in All(type))
        {
            if (member is not FieldInfo && member is not PropertyInfo) continue;
            if (Matches(member.Name, candidates)) return member;
        }
        return null;
    }

    /// <summary>Reads a field or a property through reflection; null if it is not readable.</summary>
    internal static object? Read(MemberInfo? member, object target)
    {
        if (member == null) return null;
        try
        {
            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property && property.CanRead) return property.GetValue(target);
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>Writes a field or a property through reflection; false if it is not writable.</summary>
    internal static bool Write(MemberInfo? member, object target, object? value)
    {
        if (member == null) return false;
        try
        {
            if (member is FieldInfo field)
            {
                field.SetValue(target, value);
                return true;
            }
            if (member is PropertyInfo property && property.CanWrite)
            {
                property.SetValue(target, value);
                return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>Invokes a member, tolerating exceptions; false if it could not be called.</summary>
    internal static bool Invoke(MemberInfo? member, object target, params object?[] arguments)
    {
        switch (member)
        {
            case null:
                return false;
            case MethodInfo method:
                try
                {
                    method.Invoke(target, arguments);
                    return true;
                }
                catch (Exception) { return false; }
            case PropertyInfo property when arguments.Length == 0:
                return Read(property, target) != null;
            default:
                return false;
        }
    }

    /// <summary>
    /// True when the member name starts with, or equals, one of the candidates. A prefix test is
    /// used rather than a substring test so that "Active" does not match "Inactive" and "Arm" does
    /// not match "ArmorType" - both of which the offline tool fell for.
    /// </summary>
    internal static bool Matches(string name, string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("_" + candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Short, readable form of a member for a log line.</summary>
    internal static string Describe(MemberInfo? member)
    {
        switch (member)
        {
            case null:
                return "<not found>";
            case MethodInfo method:
                var parameters = method.GetParameters();
                var names = new string[parameters.Length];
                for (int i = 0; i < parameters.Length; i++) names[i] = parameters[i].ParameterType.Name;
                return method.DeclaringType?.Name + "." + method.Name + "(" + string.Join(", ", names) + ")";
            default:
                return member.DeclaringType?.Name + "." + member.Name;
        }
    }
}
