using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The unit's weapons status, reached entirely at runtime.
///
/// This exists because <c>WeaponStatus</c> turned out to be a poor thing to name in code: it is the
/// parameter type of <c>ObjectBase.SetWeaponStatus</c>, but the build failed with "type or namespace
/// WeaponStatus could not be found", which means it does not live in a namespace this project can
/// simply import. Guessing again would cost another build cycle.
///
/// So the enum is taken from the method's own signature: whatever type
/// <c>SetWeaponStatus</c> actually takes is the type used, wherever it is declared. The values are
/// chosen from their names, which also survives a patch that renames or reorders them.
/// </summary>
internal static class WeaponStatusControl
{
    private static bool resolved;
    private static MethodInfo? setter;
    private static Type? statusType;

    /// <summary>Every value name in the enum, for the log.</summary>
    internal static string Names => statusType == null ? "<unresolved>" : string.Join(", ", Enum.GetNames(statusType));

    private static void Resolve()
    {
        if (resolved) return;
        resolved = true;

        try
        {
            foreach (MemberInfo member in MemberProbe.All(typeof(ObjectBase)))
            {
                if (member is not MethodInfo method) continue;
                if (!string.Equals(method.Name, "SetWeaponStatus", StringComparison.OrdinalIgnoreCase)) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 1 || !parameters[0].ParameterType.IsEnum) continue;

                setter = method;
                statusType = parameters[0].ParameterType;
                break;
            }

            ModLog.Info(setter != null
                ? "Weapons status: found SetWeaponStatus(" + statusType!.Name + ") with values " + Names
                : "Weapons status: SetWeaponStatus was not found; readiness features fall back to the reload clock alone");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not resolve the weapons status type", error);
        }
    }

    /// <summary>True when the game exposes a weapons status this mod can set.</summary>
    internal static bool Available
    {
        get
        {
            Resolve();
            return setter != null && statusType != null;
        }
    }

    /// <summary>
    /// Sets the posture. <paramref name="aggressive"/> picks free/open/engage when true and
    /// hold/tight/safe when false, by matching the value names.
    /// </summary>
    internal static bool Set(ObjectBase unit, bool aggressive)
    {
        if (unit == null) return false;
        if (!Available) return false;

        object? value = Pick(aggressive);
        if (value == null) return false;

        try
        {
            // The arguments are built from the method's own signature rather than assumed. The
            // offline dump suggested a second bool, but an overload with a different arity would
            // throw here and turn a working call into a silent no-op, so nothing is hard-coded.
            ParameterInfo[] parameters = setter!.GetParameters();
            var arguments = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type == statusType) arguments[i] = value;
                else if (type == typeof(bool)) arguments[i] = true;
                else if (parameters[i].HasDefaultValue) arguments[i] = parameters[i].DefaultValue;
                else return false;
            }

            setter.Invoke(unit, arguments);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The enum value for a posture, or null if this build has no matching name.</summary>
    internal static object? Pick(bool aggressive)
    {
        if (!Available) return null;

        string[] fragments = aggressive
            ? new[] { "free", "open", "engage", "attack" }
            : new[] { "hold", "tight", "safe", "cease", "defensive" };

        foreach (string name in Enum.GetNames(statusType!))
        {
            string lower = name.ToLowerInvariant();
            foreach (string fragment in fragments)
            {
                if (lower.Contains(fragment)) return Enum.Parse(statusType!, name);
            }
        }
        return null;
    }

    /// <summary>A description of the two postures, for the tooltip and the log.</summary>
    internal static List<string> Describe()
    {
        var lines = new List<string>();

        if (!Available)
        {
            lines.Add("  --   weapons status                not exposed by this build");
            return lines;
        }

        lines.Add("  ok   weapons status                " + statusType!.Name + ": " + Names);
        lines.Add("       hold posture   -> " + (Pick(false) ?? "<no matching value>"));
        lines.Add("       free posture   -> " + (Pick(true) ?? "<no matching value>"));
        return lines;
    }
}
