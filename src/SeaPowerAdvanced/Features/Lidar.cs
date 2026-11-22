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

        if (!fit.Fitted)
        {
            report = Name(unit) + " has nothing to look through: " + fit.Evidence;
            ModLog.Warn("LIDAR: " + report);
            return false;
        }

        record.Lidar = true;

        report = "looking with " + fit.Kind + " - " + fit.Evidence;
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
            : "on, nothing in the arc";
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
            text.AppendLine("  A range-limited sight: a laser rangefinder where the hull has one, and the");
            text.AppendLine("  game's own Eyes Mk 1 lookouts where it has not. A hull whose own sensor list");
            text.AppendLine("  holds either may be ordered to look with it,");
            text.AppendLine("  and every contact the FORCE ALREADY HOLDS that is inside the range and the");
            text.AppendLine("  arc is put on that hull's picture through the game's own reveal call - the");
            text.AppendLine("  route a reconnaissance report uses. Position only: no identity, no class.");
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
                            + ", range " + (int)ModSettings.LidarRangeNm + " nm"
                            + " (lookouts " + (int)ModSettings.LidarLookoutNm + " nm)"
                            + ", arc " + (int)ModSettings.LidarArcDegrees + " deg"
                            + ", look every " + ModSettings.LidarSeconds.ToString("0.#") + " s");
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
                    text.AppendLine("    fit: " + fit.Kind + " - " + fit.Evidence);
                    text.AppendLine("    state: " + Describe(hull));
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

    /// <summary>Called from the ticker.</summary>
    internal static void Tick()
    {
        if (!ModSettings.FeaturesEnabled) return;
        if (!ModSettings.LidarEnabled) return;

        // No hull looking means no sweep: this is on by default, and reading the whole contact
        // picture every four seconds for nothing would be a cost paid by every player who never
        // opens the entry.
        if (!AnyOrdered()) return;

        // Look orders are this machine's own picture, so they work on a client as well as a host:
        // what is written is a track on a hull this machine commands, not a change to the world.
        float interval = Mathf.Clamp(ModSettings.LidarSeconds, 1f, 60f);
        if (Time.unscaledTime < nextSweep) return;

        nextSweep = Time.unscaledTime + interval;
        Sweep(force: false);
    }

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
    /// One look: every hull with the order in force puts the contacts it can see with its laser on
    /// its own picture, and the sweep records what it managed.
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
            // The decisive limit, and it is not a failure: with no prior sighting there is nothing
            // for a rangefinder to be pointed at.
            Set("nothing on the force's picture to look at - a laser needs a bearing from somebody "
                + "else's picture", force);

            Face(fleet, "nothing left on the force's picture to hold");
            return;
        }

        double arc = Mathf.Clamp(ModSettings.LidarArcDegrees, 10f, 360f) / 2.0;

        var parts = new List<string>();
        int ordered = 0;
        int noFit = 0;

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

                if (!fit.Fitted)
                {
                    noFit++;
                    record.Lidar = false;
                    parts.Add(Name(hull) + ": nothing to look through - " + fit.Evidence);
                    continue;
                }

                // How far this particular fit may look: an instrument gets the configured range, a
                // hull with only lookouts gets the shorter lookout range. The beam is one bearing
                // either way.
                double range = LidarControl.RangeNm(fit, ModSettings.LidarRangeNm,
                                                    ModSettings.LidarLookoutNm);

                // One beam, one bearing. The nearest eligible contact in the arc is what a watch
                // team would put the glass on first.
                string best = "";
                double bestRange = double.MaxValue;
                ObjectBase? bestUnit = null;
                double? heading = HullHeading(hull);

                foreach (ContactPicture.Contact contact in contacts)
                {
                    ObjectBase? target = contact.Unit;
                    if (target == null || target.IsDestroyed) continue;

                    // Never our own ships.
                    if (UnitFit.IsPlayer(target)) continue;

                    double? nm = Inbound.RangeNm(hull, target);
                    if (!nm.HasValue || nm.Value > range) continue;

                    double? bearing = Bearing(hull, target);
                    if (bearing.HasValue && CircularArc(heading, bearing.Value) > arc) continue;

                    if (nm.Value >= bestRange) continue;

                    bestRange = nm.Value;
                    bestUnit = target;
                    best = Name(target);
                }

                if (bestUnit == null)
                {
                    record.LidarSighted = "";
                    parts.Add(Name(hull) + ": nothing in " + (int)range + " nm inside the arc"
                                + " for " + (fit.Laser ? "its laser" : "its lookouts"));
                    continue;
                }

                // Already detected by the force's own sensors? Then that fix is better than anything
                // a reveal can write - the game's own track errors are the point of the game - and
                // the laser adds nothing. A track the force holds only because it was handed one is
                // refreshed: that is what stops it ageing out.
                //
                // Note what this reads: the picture belongs to the force, not the hull, so the beam
                // lands in the force's picture with this hull credited as the sensor. The report
                // says so rather than pretending each ship has a picture of its own.
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

                // Verified, not assumed: the hull's table is read again and the contact has to be
                // there. A reveal that writes only to the player's own force would otherwise report
                // success for ever.
                bool landed = DatalinkControl.Read(FleetTable(hull)).Holds(bestUnit);

                if (!landed)
                {
                    parts.Add(Name(hull) + ": revealed " + best + " but its picture did not gain it");
                    continue;
                }

                sighted++;
                record.LidarSighted = best + " at " + bestRange.ToString("0.0") + " nm";

                parts.Add(Name(hull) + ": " + record.LidarSighted
                          + " with " + (fit.Laser ? "its laser" : "its lookouts"));
            }
            catch (Exception error)
            {
                parts.Add(Name(hull) + ": failed (" + error.GetType().Name + ")");
            }
        }

        if (ordered == 0)
        {
            Set("no hull has the laser order - the menu entry on a ship's Sensors submenu gives it",
                force);
            return;
        }

        Set(parts.Count == 0 ? "nothing to look at" : string.Join("; ", parts.ToArray()), force);

        if (noFit > 0)
            ModLog.Warn("LIDAR: " + noFit + " hull(s) with the order have nothing to look through");
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
    /// Clears bookkeeping that would otherwise keep claiming a contact the hull no longer holds: an
    /// order is not a sighting, and the menu must not read "holding X" after X has gone.
    /// </summary>
    private static void Face(List<ObjectBase> fleet, string reason)
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

        if (reason.Length > 0 && fleet.Count > 0) ModLog.Info("LIDAR: " + reason);
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

    /// <summary>
    /// The hull's heading in degrees true, or null when it cannot be read.
    ///
    /// <c>Heading</c> is a property, so <see cref="MemberProbe.Value"/> finds it by its own name -
    /// which means the candidates are the member names, not the accessor names. A float, a double or
    /// a ReactiveProperty of either is accepted; anything else is "unknown", and an unknown heading
    /// means the arc is not applied rather than a sighting being refused on a guess.
    /// </summary>
    private static double? HullHeading(ObjectBase hull)
    {
        try
        {
            foreach (string name in new[] { "Heading", "Course", "Bearing" })
            {
                MemberInfo? member = MemberProbe.Value(hull.GetType(), name);
                if (member == null) continue;

                object? value = MemberProbe.Read(member, hull);

                if (value is float f) return f;
                if (value is double d) return d;
                if (value is int i) return i;

                // A ReactiveProperty<float> does not read as a number through MemberProbe.Read, and
                // this deliberately does not guess at its carrier: an unreadable heading means the
                // arc is not applied, which the report says.
            }
        }
        catch (Exception) { }

        return null;
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
