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
/// off, re-asserted ten times a second, so the target is blind for as long as the lock is held and
/// the ship keeps radiating.
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

        /// <summary>Air-search state before the jammer was pointed at anything, so it can be put back.</summary>
        internal bool AirWasOn;
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
            ? "signature lock armed. Pick a contact from the lock menu; air search will radiate "
              + "while it jams"
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

        // 2. The hold: every family it has is switched off and re-asserted until released.
        Modes[ship] = Mode.SignatureLock;
        Held[ship] = new Hold
        {
            Target = target,
            NextAssertAt = 0f,
            Assertions = 0,
            Reported = false,
            AirWasOn = SensorControl.GetAirSearchOn(ship) ?? false,
        };

        List<SensorSet.Reading> silenced = SensorSet.Silence(target);
        string outcome = SensorSet.FullyBlind(silenced)
            ? "fully blind"
            : "PARTIALLY blind - " + Stubborn(silenced);

        EcmControl.Engage(ship, target, out string jam);

        report = "lock on " + Name(target) + " (" + (int)range + " nm, hull " + signature + "): "
                 + outcome + " | read " + SensorSet.Describe(signatures) + " | " + jam;

        ModLog.Info("Offensive ECM: " + Name(ship) + " " + report);
        WriteReport(ship, target, signature, signatures, silenced, jam);

        // Not marked as reported on purpose: the first assertion a tenth of a second later reads the
        // target back and logs the confirmed picture, which is the line worth trusting.
        return true;
    }

    /// <summary>Drops the lock. The mod stops holding the target's switches down.</summary>
    internal static bool Release(ObjectBase ship, out string report)
    {
        report = "";

        if (ship == null || !Held.TryGetValue(ship, out Hold hold)) return false;

        string target = Name(hold.Target);
        Held.Remove(ship);

        EcmControl.StandDown(ship, out _);

        // Put the air search back as it was found: the jammer needed it, the ship afterwards does
        // not, and leaving it radiating would be the mod giving the position away for nothing.
        if (!hold.AirWasOn) SensorControl.SetAirSearch(ship, false);

        report = "released " + target + " after " + hold.Assertions
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
                if (ship == null) { Held.Remove(ship); continue; }
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

                if (now < hold.NextAssertAt) continue;
                hold.NextAssertAt = now + AssertSeconds;
                hold.Assertions++;

                // Re-assert the jammer itself as well as the suppression: the target's crew can
                // switch a set back on between assertions, and the game can reset our own set.
                EcmControl.Engage(ship, target, out _);
                List<SensorSet.Reading> readings = SensorSet.Silence(target);

                bool blind = SensorSet.FullyBlind(readings);
                string summary = SensorSet.Describe(readings);

                if (!hold.Reported || summary != hold.LastReport)
                {
                    hold.Reported = true;
                    hold.LastReport = summary;

                    ModLog.Info("Offensive ECM: " + Name(ship) + " holding " + Name(target) + " ("
                                + (int)range + " nm) - " + (blind ? "blind" : "NOT yet blind")
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
            return "locked on " + Name(hold.Target) + " (" + (int)range + " nm)";
        }

        return "signature lock, no target yet";
    }

    /// <summary>How many contacts are within reach, for the submenu label.</summary>
    internal static string ContactSummary(ObjectBase ship) => Contacts(ship).Count + " contact(s) in range";

    private static string Stubborn(List<SensorSet.Reading> readings)
    {
        var names = new List<string>();

        foreach (SensorSet.Reading reading in readings)
        {
            if (reading.Stubborn) names.Add(reading.Name);
        }

        return names.Count > 0 ? string.Join(", ", names) : "one or more sensors could not be confirmed off";
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
            text.AppendLine("switch off; the menu says 'partially blind' rather than claiming otherwise.");
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
