using System;
using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Civilian;

/// <summary>The numbers the user typed into the panel.</summary>
internal sealed class TrafficRequest
{
    internal int MinShips = 2;
    internal int MaxShips = 6;
    internal int MinPlanes;
    internal int MaxPlanes = 2;
    internal int MaxLanes = 4;
    internal int ClassesPerRoute = 6;
    internal string Label = "Zone traffic";
}

/// <summary>
/// Turns "a zone and four numbers" into civilian traffic.
///
/// One route is one traffic lane: it spawns from a single point and follows one course. Spreading
/// the requested count over several lanes is what makes a zone feel alive — a single lane delivers
/// every ship from the same spot on the same heading. Each lane also gets its own random subset of
/// civilian classes, so one lane can be container ships while another is fishing boats.
/// </summary>
internal static class TrafficGenerator
{
    private const int MaximumPerType = 200;
    private const int MaximumLanes = 6;

    private static readonly Random Random = new();

    /// <summary>Routes created since the mod started, so the panel can remove them again.</summary>
    internal static List<CivilianRoute> Generated { get; } = new();

    internal static List<CivilianRoute> Generate(TrafficZone zone, TrafficRequest request, out string report)
    {
        List<CivilianRoute> created = new();

        CivilianCatalogue.Refresh();

        MissionManager? manager = Singleton<MissionManager>.Instance;
        if (manager == null)
        {
            report = "The mission manager is not running yet — open or start a mission first.";
            return created;
        }

        if (!RouteFactory.HasTaskforce)
        {
            report = "There is no neutral taskforce yet — load a mission first, then try again.";
            return created;
        }

        // Where the routes live is looked for rather than assumed: in the mission editor the
        // mission manager's own list is often null, because the manager is what runs a mission and
        // the editor is holding the one being edited. See RouteContainers.
        System.Collections.IList? container = RouteContainers.Find(out string where);

        if (container == null)
        {
            report = "No civilian route list to add to - " + where
                     + ". Open a mission in the editor and try again; traffic-report.txt says "
                     + "exactly what was looked for.";
            ModLog.Warn("Civilian traffic: " + report);
            return created;
        }

        if (!RouteContainers.CanAdd(container, out string why))
        {
            report = "Found the civilian route list (" + where + ") but it cannot be added to: " + why;
            ModLog.Warn("Civilian traffic: " + report);
            return created;
        }

        ModLog.Info("Civilian traffic: adding routes to " + where);

        List<CivilianRoute>? routes = container as List<CivilianRoute>;

        if (routes == null)
        {
            report = "The civilian route list (" + where + ") is not a list of routes the mod can "
                     + "write to.";
            ModLog.Warn("Civilian traffic: " + report);
            return created;
        }

        int minShips = Clamp(request.MinShips);
        int maxShips = Clamp(request.MaxShips);
        if (minShips > maxShips) (minShips, maxShips) = (maxShips, minShips);

        int minPlanes = Clamp(request.MinPlanes);
        int maxPlanes = Clamp(request.MaxPlanes);
        if (minPlanes > maxPlanes) (minPlanes, maxPlanes) = (maxPlanes, minPlanes);

        int shipCount = Roll(minShips, maxShips);
        int planeCount = Roll(minPlanes, maxPlanes);

        List<string> problems = new();
        int nextIndex = NextSectionIndex(routes);

        int shipRoutes = shipCount > 0
            ? BuildRoutes(zone, request, ObjectBase.ObjectType.Vessel, "ship", CivilianCatalogue.Ships, shipCount,
                          routes, created, ref nextIndex, problems)
            : 0;

        int planeRoutes = planeCount > 0
            ? BuildRoutes(zone, request, ObjectBase.ObjectType.Aircraft, "aircraft", CivilianCatalogue.Planes, planeCount,
                          routes, created, ref nextIndex, problems)
            : 0;

        foreach (CivilianRoute route in created) Generated.Add(route);

        // _maxUnits is a concurrency limit, not a total: the route keeps topping up as units finish
        // their course, so the zone keeps flowing instead of emptying out.
        report = "Added " + shipRoutes + " ship lane(s) (up to " + shipCount + " vessel(s) at a time) and "
                 + planeRoutes + " aircraft lane(s) (up to " + planeCount + " aircraft at a time) to "
                 + where + ". Save the mission to keep them.";

        if (shipCount > 0 && CivilianCatalogue.Ships.Count == 0) report += " No civilian vessel classes found.";
        if (planeCount > 0 && CivilianCatalogue.Planes.Count == 0) report += " No civilian aircraft classes found.";
        if (problems.Count > 0) report += " Problems: " + string.Join("; ", problems);

        ModLog.Info("Civilian traffic: " + report);
        TrafficDiagnostics.Write(report);

        return created;
    }

    /// <summary>Removes every route this mod added, back out of the mission.</summary>
    internal static int RemoveGenerated()
    {
        int removed = 0;

        System.Collections.IList? container = RouteContainers.Find(out _);
        List<CivilianRoute>? routes = container as List<CivilianRoute>;

        if (routes != null)
        {
            foreach (CivilianRoute route in Generated)
            {
                if (routes.Remove(route)) removed++;
            }
        }

        Generated.Clear();
        ModLog.Info("Civilian traffic: removed " + removed + " generated route(s).");
        return removed;
    }

    private static int BuildRoutes(
        TrafficZone zone,
        TrafficRequest request,
        ObjectBase.ObjectType type,
        string label,
        IReadOnlyList<string> catalogue,
        int unitCount,
        List<CivilianRoute> destination,
        List<CivilianRoute> created,
        ref int nextIndex,
        List<string> problems)
    {
        if (unitCount <= 0) return 0;
        if (catalogue.Count == 0)
        {
            problems.Add("no civilian " + label + " classes in this install");
            return 0;
        }

        int lanes = LaneCount(unitCount, request);
        int createdRoutes = 0;
        int remaining = unitCount;

        for (int lane = 0; lane < lanes && remaining > 0; lane++)
        {
            int left = lanes - lane;
            int units = Math.Max(1, remaining / left);
            remaining -= units;

            RouteSpec spec = new()
            {
                Name = request.Label + " " + label + " " + (lane + 1),
                Type = type,
                Count = units,
                GroupSize = units >= 8 && Random.NextDouble() < 0.4 ? 2 : 1,
                AllowedUnits = PickClasses(catalogue, request.ClassesPerRoute),
                Waypoints = zone.RandomPath(Random, 2 + Random.Next(3))
            };

            // Stagger the lanes so traffic does not appear all at once.
            spec.IntervalMin = 15f + (float)Random.NextDouble() * 35f;
            spec.IntervalMax = spec.IntervalMin + 20f + (float)Random.NextDouble() * 60f;

            string section = "CivilianRoute" + nextIndex;
            CivilianRoute? route = RouteFactory.Create(spec, section, nextIndex, out string error);

            if (route == null)
            {
                problems.Add(section + " failed: " + error);
                nextIndex++;
                continue;
            }

            nextIndex++;
            destination.Add(route);
            created.Add(route);
            createdRoutes++;
        }

        return createdRoutes;
    }

    private static int LaneCount(int units, TrafficRequest request)
    {
        int lanes = units <= 2 ? 1 : units <= 5 ? 2 : 1 + units / 6;
        lanes = Math.Max(1, Math.Min(lanes, Math.Min(MaximumLanes, Math.Max(1, request.MaxLanes))));
        return Math.Min(lanes, units);
    }

    /// <summary>A random contiguous slice of the catalogue, so lanes differ from each other.</summary>
    private static List<string> PickClasses(IReadOnlyList<string> catalogue, int wanted)
    {
        int size = Math.Max(1, Math.Min(wanted, catalogue.Count));

        int start = catalogue.Count > size ? Random.Next(catalogue.Count - size + 1) : 0;

        List<string> chosen = new(size);
        for (int i = 0; i < size; i++) chosen.Add(catalogue[(start + i) % catalogue.Count]);
        return chosen;
    }

    /// <summary>
    /// Route sections have to stay dense and in list order, because the loader rebuilds the names
    /// as CivilianRoute1, CivilianRoute2, … for however many the mission declares.
    /// </summary>
    private static int NextSectionIndex(List<CivilianRoute> routes)
    {
        int highest = 0;

        foreach (CivilianRoute route in routes)
        {
            string? section = RouteFactory.ReadString(route, "_section");
            if (string.IsNullOrEmpty(section)) continue;

            int index = 0;
            int i = 0;
            while (i < section.Length && !char.IsDigit(section[i])) i++;
            while (i < section.Length && char.IsDigit(section[i]))
            {
                index = (index * 10) + (section[i] - '0');
                i++;
            }

            highest = Math.Max(highest, index);
        }

        return highest + 1;
    }

    private static int Roll(int minimum, int maximum)
        => minimum >= maximum ? minimum : Random.Next(minimum, maximum + 1);

    private static int Clamp(int value) => Math.Max(0, Math.Min(MaximumPerType, value));
}
