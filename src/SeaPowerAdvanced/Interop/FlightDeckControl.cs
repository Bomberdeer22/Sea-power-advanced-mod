using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Launches a helicopter through the game's own flight-deck API, without naming its types.
///
/// The documented shape is <c>FlightDeckLaunchUnit(ObjectType, string, UnitRoles, MissionType, bool,
/// float, bool)</c> and <c>FlightDeckHasUnit(ObjectType, UnitRoles)</c>, but <c>UnitRoles</c> and
/// <c>MissionType</c> are as unreachable from this project's namespaces as <c>WeaponStatus</c> and
/// <c>WaypointHeightState</c> turned out to be. Naming them cost a build. So the methods are found by
/// name, and their arguments are built from their own parameter lists.
///
/// The one thing this file is careful about: it never invents an aircraft. Every launch is preceded
/// by the game's own "is there a helicopter on deck" query where that query exists, so a ship with an
/// empty deck reports that instead of the mod pretending to fly something.
/// </summary>
internal static class FlightDeckControl
{
    /// <summary>Name fragments that mark an anti-submarine role in whatever enum the game uses.</summary>
    private static readonly string[] RoleHints = { "asw", "antisub", "anti_sub", "hunter", "sub" };

    /// <summary>Name fragments that mark a patrol/search sortie.</summary>
    private static readonly string[] MissionHints = { "patrol", "search", "asw" };

    private static bool resolved;
    private static MethodInfo? launch;
    private static MethodInfo? hasUnit;

    private static void Resolve()
    {
        if (resolved) return;
        resolved = true;

        try
        {
            var launches = new List<MethodInfo>();
            var queries = new List<MethodInfo>();

            foreach (MemberInfo member in MemberProbe.All(typeof(ObjectBase)))
            {
                if (member is not MethodInfo method || method.IsAbstract) continue;
                if (method.Name.IndexOf("FlightDeck", StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (method.Name.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0) launches.Add(method);
                else if (method.Name.IndexOf("HasUnit", StringComparison.OrdinalIgnoreCase) >= 0) queries.Add(method);
            }

            // The launch call takes the most parameters (airframe, role, sortie type, flags); the
            // deck query the fewest (type, role). Where overloads exist, taking the extremes is a
            // better guess than taking whichever came first out of the metadata.
            foreach (MethodInfo candidate in launches)
            {
                if (launch == null || candidate.GetParameters().Length > launch.GetParameters().Length)
                    launch = candidate;
            }

            foreach (MethodInfo candidate in queries)
            {
                if (hasUnit == null || candidate.GetParameters().Length < hasUnit.GetParameters().Length)
                    hasUnit = candidate;
            }

            ModLog.Info(launch != null
                ? "Flight deck: launch = " + MemberProbe.Describe(launch) + "; query = "
                  + (hasUnit == null ? "<none, launching unverified>" : MemberProbe.Describe(hasUnit))
                : "Flight deck: this build exposes no flight-deck launch method, so one-click helo "
                  + "patterns cannot start one (they still fly a helo that is already airborne)");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not resolve the flight-deck methods", error);
        }
    }

    /// <summary>True when a launch is possible at all in this build.</summary>
    internal static bool Available
    {
        get
        {
            Resolve();
            return launch != null;
        }
    }

    /// <summary>Signatures, for capabilities.txt.</summary>
    internal static string Signature =>
        launch == null ? "<none>" : MemberProbe.Describe(launch) + "; query " +
        (hasUnit == null ? "<none>" : MemberProbe.Describe(hasUnit));

    /// <summary>
    /// Launches an ASW helicopter if one is on deck. <paramref name="why"/> always explains the
    /// outcome, because "nothing happened" is the least useful thing a menu entry can report.
    /// </summary>
    internal static bool TryLaunch(ObjectBase ship, out string why)
    {
        why = "";

        if (ship == null)
        {
            why = "no ship";
            return false;
        }

        if (!Available)
        {
            why = "this build exposes no flight-deck launch method the mod can call "
                  + "(see capabilities.txt)";
            return false;
        }

        // The game's own answer to "is there a helicopter aboard", when it offers one. A false here
        // is not a guess: it is the simulation saying the deck is empty.
        if (hasUnit != null &&
            Build(hasUnit, ObjectBase.ObjectType.Helicopter, out object?[] query, out _, out _))
        {
            try
            {
                if (hasUnit.Invoke(ship, query) is bool present && !present)
                {
                    why = "no ASW helicopter is on deck - this ship has nothing to fly";
                    return false;
                }
            }
            catch (Exception)
            {
                // A failed query is not a refusal; try the launch and let the game decide.
            }
        }

        if (!Build(launch!, ObjectBase.ObjectType.Helicopter, out object?[] arguments, out string note, out why))
            return false;

        try
        {
            object? result = launch!.Invoke(ship, arguments);

            if (result is bool launched && !launched)
            {
                why = "the game refused the launch (deck busy, or no airframe fits)";
                return false;
            }

            ModLog.Info("Flight deck: launch called with " + note);
            why = "launching";
            return true;
        }
        catch (Exception error)
        {
            why = "the game threw on launch: " + error.GetType().Name
                  + (error.InnerException == null ? "" : " (" + error.InnerException.GetType().Name + ")");
            ModLog.Error("FlightDeckLaunchUnit failed with " + note, error);
            return false;
        }
    }

    /// <summary>
    /// Fills a method's arguments from its own signature. <paramref name="note"/> records what was
    /// passed, so the log shows exactly which enum values were chosen.
    /// </summary>
    private static bool Build(MethodInfo method, ObjectBase.ObjectType kind,
                              out object?[] arguments, out string note, out string why)
    {
        arguments = System.Array.Empty<object?>();
        note = "";
        why = "";

        ParameterInfo[] parameters = method.GetParameters();
        var built = new object?[parameters.Length];
        var notes = new List<string>();
        int enums = 0;

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            Type type = parameter.ParameterType;

            if (parameter.IsOut || type.IsByRef)
            {
                why = "the method takes a ref/out parameter";
                return false;
            }

            if (type == typeof(ObjectBase.ObjectType))
            {
                built[i] = kind;
                notes.Add(parameter.Name + "=" + kind);
                continue;
            }

            if (type.IsEnum)
            {
                // The first enum on a flight-deck call is the role, the second the sortie type.
                object value = Pick(type, enums == 0 ? RoleHints : MissionHints)
                               ?? Enum.ToObject(type, 0);
                enums++;
                built[i] = value;
                notes.Add(type.Name + "=" + value);
                continue;
            }

            if (type == typeof(bool)) { built[i] = true; continue; }
            if (type == typeof(float)) { built[i] = 0f; continue; }
            if (type == typeof(double)) { built[i] = 0.0; continue; }
            if (type == typeof(int)) { built[i] = 0; continue; }
            if (type == typeof(string)) { built[i] = ""; continue; }

            if (!type.IsValueType || Nullable.GetUnderlyingType(type) != null)
            {
                built[i] = null;   // an aircraft reference, or any other object: "whatever fits"
                continue;
            }

            why = "the flight-deck method wants a " + type.Name
                  + ", which this mod cannot fill without guessing";
            return false;
        }

        arguments = built;
        note = string.Join(", ", notes);
        return true;
    }

    /// <summary>The enum value whose name matches one of the fragments, or null.</summary>
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
