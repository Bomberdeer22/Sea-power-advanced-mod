using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Degraded local control: when the ship loses its fire-control picture, turrets and CIWS fight
/// their own battle.
///
/// A real ship that loses its directors or its datalink does not stop shooting - it falls back to
/// local control, which is worse but is not nothing. The readiness report found the game models the
/// discipline, in <c>SeaPower.CiwsDisciplineState</c>, but not as something with an obvious setter.
///
/// The approach is therefore reflective and conservative: find any member on the ship whose type is
/// the discipline enum, and set it to the value whose own name mentions "local", "manual" or
/// "autonomous". If this build has no such member, the mod keeps the state itself and reports that
/// the game was not told - it never pretends a setting took effect.
/// </summary>
internal static class LocalControl
{
    internal static bool IsLocal(ObjectBase unit) => unit != null && UnitState.Of(unit).LocalControl;

    internal static bool Toggle(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        UnitRecord record = UnitState.Of(unit);
        record.LocalControl = !record.LocalControl;

        int applied = Apply(unit, record.LocalControl, out string how);

        report = record.LocalControl
            ? "local control on"
              + (applied > 0 ? ": " + applied + " system(s) set to " + how : " (this build exposes no discipline setting)")
            : "local control off"
              + (applied > 0 ? ": " + applied + " system(s) set to " + how : "");

        return applied > 0;
    }

    /// <summary>Sets every discipline-typed member on the ship. Returns how many responded.</summary>
    private static int Apply(ObjectBase unit, bool local, out string valueName)
    {
        valueName = "?";
        int changed = 0;

        try
        {
            foreach (MemberInfo member in MemberProbe.All(unit.GetType()))
            {
                Type? type = member switch
                {
                    FieldInfo field => field.FieldType,
                    PropertyInfo property => property.PropertyType,
                    _ => null,
                };

                if (type == null) continue;
                if (!type.IsEnum) continue;
                if (!type.Name.Contains("Discipline")) continue;

                string[] wanted = local
                    ? new[] { "local", "manual", "autonomous", "free", "auto" }
                    : new[] { "central", "ship", "director", "controlled", "hold" };

                object? value = Pick(type, wanted);
                if (value == null) continue;

                if (MemberProbe.Write(member, unit, value))
                {
                    valueName = value.ToString() ?? "?";
                    changed++;
                }
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Local control could not inspect the ship's systems", error);
        }

        return changed;
    }

    private static object? Pick(Type type, string[] fragments)
    {
        try
        {
            foreach (string name in Enum.GetNames(type))
            {
                string lower = name.ToLowerInvariant();
                foreach (string fragment in fragments)
                {
                    if (lower.Contains(fragment)) return Enum.Parse(type, name);
                }
            }
        }
        catch (Exception) { }
        return null;
    }
}
