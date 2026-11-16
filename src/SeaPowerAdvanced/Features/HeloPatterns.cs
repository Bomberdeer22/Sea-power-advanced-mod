using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// One-click ASW search patterns for a helicopter: ladder, expanding square, sector.
///
/// This is pure staff work. A dipping helicopter's job is to fly a pattern that covers water
/// systematically instead of wandering, and writing the waypoints by hand on the map is exactly the
/// tedium the game leaves to the player.
///
/// Members used, all confirmed in the shipped assembly:
///
/// <code>
/// ObjectBase.FlightDeckHasUnit(ObjectType, UnitRoles)      is a helo on deck?
/// ObjectBase.FlightDeckLaunchUnit(ObjectType, string, UnitRoles, MissionType, bool, float, bool)
/// ObjectBase._childAircraft                                what this ship has airborne
/// ObjectBase.setWaypointTask(GeoPosition, string, WaypointHeightState)
/// </code>
///
/// The mod launches nothing the player did not ask for: it flies the pattern for a helicopter that
/// is already on deck or already airborne.
/// </summary>
internal enum SearchPattern
{
    Ladder,
    ExpandingSquare,
    Sector,
}

internal static class HeloPatterns
{
    /// <summary>Flies the chosen pattern from the ship's position, or reports why it cannot.</summary>
    internal static bool Fly(ObjectBase ship, SearchPattern pattern, out string report)
    {
        if (ship == null)
        {
            report = "no ship";
            return false;
        }

        ObjectBase? helo = FindHelicopter(ship);

        if (helo == null)
        {
            if (!LaunchHelicopter(ship, out string why))
            {
                report = why;
                return false;
            }

            // Launching is asynchronous: the aircraft appears as a child a moment later, so the
            // pattern is queued and flown by the ticker rather than pretended to have happened.
            Queued[ship] = pattern;
            report = "helicopter launching; the " + pattern + " pattern will be flown as it clears the deck";
            return true;
        }

        return FlyPattern(ship, helo, pattern, out report);
    }

    private static readonly Dictionary<ObjectBase, SearchPattern> Queued = new();

    /// <summary>Flights queued behind a launch are started here once the aircraft exists.</summary>
    internal static void Tick()
    {
        if (Queued.Count == 0) return;

        foreach (KeyValuePair<ObjectBase, SearchPattern> entry in new List<KeyValuePair<ObjectBase, SearchPattern>>(Queued))
        {
            try
            {
                ObjectBase? helo = FindHelicopter(entry.Key);
                if (helo == null) continue;

                Queued.Remove(entry.Key);
                FlyPattern(entry.Key, helo, entry.Value, out string report);
                ModLog.Info("Helo pattern: " + report);
            }
            catch (Exception error)
            {
                Queued.Remove(entry.Key);
                ModLog.Error("Helo pattern failed after a launch", error);
            }
        }
    }

    private static bool FlyPattern(ObjectBase ship, ObjectBase helo, SearchPattern pattern, out string report)
    {
        GeoPosition origin;
        try { origin = ship.getGeoPosition(); }
        catch (Exception error)
        {
            report = "could not read the ship's position: " + error.GetType().Name;
            return false;
        }

        List<GeoPosition> points = Build(origin, pattern);
        int pushed = 0;

        foreach (GeoPosition point in points)
        {
            try
            {
                helo.setWaypointTask(point, pattern.ToString(), default(WaypointHeightState));
                pushed++;
            }
            catch (Exception error)
            {
                ModLog.Error("Could not push a search waypoint", error);
                break;
            }
        }

        report = pushed + " waypoint(s) pushed for a " + pattern + " search from " + Round(origin);
        return pushed > 0;
    }

    private static List<GeoPosition> Build(GeoPosition origin, SearchPattern pattern)
    {
        var points = new List<GeoPosition>();
        const double leg = 0.06;   // ~3.5 nm per leg
        const double spacing = 0.03; // ~1.8 nm between legs

        switch (pattern)
        {
            case SearchPattern.Ladder:
                for (int i = 0; i < 4; i++)
                {
                    double east = spacing * i;
                    points.Add(Offset(origin, leg, east));
                    points.Add(Offset(origin, -leg, east));
                }
                break;

            case SearchPattern.ExpandingSquare:
                double step = leg;
                points.Add(Offset(origin, step, 0));
                points.Add(Offset(origin, step, step));
                points.Add(Offset(origin, 0, step));
                points.Add(Offset(origin, -step, step));
                points.Add(Offset(origin, -step, 0));
                points.Add(Offset(origin, -step, -step));
                points.Add(Offset(origin, 0, -step));
                points.Add(Offset(origin, step, -step));
                break;

            case SearchPattern.Sector:
                for (int i = -2; i <= 2; i++)
                    points.Add(Offset(origin, leg * 1.5, spacing * i));
                break;
        }

        return points;
    }

    /// <summary>North/east offset in degrees, longitude corrected for latitude.</summary>
    private static GeoPosition Offset(GeoPosition origin, double northDegrees, double eastDegrees)
    {
        double cos = Math.Max(Math.Cos(origin.Latitude * Math.PI / 180.0), 0.1);
        return new GeoPosition(
            origin.Latitude + northDegrees,
            origin.Longitude + eastDegrees / cos,
            0);
    }

    private static ObjectBase? FindHelicopter(ObjectBase ship)
    {
        try
        {
            if (ship._childAircraft == null) return null;

            foreach (ObjectBase child in ship._childAircraft)
            {
                try { if (child._type == ObjectBase.ObjectType.Helicopter) return child; }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>
    /// Launches a helicopter through the game's own flight deck API.
    ///
    /// The two enum arguments (<c>UnitRoles</c>, <c>MissionType</c>) are resolved by name at
    /// runtime rather than written into the code. Their members were not part of the API dump, and
    /// guessing an enum value is how a feature ends up launching the wrong thing.
    /// </summary>
    private static bool LaunchHelicopter(ObjectBase ship, out string why)
    {
        try
        {
            object? aswRole = EnumValue(typeof(UnitRoles), "asw", "antisub", "anti");
            object? patrol = EnumValue(typeof(MissionType), "patrol", "asw", "search");

            if (aswRole == null)
            {
                why = "this build has no ASW role in its UnitRoles enum";
                return false;
            }

            if (!ship.FlightDeckHasUnit(ObjectBase.ObjectType.Helicopter, (UnitRoles)aswRole))
            {
                why = "no ASW helicopter is on deck - this ship has nothing to fly";
                return false;
            }

            bool launched = ship.FlightDeckLaunchUnit(
                ObjectBase.ObjectType.Helicopter,
                null!,                            // any airframe that fits
                (UnitRoles)aswRole,
                patrol == null ? default : (MissionType)patrol,
                true,
                0f,
                false);

            why = launched
                ? "launching"
                : "the game refused the launch (deck busy, or no airframe fits)";
            return launched;
        }
        catch (Exception error)
        {
            why = "could not launch: " + error.GetType().Name;
            return false;
        }
    }

    /// <summary>The first enum value whose name contains one of the fragments, or null.</summary>
    private static object? EnumValue(Type type, params string[] fragments)
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

    private static string Round(GeoPosition position) =>
        position.Latitude.ToString("0.00") + ", " + position.Longitude.ToString("0.00");
}
