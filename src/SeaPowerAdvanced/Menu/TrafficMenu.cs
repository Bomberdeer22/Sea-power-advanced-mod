using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Civilian;
using SeapowerUI;
using SeapowerUI.ViewModels;
using UniRx;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// Civilian traffic as part of the game's own right-click menu.
///
/// This is the way in, and the reason is simple: the floating panel drew, but the game's own interface
/// layer swallowed the mouse, so none of its buttons could be clicked. The context menu is the game's
/// own control - it works, it is themed, it is where every other feature in this mod lives, and the
/// player is already using it while placing units in the mission editor.
///
/// Everything the panel could do is here, as menu entries, and nothing needs a text field: the counts
/// cycle through presets. Every action reports what happened twice over - in the mod's log, and on
/// screen through the game's own order feedback (<c>ShowOrderFeedback</c>), which is the same channel
/// the ship orders use.
///
/// The submenu also carries its own status line, so opening it again shows the zone, the counts, and
/// the outcome of the last attempt without reading a file.
/// </summary>
internal static class TrafficMenu
{
    /// <summary>The counts offered, as (minimum, maximum) at a time. Cycling beats typing in a menu:
    /// a right-click menu has no text field, and asking for a keyboard would put the feature back
    /// where it was - fighting the game for input.</summary>
    private static readonly (int Min, int Max)[] ShipChoices =
    {
        (0, 0), (1, 2), (2, 4), (2, 6), (4, 10), (8, 20), (16, 40),
    };

    private static readonly (int Min, int Max)[] PlaneChoices =
    {
        (0, 0), (1, 2), (2, 4), (4, 8), (8, 16),
    };

    /// <summary>The zone built by "use the map area", used when no polygon has been drawn.</summary>
    private static List<GeoPosition>? mapArea;

    /// <summary>
    /// The last thing that happened, shown as the first line of the submenu. A menu cannot pop up a
    /// message of its own, so the result has to be somewhere the player will look - and this is it.
    /// </summary>
    private static string lastResult = "nothing spawned yet this session";

    /// <summary>
    /// Adds the section to one menu. Returns the number of entries written, so the caller can tell
    /// whether anything was added.
    /// </summary>
    internal static int Install(ObjectBase? unit, List<ContextMenuItem> roots, object live)
    {
        // Only in the mission editor. Traffic is written into the mission being edited, so the entry
        // would be a dead end in a running battle - and a context menu full of things that cannot
        // work is worse than a short one.
        if (!InEditor()) return 0;

        var items = new List<ContextMenuItem>();

        // --- where the traffic goes -------------------------------------------------------------
        items.Add(Disabled("Zone: " + ZoneText(),
            "The area traffic is placed in. Either draw one on the map, or use the sea your own "
            + "units are already in."));

        if (ZoneTool.IsDrawing)
        {
            items.Add(Action("Finish the zone",
                "Stops drawing and keeps the points clicked so far.",
                () => Say(unit, "zone", ZoneTool.Finish())));
        }
        else
        {
            items.Add(Action("Draw a zone on the map",
                "Hands the mouse to the game's polygon tool: left-click points, middle-click or "
                + "'Finish the zone' when done.",
                () =>
                {
                    string problem = ZoneTool.Begin();

                    Say(unit, "zone", problem.Length == 0
                        ? "Click points on the map, then 'Finish the zone'."
                        : problem);
                }));
        }

        items.Add(Action("Use the map area around your units",
            "Builds the zone from the sea the mission's units are already in - no drawing needed.",
            () => UseMapArea(unit)));

        items.Add(Action("Clear the zone", "Forgets the drawn zone and the map area.",
            () =>
            {
                ZoneTool.ClearPolygon();
                mapArea = null;
                Say(unit, "zone", "Zone cleared.");
            }));

        // --- how much ----------------------------------------------------------------------------
        (int shipMin, int shipMax) = Ships();
        items.Add(Action("Ships at a time: " + Range(shipMin, shipMax) + " - click to change",
            "Cycles through the amounts: none, a couple, a busy lane. 'At a time' is a concurrency "
            + "cap - the lane keeps topping itself up as ships leave.",
            () => CycleShips(unit)));

        (int planeMin, int planeMax) = Planes();
        items.Add(Action("Aircraft at a time: " + Range(planeMin, planeMax) + " - click to change",
            "Cycles through the amounts. Aircraft cost more of the game's attention than ships, so "
            + "few of them is usually enough.",
            () => CyclePlanes(unit)));

        // --- do it -------------------------------------------------------------------------------
        items.Add(Action("Spawn traffic now",
            "Creates civilian routes in the zone. They are saved with the mission.",
            () => Spawn(unit)));

        items.Add(Action("Remove generated traffic",
            "Takes back the routes this session created, so you can roll a different set.",
            () =>
            {
                int removed = TrafficGenerator.RemoveGenerated();
                Say(unit, "remove", removed == 0
                    ? "Nothing to remove - no routes were generated this session."
                    : "Removed " + removed + " generated route(s).");
            }));

        // --- when it does not work ---------------------------------------------------------------
        items.Add(Disabled("Last: " + lastResult, "The outcome of the last action in this menu."));

        items.Add(Action("Check setup - writes traffic-report.txt",
            "Writes a checklist to the mod folder: every prerequisite marked ok or --, and the exact "
            + "type of the civilian route list it found.",
            () =>
            {
                TrafficDiagnostics.Write(lastResult);
                Say(unit, "check setup", "Wrote traffic-report.txt into the mod folder.");
            }));

        var section = new ContextMenuItem(
            label: "Civilian traffic",
            items: new TrulyObservableCollection<ContextMenuItem>(items),
            tooltip: "Put civilian shipping and aircraft into this mission.");

        // Top level, not nested inside Orders or Sensors: this is a mission-building tool, not an
        // order to a ship, and burying it would hide the one thing that works.
        string where = MenuRouting.Place(roots, live, section, Array.Empty<string>());
        MenuRouting.Placements.Add("Civilian traffic  ->  " + where);

        return items.Count;
    }

    private static bool editorSeen;

    /// <summary>Whether the mission editor is open, logged once either way so the menu appearing or
    /// not appearing is answerable from the log.</summary>
    private static bool InEditor()
    {
        try
        {
            if (MissionEditorViewModel.Instance != null)
            {
                if (!editorSeen)
                {
                    editorSeen = true;
                    ModLog.Info("Civilian traffic: the mission editor is open, so the traffic menu is "
                                + "in every unit's right-click menu under 'Civilian traffic'.");
                }
                return true;
            }

            if (!editorSeen)
            {
                editorSeen = true;
                ModLog.Info("Civilian traffic: no mission editor open - the traffic menu is not added "
                            + "(it writes into the mission being edited). The F8 panel says the same.");
            }
        }
        catch (Exception) { }

        return false;
    }

    // --- the entries ----------------------------------------------------------------------------

    private static void UseMapArea(ObjectBase? unit)
    {
        List<GeoPosition> area = MapArea.AroundUnits(out string note);

        if (area.Count < 3)
        {
            mapArea = null;
            Say(unit, "map area", "No zone built: " + note + ".");
            return;
        }

        mapArea = area;
        Say(unit, "map area", "Zone set from the map: " + note + ".");
    }

    private static void CycleShips(ObjectBase? unit)
    {
        (int min, int max) = Ships();
        (int nextMin, int nextMax) = Next(ShipChoices, min, max);

        TryStore(() => ModSettings.TrafficMinShips = nextMin);
        TryStore(() => ModSettings.TrafficMaxShips = nextMax);

        Say(unit, "ships", "Ships at a time: " + Range(nextMin, nextMax)
                           + ". Spawn traffic to apply it.");
    }

    private static void CyclePlanes(ObjectBase? unit)
    {
        (int min, int max) = Planes();
        (int nextMin, int nextMax) = Next(PlaneChoices, min, max);

        TryStore(() => ModSettings.TrafficMinPlanes = nextMin);
        TryStore(() => ModSettings.TrafficMaxPlanes = nextMax);

        Say(unit, "aircraft", "Aircraft at a time: " + Range(nextMin, nextMax)
                              + ". Spawn traffic to apply it.");
    }

    /// <summary>The choice after this one. A pair that is not on the list (an edited config file)
    /// restarts at the first, rather than jumping somewhere arbitrary.</summary>
    private static (int Min, int Max) Next((int Min, int Max)[] choices, int min, int max)
    {
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i].Min != min || choices[i].Max != max) continue;
            return choices[(i + 1) % choices.Length];
        }

        return choices[0];
    }

    private static void Spawn(ObjectBase? unit)
    {
        TrafficZone? zone = CurrentZone(out string how);

        if (zone == null)
        {
            Say(unit, "spawn", "No zone yet - draw one, or press 'Use the map area around your "
                               + "units'.");
            return;
        }

        (int shipMin, int shipMax) = Ships();
        (int planeMin, int planeMax) = Planes();

        TrafficRequest request = new()
        {
            MinShips = Math.Min(shipMin, shipMax),
            MaxShips = Math.Max(shipMin, shipMax),
            MinPlanes = Math.Min(planeMin, planeMax),
            MaxPlanes = Math.Max(planeMin, planeMax),
            MaxLanes = ModSettings.TrafficMaxLanes,
            ClassesPerRoute = ModSettings.TrafficClassesPerRoute,
            Label = "Zone traffic"
        };

        string report;

        try
        {
            TrafficGenerator.Generate(zone, request, out report);
        }
        catch (Exception error)
        {
            ModLog.Error("Civilian traffic generation failed", error);
            report = "Failed: " + error.GetType().Name + ": " + error.Message;
        }

        TrafficDiagnostics.Note(report);
        Say(unit, "spawn", report);
    }

    // --- state ----------------------------------------------------------------------------------

    private static TrafficZone? CurrentZone(out string text)
    {
        if (ZoneTool.HasPolygon)
        {
            TrafficZone? drawn = TrafficZone.Create(ZoneTool.Polygon);
            if (drawn != null)
            {
                text = ZoneTool.Polygon.Count + " points drawn, ~"
                       + Math.Round(drawn.AreaSquareNauticalMiles, 0) + " nm²";
                return drawn;
            }

            text = ZoneTool.Polygon.Count + " point(s) - too small to use";
            return null;
        }

        if (mapArea != null)
        {
            TrafficZone? area = TrafficZone.Create(mapArea);
            if (area != null)
            {
                text = "map area, ~" + Math.Round(area.AreaSquareNauticalMiles, 0) + " nm²";
                return area;
            }
        }

        text = "none - draw one or use the map area";
        return null;
    }

    private static string ZoneText()
    {
        CurrentZone(out string text);
        return text;
    }

    private static (int Min, int Max) Ships() =>
        (ModSettings.TrafficMinShips, ModSettings.TrafficMaxShips);

    private static (int Min, int Max) Planes() =>
        (ModSettings.TrafficMinPlanes, ModSettings.TrafficMaxPlanes);

    private static string Range(int min, int max)
    {
        if (min <= 0 && max <= 0) return "none";

        return min == max ? min.ToString() : min + "-" + max;
    }

    /// <summary>
    /// Reports one action: the log always, the screen when there is a unit to speak through, and the
    /// submenu's own status line so the result is still there when it is reopened.
    /// </summary>
    private static void Say(ObjectBase? unit, string what, string message)
    {
        lastResult = Shorten(message);

        ModLog.Info("Civilian traffic menu (" + what + "): " + message);

        try { unit?.ShowOrderFeedback("Traffic: " + lastResult, 6f); }
        catch (Exception) { }
    }

    private static string Shorten(string message)
    {
        string single = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= 90 ? single : single.Substring(0, 87) + "...";
    }

    private static void TryStore(Action write)
    {
        try { write(); }
        catch (Exception error) { ModLog.Error("Could not save the traffic counts", error); }
    }

    // --- the game's own menu item types -----------------------------------------------------------

    private static ContextMenuItem Action(string label, string tooltip, Action action) =>
        new(label: label, items: null, command: new DelegateCommand(_ => action()), tooltip: tooltip);

    private static ContextMenuItem Disabled(string label, string tooltip) =>
        new(label: label, items: null, command: null, tooltip: tooltip);
}
