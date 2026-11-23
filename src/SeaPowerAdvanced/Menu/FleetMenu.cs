using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;
using SeaPowerAdvanced.UI;
using SeapowerUI;
using SeapowerUI.ViewModels;
using UniRx;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// The advanced features, dropped into the section of the game's own right-click menu where
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

    /// <summary>
    /// The two submenus the Sensors section uses once a hull carries enough entries for the list to
    /// become a hunt. Named constants rather than string literals at a dozen call sites, so a typo
    /// cannot silently split a group in two.
    /// </summary>
    private const string RadarsGroup = "Radars and sonar";
    private const string EcmGroup = "ECM";
    private const string LinkGroup = "Datalink";

    /// <summary>True when this menu has already been augmented.</summary>
    internal static bool Present(object collection) => MenuRouting.AlreadyHandled(collection);

    /// <summary>
    /// Adds every feature that applies to this unit, each into its own section. Returns how many
    /// sections were written to, for the first-time log line.
    /// </summary>
    internal static int Install(ObjectBase unit, List<ContextMenuItem> roots, object live)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);

        MenuRouting.Placements.Clear();
        MenuRouting.ResetGroups();

        List<(ContextMenuItem item, string group, string[][] route)> entries = Entries(unit);

        // The grouping rule, and it is one rule: a named group with more than one entry becomes a
        // submenu inside its section; anything else is placed exactly as before. One rule because
        // the alternative - a count threshold per section - makes the menu's shape depend on how
        // many sensors a hull happens to carry, so the same feature moves between one hull and the
        // next. A group of one is not a group, which is what keeps a ship without ECM from growing
        // an "ECM" submenu containing a single entry.
        Dictionary<string, int> sizes = GroupSizes(roots, entries);

        foreach ((ContextMenuItem item, string group, string[][] route) in entries)
        {
            try
            {
                // Same key MenuRouting uses for the submenu, so a group that would be split across
                // two sections is counted as the one it will actually be placed in.
                string key = MenuRouting.SectionLabelFor(roots, route) + " | " + group;

                bool grouped = !string.IsNullOrEmpty(group)
                               && sizes.TryGetValue(key, out int size) && size > 1;

                string where = grouped
                    ? MenuRouting.PlaceInGroup(roots, live, item, group, GroupTooltip(group), route)
                    : MenuRouting.Place(roots, live, item, route);

                written.Add(where);
                MenuRouting.Placements.Add((item.Label as string ?? "<unnamed>") + "  ->  " + where);
            }
            catch (Exception error)
            {
                ModLog.Error("Could not place menu entry " + (item.Label as string), error);
                MenuRouting.Placements.Add((item.Label as string ?? "<unnamed>") + "  ->  FAILED: "
                                           + error.GetType().Name);
            }
        }

        return written.Count;
    }

    /// <summary>How many entries each section-and-group pair has, so the rule above is decided once.</summary>
    private static Dictionary<string, int> GroupSizes(
        List<ContextMenuItem> roots,
        List<(ContextMenuItem item, string group, string[][] route)> entries)
    {
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach ((ContextMenuItem _, string group, string[][] route) in entries)
        {
            if (string.IsNullOrEmpty(group)) continue;

            string key = MenuRouting.SectionLabelFor(roots, route) + " | " + group;
            sizes[key] = sizes.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        return sizes;
    }

    /// <summary>The hover text for a group submenu: what the entries inside it have in common.</summary>
    private static string GroupTooltip(string group) => group switch
    {
        RadarsGroup => "This ship's emitters: radars, active sonar and the towed array. Every switch "
                       + "here is the game's own, and the label shows what is on.",
        EcmGroup => "Electronic warfare: the automatic defences, the focused jammer and what the "
                    + "game's own ECM interface looks like on this install.",
        LinkGroup => "The datalink to the other player's force: whether this force's picture is "
                     + "being handed to theirs, and what either side can actually see.",
        _ => "Entries for this ship.",
    };

    /// <summary>
    /// The menu for a unit that is not ours: a hostile or neutral contact.
    ///
    /// A contact is not a command of ours, so nothing here orders it about. The only thing the mod
    /// can legitimately offer is the jammer, and only when one of our own ships could actually reach
    /// it. The game's own entries - mark hostile / neutral, request identify - are left exactly as
    /// they are: they are the game's, and they are right.
    ///
    /// Returns how many entries were added (0 or 1).
    /// </summary>
    internal static int InstallContact(ObjectBase contact, List<ContextMenuItem> roots, object live)
    {
        MenuRouting.Placements.Clear();

        try
        {
            // The gate is the jammer itself, not the old name scan: a build that has a jam order and
            // a ship carrying a set can jam, and that is the only thing that matters here. The old
            // gate looked for ECM members on the unit type, found none, and returned before it added
            // anything - which is why the contact menu had no entry at all.
            if (!OffensiveJam.Available)
            {
                // Rare, and worth saying out loud: this build has no jam order the mod can name, so
                // there is nothing to offer. The file names what was looked for.
                var missing = Disabled("ECM: this build exposes no jam order",
                    "offensive-ecm.txt in the mod folder records what was searched for.");

                MenuRouting.Place(roots, live, missing, Array.Empty<string>());
                MenuRouting.Placements.Add(missing.Label + "  ->  top level");
                return 1;
            }

            ObjectBase? jammer = OffensiveEcm.BestJammerFor(contact);

            if (jammer == null)
            {
                // A jammer exists in the fleet but none is near enough: say so, once, rather than
                // adding nothing and leaving the player wondering whether the feature is installed.
                ObjectBase? anywhere = AnyJammer();

                if (anywhere == null) return 0;

                int reach = (int)OffensiveEcm.Reach(contact);

                var tooFar = Disabled(
                    "ECM: no jammer within " + reach + " nm",
                    "The nearest ship of ours carrying ECM is further away than the jammer reaches "
                    + "against " + UnitFit.Kind(contact) + " (offensive-ecm.txt names what the mod "
                    + "found).");

                MenuRouting.Place(roots, live, tooFar, Array.Empty<string>());
                MenuRouting.Placements.Add(tooFar.Label + "  ->  top level");
                return 1;
            }

            int range = -1;
            try
            {
                range = (int)ThreatAxis.Distance(jammer.getGeoPosition(), contact.getGeoPosition());
            }
            catch (Exception) { }

            string kind = UnitFit.Kind(contact);
            bool? identified = ContactPicture.Identified(contact);

            string label = range >= 0
                ? "Jam with ECM (" + ContactName(jammer) + ", " + range + " nm, " + kind + ")"
                : "Jam with ECM";

            var item = new ContextMenuItem(
                label: label,
                items: null,
                command: new DelegateCommand(_ => Report(contact, "jam with ECM",
                    OffensiveEcm.LockOnContact(contact, out string r), r)),
                tooltip: "Point " + ContactName(jammer) + "'s jammer at this " + kind
                         + " through the game's own jam order, so the game's ECM does the work."
                         + (identified == false
                            ? " It is not identified, so it is jammed on its bearing - the order the "
                              + "game provides for exactly that case. It does not have to be "
                              + "identified first."
                            : "")
                         + (UnitFit.IsAir(contact)
                            ? " An aircraft is reached at the air range - both ends are high above "
                              + "the radio horizon."
                            : ""));

            // Top level: a contact menu is short, and hiding this inside a section would make the
            // player hunt for the one entry the mod adds there.
            string where = MenuRouting.Place(roots, live, item, Array.Empty<string>());
            MenuRouting.Placements.Add(label + "  ->  " + where);

            int added = 1;

            // Already locked on this contact? Then the manual transmit order belongs here too, next
            // to the lock it acts on.
            bool locked = OffensiveEcm.IsLockedOn(jammer, contact);

            if (locked)
            {
                bool holding = OffensiveEcm.IsHolding(jammer);

                // The limit is named before the click, not after it: either the window is running
                // out, the contact's crew have their own window, or the force has no channel left.
                bool allowed = OffensiveEcm.CanHold(jammer, out string canHold);

                string left = holding && OffensiveEcm.HoldLeft(jammer) > 0
                    ? " (" + OffensiveEcm.HoldLeft(jammer) + " s)"
                    : "";

                ContextMenuItem transmit;

                if (holding)
                {
                    transmit = new ContextMenuItem(
                        label: "Offensive ECM: holding its sensors down" + left + " - click to stop",
                        items: null,
                        command: new DelegateCommand(_ => Report(contact, "stop holding",
                            OffensiveEcm.StopJam(jammer, out string s2), s2)),
                        tooltip: "Stops holding the contact's individual sets down. The game's own "
                                 + "jam order stays, so the jam itself continues. Its crew get the "
                                 + "recovery window with their sensors back, because stopping by "
                                 + "hand must not be a way round the limit.");
                }
                else if (allowed)
                {
                    transmit = new ContextMenuItem(
                        label: "Offensive ECM: hold its sensors down",
                        items: null,
                        command: new DelegateCommand(_ => Report(contact, "hold down",
                            OffensiveEcm.JamNow(jammer, out string s2), s2)),
                        tooltip: "The game's jam is already working on it. This adds the second "
                                 + "half: switch the contact's own sensors off and keep them off. "
                                 + "It needs the contact to be identified, and it is a burst: the "
                                 + "hold runs for a set time, then its crew get their sensors back "
                                 + "for a while before it can be held down again.");
                }
                else
                {
                    transmit = Disabled(
                        "Offensive ECM: cannot hold it down - "
                        + (canHold.Length > 0 ? canHold : "it is not identified"),
                        "The game's jam on this contact is already running and does not need "
                        + "either of these. Holding its own sets down needs it identified, needs "
                        + "a suppression channel free in the task force, and needs its crew to "
                        + "have finished recovering from the last hold. The menu says which.");
                }

                MenuRouting.Place(roots, live, transmit, Array.Empty<string>());
                MenuRouting.Placements.Add(transmit.Label + "  ->  top level");
                added++;

                var release = new ContextMenuItem(
                    label: "Release the ECM lock",
                    items: null,
                    command: new DelegateCommand(_ => Report(contact, "release ECM lock",
                        OffensiveEcm.Release(jammer, out string s3), s3)),
                    tooltip: "Stops the jam through the game's own teardown and lets go of the track.");

                MenuRouting.Place(roots, live, release, Array.Empty<string>());
                MenuRouting.Placements.Add(release.Label + "  ->  top level");
                added++;
            }

            return added;
        }
        catch (Exception error)
        {
            ModLog.Error("Could not add the ECM lock entry to a contact menu", error);
            return 0;
        }
    }

    /// <summary>
    /// Every entry for this unit, with the section keywords it belongs in and, optionally, the name
    /// of the group it belongs to inside that section.
    /// </summary>
    private static List<(ContextMenuItem item, string group, string[][] route)> Entries(ObjectBase unit)
    {
        var entries = new List<(ContextMenuItem, string, string[][])>();

        void Add(ContextMenuItem item, params string[][] route) => entries.Add((item, "", route));

        // Same as Add, but the entry is eligible for a submenu - see the rule in Install.
        void Grouped(string group, ContextMenuItem item, params string[][] route) =>
            entries.Add((item, group, route));

        // --- Sensors and EMCON ---------------------------------------------------------------
        Grouped(RadarsGroup, Action("Radars on", "Radiate everything.",
            () => Act(unit, "radars on", u => SensorControl.SetAllRadars(u, true))), MenuRouting.Sensors);

        Grouped(RadarsGroup, Action("Radars off", "Silence the radars entirely.",
            () => Act(unit, "radars off", u => SensorControl.SetAllRadars(u, false))), MenuRouting.Sensors);

        Grouped(RadarsGroup, Action("Air search only",
            "Air-search radar only: a partial, older-looking emission.",
            () => Act(unit, "air search only", u =>
            {
                SensorControl.SetAllRadars(u, false);
                return SensorControl.SetAirSearch(u, true);
            })), MenuRouting.Sensors);

        Grouped(RadarsGroup, Action("Surface search only", "Surface-search radar only.",
            () => Act(unit, "surface search only", u =>
            {
                SensorControl.SetAllRadars(u, false);
                return SensorControl.SetSurfaceSearch(u, true);
            })), MenuRouting.Sensors);

        // Sonar entries only on hulls that have the fit. Unknown counts as fitted: the game does
        // not always expose a HasX for every hull, and a hidden feature looks like a broken one.
        if (SensorControl.ActiveSonarFit(unit) != false)
        {
            Grouped(RadarsGroup, Action("Active sonar on",
                "Go active: finds submarines, gives the ship away.",
                () => Act(unit, "active sonar on", u => SensorControl.SetActiveSonar(u, true))),
                MenuRouting.Sensors);

            Grouped(RadarsGroup, Action("Active sonar off", "Passive only.",
                () => Act(unit, "active sonar off", u => SensorControl.SetActiveSonar(u, false))),
                MenuRouting.Sensors);
        }

        if (SensorControl.TowedArrayFit(unit) != false)
        {
            Grouped(RadarsGroup, Action("Deploy towed array", "Stream the towed array.",
                () => Act(unit, "towed array out", u => SensorControl.SetTowedSonar(u, true))),
                MenuRouting.Sensors);

            Grouped(RadarsGroup, Action("Retract towed array",
                "Bring the array in before making speed.",
                () => Act(unit, "towed array in", u => SensorControl.SetTowedSonar(u, false))),
                MenuRouting.Sensors);
        }

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

        // Radar blink: the player chooses which set to cycle, because the two do different jobs and
        // only one of them is what an anti-radiation shot homes on.
        if (Blink.IsOn(unit))
        {
            Add(Action("Radar blink: " + Blink.State(unit) + " - click to stop",
                "Stops cycling and puts the radar(s) back exactly as they were found.",
                () => Report(unit, "blink", Blink.Stop(unit, out string r), r)), MenuRouting.Sensors);
        }
        else
        {
            Add(BlinkRadarItem(unit), MenuRouting.Sensors);
        }

        if (Multiplayer.IsClient)
        {
            // In a session this machine does not decide when a radar radiates, so the entry says so
            // instead of offering a switch the host would refuse.
            // The refusal text comes from Multiplayer's own table, so the menu cannot promise
            // something the report then denies.
            Grouped(EcmGroup, Disabled("Auto-ARM shutoff: not available in multiplayer",
                Multiplayer.ClientRefusal("Auto-ARM shutoff")
                ?? "The host decides when a ship radiates in a session."),
                MenuRouting.Sensors);
        }
        else
        {
            Grouped(EcmGroup, AutoArm.IsOn(unit)
                ? Action("Auto-ARM shutoff: on - click to turn off",
                    "Stops cutting the radars automatically.",
                    () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r))
                : Action("Auto-ARM shutoff: off - click to turn on",
                    "Cuts the radars for eight seconds whenever a weapon is inbound.",
                    () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r)),
                MenuRouting.Sensors);
        }

        // --- the rotating LIDAR ------------------------------------------------------------
        // A 360-degree set that turns and only sees what the beam is on: the map overlay draws the
        // spoke, and these are the switches. Every hull under command may have it, because the set
        // is the mod's own - that is what the feature is - but what the hull actually carries is
        // still reported, because a laser director and a pair of binoculars are not the same thing
        // even when the mod is doing the looking.
        if (!UnitFit.IsSubmarine(unit))
        {
            LidarControl.Fit laser = LidarControl.Detect(unit);

            if (!ModSettings.LidarEnabled)
            {
                Add(Disabled("LIDAR: off in the settings",
                    "Features/LidarEnabled is false, so no hull may scan. The setting is in the "
                    + "mod's own config file."), MenuRouting.Sensors);
            }
            else if (!laser.Allowed)
            {
                // Only reachable with LidarAnyHull off: the same rule the sweep applies, said in the
                // menu rather than discovered when the click is refused.
                Add(Disabled("LIDAR: nothing to look through on this hull",
                    "Features/LidarAnyHull is off and this hull's own sensor list names no optical "
                    + "set: it says " + laser.Quote + ". Turn LidarAnyHull on to fit the set to every "
                    + "hull, which is what the feature does by default."), MenuRouting.Sensors);
            }
            else
            {
                Add(Lidar.IsOn(unit)
                    ? Action("LIDAR: " + Lidar.Describe(unit) + " - click to stop scanning",
                        "Stops the set. The tracks it gave the force age out on the game's own "
                        + "sensor clock, exactly like a lost radar contact.",
                        () => Report(unit, "LIDAR", Lidar.Toggle(unit, out string r), r))
                    : Action("LIDAR: off - click to start the scanner ("
                             + (int)ModSettings.LidarRangeNm + " nm, 360 deg)",
                        "Starts a rotating set on this hull: it turns through 360 degrees at "
                        + (int)ModSettings.LidarSpinRpm + " rpm and sees whatever its beam passes "
                        + "over inside " + (int)ModSettings.LidarRangeNm + " nm. A contact is only "
                        + "sighted when the beam is ON it - watch the spoke on the map. It never "
                        + "looks for a contact the force has not already been told about, and it "
                        + "never gives an identity, only a position.",
                        () => Report(unit, "LIDAR", Lidar.Toggle(unit, out string r), r)),
                    MenuRouting.Sensors);

                if (!laser.Fitted)
                {
                    Add(Action("LIDAR: lookouts assumed - click for the report",
                        "This hull's own sensor list names no optical set (" + laser.Quote + "). The "
                        + "set is mounted anyway - the scanner is the mod's, and a warship has "
                        + "lookouts - but that is an assumption and this is where it is written "
                        + "down. lidar.txt prints what each hull's data really holds.",
                        () =>
                        {
                            Lidar.WriteReport("lookouts assumed on " + Name(unit));
                            ModLog.Info("Menu: lidar.txt written for " + Name(unit));
                        }), MenuRouting.Sensors);
                }
            }
        }

        // The view itself: one entry, on any hull, because it is a property of the map and not of
        // the ship whose menu happens to be open.
        if (ModSettings.LidarEnabled && ModSettings.LidarOverlay)
        {
            Add(LidarOverlay.Infrared
                ? Action("LIDAR: infrared view on - click for the dark overlay",
                    "The game's own map, with the beams drawn over it. Click again for the dark "
                    + "overlay, which is the one that makes a thin beam easy to see.", () =>
                    {
                        LidarOverlay.ToggleInfrared();
                        ModLog.Info("Menu: LIDAR back to the overlay view");
                    })
                : Action("LIDAR: overlay view on - click for infrared",
                    "The dark sea with the beam as the only light: emitter glowing, trail fading "
                    + "behind the head, ring at the range. The key is "
                    + ModSettings.LidarOverlayKey + " as well. Both views are drawn over the game's "
                    + "map - the mod cannot replace it, and does not pretend to.", () =>
                    {
                        LidarOverlay.ToggleInfrared();
                        ModLog.Info("Menu: LIDAR infrared view on");
                    }), MenuRouting.Sensors);

            // The one entry that answers "I turned it on and I see nothing". It writes the same
            // file as everything else, but it says in one line which of the four causes it is,
            // instead of leaving a person to read a report to find out.
            Add(Action("LIDAR: nothing on the map? - click to check why",
                LidarOverlay.Diagnose(),
                () =>
                {
                    Lidar.WriteReport("the menu asked why the overlay is empty");
                    ModLog.Info("Menu: LIDAR diagnosis written to lidar.txt");
                }), MenuRouting.Sensors);
        }

        // --- ECM: only on hulls that actually carry it --------------------------------------
        // A submarine has no jammer, so it gets no ECM entries at all - not a disabled one, nothing.
        // The same is now true of a surface hull with no set: a Huak class carries a radar and an
        // RWR, and an ECM entry on it could only ever radiate for nothing.
        //
        // "No jammer" has to be read, not assumed. The fit is asked once here and the group is built
        // from the answer: a hull that positively carries no set gets no ECM submenu - the same rule
        // a submarine has always had - and a hull whose fit could not be read gets the group and one
        // line saying so, instead of entries that would act on a maybe.
        OffensiveJam.JammerFit fit = UnitFit.EcmFit(unit, out string fitEvidence);
        bool carriesEcm = fit != OffensiveJam.JammerFit.None;

        // And only on a ship this machine commands. In a co-op session the fleet holds the other
        // player's ships as well, and a menu that arms their jammer is how one client switched
        // another client's defensive ECM on. One line says whose the ship is instead.
        bool commands = Multiplayer.Commands(unit, out string notMine);

        if (!commands)
        {
            Grouped(EcmGroup, Disabled("ECM: this ship is not this machine's to command",
                notMine + "."), MenuRouting.Sensors);

            carriesEcm = false;
        }

        if (carriesEcm && fit != OffensiveJam.JammerFit.Fitted)
        {
            Grouped(EcmGroup, Disabled("ECM: this hull's jammer fit could not be read",
                fitEvidence + ". An ecm-report.txt written from this menu lists everything the mod "
                + "could see on this install."), MenuRouting.Sensors);
        }

        if (carriesEcm && fit == OffensiveJam.JammerFit.Fitted)
        {
            if (Multiplayer.IsClient)
            {
                // Missile defence is resolved by the host: weapons are simulated there and
                // replicated, so a jam written on a client is overwritten and jams nothing. One
                // plain entry, with the reason, beats an armed feature that never fires.
                Grouped(EcmGroup, Disabled("Defensive ECM: the host runs missile defence in multiplayer",
                    Multiplayer.ClientRefusal("Defensive ECM")
                    ?? "Weapons are simulated by the host, so jamming has to be armed there."),
                    MenuRouting.Sensors);
            }
            else
            {
                Grouped(EcmGroup, DefensiveEcm.IsArmed(unit)
                    ? Action("Defensive ECM: " + DefensiveEcm.Describe(unit) + " - click to disarm",
                        "Stops jamming incoming missiles automatically, and stops putting the close "
                        + "ones in the water.",
                        () => Report(unit, "defensive ECM", DefensiveEcm.Toggle(unit, out string r), r))
                    : Action("Defensive ECM: off - click to arm automatic jamming",
                        "Points this ship's jammer at any missile inbound to it or to the task force, "
                        + "and holds the lock. Any missile that gets inside "
                        + (int)DefensiveEcm.DiveNm + " nm has its lock broken and is put in the "
                        + "water. Needs air search; offensive jamming does not.",
                        () => Report(unit, "defensive ECM", DefensiveEcm.Toggle(unit, out string r), r)),
                    MenuRouting.Sensors);

                Grouped(EcmGroup, DefensiveEcmFleetItem(unit), MenuRouting.Sensors);
            }

            // Offensive ECM stays on offer in a session: the jam travels to the host as the game's
            // own jam order, so the host runs it with the game's own ECM.
            Grouped(EcmGroup, OffensiveEcm.ModeOf(unit) == OffensiveEcm.Mode.SignatureLock
                ? Action("Offensive ECM: focused - click for standard",
                    "Releases the lock and hands the jammer back to the game's own behaviour.",
                    () => Report(unit, "offensive ECM",
                        OffensiveEcm.SetMode(unit, OffensiveEcm.Mode.Standard, out string r), r))
                : Action("Offensive ECM: standard - click for focused",
                    "Focused runs on EMCON: the ship radiates nothing of its own. No radar is"
                    + " switched on for a jam.",
                    () => Report(unit, "offensive ECM",
                        OffensiveEcm.SetMode(unit, OffensiveEcm.Mode.SignatureLock, out string r), r)),
                MenuRouting.Sensors);

            // The lock list is on offer whatever the mode says. Hiding it behind the mode toggle was
            // half of why the menu looked broken: the toggle changed a label and nothing else
            // happened, because there was no way to pick a contact. Taking a lock also switches the
            // mode, so the toggle catches up on its own.
            Grouped(EcmGroup, OffensiveLockMenu(unit), MenuRouting.Sensors);

            if (OffensiveEcm.IsLocked(unit))
            {
                // Two halves, and the menu says which one it is offering. The game's own jam order
                // is already running from the lock; this second half holds the target's own sets
                // down as well - as a burst, because a jammer that blinds a ship for ever is not a
                // model of anything.
                int held = OffensiveEcm.HoldLeft(unit);
                bool canHold = OffensiveEcm.CanHold(unit, out string why);

                Grouped(EcmGroup, OffensiveEcm.IsHolding(unit)
                    ? Action("Offensive ECM: holding its sensors down"
                             + (held > 0 ? " (" + held + " s)" : "") + " - click to stop",
                        "Stops holding the contact's sets down. The game's own jam continues, and "
                        + "its crew get the recovery window with their sensors back.",
                        () => Report(unit, "stop holding", OffensiveEcm.StopJam(unit, out string r), r))
                    : canHold
                        ? Action("Offensive ECM: hold its sensors down too",
                            "Switches the contact's own sensors off and keeps them off, on top of "
                            + "the game's jam - for one window, after which its crew get their "
                            + "sensors back for a while.",
                            () => Report(unit, "hold down", OffensiveEcm.JamNow(unit, out string r), r))
                        : Disabled("Offensive ECM: cannot hold it down - " + why,
                            "The game's jam on this contact is running anyway. Holding its own "
                            + "sets down also needs a free suppression channel in the task force "
                            + "and the contact's crew to have finished recovering from the last "
                            + "hold; both limits are in the mod's settings."),
                    MenuRouting.Sensors);
            }

            // The budget line is only worth the space once the focused mode is in use: in
            // standard mode the mod does not hold anything down, so a channel count there would be
            // noise on every hull that carries a jammer.
            if (OffensiveEcm.ModeOf(unit) == OffensiveEcm.Mode.SignatureLock)
            {
                Grouped(EcmGroup, Disabled("Offensive ECM: " + OffensiveEcm.ChannelLine(),
                    "The task force's suppression budget: how many contacts can have their own "
                    + "sensors held down at once. Every lock still gets the game's jam."),
                    MenuRouting.Sensors);
            }

            Grouped(EcmGroup, Disabled("Offensive ECM: " + OffensiveEcm.Describe(unit),
                "What the focused jammer is doing right now - its own state and the game's."),
                MenuRouting.Sensors);
        }

        if (UnitFit.IsSurfaceHull(unit))
        {
            // Always offered on a surface hull, whether or not a jammer was found: this is the entry
            // that says which it is, and it is how a hull the mod has got wrong can be shown in one
            // file without a debugger. On a hull with no set it is the only ECM entry there is.
            Grouped(EcmGroup, Action(fit == OffensiveJam.JammerFit.None
                    ? "ECM: no jammer on this hull - click for the report"
                    : "ECM: what this build exposes",
                "Writes ecm-report.txt and logs what the jammer interface looks like in this install.",
                () =>
                {
                    EcmControl.WriteReport();
                    ModLog.Info("Menu: ECM report written - " + EcmControl.Summary());
                }), MenuRouting.Sensors);
        }

        // --- the datalink to the other player's force ------------------------------------------
        // A co-op mission with a side per player has two task forces, and every contact picture in
        // this game belongs to one. This is the switch that hands ours to theirs.
        Grouped(LinkGroup, Datalink.On
            ? Action("Datalink: " + Datalink.Describe() + " - click to stop sharing",
                "Stops handing this force's picture to the other player's force. Tracks already "
                + "passed age out on the game's own sensor clock.",
                () => Report(unit, "datalink", Datalink.Toggle(out string r), r))
            : Action("Datalink: off - click to share this force's picture",
                "Hands every contact this force holds to the other player's force, through the game's "
                + "own intel-sharing call: position only, never an identity. Only in a co-op session, "
                + "and never to a force we are plotting as a contact. datalink.txt records exactly "
                + "what was handed over and what the far side's table did with it.",
                () => Report(unit, "datalink", Datalink.Toggle(out string r), r)),
            MenuRouting.Sensors);

        Grouped(LinkGroup, Action("Datalink: resynchronise both pictures",
            "Asks the multiplayer mod to re-run its own contact sweeps as well as taking one here. "
            + "The cheapest thing to try when one side has gone stale.",
            () => Report(unit, "datalink resync", Datalink.Resync(out string r), r)),
            MenuRouting.Sensors);

        Grouped(LinkGroup, Action("Datalink: what either side can see",
            "Writes datalink.txt: this machine's forces and their pictures, what was handed over, "
            + "and what the multiplayer mod's own contact sharing is doing.",
            () =>
            {
                Datalink.WriteReport("asked from the menu");
                ModLog.Info("Menu: datalink report written - " + Datalink.Describe());
            }), MenuRouting.Sensors);

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

        // --- Air operations: only on hulls that can put an aircraft up ----------------------
        if (UnitFit.HasAviation(unit))
        {
            Add(Action("Fly a ladder search", "Launches (if needed) and flies a ladder pattern.",
                () => Report(unit, "helo ladder", HeloPatterns.Fly(unit, SearchPattern.Ladder, out string r), r)),
                MenuRouting.Air);

            Add(Action("Fly an expanding square", "Launches (if needed) and flies an expanding square.",
                () => Report(unit, "helo square", HeloPatterns.Fly(unit, SearchPattern.ExpandingSquare, out string r), r)),
                MenuRouting.Air);

            Add(Action("Fly a sector search", "Launches (if needed) and sweeps a sector.",
                () => Report(unit, "helo sector", HeloPatterns.Fly(unit, SearchPattern.Sector, out string r), r)),
                MenuRouting.Air);
        }

        // --- Damage control ------------------------------------------------------------------
        Add(LocalControl.IsLocal(unit)
            ? Action("Local control: on - click to return to ship control",
                "Puts the mounts back under the ship's own picture.",
                () => Report(unit, "local control", LocalControl.Toggle(unit, out string r), r))
            : Action("Local control: off - click for local control",
                "Guns and CIWS fight their own battle when the picture is lost.",
                () => Report(unit, "local control", LocalControl.Toggle(unit, out string r), r)),
            MenuRouting.Damage, MenuRouting.Weapons);

        if (UnitFit.IsSurfaceHull(unit))
        {
            Add(Action("Replenish alongside", "Takes on ammunition from a supply ship within 2 nm.",
                () => Report(unit, "replenishment", Replenishment.Alongside(unit, out string r), r)),
                MenuRouting.Logistics, MenuRouting.Damage);
        }

        // --- Top level: the emergency action ------------------------------------------------
        Add(Action(BreakawayLabel, "Full ahead, hard rudder, chaff away - all at once, now.",
            () => Report(unit, "breakaway", Breakaway.Execute(unit, out string r), r)),
            Array.Empty<string>());

        return entries;
    }

    /// <summary>
    /// The radar-blink submenu: which set to switch on for a few seconds and off again. Air search
    /// only appears if the hull has it, and likewise surface search - the same rule as everywhere
    /// else, so a submarine is not offered a radar it does not carry.
    /// </summary>
    private static ContextMenuItem BlinkRadarItem(ObjectBase unit)
    {
        var items = new List<ContextMenuItem>();

        bool? airFit = SensorControl.AirSearchFit(unit);
        bool? surfaceFit = SensorControl.SurfaceSearchFit(unit);

        if (airFit != false)
        {
            items.Add(Action("Air search radar",
                "Sweep the air-search radar on and off. This is the set an anti-radiation missile "
                + "homes on.",
                () => Report(unit, "blink (air)", Blink.Start(unit, BlinkRadar.AirSearch, out string r), r)));
        }

        if (surfaceFit != false)
        {
            items.Add(Action("Surface search radar",
                "Sweep the surface-search radar on and off: still sees ships, much harder to home on.",
                () => Report(unit, "blink (surface)",
                    Blink.Start(unit, BlinkRadar.SurfaceSearch, out string r), r)));
        }

        if (airFit != false && surfaceFit != false)
        {
            items.Add(Action("Both radars",
                "Cycle both sets together. Two intermittent emissions, one timer.",
                () => Report(unit, "blink (both)", Blink.Start(unit, BlinkRadar.Both, out string r), r)));
        }

        if (items.Count == 0)
        {
            items.Add(Disabled("This hull has no radar to cycle",
                "Blink needs an air-search or surface-search radar."));
        }

        return new ContextMenuItem(
            label: "Radar blink: off",
            items: new TrulyObservableCollection<ContextMenuItem>(items),
            tooltip: "Switch a radar on for a few seconds, then off again.");
    }

    /// <summary>
    /// The lock menu: one entry per hostile contact the ship can actually see, plus release.
    ///
    /// The list is rebuilt every time the menu opens from the ship's own contact picture, so a
    /// target that has been sunk or has run out of range is not offered. Nothing here invents a
    /// contact the force cannot see.
    /// </summary>
    private static ContextMenuItem OffensiveLockMenu(ObjectBase unit)
    {
        var items = new List<ContextMenuItem>();

        List<ObjectBase> contacts = OffensiveEcm.Contacts(unit);
        int offered = 0;

        foreach (ObjectBase contact in contacts)
        {
            if (offered >= 8) break;
            offered++;

            bool air = UnitFit.IsAir(contact);
            bool? identified = ContactPicture.Identified(contact);
            string unknown = identified == false ? "unidentified, " : "";

            items.Add(Action("Jam " + ContactName(contact) + " - " + unknown + UnitFit.Kind(contact)
                             + " (" + ContactRange(unit, contact) + " nm)",
                "Points this ship's jammer at the contact through the game's own jam order and "
                + "keeps it pointed. It does not have to be identified: an unidentified track is "
                + "jammed on its bearing, which is the order the game provides for that case."
                + (air
                    ? " An aircraft is reached at the air range - both ends are high above the "
                      + "radio horizon."
                    : ""),
                () => Report(unit, "offensive jam",
                    OffensiveEcm.Lock(unit, contact, out string r), r)));
        }

        if (offered == 0)
        {
            items.Add(Disabled("No hostile contact within " + (int)ModSettings.JamRangeNm + " nm",
                "The task force's picture has to hold the contact before the jammer can be pointed "
                + "at it - the mod does not invent targets. Aircraft are reached out to "
                + (int)ModSettings.JamAirRangeNm + " nm. offensive-ecm.txt says how many contacts "
                + "the picture held."));
        }

        items.Add(Action("Release the lock", "Stops the jam through the game's own teardown.",
            () => Report(unit, "offensive release", OffensiveEcm.Release(unit, out string r), r)));

        return new ContextMenuItem(
            label: "Offensive ECM lock (" + OffensiveEcm.ContactSummary(unit) + ")",
            items: new TrulyObservableCollection<ContextMenuItem>(items),
            tooltip: "Point this ship's jammer at one contact - ship, submarine or aircraft - "
                     + "identified or not.");
    }

    /// <summary>Any of our units carrying a jammer, whether or not it is in range of a particular
    /// contact. Used only to tell "no jammer in the fleet" apart from "none near enough".</summary>
    private static ObjectBase? AnyJammer()
    {
        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            try
            {
                if (!OffensiveJam.HasEcm(ship)) continue;
                if (!Multiplayer.Commands(ship, out _)) continue;

                return ship;
            }
            catch (Exception) { }
        }

        return null;
    }

    private static string ContactName(ObjectBase contact)
    {
        try { return contact.DisplayNameShort; }
        catch (Exception) { return "<contact>"; }
    }

    private static int ContactRange(ObjectBase ship, ObjectBase contact)
    {
        try { return (int)ThreatAxis.Distance(ship.getGeoPosition(), contact.getGeoPosition()); }
        catch (Exception) { return -1; }
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

    /// <summary>
    /// The task force's defensive-ECM switch: one entry that says what the whole force is doing and
    /// turns it the other way.
    ///
    /// It used to arm and nothing else. The entry said *for the whole task force* and always ran the
    /// arming path, so a second click repeated an order that had already been given, the label never
    /// changed, and the only way back was ship by ship through the per-ship entry. A switch is what
    /// the label promises, so it is what this is: the same entry arms when the force is off and
    /// stands it down when anything in it is on.
    ///
    /// The counts come from <see cref="DefensiveEcm.FleetState"/>, which uses the arming's own rule,
    /// so the number on the label is the number the click will change.
    /// </summary>
    private static ContextMenuItem DefensiveEcmFleetItem(ObjectBase unit)
    {
        DefensiveEcm.FleetStanding standing = DefensiveEcm.FleetState();

        if (standing.Offered == 0)
        {
            // Nothing to switch. An entry that looks broken is worse than one that says why, and the
            // counts are read the same way the arming reads them.
            return Disabled("Defensive ECM: no ship under command carries a jammer",
                "Counted from each ship's own sensor list: " + standing.NoJammer + " with no ECM set, "
                + standing.Unreadable + " whose fit could not be read"
                + (standing.Others > 0
                    ? ", " + standing.Others + " not this machine's to command"
                    : "")
                + ". A hull with no jammer cannot jam, so the mod will not show one doing it - and "
                + "arming a force that has nothing to arm would do exactly that. ecm-report.txt lists "
                + "what was read from each hull.");
        }

        if (standing.Armed == 0)
        {
            return Action("Defensive ECM: task force off - click to arm " + standing.Ready + " ship(s)",
                "Arms automatic defensive jamming on every ship under command that carries a jammer "
                + "- " + standing.Ready + " of the " + standing.Ships + " in the picture. Each armed "
                + "ship points its own set at a missile inbound to it or to the task force, holds the "
                + "lock, and puts anything that gets inside " + (int)DefensiveEcm.DiveNm + " nm in the "
                + "water. Click this same entry again to stand the whole force down.",
                () => DefensiveEcmFleetSwitch(unit, true));
        }

        return Action(
            standing.Ready > 0
                ? "Defensive ECM: on for " + standing.Armed + " of " + standing.Offered
                  + " ship(s) - click to disarm the task force"
                : "Defensive ECM: task force armed (" + standing.Armed + " ship(s)) - click to disarm",
            "Stands the whole task force down: every armed ship stops jamming, any radar the mod "
            + "switched on for it goes back the way it was, and the close-in dive stops with it. Ships "
            + "armed one at a time are included. In a session the host is the machine that runs "
            + "missile defence, "
            + "so a hull left armed there would go on jamming - which is why this click does not skip "
            + "one. Nothing else about the ship changes.",
            () => DefensiveEcmFleetSwitch(unit, false));
    }

    private static void DefensiveEcmFleetSwitch(ObjectBase unit, bool on)
    {
        int changed = DefensiveEcm.ArmFleet(on, out string report);

        // Both an answer and a refusal are worth a line in the log and a moment on screen: this entry
        // acts on ships the player may not be looking at, so "nothing happened" is the one thing it
        // must not leave them guessing about.
        Report(unit, "defensive ECM (task force)", changed > 0, report);
    }

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
