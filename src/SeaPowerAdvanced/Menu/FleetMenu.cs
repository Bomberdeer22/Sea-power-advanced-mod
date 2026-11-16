using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// The twelve advanced features, dropped into the section of the game's own right-click menu where
/// each belongs rather than stacked into one submenu.
///
/// Where things go, and why:
///
/// <code>
/// Sensors / EMCON    radars, air/surface search, active sonar, towed array,
///                    EMCON profiles, radar blink, auto-ARM, commercial disguise
/// Weapons / Engage   salvo deconfliction (it is about who may fire, and when)
/// Rules of engagement  action stations, cold cruise, crew fatigue
/// Formation          sprint and drift, turning the screen onto the threat
/// Air operations     helicopter search patterns
/// Damage control     local control, replenishment alongside
/// ------------------  top level
/// Breakaway          one click, because it is the "a missile is going to hit us" button
/// </code>
///
/// Each feature degrades to the Orders section, then to the top level, if this build of the game
/// has no section for it - see <see cref="MenuRouting"/>. Nothing is ever hidden.
/// </summary>
internal static class FleetMenu
{
    /// <summary>Entries that must reach the player in one click, so they sit at the top level.</summary>
    private const string BreakawayLabel = "Breakaway: chaff and hard rudder";

    /// <summary>Labels this mod owns, used to avoid adding the same entry twice.</summary>
    private static readonly string[] OwnMarkers =
    {
        BreakawayLabel, "Radars on", "Radars off", "Air search only", "Surface search only",
        "Active sonar on", "Active sonar off", "Deploy towed array", "Retract towed array",
        "EMCON profile", "Radar blink", "Auto-ARM", "Commercial disguise",
        "Deconflict the salvo", "Release everyone now", "Action stations", "Stand down to cold cruise",
        "Sprint and drift", "Turn the screen onto the threat",
        "Fly a ladder search", "Fly an expanding square", "Fly a sector search",
        "Local control", "Replenish alongside",
    };

    /// <summary>True when this menu already carries our entries.</summary>
    internal static bool Present(IEnumerable<ContextMenuItem> roots)
    {
        foreach (ContextMenuItem root in roots)
        {
            if (root == null) continue;

            if (Carries(root)) return true;

            if (root.Items == null) continue;
            foreach (ContextMenuItem child in root.Items)
                if (child != null && Carries(child)) return true;
        }
        return false;
    }

    private static bool Carries(ContextMenuItem item)
    {
        string? label = item.Label as string;
        if (label == null) return false;

        foreach (string marker in OwnMarkers)
        {
            if (label.StartsWith(marker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Adds every feature that applies to this unit, each into its own section. Returns how many
    /// sections were written to, for the first-time log line.
    /// </summary>
    internal static int Install(ObjectBase unit, List<ContextMenuItem> roots)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach ((ContextMenuItem item, string[][] route) in Entries(unit))
        {
            try
            {
                written.Add(MenuRouting.Place(roots, item, route));
            }
            catch (Exception error)
            {
                ModLog.Error("Could not place menu entry " + (item.Label as string), error);
            }
        }

        return written.Count;
    }

    /// <summary>Every entry for this unit, paired with the section keywords it belongs in.</summary>
    private static List<(ContextMenuItem item, string[][] route)> Entries(ObjectBase unit)
    {
        var entries = new List<(ContextMenuItem, string[][])>();

        void Add(ContextMenuItem item, params string[][] route) => entries.Add((item, route));

        // --- Sensors and EMCON ---------------------------------------------------------------
        Add(Action("Radars on", "Radiate everything.",
            () => Act(unit, "radars on", u => SensorControl.SetAllRadars(u, true))), MenuRouting.Sensors);

        Add(Action("Radars off", "Silence the radars entirely.",
            () => Act(unit, "radars off", u => SensorControl.SetAllRadars(u, false))), MenuRouting.Sensors);

        Add(Action("Air search only", "Air-search radar only: a partial, older-looking emission.",
            () => Act(unit, "air search only", u =>
            {
                SensorControl.SetAllRadars(u, false);
                return SensorControl.SetAirSearch(u, true);
            })), MenuRouting.Sensors);

        Add(Action("Surface search only", "Surface-search radar only.",
            () => Act(unit, "surface search only", u =>
            {
                SensorControl.SetAllRadars(u, false);
                return SensorControl.SetSurfaceSearch(u, true);
            })), MenuRouting.Sensors);

        Add(Action("Active sonar on", "Go active: finds submarines, gives the ship away.",
            () => Act(unit, "active sonar on", u => SensorControl.SetActiveSonar(u, true))), MenuRouting.Sensors);

        Add(Action("Active sonar off", "Passive only.",
            () => Act(unit, "active sonar off", u => SensorControl.SetActiveSonar(u, false))), MenuRouting.Sensors);

        Add(Action("Deploy towed array", "Stream the towed array.",
            () => Act(unit, "towed array out", u => SensorControl.SetTowedSonar(u, true))), MenuRouting.Sensors);

        Add(Action("Retract towed array", "Bring the array in before making speed.",
            () => Act(unit, "towed array in", u => SensorControl.SetTowedSonar(u, false))), MenuRouting.Sensors);

        Add(new ContextMenuItem(
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
            tooltip: "What this ship gives away."), MenuRouting.Sensors);

        Add(Blink.IsOn(unit)
            ? Action("Radar blink: on - click to stop",
                "Stops cycling and puts the radar back as it was found.",
                () => Report(unit, "blink", Blink.Stop(unit, out string r), r))
            : Action("Radar blink: off - click to start",
                "Cycles the surface-search radar so a passive seeker has a harder target.",
                () => Report(unit, "blink", Blink.Start(unit, out string r), r)), MenuRouting.Sensors);

        Add(AutoArm.IsOn(unit)
            ? Action("Auto-ARM shutoff: on - click to turn off",
                "Stops cutting the radars automatically.",
                () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r))
            : Action("Auto-ARM shutoff: off - click to turn on",
                "Cuts the radars for eight seconds whenever a weapon is inbound.",
                () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r)), MenuRouting.Sensors);

        Add(MerchantDisguise.IsDisguised(unit)
            ? Action("Commercial disguise: on - click to drop it",
                "Restores the warship's emissions and speed.",
                () => Report(unit, "disguise", MerchantDisguise.Toggle(unit, out string r), r))
            : Action("Commercial disguise: off - click to assume one",
                "Goes dark, slow and civilian-looking.",
                () => Report(unit, "disguise", MerchantDisguise.Toggle(unit, out string r), r)),
            MenuRouting.Sensors);

        // --- Weapons / Engage ----------------------------------------------------------------
        Add(Action("Deconflict the salvo",
            "Holds every ship but the leader, then releases them one at a time.",
            () => Fleet("salvo deconfliction", SalvoPlan.Start(out string r), r)), MenuRouting.Weapons);

        Add(Action("Release everyone now", "Cancels the plan and lets every ship fire.",
            () => Fleet("salvo release", SalvoPlan.Release(out string r), r)), MenuRouting.Weapons);

        // --- Rules of engagement -------------------------------------------------------------
        Add(Readiness.AtActionStations(unit)
            ? Action("Stand down to cold cruise", "Reloads back to normal; the crew starts to recover.",
                () => Report(unit, "readiness", Readiness.SetGeneralQuarters(unit, false, out string r), r))
            : Action("Action stations", "Closes the ship up: reloads faster while held, at the cost of fatigue.",
                () => Report(unit, "readiness", Readiness.SetGeneralQuarters(unit, true, out string r), r)),
            MenuRouting.Rules);

        Add(Disabled("Readiness: " + Readiness.Describe(unit),
            "Current readiness and crew fatigue for this ship."), MenuRouting.Rules);

        // --- Formation -----------------------------------------------------------------------
        Add(SprintDoctrineItem(unit), MenuRouting.Formation);

        Add(Action("Sprint and drift for the whole task force",
            "Sets the doctrine on every ship under command at once.",
            () => Fleet("sprint and drift (fleet)", SprintDrift.SetFleet(true, out string r), r)),
            MenuRouting.Formation);

        Add(Action("Turn the screen onto the threat",
            "Re-stations the task force on the bearing of the nearest tracked threat.",
            () => Fleet("threat axis", ThreatAxis.Reorient(out string r), r)), MenuRouting.Formation);

        // --- Air operations ------------------------------------------------------------------
        Add(Action("Fly a ladder search", "Launches (if needed) and flies a ladder pattern.",
            () => Report(unit, "helo ladder", HeloPatterns.Fly(unit, SearchPattern.Ladder, out string r), r)),
            MenuRouting.Air);

        Add(Action("Fly an expanding square", "Launches (if needed) and flies an expanding square.",
            () => Report(unit, "helo square", HeloPatterns.Fly(unit, SearchPattern.ExpandingSquare, out string r), r)),
            MenuRouting.Air);

        Add(Action("Fly a sector search", "Launches (if needed) and sweeps a sector.",
            () => Report(unit, "helo sector", HeloPatterns.Fly(unit, SearchPattern.Sector, out string r), r)),
            MenuRouting.Air);

        // --- Damage control ------------------------------------------------------------------
        Add(LocalControl.IsLocal(unit)
            ? Action("Local control: on - click to return to ship control",
                "Puts the mounts back under the ship's own picture.",
                () => Report(unit, "local control", LocalControl.Toggle(unit, out string r), r))
            : Action("Local control: off - click for local control",
                "Guns and CIWS fight their own battle when the picture is lost.",
                () => Report(unit, "local control", LocalControl.Toggle(unit, out string r), r)),
            MenuRouting.Damage, MenuRouting.Weapons);

        Add(Action("Replenish alongside", "Takes on ammunition from a supply ship within 2 nm.",
            () => Report(unit, "replenishment", Replenishment.Alongside(unit, out string r), r)),
            MenuRouting.Logistics, MenuRouting.Damage);

        // --- Top level: the emergency action ------------------------------------------------
        Add(Action(BreakawayLabel, "Full ahead, hard rudder, chaff away - all at once, now.",
            () => Report(unit, "breakaway", Breakaway.Execute(unit, out string r), r)),
            Array.Empty<string>());

        return entries;
    }

    private static ContextMenuItem SprintDoctrineItem(ObjectBase unit)
    {
        bool? doctrine = null;
        try { doctrine = SensorControl.GetSprintDoctrine(unit); }
        catch (Exception) { }

        return doctrine == true
            ? Action("Sprint and drift: on - click to turn off",
                "The game's own ASW doctrine: fast to the datum, quiet to listen.",
                () => Report(unit, "sprint and drift", SprintDrift.Toggle(unit, out string r), r))
            : Action("Sprint and drift: off - click to turn on",
                "The game's own ASW doctrine: fast to the datum, quiet to listen.",
                () => Report(unit, "sprint and drift", SprintDrift.Toggle(unit, out string r), r));
    }

    // --- helpers -------------------------------------------------------------------------------

    private delegate bool UnitAction(ObjectBase unit);

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

    private static void Fleet(string what, int changed, string report)
    {
        ModLog.Info("Menu: " + what + ": " + report + " (" + changed + " ship(s) changed)");
    }

    private static string Name(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }

    private static ContextMenuItem Action(string label, string tooltip, Action action) =>
        new(label: label, items: null, command: new DelegateCommand(_ => action()), tooltip: tooltip);

    private static ContextMenuItem Disabled(string label, string tooltip) =>
        new(label: label, items: null, command: null, tooltip: tooltip);
}
