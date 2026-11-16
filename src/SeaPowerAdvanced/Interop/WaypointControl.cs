using System;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Pushes a waypoint onto a unit through the game's own <c>setWaypointTask</c>.
///
/// This wrapper exists because of a build failure worth remembering: calling
/// <c>unit.setWaypointTask(point, "label", default(WaypointHeightState))</c> compiled the call and
/// then failed on the type name — <c>WaypointHeightState</c> is not reachable from any namespace this
/// project can import, exactly like <c>WeaponStatus</c>. Naming a game enum in code is a guess about
/// where it lives, and the guess is wrong often enough that this file stops guessing.
///
/// So the method is found by name on <see cref="ObjectBase"/>, and its arguments are built from its
/// own signature: the <see cref="GeoPosition"/> parameter gets the position, a string gets the label,
/// an enum gets its zero value (which is what <c>default(...)</c> did), and anything genuinely
/// unknown makes the call refuse rather than guess.
/// </summary>
internal static class WaypointControl
{
    private static bool resolved;
    private static MethodInfo? push;

    private static void Resolve()
    {
        if (resolved) return;
        resolved = true;

        try
        {
            // Prefer the three-argument form the civilian route code uses; fall back to any
            // overload with the same name if this build's shape differs.
            push = MemberProbe.Method(typeof(ObjectBase), 3, "setWaypointTask")
                   ?? MemberProbe.Method(typeof(ObjectBase), null, "setWaypointTask");

            ModLog.Info(push != null
                ? "Waypoints: " + MemberProbe.Describe(push)
                : "Waypoints: this build has no setWaypointTask, so helo patterns and the threat-axis "
                  + "turn cannot move a unit (the menu entry will say so)");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not resolve the waypoint method", error);
        }
    }

    /// <summary>True when this build has a waypoint method the mod can call.</summary>
    internal static bool Available
    {
        get
        {
            Resolve();
            return push != null;
        }
    }

    /// <summary>The method's signature, for the log and the capability report.</summary>
    internal static string Signature => push == null ? "<none>" : MemberProbe.Describe(push);

    /// <summary>
    /// Sends <paramref name="unit"/> to <paramref name="point"/>, with a label the game shows on the
    /// map. Returns false and sets <paramref name="why"/> when the call could not be made.
    /// </summary>
    internal static bool Push(ObjectBase unit, GeoPosition point, string label, out string why)
    {
        why = "";

        if (unit == null)
        {
            why = "no unit";
            return false;
        }

        if (!Available)
        {
            why = "this build has no setWaypointTask this mod can call";
            return false;
        }

        try
        {
            ParameterInfo[] parameters = push!.GetParameters();
            var arguments = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                Type type = parameter.ParameterType;

                if (type == typeof(GeoPosition) || Nullable.GetUnderlyingType(type) == typeof(GeoPosition))
                {
                    arguments[i] = point;
                }
                else if (type == typeof(string))
                {
                    arguments[i] = label;
                }
                else if (type.IsEnum)
                {
                    // default(SomeEnum) is what the failing code asked for; the zero value is the
                    // same thing without naming the type.
                    arguments[i] = Enum.ToObject(type, 0);
                }
                else if (type.IsValueType && !parameter.HasDefaultValue
                         && Nullable.GetUnderlyingType(type) == null)
                {
                    why = "the waypoint method wants a " + type.Name
                          + ", which this mod cannot fill without guessing";
                    return false;
                }
                else if (parameter.HasDefaultValue)
                {
                    arguments[i] = parameter.DefaultValue;
                }
                else
                {
                    arguments[i] = null;
                }
            }

            push.Invoke(unit, arguments);
            return true;
        }
        catch (Exception error)
        {
            why = "the game refused the waypoint: " + error.GetType().Name;
            ModLog.Error("setWaypointTask failed", error);
            return false;
        }
    }
}
