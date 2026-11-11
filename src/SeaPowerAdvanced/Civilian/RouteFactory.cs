using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using UnityEngine;

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
/// <see cref="CivilianRoute"/> keeps everything in private fields and has no public setters, so the
/// values are written by reflection. Every field name below was read out of the shipped
/// <c>Seapower-Scripts.dll</c>; if a game patch renames one, <see cref="Set"/> records the failure
/// and the route is discarded instead of producing a half-initialised traffic route.
/// </summary>
internal static class RouteFactory
{
    private static readonly Dictionary<string, FieldInfo?> Fields = new();

    /// <summary>True once the neutral taskforce was resolvable; civilian traffic needs a side.</summary>
    internal static bool HasTaskforce => Globals._neutralTaskforce != null;

    internal static CivilianRoute? Create(RouteSpec spec, string section, int index, out string error)
    {
        error = string.Empty;

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

        List<string> failed = new();

        void Try(string field, object? value)
        {
            string message = Set(route, field, value);
            if (message.Length != 0) failed.Add(message);
        }

        Try("_index", index);
        Try("_section", section);
        Try("_name", spec.Name);
        Try("_description", string.Empty);
        Try("_operationKey", string.Empty);
        Try("_squadronName", string.Empty);

        Try("_unitsType", spec.Type);
        Try("_allowedUnits", spec.AllowedUnits);
        Try("_allowedUnitsData", string.Join(",", spec.AllowedUnits));
        // "all" is what the game's own mission files contain by default, so this follows the path
        // authored civilian traffic already takes.
        Try("_allowedNations", new List<string> { "all" });
        Try("_allowedNationsData", "all");
        Try("_allowedLoadouts", new List<string> { "all" });
        Try("_allowedLoadoutsData", "all");

        Try("_maxUnits", spec.Count);
        Try("_initialMaxUnits", spec.Count);
        Try("_spawnGroupSize", spec.GroupSize);
        Try("_spawnInterval", new Vector2(spec.IntervalMin, spec.IntervalMax));
        Try("_currentSpawnInterval", spec.IntervalMin);

        Try("_startDelay", 0f);
        Try("_startTime", 0d);
        Try("_lastSpawnTime", 0d);

        Try("_repeatable", true);
        Try("_disabled", false);
        Try("_initiallyDisabled", false);
        Try("_completed", false);
        Try("_radarsActive", false);
        Try("_requiresHomeBase", false);
        Try("_requiresDestinationObj", false);
        Try("_isEditorAuthored", true);

        Try("_enableWeaponsAtWaypoint", -1);
        Try("_failedSpawnCounter", 0);
        Try("_fdUnitsToLaunch", 0);
        Try("_activeUnits", new List<ObjectBase>());

        Try("_waypoints", spec.Waypoints);
        Try("_waypointDefinitions", new List<WaypointData>());
        Try("_waypointsData", string.Empty);
        Try("_spawnGeoPosition", spawn);

        // Same as the game's loader: the heading lives in .y. The loader points it at the spawn
        // position itself (a zero heading); pointing at the next waypoint makes the traffic start
        // out along its route instead of turning first.
        Vector3 heading = Vector3.zero;
        if (spec.Waypoints.Count > 1)
        {
            try
            {
                heading.y = spawn.HeadingRangeMeters(spec.Waypoints[1]).Item1;
            }
            catch (Exception)
            {
                heading.y = 0f;
            }
        }
        Try("_spawnHeading", heading);

        Try("_spawnedUnitTaskforce", Globals._neutralTaskforce);

        if (failed.Count > 0)
        {
            error = string.Join("; ", failed);
            return null;
        }

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
        try
        {
            return Find(name)?.GetValue(route) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FieldInfo? Find(string name)
    {
        if (Fields.TryGetValue(name, out FieldInfo? cached)) return cached;

        FieldInfo? field = null;
        try
        {
            field = typeof(CivilianRoute).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
        catch (Exception error)
        {
            ModLog.Error("Reflection lookup for CivilianRoute." + name + " failed", error);
        }

        Fields[name] = field;
        return field;
    }
}
