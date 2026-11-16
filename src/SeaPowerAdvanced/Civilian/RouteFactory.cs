using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Civilian;

/// <summary>What one generated route should be.</summary>
internal sealed class RouteSpec
{
    internal string Name = "Civilian traffic";
    internal ObjectBase.ObjectType Type = ObjectBase.ObjectType.Vessel;
    internal List<string> AllowedUnits = new();
    internal int Count = 1;
    internal int GroupSize = 1;
    internal float IntervalMin = 30f;
    internal float IntervalMax = 90f;
    internal List<GeoPosition> Waypoints = new();
}

/// <summary>
/// Builds <see cref="CivilianRoute"/> objects the way <c>SceneCreator.LoadCivilianRoutes</c> would.
///
/// <see cref="CivilianRoute"/> keeps everything in private fields, so the values are written by
/// reflection. Two rules make this survive a game patch:
///
/// <list type="number">
/// <item><b>Four fields are essential</b> - the waypoints, the unit type, the allowed unit list and
///   the concurrency cap. Without those a route is not a route, and it is refused with the reason
///   named.</item>
/// <item><b>Everything else is best effort.</b> The full field list runs to forty names, and it used
///   to be all-or-nothing: one renamed field - a diagnostic counter, a loadout list - threw away the
///   whole route and the feature reported nothing but "Problems: no field _something". A route
///   missing an optional field still spawns traffic; the names that were skipped are recorded in
///   <see cref="Skipped"/> and written to <c>traffic-report.txt</c> so a patch can be followed
///   exactly.</item>
/// </list>
/// </summary>
internal static class RouteFactory
{
    private static readonly Dictionary<string, FieldInfo?> Fields = new();

    /// <summary>Optional fields this build does not have, cleared at the start of every route.</summary>
    internal static readonly List<string> Skipped = new();

    /// <summary>
    /// The side civilian traffic belongs to. <c>Globals._neutralTaskforce</c> is the documented one;
    /// if this build leaves it null in the mission editor, any other neutral taskforce in the globals
    /// is used instead, and the report says which.
    /// </summary>
    internal static string TaskforceSource { get; private set; } = "not looked for yet";

    internal static bool HasTaskforce => ResolveTaskforce(out _) != null;

    internal static object? ResolveTaskforce(out string source)
    {
        source = TaskforceSource;

        try
        {
            if (Globals._neutralTaskforce != null)
            {
                source = "Globals._neutralTaskforce";
                TaskforceSource = source;
                return Globals._neutralTaskforce;
            }
        }
        catch (Exception) { }

        // Not set (the mission editor often has not assigned it yet). Look for any taskforce in the
        // globals whose own name says neutral, rather than giving up: a route with no side is a
        // route that spawns nothing.
        try
        {
            // Resolved by name rather than by naming the type: Globals is not in every build's
            // public surface, and a typeof() on a type the game has moved is a compile error, not a
            // logged miss.
            Type? globals = typeof(CivilianRoute).Assembly.GetType("Globals")
                            ?? typeof(CivilianRoute).Assembly.GetType("SeaPower.Globals");

            if (globals == null)
            {
                source = "no Globals type in this build";
                TaskforceSource = source;
                return null;
            }

            foreach (MemberInfo member in globals.GetMembers(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (member is MethodInfo || member is Type) continue;

                string name = member.Name.ToLowerInvariant();
                if (!name.Contains("taskforce") && !name.Contains("task_force")) continue;
                if (name.Contains("enemy") || name.Contains("player")) continue;

                object? value = member is PropertyInfo property ? property.GetValue(null)
                               : ((FieldInfo)member).GetValue(null);

                if (value == null) continue;

                string label = value.ToString() ?? "";
                if (!name.Contains("neutral") && label.IndexOf("neutral", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                source = "Globals." + member.Name;
                TaskforceSource = source;
                return value;
            }
        }
        catch (Exception error)
        {
            ModLog.Warn("Civilian routes: could not look for a neutral taskforce - " + error.Message);
        }

        source = "none found";
        TaskforceSource = source;
        return null;
    }

    internal static CivilianRoute? Create(RouteSpec spec, string section, int index, out string error)
    {
        error = string.Empty;
        Skipped.Clear();

        if (spec.Waypoints.Count == 0)
        {
            error = "no waypoints";
            return null;
        }

        if (spec.AllowedUnits.Count == 0)
        {
            error = "no allowed units";
            return null;
        }

        CivilianRoute route = new();
        GeoPosition spawn = spec.Waypoints[0];

        List<string> essential = new();
        List<string> skipped = new();

        void Required(string field, object? value)
        {
            string message = Set(route, field, value);
            if (message.Length != 0) essential.Add(message);
        }

        void Optional(string field, object? value)
        {
            string message = Set(route, field, value);
            if (message.Length != 0) skipped.Add(field);
        }

        // --- the four that make it a route -----------------------------------------------------
        Required("_unitsType", spec.Type);
        Required("_allowedUnits", spec.AllowedUnits);
        Required("_maxUnits", spec.Count);
        Required("_waypoints", spec.Waypoints);

        if (essential.Count > 0)
        {
            error = "this build's CivilianRoute is missing " + string.Join(", ", essential);
            Skipped.AddRange(skipped);
            return null;
        }

        // --- identity ---------------------------------------------------------------------------
        Optional("_index", index);
        Optional("_section", section);
        Optional("_name", spec.Name);
        Optional("_description", string.Empty);
        Optional("_operationKey", string.Empty);
        Optional("_squadronName", string.Empty);

        // --- what it may spawn ------------------------------------------------------------------
        Optional("_allowedUnitsData", string.Join(",", spec.AllowedUnits));
        Optional("_allowedNations", new List<string> { "all" });
        Optional("_allowedNationsData", "all");
        Optional("_allowedLoadouts", new List<string> { "all" });
        Optional("_allowedLoadoutsData", "all");

        // --- how much and how often -------------------------------------------------------------
        Optional("_initialMaxUnits", spec.Count);
        Optional("_spawnGroupSize", spec.GroupSize);
        Optional("_spawnInterval", new UnityEngine.Vector2(spec.IntervalMin, spec.IntervalMax));
        Optional("_currentSpawnInterval", spec.IntervalMin);

        Optional("_startDelay", 0f);
        Optional("_startTime", 0d);
        Optional("_lastSpawnTime", 0d);

        Optional("_repeatable", true);
        Optional("_disabled", false);
        Optional("_initiallyDisabled", false);
        Optional("_completed", false);
        Optional("_radarsActive", false);
        Optional("_requiresHomeBase", false);
        Optional("_requiresDestinationObj", false);
        Optional("_isEditorAuthored", true);
        Optional("_enableWeaponsAtWaypoint", -1);

        Optional("_failedSpawnCounter", 0);
        Optional("_fdUnitsToLaunch", 0);
        Optional("_activeUnits", new List<ObjectBase>());

        Optional("_waypointDefinitions", new List<WaypointData>());
        Optional("_waypointsData", string.Empty);
        Optional("_spawnGeoPosition", spawn);

        // Same as the game's loader: the heading lives in .y. The loader points it at the spawn
        // position itself (a zero heading); pointing at the next waypoint makes the traffic start
        // out along its route instead of turning first.
        UnityEngine.Vector3 heading = UnityEngine.Vector3.zero;
        if (spec.Waypoints.Count > 1)
        {
            try { heading.y = spawn.HeadingRangeMeters(spec.Waypoints[1]).Item1; }
            catch (Exception) { heading.y = 0f; }
        }
        Optional("_spawnHeading", heading);

        // The side. A null here is not fatal to the route object itself, but it is the reason traffic
        // would never appear, so it is said out loud rather than silently written as null.
        object? taskforce = ResolveTaskforce(out string source);
        Optional("_spawnedUnitTaskforce", taskforce);

        if (taskforce == null)
            skipped.Add("_spawnedUnitTaskforce (no neutral taskforce: " + source + ")");

        Skipped.AddRange(skipped);
        return route;
    }

    private static string Set(object target, string name, object? value)
    {
        FieldInfo? field = Find(name);
        if (field == null) return "no field " + name;

        try
        {
            field.SetValue(target, value);
            return string.Empty;
        }
        catch (Exception error)
        {
            return name + " (" + error.GetType().Name + ": " + error.Message + ")";
        }
    }

    /// <summary>Reads a private string field, for diagnostics and section numbering.</summary>
    internal static string? ReadString(CivilianRoute route, string name)
    {
        try { return Find(name)?.GetValue(route) as string; }
        catch (Exception) { return null; }
    }

    private static FieldInfo? Find(string name)
    {
        if (Fields.TryGetValue(name, out FieldInfo? cached)) return cached;

        FieldInfo? field = null;
        try
        {
            field = typeof(CivilianRoute).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
        catch (Exception error)
        {
            ModLog.Error("Reflection lookup for CivilianRoute." + name + " failed", error);
        }

        Fields[name] = field;
        return field;
    }
}
