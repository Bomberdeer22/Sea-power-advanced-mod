using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Defensive ECM: every ship that carries an offensive jammer also uses it to defend itself and its
/// formation, automatically, the moment a vampire is in the air.
///
/// The tactics are real and old. A ship with a jammer does not wait for the missile to arrive: it
/// puts a fire-control lock on the seeker's bearing, points the jammer at the missile itself, and
/// holds that lock for the whole engagement, because a missile that loses its lock has to go blind
/// or go home. The bill is paid in emissions - a jammer needs a radar-quality lock to know where to
/// point, so the ship radiates air search at exactly the moment the other side is listening hardest.
/// That is why this is a decision and not a freebie, and why it is armed per ship or per task force
/// rather than switched on for the player.
///
/// What the mod does *not* claim: that the game's missile will certainly miss. It cannot. It points
/// the game's own jammer at the game's own missile and holds it there; whether the seeker breaks
/// lock is the simulation's answer, not the mod's. See <see cref="EcmControl"/> for what this build
/// of the game actually exposes, and <c>ecm-report.txt</c> for the same in writing.
/// </summary>
internal static class DefensiveEcm
{
    /// <summary>How long the jammer stays up after the last vampire stops being tracked.</summary>
    private const float HoldSeconds = 6f;

    /// <summary>How often the lock is re-pointed while a missile is up. Cheap, but not per frame.</summary>
    private const float RefreshSeconds = 0.25f;

    /// <summary>A missile aimed at a ship this close is treated as aimed at us too.</summary>
    private const float FormationNm = 25f;

    internal static bool IsArmed(ObjectBase unit) => unit != null && UnitState.Of(unit).DefensiveEcm;

    internal static bool IsEngaged(ObjectBase unit) => unit != null && UnitState.Of(unit).EcmEngaged;

    /// <summary>Arms or disarms the automatic defence for one ship.</summary>
    internal static bool Toggle(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        UnitRecord record = UnitState.Of(unit);
        record.DefensiveEcm = !record.DefensiveEcm;

        if (!record.DefensiveEcm)
        {
            if (record.EcmEngaged) Stop(unit, record, "disarmed");
            report = "defensive ECM off";
            return true;
        }

        report = EcmControl.Available
            ? "armed: " + EcmControl.Summary() + ". Air search will radiate while it jams."
            : "armed, but this build exposes no ECM to drive - see ecm-report.txt";
        return true;
    }

    /// <summary>Arms or disarms every ship under command.</summary>
    internal static int ArmFleet(bool on, out string report)
    {
        int changed = 0;

        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            try
            {
                UnitRecord record = UnitState.Of(ship);
                if (record.DefensiveEcm == on) continue;

                record.DefensiveEcm = on;
                if (!on && record.EcmEngaged) Stop(ship, record, "task force disarmed");
                changed++;
            }
            catch (Exception error)
            {
                ModLog.Error("Defensive ECM could not be armed on a ship", error);
            }
        }

        report = on
            ? "defensive ECM armed on " + changed + " ship(s)"
            : "defensive ECM disarmed on " + changed + " ship(s)";
        return changed;
    }

    /// <summary>Called from the ticker: watches for vampires and works the jammer.</summary>
    internal static void Tick()
    {
        if (!ModSettings.FeaturesEnabled) return;

        List<ObjectBase> fleet = FleetTicker.Tracked();
        if (fleet.Count == 0) return;

        float now = Time.unscaledTime;

        foreach (ObjectBase ship in fleet)
        {
            try
            {
                UnitRecord record = UnitState.Of(ship);

                if (!record.DefensiveEcm)
                {
                    if (record.EcmEngaged) Stop(ship, record, "defensive ECM was off");
                    continue;
                }

                bool inbound = Inbound.AnyNearby(ship, fleet, FormationNm,
                                                 out object? weapon, out ObjectBase? threatened);

                if (inbound)
                {
                    if (!record.EcmEngaged)
                    {
                        // Remember how the air-search radar was left, so standing down puts it back
                        // rather than guessing what the player wanted.
                        record.EcmAirWasOn = SensorControl.GetAirSearchOn(ship) ?? false;
                        record.EcmEngaged = true;
                        record.EcmRefreshAt = 0f;
                    }

                    record.EcmReleaseAt = now + HoldSeconds;

                    if (now < record.EcmRefreshAt) continue;
                    record.EcmRefreshAt = now + RefreshSeconds;

                    bool ok = EcmControl.Engage(ship, weapon, out string note);

                    if (!record.EcmEngagedLogged)
                    {
                        record.EcmEngagedLogged = true;
                        ModLog.Info("Defensive ECM: " + Name(ship) + " engaging a missile inbound to "
                                    + Name(threatened) + " - " + note + (ok ? "" : " [no effect]"));
                    }
                    else if (ModSettings.VerboseLog)
                    {
                        ModLog.Info("Defensive ECM: " + Name(ship) + " holding lock - " + note);
                    }

                    continue;
                }

                if (record.EcmEngaged && now >= record.EcmReleaseAt)
                    Stop(ship, record, "the sky is clear");
            }
            catch (Exception error)
            {
                ModLog.Error("Defensive ECM failed for a ship", error);
            }
        }
    }

    /// <summary>Stands the jammer down and puts the air-search radar back as it was found.</summary>
    private static void Stop(ObjectBase ship, UnitRecord record, string why)
    {
        if (!record.EcmEngaged) return;

        bool off = EcmControl.StandDown(ship, out string note);

        // Only switch the radar off again if it was off before the engagement; a ship that was
        // already radiating keeps radiating.
        if (!record.EcmAirWasOn) SensorControl.SetAirSearch(ship, false);

        record.EcmEngaged = false;
        record.EcmEngagedLogged = false;

        ModLog.Info("Defensive ECM: " + Name(ship) + " stood down (" + why + ") - "
                    + (off ? note : "nothing to switch off; " + note));
    }

    /// <summary>One line describing this ship's state, for the menu tooltip.</summary>
    internal static string Describe(ObjectBase unit)
    {
        if (unit == null) return "no unit";

        UnitRecord record = UnitState.Of(unit);
        if (!record.DefensiveEcm) return "off";
        return record.EcmEngaged ? "armed, jamming now" : "armed, waiting for a vampire";
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
