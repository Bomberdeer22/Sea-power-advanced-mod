using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;
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

        foreach ((ContextMenuItem item, string[][] route) in Entries(unit))
        {
            try
            {
                string where = MenuRouting.Place(roots, live, item, route);
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
            bool locked = OffensiveEcm.IsLocked(jammer) &&
                          ReferenceEquals(OffensiveEcm.TargetOf(jammer), contact);

            if (locked)
            {
                bool holding = OffensiveEcm.IsHolding(jammer);

                bool named = ContactPicture.Identified(contact) != false;

                var transmit = named ? new ContextMenuItem(
                    label: holding
                        ? "Offensive ECM: hold its sensors down - on, click to stop"
                        : "Offensive ECM: hold its sensors down",
                    items: null,
                    command: new DelegateCommand(_ => Report(contact,
                        holding ? "stop holding" : "hold down",
                        holding ? OffensiveEcm.StopJam(jammer, out string s2)
                                : OffensiveEcm.JamNow(jammer, out s2), s2)),
                    tooltip: holding
                        ? "Stops holding the contact's individual sets down. The game's own jam "
                          + "order stays, so the jam itself continues."
                        : "The game's jam is already working on it. This adds the second half: "
                          + "switch the contact's own sensors off and keep them off. Needs the "
                          + "contact to be identified, because there is nothing to hold down on a "
                          + "track nobody has named.")
                    : Disabled("Offensive ECM: hold its sensors down - needs it identified",
                        "An unidentified contact is jammed on its bearing, which is the game's own "
                        + "jam and is already running. Its sensor fit cannot be held down until the "
                        + "picture knows whose it is.");

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

        // Sonar entries only on hulls that have the fit. Unknown counts as fitted: the game does
        // not always expose a HasX for every hull, and a hidden feature looks like a broken one.
        if (SensorControl.ActiveSonarFit(unit) != false)
        {
            Add(Action("Active sonar on", "Go active: finds submarines, gives the ship away.",
                () => Act(unit, "active sonar on", u => SensorControl.SetActiveSonar(u, true))), MenuRouting.Sensors);

            Add(Action("Active sonar off", "Passive only.",
                () => Act(unit, "active sonar off", u => SensorControl.SetActiveSonar(u, false))), MenuRouting.Sensors);
        }

        if (SensorControl.TowedArrayFit(unit) != false)
        {
            Add(Action("Deploy towed array", "Stream the towed array.",
                () => Act(unit, "towed array out", u => SensorControl.SetTowedSonar(u, true))), MenuRouting.Sensors);

            Add(Action("Retract towed array", "Bring the array in before making speed.",
                () => Act(unit, "towed array in", u => SensorControl.SetTowedSonar(u, false))), MenuRouting.Sensors);
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
            Add(Disabled("Auto-ARM shutoff: not available in multiplayer",
                "Cutting the radars is a radar switch, and the host decides those in a session."),
                MenuRouting.Sensors);
        }
        else
        {
            Add(AutoArm.IsOn(unit)
                ? Action("Auto-ARM shutoff: on - click to turn off",
                    "Stops cutting the radars automatically.",
                    () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r))
                : Action("Auto-ARM shutoff: off - click to turn on",
                    "Cuts the radars for eight seconds whenever a weapon is inbound.",
                    () => Report(unit, "auto-ARM", AutoArm.Toggle(unit, out string r), r)),
                MenuRouting.Sensors);
        }

        // --- ECM: only on hulls that actually carry it --------------------------------------
        // A submarine has no jammer, so it gets no ECM entries at all - not a disabled one, nothing.
        // The wording here also stops promising that this mode radiates: it runs on EMCON.
        bool carriesEcm = UnitFit.HasEcm(unit);

        if (carriesEcm)
        {
            if (Multiplayer.IsClient)
            {
                // Missile defence is resolved by the host: weapons are simulated there and
                // replicated, so a jam written on a client is overwritten and jams nothing. One
                // plain entry, with the reason, beats an armed feature that never fires.
                Add(Disabled("Defensive ECM: the host runs missile defence in multiplayer",
                    "Weapons are simulated by the host, so automatic jamming has to be armed there."),
                    MenuRouting.Sensors);
            }
            else
            {
                Add(DefensiveEcm.IsArmed(unit)
                    ? Action("Defensive ECM: " + DefensiveEcm.Describe(unit) + " - click to disarm",
                        "Stops jamming incoming missiles automatically.",
                        () => Report(unit, "defensive ECM", DefensiveEcm.Toggle(unit, out string r), r))
                    : Action("Defensive ECM: off - click to arm automatic jamming",
                        "Points this ship's jammer at any missile inbound to it or to the task force, "
                        + "and holds the lock. Defensive jamming needs air search; offensive does not.",
                        () => Report(unit, "defensive ECM", DefensiveEcm.Toggle(unit, out string r), r)),
                    MenuRouting.Sensors);

                Add(Action("Defensive ECM for the whole task force",
                    "Arms automatic defensive jamming on every ship under command that carries ECM.",
                    () => Fleet("defensive ECM (fleet)", DefensiveEcm.ArmFleet(true, out string r), r)),
                    MenuRouting.Sensors);
            }

            // Offensive ECM stays on offer in a session: the jam travels to the host as the game's
            // own jam order, so the host runs it with the game's own ECM.
            Add(OffensiveEcm.ModeOf(unit) == OffensiveEcm.Mode.SignatureLock
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
            Add(OffensiveLockMenu(unit), MenuRouting.Sensors);

            if (OffensiveEcm.IsLocked(unit))
            {
                // Two halves, and the menu says which one it is offering. The game's own jam order
                // is already running from the lock; this second half holds the target's own sets
                // down as well.
                bool lockNamed = ContactPicture.Identified(OffensiveEcm.TargetOf(unit)) != false;

                Add(OffensiveEcm.IsHolding(unit)
                    ? Action("Offensive ECM: holding its sensors down - click to stop",
                        "Stops holding the contact's sets down. The game's own jam continues.",
                        () => Report(unit, "stop holding", OffensiveEcm.StopJam(unit, out string r), r))
                    : lockNamed
                        ? Action("Offensive ECM: hold its sensors down too",
                            "Switches the contact's own sensors off and keeps them off, on top of "
                            + "the game's jam.",
                            () => Report(unit, "hold down", OffensiveEcm.JamNow(unit, out string r), r))
                        : Disabled("Offensive ECM: hold its sensors down - needs it identified",
                            "The game's jam is already running on this contact. Its own sensor fit "
                            + "can only be held down once the picture knows whose it is."),
                    MenuRouting.Sensors);
            }

            Add(Disabled("Offensive ECM: " + OffensiveEcm.Describe(unit),
                "What the focused jammer is doing right now - its own state and the game's."),
                MenuRouting.Sensors);
        }

        if (UnitFit.IsSurfaceHull(unit))
        {
            Add(Action("ECM: what this build exposes",
                "Writes ecm-report.txt and logs what the jammer interface looks like in this install.",
                () =>
                {
                    EcmControl.WriteReport();
                    ModLog.Info("Menu: ECM report written - " + EcmControl.Summary());
                }), MenuRouting.Sensors);
        }

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
