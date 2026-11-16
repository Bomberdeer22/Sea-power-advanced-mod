using System;
using System.Collections.Generic;
using System.Text;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Offensive ECM, in two modes.
///
/// <b>Standard</b> is what the game already does with the ship's own jammer, and is the default:
/// the mod does not touch it. Nothing here runs, nothing here radiates, and switching back to
/// standard releases any lock immediately.
///
/// <b>Signature lock</b> is the focused mode. The ship's ECM is pointed at one contact and held
/// there, and the mod first *reads* that contact's sensor fit - which radars, which sonar, whether
/// each is up, and what its hull is radiating as. That reading is the lock: without it the jammer
/// does not know what to jam. With it, every sensor family the contact has is switched off and kept
/// off, re-asserted ten times a second, so the target is blind for as long as the lock is held.
///
/// Unlike the defensive feature, this mode does **not** switch the attacking ship's radar on: it runs
/// on EMCON. Nothing here radiates, so the jammer gives away nothing while it works - which is the
/// whole reason to use ECM instead of a radar lock.
///
/// The effect is real and it is entirely the game's own switches: the target's radar, its sonar, its
/// towed array and its own electronic warfare fit are all turned off through the same members this
/// mod uses on friendly ships. The mod is not editing anyone's picture; it is holding another ship's
/// switches down.
///
/// What that means in practice, stated plainly: the target's crew can switch a set back on between
/// assertions, and this re-asserts. It is sustained suppression, not a permanent state change. And
/// if a sensor family cannot be reached on this build of the game, the menu and
/// <c>offensive-ecm.txt</c> name it instead of calling the target blind.
/// </summary>
internal static class OffensiveEcm
{
    /// <summary>How the ship's ECM is being used.</summary>
    internal enum Mode
    {
        /// <summary>The game's own behaviour. The mod does nothing at all.</summary>
        Standard = 0,

        /// <summary>Read one contact's signatures, then hold it blind.</summary>
        SignatureLock = 1,
    }

    private sealed class Hold
    {
        internal ObjectBase? Target;
        internal float NextAssertAt;
        internal int Assertions;
        internal string LastReport = "";
        internal bool Reported;

        /// <summary>
        /// True once the player has told it to transmit. Locking only aims the beam: holding the
        /// target's switches down is a separate, deliberate order.
        /// </summary>
        internal bool Jamming;
    }

    private static readonly Dictionary<ObjectBase, Hold> Held = new();
    private static readonly Dictionary<ObjectBase, Mode> Modes = new();

    /// <summary>
    /// How often the suppression is re-asserted. Ten times a second, because the target's own crew
    /// works on the same switches and the point of the hold is that it does not lapse between ticks.
    /// </summary>
    private const float AssertSeconds = 0.1f;

    /// <summary>How far the focused jammer reaches. Beyond this the lock drops.</summary>
    private static float RangeNm => Mathf.Clamp(ModSettings.JamRangeNm, 5f, 200f);

    // --- mode ------------------------------------------------------------------------------------

    internal static Mode ModeOf(ObjectBase? unit)
    {
        if (unit == null) return Mode.Standard;
        return Modes.TryGetValue(unit, out Mode mode) ? mode : Mode.Standard;
    }

    internal static bool IsLocked(ObjectBase? unit) =>
        unit != null && Held.TryGetValue(unit, out Hold hold) && hold.Target != null;

    internal static ObjectBase? TargetOf(ObjectBase? unit) =>
        unit != null && Held.TryGetValue(unit, out Hold hold) ? hold.Target : null;

    /// <summary>Switches between the game's own ECM and the focused, signature-lock mode.</summary>
    internal static bool SetMode(ObjectBase unit, Mode mode, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        Modes[unit] = mode;

        if (mode == Mode.Standard)
        {
            Release(unit, out string released);
            report = released.Length > 0
                ? "standard ECM: the game's own behaviour, and the mod stands off (" + released + ")"
                : "standard ECM: the game's own behaviour, and the mod stands off";
            return true;
        }

        report = EcmControl.Available
            ? "signature lock armed. Runs on EMCON - no radar, nothing radiated by this ship. "
              + "Pick a contact to lock."
            : "signature lock selected, but this build exposes no ECM to drive - see ecm-report.txt";
        return true;
    }

    // --- contacts --------------------------------------------------------------------------------

    /// <summary>
    /// Hostile contacts this ship's own picture holds, nearest first. Read from the ship's contact
    /// lists - the same ones the threat-axis order uses - so the mod never invents a target the
    /// force cannot actually see.
    /// </summary>
    internal static List<ObjectBase> Contacts(ObjectBase ship)
    {
        var contacts = new List<ObjectBase>();
        if (ship == null) return contacts;

        GeoPosition own;
        try { own = ship.getGeoPosition(); }
        catch (Exception) { return contacts; }

        var seen = new HashSet<ObjectBase>();

        foreach (ObjectBase contact in Candidates(ship))
        {
            try
            {
                if (contact == null || contact.IsDestroyed) continue;
                if (contact.IsPlayerUnit) continue;
                if (ReferenceEquals(contact, ship)) continue;
                if (!seen.Add(contact)) continue;

                double range = ThreatAxis.Distance(own, contact.getGeoPosition());
                if (range > RangeNm) continue;

                contacts.Add(contact);
            }
            catch (Exception) { }
        }

        contacts.Sort((a, b) =>
        {
            double ra = SafeRange(own, a);
            double rb = SafeRange(own, b);
            return ra.CompareTo(rb);
        });

        return contacts;
    }

    private static IEnumerable<ObjectBase> Candidates(ObjectBase ship)
    {
        if (ship._threats != null) foreach (ObjectBase unit in ship._threats) yield return unit;
        if (ship._spottedObjects != null) foreach (ObjectBase unit in ship._spottedObjects) yield return unit;
        if (ship._dangerousMissiles != null) foreach (ObjectBase unit in ship._dangerousMissiles) yield return unit;
    }

    private static double SafeRange(GeoPosition own, ObjectBase? unit)
    {
        try { return unit == null ? double.MaxValue : ThreatAxis.Distance(own, unit.getGeoPosition()); }
        catch (Exception) { return double.MaxValue; }
    }

    // --- the lock --------------------------------------------------------------------------------

    /// <summary>
    /// Reads the target's sensor fit and, if that read succeeds, holds it blind.
    ///
    /// The read comes first and on its own: a jammer that has not identified what the other ship is
    /// radiating with has nothing to aim at. That is the honest basis for calling this a lock rather
    /// than a switch.
    /// </summary>
    internal static bool Lock(ObjectBase ship, ObjectBase target, out string report)
    {
        if (ship == null || target == null)
        {
            report = "no target";
            return false;
        }

        if (!EcmControl.Available)
        {
            report = "this build exposes no ECM to drive - ecm-report.txt lists what the scan found";
            return false;
        }

        if (!UnitFit.HasEcm(ship))
        {
            report = Name(ship) + " carries no ECM, so it cannot jam anything";
            return false;
        }

        double range = SafeRange(Position(ship), target);
        if (range > RangeNm)
        {
            report = Name(target) + " is " + (int)range + " nm away - beyond the " + (int)RangeNm
                     + " nm the focused jammer reaches";
            return false;
        }

        // 1. The read. No read, no lock.
        List<SensorSet.Reading> signatures = SensorSet.Read(target);
        string signature = SensorSet.SignatureOf(target);

        if (signatures.Count == 0)
        {
            report = "could not read " + Name(target) + "'s sensor fit - no lock";
            return false;
        }

        // 2. The lock itself: hold the track and aim the beam. Nothing is transmitted yet - that is
        //    a separate order, so a lock never jams by accident and the player decides when the set
        //    actually radiates.
        Modes[ship] = Mode.SignatureLock;
        Held[ship] = new Hold
        {
            Target = target,
            NextAssertAt = 0f,
            Assertions = 0,
            Reported = false,
            Jamming = false,
        };

        report = "locked on " + Name(target) + " (" + (int)range + " nm, hull " + signature + "). "
                 + "Read " + SensorSet.Describe(signatures) + ". The beam is on the map; use "
                 + "'Jam now' to transmit.";

        if (Multiplayer.IsClient)
        {
            report += " In a session the jam goes to the host as the game's own jam order, so the"
                      + " game's ECM decides the effect - the mod cannot hold a host ship's switches"
                      + " down from here.";
        }

        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);

        WriteReport(ship, target, signature, signatures,
                    new List<SensorSet.Reading>(), "lock only - not transmitting");
        return true;
    }

    /// <summary>
    /// Locks a contact from whichever of our own ships can actually do it: the nearest one with ECM,
    /// inside jam range. This is what the enemy contact's menu calls, so the player never has to
    /// first work out which of their ships has a jammer.
    /// </summary>
    internal static bool LockOnContact(ObjectBase contact, out string report)
    {
        report = "";

        if (contact == null)
        {
            report = "no contact";
            return false;
        }

        ObjectBase? jammer = BestJammerFor(contact);

        if (jammer == null)
        {
            report = "no ship of ours with ECM is within " + (int)RangeNm + " nm of "
                     + Name(contact) + " - nothing to jam it with";
            ModLog.Warn("Offensive ECM: " + report);
            return false;
        }

        bool ok = Lock(jammer, contact, out string inner);
        report = ok ? Name(jammer) + ": " + inner : inner;
        return ok;
    }

    /// <summary>The nearest of our ships with ECM that is inside jam range of this contact.</summary>
    internal static ObjectBase? BestJammerFor(ObjectBase? contact)
    {
        if (contact == null) return null;

        ObjectBase? best = null;
        double bestRange = double.MaxValue;

        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            try
            {
                if (!UnitFit.HasEcm(ship)) continue;

                double range = SafeRange(Position(ship), contact);
                if (range > RangeNm || range >= bestRange) continue;

                bestRange = range;
                best = ship;
            }
            catch (Exception) { }
        }

        return best;
    }

    /// <summary>
    /// Starts transmitting on the held lock: the target's sensors are switched off and held down
    /// from here. This is the manual step - the lock on its own only aims the beam.
    /// </summary>
    internal static bool JamNow(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold) || hold.Target == null)
        {
            report = "no lock to jam - lock a contact first";
            return false;
        }

        if (hold.Jamming)
        {
            report = "already jamming " + Name(hold.Target);
            return true;
        }

        hold.Jamming = true;
        hold.NextAssertAt = Time.unscaledTime + AssertSeconds;
        hold.Reported = false;

        // In a session the target is simulated by the other machine, so switching its sensors off
        // here would reach a replica and nothing else. The game's own jam order is the route the
        // multiplayer mod forwards to whoever is simulating it - and there the game's ECM model
        // decides the effect, not this mod's assertion loop.
        if (Multiplayer.IsClient)
        {
            bool sent = Multiplayer.Jam(ship, hold.Target, out string order);

            report = "jamming " + Name(hold.Target) + ": " + order
                     + ". The target is simulated by the host, so the mod cannot hold its switches "
                     + "down from here - the game's own ECM decides the effect.";
            if (!sent) hold.Jamming = false;

            ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
            Multiplayer.Note(report);
            return sent;
        }

        List<SensorSet.Reading> readings = SensorSet.Silence(hold.Target);
        EcmControl.Engage(ship, hold.Target, false, out string jam);

        bool blind = SensorSet.FullyBlind(readings);
        report = "jamming " + Name(hold.Target) + " on EMCON: "
                 + (blind ? "every sensor off" : "NOT all sensors off - " + Stubborn(readings))
                 + " | " + jam;

        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
        return true;
    }

    /// <summary>Stops transmitting but keeps the lock and the beam, so it can be resumed.</summary>
    internal static bool StopJam(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold) || !hold.Jamming)
        {
            report = "not jamming";
            return false;
        }

        hold.Jamming = false;
        EcmControl.StandDown(ship, out _);

        report = Multiplayer.IsClient
            ? "stopped here; the lock is still held. The jam itself is the host's task - the "
              + "multiplayer mod has no stop-jamming order, so it runs until that task ends."
            : "stopped jamming " + Name(hold.Target) + "; the lock is still held";
        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
        return true;
    }

    /// <summary>Whether this ship is transmitting right now (as opposed to merely locked on).</summary>
    internal static bool IsJamming(ObjectBase? ship) =>
        ship != null && Held.TryGetValue(ship, out Hold hold) && hold.Jamming;

    /// <summary>The beams the map overlay draws for the offensive set.</summary>
    internal static List<EcmBeam> Beams()
    {
        var beams = new List<EcmBeam>();

        foreach (KeyValuePair<ObjectBase, Hold> entry in Held)
        {
            if (entry.Key == null || entry.Value.Target == null) continue;
            beams.Add(new EcmBeam(entry.Key, entry.Value.Target, entry.Value.Jamming, true));
        }

        return beams;
    }

    /// <summary>Drops the lock. The mod stops holding the target's switches down.</summary>
    internal static bool Release(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold)) return false;

        string target = Name(hold.Target);
        Held.Remove(ship);

        // Nothing to restore on the radar front: this mode never switched it on. Offensive jamming
        // runs on EMCON - silent, radiating nothing - which is the point of using ECM over radar.
        EcmControl.StandDown(ship, out _);

        report = Multiplayer.IsClient
            ? "released " + target + " here; the host's jam task is its own, so it ends there"
            : "released " + target + " after " + hold.Assertions
              + " assertion(s); its own crew can bring its sensors back up";
        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
        return true;
    }

    /// <summary>Releases every ship's lock - used when the feature is switched off wholesale.</summary>
    internal static int ReleaseAll(out string report)
    {
        int released = 0;

        foreach (ObjectBase ship in new List<ObjectBase>(Held.Keys))
        {
            if (Release(ship, out _)) released++;
        }

        report = released > 0 ? "released " + released + " lock(s)" : "nothing was locked";
        return released;
    }

    // --- the tick --------------------------------------------------------------------------------

    /// <summary>
    /// Keeps every lock alive. Called from the ticker: this is where a lock is re-asserted, where a
    /// target that has been sunk or has run out of range is let go, and where the log records the
    /// moment a target went blind.
    /// </summary>
    internal static void Tick()
    {
        if (Held.Count == 0) return;
        if (!ModSettings.FeaturesEnabled)
        {
            ReleaseAll(out _);
            return;
        }

        float now = Time.unscaledTime;

        foreach (KeyValuePair<ObjectBase, Hold> entry in new List<KeyValuePair<ObjectBase, Hold>>(Held))
        {
            ObjectBase ship = entry.Key;
            Hold hold = entry.Value;

            try
            {
                // Unity's == reports true for a destroyed object even though the reference is
                // real, so the entry has to be removed by the key that is actually in the map.
                if (ship == null) { Held.Remove(entry.Key); continue; }
                if (ModeOf(ship) != Mode.SignatureLock) { Release(ship, out _); continue; }

                ObjectBase? target = hold.Target;

                if (target == null || target.IsDestroyed)
                {
                    ModLog.Info("Offensive ECM: " + Name(ship) + " released "
                                + (target == null ? "an empty track" : Name(target) + " - sunk or gone"));
                    Release(ship, out _);
                    continue;
                }

                double range = SafeRange(Position(ship), target);
                if (range > RangeNm)
                {
                    ModLog.Info("Offensive ECM: " + Name(ship) + " lost the lock on " + Name(target)
                                + " at " + (int)range + " nm - out of range");
                    Release(ship, out _);
                    continue;
                }

                // Locked but silent: the beam is on the map, nothing is being transmitted, and the
                // target's own crew is free to use its sensors. That is the point of the two-step
                // order.
                if (!hold.Jamming) continue;

                // A client holds no switches on the host's ships: the jam travels as an order, and
                // re-asserting locally would only write to a replica that is overwritten each frame.
                if (Multiplayer.IsClient)
                {
                    if (hold.Reported) continue;

                    hold.Reported = true;
                    ModLog.Info("Offensive ECM: " + Name(ship) + " holding " + Name(target) + " ("
                                + (int)range + " nm) - jam order sent to the host");
                    continue;
                }

                if (now < hold.NextAssertAt) continue;
                hold.NextAssertAt = now + AssertSeconds;
                hold.Assertions++;

                // Re-assert the jammer itself as well as the suppression: the target's crew can
                // switch a set back on between assertions, and the game can reset our own set.
                EcmControl.Engage(ship, target, false, out _);
                List<SensorSet.Reading> readings = SensorSet.Silence(target);

                bool blind = SensorSet.FullyBlind(readings);
                string summary = SensorSet.Describe(readings);

                if (!hold.Reported || summary != hold.LastReport)
                {
                    hold.Reported = true;
                    hold.LastReport = summary;

                    ModLog.Info("Offensive ECM: " + Name(ship) + " holding " + Name(target) + " ("
                                + (int)range + " nm) - "
                                + (blind ? "every sensor off" : "some sensors still up")
                                + ": " + summary);
                }
            }
            catch (Exception error)
            {
                ModLog.Error("Offensive ECM failed for a ship", error);
                Held.Remove(ship);
            }
        }
    }

    // --- describing ------------------------------------------------------------------------------

    /// <summary>One line for the menu label: mode, and what the lock is doing.</summary>
    internal static string Describe(ObjectBase? unit)
    {
        if (unit == null) return "off";

        if (ModeOf(unit) == Mode.Standard) return "standard (the game's own)";

        if (Held.TryGetValue(unit, out Hold hold) && hold.Target != null)
        {
            double range = SafeRange(Position(unit), hold.Target);
            return (hold.Jamming ? "jamming " : "locked on ") + Name(hold.Target)
                   + " (" + (int)range + " nm)";
        }

        return "signature lock, no target yet";
    }

    /// <summary>How many contacts are within reach, for the submenu label.</summary>
    internal static string ContactSummary(ObjectBase ship) => Contacts(ship).Count + " contact(s) in range";

    /// <summary>
    /// Names the sensor families this build would not let the mod switch, so the report says which
    /// one is still working instead of a vague "partially".
    /// </summary>
    private static string Stubborn(List<SensorSet.Reading> readings)
    {
        var names = new List<string>();

        foreach (SensorSet.Reading reading in readings)
        {
            if (reading.Stubborn) names.Add(reading.Name);
        }

        return names.Count > 0
            ? string.Join(", ", names) + " would not switch off on this build"
            : "one or more sensors could not be confirmed off on this build";
    }

    private static GeoPosition Position(ObjectBase? unit)
    {
        try { return unit == null ? new GeoPosition(0, 0, 0) : unit.getGeoPosition(); }
        catch (Exception) { return new GeoPosition(0, 0, 0); }
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }

    /// <summary>
    /// Writes <c>offensive-ecm.txt</c> for one lock: what was read off the target, what was silenced,
    /// and what was not. Written per lock rather than per tick, and appended rather than overwritten,
    /// so a mission's whole jamming history is in one file.
    /// </summary>
    private static void WriteReport(ObjectBase ship, ObjectBase target, string signature,
                                    List<SensorSet.Reading> signatures,
                                    List<SensorSet.Reading> applied, string jammer)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - offensive ECM lock");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine("jammer  " + Name(ship));
            text.AppendLine("target  " + Name(target) + "   hull signature as read: " + signature);
            text.AppendLine("jammer path: " + jammer);
            text.AppendLine();
            text.AppendLine("Signatures read off the target before jamming");
            text.AppendLine("-------------------------------------------");
            if (signatures.Count == 0) text.AppendLine("  (none readable)");
            foreach (SensorSet.Reading reading in signatures) text.AppendLine("  " + Line(reading));
            text.AppendLine();
            text.AppendLine("What the mod then did, per sensor family");
            text.AppendLine("----------------------------------------");
            if (applied.Count == 0) text.AppendLine("  (nothing to switch)");
            foreach (SensorSet.Reading reading in applied) text.AppendLine("  " + Line(reading));
            text.AppendLine();
            text.AppendLine("A sensor marked STILL is one this build of the game will not let the mod");
            text.AppendLine("switch off; the menu says 'NOT all sensors off' and names it rather than");
            text.AppendLine("claiming otherwise.");
            text.AppendLine("The hold is re-asserted ten times a second - it is sustained suppression, not");
            text.AppendLine("a permanent state change, and it stops the moment the lock is released.");

            string path = ModFolders.AppendText("offensive-ecm.txt", text.ToString());
            ModLog.Info("Offensive ECM report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write offensive-ecm.txt", error);
        }
    }

    private static string Line(SensorSet.Reading reading)
    {
        string state = reading.Verdict.Length > 0
            ? reading.Verdict
            : reading.Radiating == true ? "radiating"
            : reading.Radiating == false ? "quiet"
            : "state not exposed";

        return reading.Name.PadRight(28) + state;
    }
}
