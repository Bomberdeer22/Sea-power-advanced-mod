using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// A laser rangefinder and sight: what a warship's optronics actually give a watch team, built out of
/// what this game will let a mod touch.
///
/// <b>What the game is missing.</b> The sensor families a hull's parameter block lists are radar,
/// sonar, ESM/RWR and ECM. There is no laser, no optronic director, and no passive visual set in the
/// list - so a ship in Sea Power with every radar silent is blind beyond what its ESM hears, even
/// when it is carrying the kind of optical director every modern escort has. That is the gap.
///
/// <b>What this does about it.</b> A hull whose own sensor list holds something to look through may be
/// ordered to <i>look</i> with it - a laser or optronic director where the hull carries one, and the
/// game's own <b>Eyes Mk 1</b> lookouts where it carries only those. The lookouts are a man with
/// binoculars, so they reach a shorter, separately configured distance; the director gets the full
/// range. Every sweep, for as long as the order stands:
///
/// <list type="number">
/// <item>the player's own contact picture is read - the same picture
///   <see cref="ContactPicture.Hostiles"/> reads, and the same one the offensive jammer aims from;</item>
/// <item>a contact inside the order's range, inside the order's arc of the hull's heading, on the
///   surface (or airborne, for a director that can look up) and <b>already in that picture</b> is
///   put on the hull's own picture through the game's own reveal call -
///   <c>Utils.RevealContactToObject</c>, the route a reconnaissance report uses;</item>
/// <item>and nothing else. A contact the picture does not already hold is never looked for. This is
///   not a search radar in disguise: the LIDAR confirms and fixes what the force already knows
///   about, at a range and on a bearing a laser can actually reach.</item>
/// </list>
///
/// <b>Why the fog-of-war rule is the feature, not a limitation of it.</b> Letting the mod look for
/// contacts would mean the mod deciding what exists in the world - a wallhack with a laser's name on
/// it. Requiring a prior sighting is also how the real thing works: a ship does not sweep the sea
/// with a rangefinder, it puts a glass on a bearing it was given by somebody else's picture.
///
/// <b>One target at a time, and the hull pays for it.</b> A laser is a single beam on a single
/// bearing, so one contact at a time per hull (a setting), it takes a few seconds to settle, and it
/// is a bearing-and-range gate rather than a hemisphere: the contact has to be inside the arc. A hull
/// that already detects a contact with its own sensors is not given it again - its own fix is better
/// and the game's own track errors are the point.
///
/// <b>Radar silent by construction.</b> This feature switches no radar on and no radar off. It only
/// ever writes a track onto the hull's picture, so it works identically on a ship at EMCON silent,
/// and no emission of the hull's changes because of it.
/// </summary>
internal static class Lidar
{
    private static float nextSweep;
    private static int sweeps;

    /// <summary>The last sweep's verdict, for the menu and the report.</summary>
    private static string lastVerdict = "nothing swept yet";

    private static float lastLoggedAt = -60f;

    /// <summary>
    /// The overlay's calibration reading, kept so it reaches <c>lidar.txt</c>.
    ///
    /// The overlay is the only thing that can measure where the projection actually lands, and the
    /// report is the only thing that leaves the machine, so the two have to meet somewhere. They meet
    /// here rather than in the overlay, because a report is written on a click and a calibration is
    /// written on the first frame - either may come first.
    /// </summary>
    private static List<string>? calibration;

    /// <summary>Set when the overlay has measured something new; cleared by the ticker, which does
    /// the writing.</summary>
    private static bool calibrationPending;

    /// <summary>Contacts this machine has put on a hull's picture with the laser, this sweep.</summary>
    private static int sighted;

    /// <summary>True when this hull has the order. Reads the record without creating one: the
    /// ticker asks every hull in the fleet, and a state bag that grows a record per ship per sweep is
    /// a state bag that never shrinks.</summary>
    internal static bool IsOn(ObjectBase unit) =>
        unit != null && UnitState.TryGet(unit, out UnitRecord record) && record.Lidar;

    /// <summary>Orders this hull to look with its laser, or stops it looking.</summary>
    internal static bool Toggle(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        UnitRecord record = UnitState.Of(unit);

        // Standing down is unconditional: an order given on a hull whose fit has since been read as
        // "no director" must still be cancellable.
        if (record.Lidar)
        {
            record.Lidar = false;
            report = "laser off - the tracks it gave the ship age out on the game's own sensor clock";
            ModLog.Info("LIDAR: " + Name(unit) + ": " + report);
            return true;
        }

        if (!UnitFit.IsPlayer(unit))
        {
            report = Name(unit) + " is not this machine's to command";
            ModLog.Warn("LIDAR: " + report);
            return false;
        }

        if (UnitFit.IsSubmarine(unit))
        {
            // A submarine's periscope or mast is a real thing and this is deliberately not it: a boat
            // under the surface has no line of sight to anything, and pretending otherwise would put
            // a track on the picture of a boat that could not have seen it.
            report = Name(unit) + " is a submarine - a laser needs a line of sight, and a boat at "
                     + "depth has none. Surfaced, use the periscope yourself";
            ModLog.Warn("LIDAR: " + report);
            return false;
        }

        LidarControl.Fit fit = LidarControl.Detect(unit);

        if (!fit.Allowed)
        {
            report = Name(unit) + " may not look: " + fit.Evidence
                     + " (LidarAnyHull is off, so only a hull whose own sensor list shows an "
                     + "optical set may look)";
            ModLog.Warn("LIDAR: " + report);
            return false;
        }

        record.Lidar = true;

        report = "looking with " + fit.Basis + " - " + fit.Evidence;
        ModLog.Info("LIDAR: " + Name(unit) + ": " + report);

        // Answer with evidence rather than a promise: look once, now.
        Sweep(force: true);

        // The click's answer is what the sweep found, not what the order means.
        report += ". " + lastVerdict;

        return true;
    }

    /// <summary>One line for the menu: what the hull is looking at, if anything.</summary>
    internal static string Describe(ObjectBase unit)
    {
        if (unit == null || !IsOn(unit)) return "off";

        UnitRecord record = UnitState.Of(unit);

        return record.LidarSighted.Length > 0
            ? "on, holding " + record.LidarSighted
            : "on, beam at " + (int)record.LidarBearing + " deg, nothing on it";
    }

    /// <summary>Writes <c>lidar.txt</c>: the fit by hull, the order in force, and every refusal.</summary>
    internal static void WriteReport(string why)
    {
        try
        {
            var text = new System.Text.StringBuilder();

            text.AppendLine("Sea Power Advanced - the LIDAR");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine("because: " + why);
            text.AppendLine();

            text.AppendLine("What this is, in one paragraph");
            text.AppendLine("--------------------------------");
            text.AppendLine("  A rotating 360-degree set mounted on the hull. It turns at LidarSpinRpm and");
            text.AppendLine("  sees whatever its beam passes over inside LidarRangeNm - and only that: a");
            text.AppendLine("  contact is sighted when the beam is on it, not because it is near. The map");
            text.AppendLine("  overlay draws the spoke turning, the range ring and a mark on whatever the");
            text.AppendLine("  beam is holding; F10 (LidarOverlayKey) flips the map into the infrared view.");
            text.AppendLine("  and every contact the FORCE ALREADY HOLDS that is inside the range and the");
            text.AppendLine("  arc is put on that hull's picture through the game's own reveal call - the");
            text.AppendLine("  route a reconnaissance report uses. Position only: no identity, no class.");
            text.AppendLine();
            text.AppendLine("  Every hull carries it, whether or not its data names an optical sensor");
            text.AppendLine("  (LidarAnyHull, on by default): the set is the mod's, mounted for you, which is");
            text.AppendLine("  what the feature was asked for. What each hull's own data holds is still");
            text.AppendLine("  printed below - LASER DIRECTOR, LOOKOUTS LISTED or LOOKOUTS ASSUMED - so the");
            text.AppendLine("  assumption is visible rather than buried.");
            text.AppendLine();
            text.AppendLine("  It never looks for a contact the picture does not already hold. That is the");
            text.AppendLine("  difference between a sight and a wallhack, and it is also how the real");
            text.AppendLine("  thing works: a rangefinder is pointed on a bearing somebody else's picture");
            text.AppendLine("  gave you, not swept across the sea.");
            text.AppendLine();

            text.AppendLine("The limit, stated plainly");
            text.AppendLine("-------------------------");
            text.AppendLine("  THIS GAME HAS NO LASER SENSOR. The families a hull's parameter block lists");
            text.AppendLine("  are radar, sonar, ESM/RWR and ECM; nothing in that list is an optical or");
            text.AppendLine("  laser set, and a mod cannot add one - detection is simulated inside the");
            text.AppendLine("  game. So this is a mod-side aid built out of the game's own reveal call, and");
            text.AppendLine("  the hull's sensor list is used only to decide WHICH hulls may have it: a hull");
            text.AppendLine("  with no optical or laser director in its list is refused, and the report says");
            text.AppendLine("  what its list actually holds.");
            text.AppendLine();
            text.AppendLine("  Because the director may not exist as a sensor system at all, most base-game");
            text.AppendLine("  hulls will be refused. That is the feature being honest rather than the");
            text.AppendLine("  feature being broken: the list below is what your install says about your");
            text.AppendLine("  hulls, and a hull it refuses genuinely has nothing to look through.");
            text.AppendLine();

            text.AppendLine("The order in force");
            text.AppendLine("------------------");
            text.AppendLine("  setting:          LidarEnabled=" + (ModSettings.LidarEnabled ? "on" : "off")
                            + ", every hull may scan: " + (ModSettings.LidarAnyHull ? "yes" : "no")
                            + ", range " + (int)ModSettings.LidarRangeNm + " nm, 360 deg"
                            + ", beam " + (int)ModSettings.LidarBeamDeg + " deg"
                            + ", " + (int)ModSettings.LidarSpinRpm + " rpm"
                            + ", report every " + ModSettings.LidarSeconds.ToString("0.#") + " s");
            text.AppendLine("  overlay:          " + (ModSettings.LidarOverlay ? "on" : "off")
                            + ", infrared key " + ModSettings.LidarOverlayKey);
            text.AppendLine("  sweeps run:       " + sweeps);
            text.AppendLine("  last verdict:     " + lastVerdict);
            text.AppendLine("  contacts sighted: " + sighted);
            text.AppendLine();

            var looked = new List<string>();
            int ordered = 0;


            text.AppendLine("Hulls, and what their own sensor list says");
            text.AppendLine("-----------------------------------------");

            foreach (ObjectBase hull in FleetTicker.Tracked())
            {
                try
                {
                    LidarControl.Fit fit = LidarControl.Detect(hull);
                    LidarControl.Remember(hull, fit, looked);

                    if (!IsOn(hull)) continue;

                    ordered++;

                    UnitRecord record = UnitState.Of(hull);

                    text.AppendLine("  ORDERED: " + Name(hull) + " - " + Describe(hull));
                    text.AppendLine("    beam: " + (int)record.LidarBearing + " deg, "
                                    + (int)ModSettings.LidarRangeNm + " nm, "
                                    + (int)ModSettings.LidarBeamDeg + " deg wide, "
                                    + (int)ModSettings.LidarSpinRpm + " rpm");
                    text.AppendLine("    fit: " + fit.Basis + "   (evidence: " + fit.Evidence + ")");
                }
                catch (Exception) { }
            }

            foreach (string line in looked)
            {
                text.AppendLine("  " + line);
            }

            if (looked.Count > 0 && looked.Count < FleetTicker.Tracked().Count)
                text.AppendLine("  (one line per hull class: a class's sensor fit does not change)");

            if (looked.Count == 0)
            {
                text.AppendLine("  no player hulls found to ask. Select one and run this again: the fit is");
                text.AppendLine("  read from the ship, not from the game assembly.");
            }

            if (ordered == 0 && looked.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("  no hull is looking with its laser right now.");
            }

            text.AppendLine();
            text.AppendLine("Where the beam was actually drawn");
            text.AppendLine("----------------------------------");
            text.AppendLine("  The one thing the mod cannot know is where the game's map is on the screen:");
            text.AppendLine("  the projection's arithmetic comes from the game's own tile grid, but the");
            text.AppendLine("  camera it is projected through is a choice, and an error of half a screen");
            text.AppendLine("  looks exactly like a beam that is not being drawn at all.");
            text.AppendLine();

            if (calibration == null || calibration.Count == 0)
            {
                text.AppendLine("  not measured yet - the overlay writes this the first time it draws a");
                text.AppendLine("  beam. If the scanner has been running and this is still empty, the");
                text.AppendLine("  overlay never got as far as drawing, which is itself the answer.");
            }
            else
            {
                foreach (string line in calibration) text.AppendLine("  " + line);
                text.AppendLine();
                text.AppendLine("  The DIFFERENCE line is the number that matters: it is the projection's");
                text.AppendLine("  answer subtracted from where the game draws that hull's own label. A few");
                text.AppendLine("  pixels is a font's width; hundreds means the camera choice is wrong.");
            }

            text.AppendLine();
            text.AppendLine("What to send back if a hull is refused that should not be");
            text.AppendLine("--------------------------------------------------------");
            text.AppendLine("  Send this file. It names each hull class and the sensor families its own");
            text.AppendLine("  parameter block holds, so the list above is ground truth from your install");
            text.AppendLine("  rather than a guess: if a class carries an optronic director under a name the");
            text.AppendLine("  mod does not recognise, the name will be in this file and can be added.");

            string path = ModFolders.WriteText("lidar.txt", text.ToString());
            ModLog.Info("LIDAR: report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("LIDAR: could not write lidar.txt", error);
        }
    }

    /// <summary>Records the overlay's calibration reading, and logs it once.</summary>
    internal static void WriteCalibration(List<string> lines)
    {
        if (lines == null || lines.Count == 0) return;

        calibration = lines;

        // Kept, and written by the ticker rather than here: this is called from the overlay's draw
        // callback, and walking every hull through reflection and writing a file in the middle of a
        // GUI event is the sort of thing that shows up as a stutter.
        calibrationPending = true;
    }

    /// <summary>Called from the ticker.</summary>
    internal static void Tick()
    {
        if (!ModSettings.FeaturesEnabled) return;

        // Before any of the early returns below: a reading is worth writing whether or not a hull is
        // currently looking, because it is the answer to "why did I see nothing".
        if (calibrationPending)
        {
            calibrationPending = false;
            WriteReport("the overlay drew its first beam");
        }

        if (!ModSettings.LidarEnabled) return;

        // No hull looking means no work: this is on by default, and reading the whole contact
        // picture every tick for nothing would be a cost paid by every player who never opens the
        // entry.
        if (!AnyOrdered()) return;

        // A rotating set is sampled every frame - that is what makes the beam move - so this is
        // driven by the clock rather than by a sweep interval. What each frame can do is bounded by
        // the beam pointing at a contact, so the cost is a sector test, not a table walk.
        Spin(Time.unscaledDeltaTime);

        // The head keeps turning while the game is paused, because that is the mod's own drawing and
        // watching it turn is the point of the overlay. The SWEEP does not run: it writes tracks onto
        // the fleet's picture through the game's reveal call, and a paused simulation is not a
        // simulation that can be told anything new.
        if (Time.timeScale <= 0f) return;

        if (Time.unscaledTime < nextSweep) return;

        nextSweep = Time.unscaledTime + Mathf.Clamp(ModSettings.LidarSeconds, 1f, 60f);
        Sweep(force: false);
    }

    /// <summary>
    /// Advances every set's rotation and looks at whatever the beam is on.
    ///
    /// This is the part that makes it a scanner rather than a switch. A laser rangefinder with a
    /// rotating head sweeps the horizon at a steady rate; a contact is only <i>sighted</i> when the
    /// beam is actually on it, which is what the overlay draws and what the menu reports. The rate is
    /// a setting, and the sweep itself costs nothing when nothing has the order.
    /// </summary>
    private static void Spin(float delta)
    {
        if (delta <= 0f) return;

        double rate = Mathf.Clamp(ModSettings.LidarSpinRpm, 1f, 120f) * 6.0;    // degrees per second

        foreach (ObjectBase hull in FleetTicker.Tracked())
        {
            try
            {
                if (!IsOn(hull)) continue;

                UnitRecord record = UnitState.Of(hull);

                // A set that has just been switched on starts where its head happens to be, not at
                // north: the game gives no absolute head angle, and inventing one would make the
                // first sweep look like a different set from the second.
                if (!record.LidarSpun)
                {
                    record.LidarSpun = true;
                    record.LidarBearing = 0f;
                }

                record.LidarBearing = (float)((record.LidarBearing + rate * delta) % 360.0);
            }
            catch (Exception) { }
        }
    }

    /// <summary>Whether any hull has the set running. Read without creating state records.</summary>
    private static bool AnyOrdered()
    {
        foreach (ObjectBase hull in FleetTicker.Tracked())
        {
            try
            {
                if (IsOn(hull)) return true;
            }
            catch (Exception) { }
        }

        return false;
    }

    /// <summary>
    /// One sweep: for every hull with the order, look at whatever the beam happens to be pointing at,
    /// then record what happened. The beam is what decides, not the hull's heading and not a list of
    /// candidates - which is the difference between this and the arc version it replaces.
    /// </summary>
    private static void Sweep(bool force)
    {
        sweeps++;
        sighted = 0;

        if (!ModSettings.LidarEnabled && !force)
        {
            Set("the LIDAR is switched off in the settings", force);
            return;
        }

        List<ObjectBase> fleet = FleetTicker.Tracked();

        if (fleet.Count == 0)
        {
            Set("no ships under command", force);
            return;
        }

        List<ContactPicture.Contact> contacts = ContactPicture.Hostiles();

        if (contacts.Count == 0)
        {
            // Not a failure: with nothing on the picture there is nothing for a rangefinder to be
            // pointed at, however it is mounted.
            Set("nothing on the force's picture to look at - the scanner can only confirm what "
                + "somebody has already reported", force);

            Forget(fleet);
            return;
        }

        double arc = Mathf.Clamp(ModSettings.LidarBeamDeg, 1f, 90f) / 2.0;

        var parts = new List<string>();
        int ordered = 0;
        int refused = 0;

        foreach (ObjectBase hull in fleet)
        {
            try
            {
                if (!IsOn(hull)) continue;

                ordered++;

                UnitRecord record = UnitState.Of(hull);

                if (!UnitFit.IsPlayer(hull))
                {
                    record.Lidar = false;
                    parts.Add(Name(hull) + ": not this machine's - order cancelled");
                    continue;
                }

                LidarControl.Fit fit = LidarControl.Detect(hull);

                if (!fit.Allowed)
                {
                    refused++;
                    record.Lidar = false;
                    parts.Add(Name(hull) + ": may not look - " + fit.Evidence);
                    continue;
                }

                // Every hull reaches the same range now - the point of a set that is the mod's
                // sensor rather than the hull's - but what the hull has is still reported, because a
                // laser director and a pair of binoculars are not the same thing even when the mod
                // is the one doing the looking.
                double range = ModSettings.LidarRangeNm;

                // The beam, and only the beam. A contact outside this half-angle is not "dim" and it
                // is not "probable": it is not being looked at.
                string best = "";
                double bestRange = double.MaxValue;
                ObjectBase? bestUnit = null;

                foreach (ContactPicture.Contact contact in contacts)
                {
                    ObjectBase? target = contact.Unit;
                    if (target == null || target.IsDestroyed) continue;
                    if (UnitFit.IsPlayer(target)) continue;

                    double? nm = Inbound.RangeNm(hull, target);
                    if (!nm.HasValue || nm.Value > range) continue;

                    double? bearing = Bearing(hull, target);
                    if (!bearing.HasValue) continue;
                    if (CircularArc(record.LidarBearing, bearing.Value) > arc) continue;

                    if (nm.Value >= bestRange) continue;

                    bestRange = nm.Value;
                    bestUnit = target;
                    best = Name(target);
                }

                if (bestUnit == null)
                {
                    record.LidarSighted = "";
                    parts.Add(Name(hull) + ": beam at " + (int)record.LidarBearing
                                + " deg, nothing on it inside " + (int)range + " nm");
                    continue;
                }

                // Already detected by the force's own sensors? Then that fix is better than anything
                // a reveal can write, and the laser adds nothing. A track the force holds only
                // because it was handed one is refreshed: that is what stops it ageing out.
                DatalinkControl.Snapshot picture = DatalinkControl.Read(FleetTable(hull));

                if (picture.Holds(bestUnit) && picture.DetectedHere(bestUnit) != false)
                {
                    record.LidarSighted = "";
                    parts.Add(Name(hull) + ": the force's own sensors already hold " + best
                                + " - no laser needed");
                    continue;
                }

                if (!DatalinkControl.Reveal(hull, bestUnit, out string note))
                {
                    parts.Add(Name(hull) + ": the game refused the reveal (" + note + ")");
                    continue;
                }

                // Verified, not assumed: the table is read again and the contact has to be in it. A
                // reveal that writes only to the player's own force would otherwise report success
                // for ever.
                if (!DatalinkControl.Read(FleetTable(hull)).Holds(bestUnit))
                {
                    parts.Add(Name(hull) + ": revealed " + best + " but its picture did not gain it");
                    continue;
                }

                sighted++;
                record.LidarSighted = best + " at " + bestRange.ToString("0.0") + " nm";

                parts.Add(Name(hull) + ": " + record.LidarSighted
                          + " on the beam at " + (int)record.LidarBearing + " deg");
            }
            catch (Exception error)
            {
                parts.Add(Name(hull) + ": failed (" + error.GetType().Name + ")");
            }
        }

        if (ordered == 0)
        {
            Set("no hull has the scanner order - the menu entry on a ship's Sensors submenu gives it",
                force);
            return;
        }

        Set(parts.Count == 0 ? "nothing on the beam" : string.Join("; ", parts.ToArray()), force);

        if (refused > 0)
            ModLog.Warn("LIDAR: " + refused + " hull(s) may not look (LidarAnyHull is off)");
    }

    /// <summary>
    /// Every set that is spinning, for the map overlay: where the head is, how far it reaches, what
    /// it is holding and whether that contact was sighted on this beam pass.
    ///
    /// Read by the overlay a few times a second rather than every GUI event, so it is a plain list
    /// and holds no reference the overlay must not keep.
    /// </summary>
    internal static List<Beam> Beams()
    {
        var beams = new List<Beam>();

        try
        {
            foreach (ObjectBase hull in FleetTicker.Tracked())
            {
                if (!IsOn(hull)) continue;

                UnitRecord record = UnitState.Of(hull);

                beams.Add(new Beam(hull, record.LidarBearing, (float)ModSettings.LidarRangeNm,
                                   record.LidarSighted));
            }
        }
        catch (Exception error)
        {
            ModLog.Error("LIDAR: could not collect the beams", error);
        }

        return beams;
    }

    /// <summary>One rotating set, as the overlay needs it.</summary>
    internal readonly struct Beam
    {
        internal Beam(ObjectBase ship, float bearing, float rangeNm, string holding)
        {
            Ship = ship;
            Bearing = bearing;
            RangeNm = rangeNm;
            Holding = holding ?? "";
        }

        /// <summary>The ship the set is mounted on.</summary>
        internal ObjectBase Ship { get; }

        /// <summary>Where the head is pointing, degrees true.</summary>
        internal float Bearing { get; }

        /// <summary>How far the beam reaches, in nautical miles.</summary>
        internal float RangeNm { get; }

        /// <summary>What the last beam pass sighted, in words, or empty.</summary>
        internal string Holding { get; }

        /// <summary>True when this set is holding something - the beam is drawn brighter.</summary>
        internal bool Active => Holding.Length > 0;
    }

    /// <summary>Records the sweep's verdict, and logs it when the player asked for it (a click) or
    /// when it has changed - at most once every twenty seconds, because the verdict names contacts
    /// and those move. A line every four seconds is a line nobody reads.</summary>
    private static void Set(string verdict, bool log)
    {
        bool changed = !string.Equals(verdict, lastVerdict, StringComparison.Ordinal);
        lastVerdict = verdict;

        if (!log && !changed) return;
        if (!log && Time.unscaledTime - lastLoggedAt < 20f) return;

        lastLoggedAt = Time.unscaledTime;
        ModLog.Info("LIDAR: " + verdict);
    }

    /// <summary>
    /// Clears bookkeeping that would otherwise keep claiming a contact nobody holds: an order is not
    /// a sighting, and the menu must not read "holding X" after X has gone off the picture.
    /// </summary>
    private static void Forget(List<ObjectBase> fleet)
    {
        foreach (ObjectBase hull in fleet)
        {
            try
            {
                if (!IsOn(hull)) continue;

                UnitRecord record = UnitState.Of(hull);
                if (record.LidarSighted.Length == 0) continue;

                record.LidarSighted = "";
            }
            catch (Exception) { }
        }

        if (fleet.Count > 0) ModLog.Info("LIDAR: nothing left on the force's picture to hold");
    }

    /// <summary>The hull's own plotting table, which is where a reveal on it lands.</summary>
    private static object? FleetTable(ObjectBase hull)
    {
        try
        {
            return DatalinkControl.TableOf(hull._taskforce);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Bearing from one unit to another, degrees true, or null when either position is
    /// unreadable.</summary>
    private static double? Bearing(ObjectBase from, ObjectBase to)
    {
        try
        {
            GeoPosition a = from.getGeoPosition();
            GeoPosition b = to.getGeoPosition();

            double dLon = (b.Longitude - a.Longitude) * Math.Cos(a.Latitude * Math.PI / 180.0);
            double dLat = b.Latitude - a.Latitude;

            double degrees = Math.Atan2(dLon, dLat) * 180.0 / Math.PI;
            return (degrees + 360.0) % 360.0;
        }
        catch (Exception) { return null; }
    }

    /// <summary>How far one bearing is from another, in degrees, never more than 180.</summary>
    private static double CircularArc(double? heading, double bearing)
    {
        // No heading readable means the arc cannot be applied, and an arc that cannot be applied is
        // not used to refuse a sighting: this reports the range limit rather than inventing a
        // bearing. A hull whose heading is unknown is therefore looking all round, which is stated in
        // the report.
        if (!heading.HasValue) return 0;

        double difference = Math.Abs(heading.Value - bearing) % 360.0;
        return difference > 180.0 ? 360.0 - difference : difference;
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "a ship";

        try
        {
            if (!string.IsNullOrEmpty(unit.DisplayNameShort)) return unit.DisplayNameShort;
        }
        catch (Exception) { }

        try { return unit.GetType().Name; }
        catch (Exception) { return "a ship"; }
    }
}
