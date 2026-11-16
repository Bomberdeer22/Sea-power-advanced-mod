using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// The twelve advanced features, as right-click menu entries in the game's own style.
///
/// The F7 window is gone. Everything here is a <see cref="ContextMenuItem"/> hung off the same
/// Orders menu the game builds for a unit, with the same submenu-per-group convention the game uses:
///
/// <code>
/// Orders
///   Weapon hatches                 (the original feature)
///   Sensors and EMCON        >     radars, sonar, towed array, EMCON, radar blink
///   Countermeasures          >     breakaway with chaff, auto-ARM shutoff, local control
///   Readiness                >     action stations, cold cruise, fatigue
///   Manoeuvres               >     sprint and drift, threat-axis reorientation
///   Air operations           >     helicopter search patterns
///   Strike                   >     salvo deconfliction
///   Deception                >     commercial disguise
///   Logistics                >     replenishment alongside
/// </code>
///
/// Every entry states its current state in the label where that is meaningful ("Radar blink: on"),
/// because a menu that does not show state is a menu you have to re-open twice.
/// </summary>
internal static class FleetMenu
{
    internal const string SensorsLabel = "Sensors and EMCON";
    internal const string CounterLabel = "Countermeasures";
    internal const string ReadinessLabel = "Readiness";
    internal const string ManoeuvreLabel = "Manoeuvres";
    internal const string AirLabel = "Air operations";
    internal const string StrikeLabel = "Strike";
    internal const string DeceptionLabel = "Deception";
    internal const string LogisticsLabel = "Logistics";

    private static readonly string[] Labels =
    {
        SensorsLabel, CounterLabel, ReadinessLabel, ManoeuvreLabel,
        AirLabel, StrikeLabel, DeceptionLabel, LogisticsLabel,
    };

    /// <summary>True when one of this mod's menus is already in the collection.</summary>
    internal static bool Present(IEnumerable<ContextMenuItem> roots)
    {
        foreach (ContextMenuItem item in roots)
        {
            string? label = item?.Label as string;
            if (label == null) continue;
            foreach (string known in Labels)
                if (string.Equals(label, known, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Builds every submenu that applies to this unit.</summary>
    internal static List<ContextMenuItem> Build(ObjectBase unit)
    {
        var menus = new List<ContextMenuItem>();

        Add(menus, SensorsLabel, "Radar, sonar, EMCON and blink.", Sensors(unit));
        Add(menus, CounterLabel, "Chaff, anti-radiation defence, local control.", Countermeasures(unit));
        Add(menus, ReadinessLabel, "Action stations, cold cruise and the cost of both.", ReadinessItems(unit));
        Add(menus, ManoeuvreLabel, "Sprint and drift, and turning the screen.", Manoeuvres(unit));
        Add(menus, AirLabel, "Helicopter search patterns.", AirOps(unit));
        Add(menus, StrikeLabel, "Co-ordinate the task force's launches.", Strike(unit));
        Add(menus, DeceptionLabel, "Reduce what the ship gives away.", Deception(unit));
        Add(menus, LogisticsLabel, "Take on ammunition from a supply ship.", Logistics(unit));

        return menus;
    }

    private static void Add(List<ContextMenuItem> menus, string label, string tip, List<ContextMenuItem> items)
    {
        if (items.Count == 0) return;

        menus.Add(new ContextMenuItem(
            label: label,
            items: new TrulyObservableCollection<ContextMenuItem>(items),
            tooltip: tip + "  (Sea Power Advanced " + ModInfo.Version + ")"));
    }

    // --- Sensors and EMCON ---------------------------------------------------------------------

    private static List<ContextMenuItem> Sensors(ObjectBase unit)
    {
        var items = new List<ContextMenuItem>
        {
            Action("Radars on", "Radiate everything.", () => Act(unit, "radars on", u => SensorControl.SetAllRadars(u, true))),
            Action("Radars off", "Silence the radars entirely.", () => Act(unit, "radars off", u => SensorControl.SetAllRadars(u, false))),
            Action("Air search only", "Air-search radar only: a partial, older-looking emission.",
                () => Act(unit, "air search only", u =>
                {
                    SensorControl.SetAllRadars(u, false);
                    return SensorControl.SetAirSearch(u, true);
                })),
            Action("Surface search only", "Surface-search radar only.",
                () => Act(unit, "surface search only", u =>
                {
                    SensorControl.SetAllRadars(u, false);
                    return SensorControl.SetSurfaceSearch(u, true);
                })),
            Action("Active sonar on", "Go active: finds submarines, gives the ship away.",
                () => Act(unit, "active sonar on", u => SensorControl.SetActiveSonar(u, true))),
            Action("Active sonar off", "Passive only.", () => Act(unit, "active sonar off", u => SensorControl.SetActiveSonar(u, false))),
            Action("Deploy towed array", "Stream the towed array.", () => Act(unit, "towed array out", u => SensorControl.SetTowedSonar(u, true))),
            Action("Retract towed array", "Bring the array in before making speed.", () => Act(unit, "towed array in", u => SensorControl.SetTowedSonar(u, false))),
        };

        items.Add(Blink.IsOn(unit)
            ? Action("Radar blink: on - click to stop", "Stops cycling and puts the radar back as it was found.",
                () => Report(unit, "blink", Blink.Stop(unit, out string r), r))
            : Action("Radar blink: off - click to start", "Cycles the surface-search radar so a passive seeker has a harder target.",
                () => Report(unit, "blink", Blink.Start(unit, out string r), r)));

        items.Add(new ContextMenuItem(
            label: "EMCON profile",
            items: new TrulyObservableCollection<ContextMenuItem>(new[]
            {
                Action("Silent", "Nothing radiates. The ship is a hole in the map.",
                    () => Act(unit, "EMCON silent", u =>
                    {
                        SensorControl.SetAllRadars(u, false);
                        return SensorControl.SetEmcon(u, true);
                    })),
                Action("Passive", "Listen only: radars and active sonar off.",
                    () => Act(unit, "EMCON passive", u =>
                    {
                        SensorControl.SetAllRadars(u, false);
                        SensorControl.SetActiveSonar(u, false);
                        return SensorControl.SetEmcon(u, true);
                    })),
                Action("Battle stations", "Everything radiating.",
                    () => Act(unit, "EMCON battle stations", u =>
                    {
                        SensorControl.SetAllRadars(u, true);
                        SensorControl.SetEmcon(u, false);
                        return true;
                    })),
            }),
            tooltip: "What the ship gives away."));

        return items;
    }

    // --- Countermeasures -----------------------------------------------------------------------

    private static List<ContextMenuItem> Countermeasures(ObjectBase unit)
    {
        var items = new List<ContextMenuItem>
        {
            Action("Breakaway: chaff and hard rudder",
                "Full ahead, hard rudder, chaff away - all at once, now.",
                () => Report(unit, "breakaway", Breakaway.Execute(unit, out string r), r)),
        };

        items.Add(AutoArm.IsOn(unit)
            ? Action("Auto-ARM shutoff: on - click to turn off",
                "Stops cutting the radars automatically.",
                () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r))
            : Action("Auto-ARM shutoff: off - click to turn on",
                "Cuts the radars for eight seconds whenever a weapon is inbound.",
                () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r)));

        items.Add(LocalControl.IsLocal(unit)
            ? Action("Local control: on - click to return to ship control",
                "Puts the mounts back under the ship's own picture.",
                () => Report(unit, "local control", LocalControl.Toggle(unit, out string r), r))
            : Action("Local control: off - click for local control",
                "Guns and CIWS fight their own battle when the picture is lost.",
                () => Report(unit, "local control", LocalControl.Toggle(unit, out string r), r)));

        return items;
    }

    // --- Readiness -----------------------------------------------------------------------------

    private static List<ContextMenuItem> ReadinessItems(ObjectBase unit)
    {
        var items = new List<ContextMenuItem>();

        items.Add(Readiness.AtActionStations(unit)
            ? Action("Stand down to cold cruise", "Reloads back to normal; the crew starts to recover.",
                () => Report(unit, "readiness", Readiness.SetGeneralQuarters(unit, false, out string r), r))
            : Action("Action stations", "Closes the ship up: reloads "
                + "faster while held, at the cost of fatigue.",
                () => Report(unit, "readiness", Readiness.SetGeneralQuarters(unit, true, out string r), r)));

        items.Add(Disabled("Status: " + Readiness.Describe(unit),
            "Current readiness and crew fatigue for this ship."));

        return items;
    }

    // --- Manoeuvres ----------------------------------------------------------------------------

    private static List<ContextMenuItem> Manoeuvres(ObjectBase unit)
    {
        bool? doctrine = null;
        try { doctrine = SensorControl.GetSprintDoctrine(unit); }
        catch (Exception) { }

        var items = new List<ContextMenuItem>
        {
            Action((doctrine == true ? "Sprint and drift: on - click to turn off" : "Sprint and drift: off - click to turn on"),
                "The game's own ASW doctrine: fast to the datum, quiet to listen.",
                () => Report(unit, "sprint and drift", SprintDrift.Toggle(unit, out string r), r)),
            Action("Sprint and drift for the whole task force",
                "Sets the doctrine on every ship under command at once.",
                () => Fleet("sprint and drift (fleet)", SprintDrift.SetFleet(true, out string r), r)),
            Action("Turn the screen onto the threat",
                "Re-stations the task force on the bearing of the nearest tracked threat.",
                () => Fleet("threat axis", ThreatAxis.Reorient(out string r), r)),
        };

        return items;
    }

    // --- Air operations ------------------------------------------------------------------------

    private static List<ContextMenuItem> AirOps(ObjectBase unit)
    {
        return new List<ContextMenuItem>
        {
            Action("Fly a ladder search", "Launches (if needed) and flies a ladder pattern.",
                () => Report(unit, "helo ladder", HeloPatterns.Fly(unit, SearchPattern.Ladder, out string r), r)),
            Action("Fly an expanding square", "Launches (if needed) and flies an expanding square.",
                () => Report(unit, "helo square", HeloPatterns.Fly(unit, SearchPattern.ExpandingSquare, out string r), r)),
            Action("Fly a sector search", "Launches (if needed) and sweeps a sector.",
                () => Report(unit, "helo sector", HeloPatterns.Fly(unit, SearchPattern.Sector, out string r), r)),
        };
    }

    // --- Strike --------------------------------------------------------------------------------

    private static List<ContextMenuItem> Strike(ObjectBase unit)
    {
        return new List<ContextMenuItem>
        {
            Action("Deconflict the salvo",
                "Holds every ship but the leader, then releases them one at a time.",
                () => Fleet("salvo deconfliction", SalvoPlan.Start(out string r), r)),
            Action("Release everyone now", "Cancels the plan and lets every ship fire.",
                () => Fleet("salvo release", SalvoPlan.Release(out string r), r)),
        };
    }

    // --- Deception -----------------------------------------------------------------------------

    private static List<ContextMenuItem> Deception(ObjectBase unit)
    {
        return new List<ContextMenuItem>
        {
            MerchantDisguise.IsDisguised(unit)
                ? Action("Commercial disguise: on - click to drop it", "Restores the warship's emissions and speed.",
                    () => Report(unit, "disguise", MerchantDisguise.Toggle(unit, out string r), r))
                : Action("Commercial disguise: off - click to assume one", "Goes dark, slow and civilian-looking.",
                    () => Report(unit, "disguise", MerchantDisguise.Toggle(unit, out string r), r)),
        };
    }

    // --- Logistics -----------------------------------------------------------------------------

    private static List<ContextMenuItem> Logistics(ObjectBase unit)
    {
        return new List<ContextMenuItem>
        {
            Action("Replenish alongside", "Takes on ammunition from a supply ship within 2 nm.",
                () => Report(unit, "replenishment", Replenishment.Alongside(unit, out string r), r)),
        };
    }

    // --- helpers -------------------------------------------------------------------------------

    private delegate bool UnitAction(ObjectBase unit);

    /// <summary>Runs an action on this unit and reports the outcome where the player can see it.</summary>
    private static void Act(ObjectBase unit, string what, UnitAction action)
    {
        bool ok;
        try { ok = action(unit); }
        catch (Exception error)
        {
            ModLog.Error("Feature '" + what + "' failed", error);
            ok = false;
        }

        ModLog.Info("Menu: " + what + " on " + Name(unit) + ": " + (ok ? "applied" : "no effect"));

        try { unit.ShowOrderFeedback((ok ? "" : "Not available: ") + what, 2f); }
        catch (Exception) { }
    }

    private static void Report(ObjectBase unit, string what, bool ok, string report)
    {
        ModLog.Info("Menu: " + what + " on " + Name(unit) + ": " + report + (ok ? "" : " [no effect]"));

        try { unit.ShowOrderFeedback(what + ": " + report, 3f); }
        catch (Exception) { }
    }

    /// <summary>Task-force-wide actions are logged and shown, with the count they changed.</summary>
    private static void Fleet(string what, int changed, string report)
    {
        ModLog.Info("Menu: " + what + ": " + report);
        ModLog.Info("Menu: " + what + " changed " + changed + " ship(s).");
    }

    private static string Name(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }

    private static ContextMenuItem Action(string label, string tooltip, Action action) =>
        new(label: label, items: null, command: new DelegateCommand(_ => action()), tooltip: tooltip);

    /// <summary>A menu entry that only displays information, greyed out by having no command.</summary>
    private static ContextMenuItem Disabled(string label, string tooltip) =>
        new(label: label, items: null, command: null, tooltip: tooltip);
}
