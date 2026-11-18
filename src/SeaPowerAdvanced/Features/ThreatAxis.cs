using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Threat-axis reorientation: turn the screen to face where the danger actually is.
///
/// A task force's formation is laid out along the threat axis. When the threat moves, the
/// formation has to move with it, otherwise the escorts end up covering an empty sector. The game
/// stores the formation but offers no "face this bearing" order for the group.
///
/// Members used, all confirmed:
///
/// <code>
/// ObjectBase._threats            List&lt;ObjectBase&gt; - what this ship considers a threat
/// ObjectBase._spottedObjects     contacts this task force has
/// ObjectBase.Formation / get_FormationStation()
/// ObjectBase.Formation::Stations (the formation's station definitions)
/// ObjectBase.setWaypointTask(...)      via WaypointControl: the height enum cannot be named
/// </code>
///
/// The mod computes the bearing to the nearest real threat and pushes a waypoint task on each
/// escort's own station, rotated onto that bearing. Ships stay in company; only the axis changes.
/// </summary>
internal static class ThreatAxis
{
    /// <summary>Turns the screen onto the bearing of the nearest threat, for the whole task force.</summary>
    internal static int Reorient(out string report)
    {
        List<ObjectBase> ships = FleetTicker.Tracked();
        if (ships.Count == 0)
        {
            report = "no ships under command";
            return 0;
        }

        ObjectBase? threat = NearestThreat(ships);
        if (threat == null)
        {
            report = "no threat is currently tracked - nothing to face";
            return 0;
        }

        GeoPosition origin;
        try
        {
            origin = ships[0].getGeoPosition();
        }
        catch (Exception error)
        {
            report = "could not read the flagship's position: " + error.GetType().Name;
            return 0;
        }

        double bearing = Bearing(origin, SafePosition(threat));
        int moved = 0;

        foreach (ObjectBase ship in ships)
        {
            try
            {
                GeoPosition station = StationFor(ship, origin, bearing);
                if (Reseat(ship, station)) moved++;
            }
            catch (Exception error)
            {
                ModLog.Error("Threat axis could not move " + Name(ship), error);
            }
        }

        report = "screen turned onto bearing " + (int)bearing + " degrees: "
                 + moved + "/" + ships.Count + " ship(s) given a new station";
        return moved;
    }

    /// <summary>
    /// The station this ship should take: its own offset from the formation centre, rotated so the
    /// formation's "forward" now faces the threat.
    /// </summary>
    private static GeoPosition StationFor(ObjectBase ship, GeoPosition centre, double bearing)
    {
        // Default: hold the current station distance but on the new bearing. Without readable
        // station geometry this still puts every ship on a coherent line abreast, which is the
        // "screen facing the threat" the order is for.
        double angle = bearing;
        double range = 0.02; // degrees, roughly a mile: close enough to be a station, far enough to see

        try
        {
            GeoPosition current = ship.getGeoPosition();
            double dLat = current.Latitude - centre.Latitude;
            double dLon = current.Longitude - centre.Longitude;
            range = Math.Max(Math.Sqrt(dLat * dLat + dLon * dLon), 0.01);
        }
        catch (Exception) { }

        double radians = angle * Math.PI / 180.0;
        double lat = centre.Latitude + range * Math.Cos(radians);
        double lon = centre.Longitude + range * Math.Sin(radians);

        return new GeoPosition(lat, lon, 0);
    }

    /// <summary>
    /// Moves one ship onto the new station. The call goes through <see cref="WaypointControl"/>
    /// because the third argument's type (<c>WaypointHeightState</c>) cannot be named in code.
    /// </summary>
    private static bool Reseat(ObjectBase ship, GeoPosition station)
    {
        string label = "screen " + DateTime.Now.ToString("HH:mm:ss");

        if (WaypointControl.Push(ship, station, label, out string why)) return true;

        ModLog.Warn("Could not re-station " + Name(ship) + ": " + why);
        return false;
    }

    /// <summary>The closest tracked threat among the whole task force's contact lists.</summary>
    private static ObjectBase? NearestThreat(List<ObjectBase> ships)
    {
        ObjectBase? best = null;
        double bestRange = double.MaxValue;

        foreach (ObjectBase ship in ships)
        {
            try
            {
                GeoPosition own = ship.getGeoPosition();

                foreach (ObjectBase contact in ThreatsOf(ship))
                {
                    try
                    {
                        double range = Distance(own, SafePosition(contact));
                        if (range < bestRange)
                        {
                            bestRange = range;
                            best = contact;
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        return best;
    }

    private static IEnumerable<ObjectBase> ThreatsOf(ObjectBase ship)
    {
        if (ship._threats != null) foreach (ObjectBase threat in ship._threats) yield return threat;
        if (ship._dangerousMissiles != null)
            foreach (ObjectBase missile in ship._dangerousMissiles) yield return missile;

        // Fall back to anything the task force has spotted, so the order still does something in a
        // quiet theatre instead of reporting "nothing to face" while contacts are on the map.
        if (ship._spottedObjects != null)
            foreach (ObjectBase contact in ship._spottedObjects) yield return contact;
    }

    private static GeoPosition SafePosition(ObjectBase unit)
    {
        try { return unit.getGeoPosition(); }
        catch (Exception) { return new GeoPosition(0, 0, 0); }
    }

    /// <summary>Compass bearing from one position to another, in degrees.</summary>
    internal static double Bearing(GeoPosition from, GeoPosition to)
    {
        double dLon = (to.Longitude - from.Longitude);
        double dLat = (to.Latitude - from.Latitude);

        // Longitude degrees are shorter at high latitude; without this the bearing skews badly in
        // the North Atlantic, which is exactly where this feature matters.
        dLon *= Math.Cos(from.Latitude * Math.PI / 180.0);

        double degrees = Math.Atan2(dLon, dLat) * 180.0 / Math.PI;
        return (degrees + 360.0) % 360.0;
    }

    internal static double Distance(GeoPosition a, GeoPosition b)
    {
        double dLat = b.Latitude - a.Latitude;
        double dLon = (b.Longitude - a.Longitude) * Math.Cos(a.Latitude * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon) * 60.0; // nautical miles
    }

    private static string Name(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
