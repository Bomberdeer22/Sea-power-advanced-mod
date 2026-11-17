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
/// And it finishes the job at close range. A sea-skimmer whose seeker loses lock two miles out does
/// not fly on towards the ship - it loses its height reference, stops correcting, and goes into the
/// water. So once a missile is inside <see cref="DiveNm"/>, the jammer's lock on it is used for what
/// it is worth: the weapon's own lock is broken and its height is driven down every tick until it is
/// at the waterline. That is <see cref="WeaponDive"/>, and it is the one place this mod writes to a
/// weapon rather than to a ship's sensors - deliberately, because a missile in the last two miles is
/// a missile that has already been fired at somebody.
///
/// What the mod does *not* claim: that every missile in the air will miss. Guns and rockets are not
/// touched, and a missile outside the dive range is left to the simulation and to the jam. See
/// <see cref="EcmControl"/> for what this build exposes, and <c>ecm-dive.txt</c> /
/// <c>ecm-report.txt</c> for the same in writing.
/// </summary>
internal static class DefensiveEcm
{
    /// <summary>How long the jammer stays up after the last vampire stops being tracked.</summary>
    private const float HoldSeconds = 6f;

    /// <summary>How often the lock is re-pointed while a missile is up. Cheap, but not per frame.</summary>
    private const float RefreshSeconds = 0.25f;

    /// <summary>A missile aimed at a ship this close is treated as aimed at us too.</summary>
    private const float FormationNm = 25f;

    /// <summary>
    /// How close a missile gets before the jam is finished with it and it is put in the water.
    ///
    /// Two miles is the number the player asked for and it is also the honest one: inside the
    /// terminal defence envelope a jammed seeker has no time to re-acquire, and a sea-skimmer that
    /// loses its height reference in the last few seconds ends up in the sea rather than on the ship.
    /// </summary>
    internal static float DiveNm => Mathf.Clamp(ModSettings.EcmDiveNm, 0.5f, 10f);

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

        // Missile defence in a session belongs to the machine that resolves the engagement: weapons
        // are simulated by the host and replicated, so a jam written on a client is overwritten by
        // the host's picture and jams nothing. Saying so is better than arming a feature that will
        // never fire.
        string? refused = Multiplayer.RefusedTo("Defensive ECM");
        if (refused != null)
        {
            report = refused;
            ModLog.Warn("Defensive ECM: " + refused);
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

        // Same reason as Toggle: the host decides missile defence in a session.
        string? refused = Multiplayer.RefusedTo("Defensive ECM");
        if (refused != null)
        {
            report = refused;
            ModLog.Warn("Defensive ECM: " + refused);
            return 0;
        }

        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            try
            {
                // Only hulls that carry ECM. A submarine or an unarmed auxillary is not armed with
                // a jammer, and arming one would put a feature in a menu that cannot work.
                if (!UnitFit.HasEcm(ship)) continue;

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
        if (Multiplayer.IsClient) return;

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

                // A hull with no jammer cannot defend with one: leave it alone rather than reporting
                // an engagement that could not have happened.
                if (!UnitFit.HasEcm(ship)) continue;

                // Close in, before anything else: a missile inside the dive range is finished with
                // the sea, not with the jammer.
                if (Sink(ship, record)) continue;

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
                    record.EcmTarget = weapon;

                    if (now < record.EcmRefreshAt) continue;
                    record.EcmRefreshAt = now + RefreshSeconds;

                    bool ok = EcmControl.Engage(ship, weapon, true, out string note);

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

    /// <summary>
    /// Puts every missile that has closed inside <see cref="DiveNm"/> into the water.
    ///
    /// Returns true when one is being dealt with, which is the caller's signal that this ship has
    /// nothing else to do this tick. Three ticks per missile at most: break its lock, push its
    /// height down, and put it in the sea.
    /// </summary>
    private static bool Sink(ObjectBase ship, UnitRecord record)
    {
        bool busy = false;
        float reach = DiveNm;

        var weapons = new List<object>();

        try
        {
            weapons.AddRange(Inbound.WeaponsAgainst(ship));
            foreach (object dangerous in Inbound.DangerousAgainst(ship))
            {
                if (!weapons.Contains(dangerous)) weapons.Add(dangerous);
            }
        }
        catch (Exception) { return false; }

        foreach (object weapon in weapons)
        {
            try
            {
                // A weapon whose range cannot be read is still put down: it is on this ship's own
                // inbound list, so the game has already decided it is close enough to matter, and
                // "the number would not read" is the worst possible reason to let one through.
                double? range = Inbound.RangeNm(ship, weapon as ObjectBase);
                if (range.HasValue && range.Value > reach) continue;

                string name = Name(weapon, range);

                // 1. Break its lock, so it cannot correct what comes next.
                WeaponDive.BreakLock(weapon, out _);

                // 2. Push it down. The flag says it is at the waterline (or has run out of time).
                WeaponDive.PushDown(weapon, out string descent, out bool waterline);

                if (!waterline) { busy = true; continue; }

                // 3. The sea.
                bool sunk = WeaponDive.Water(weapon, out string splash);

                if (sunk) Inbound.Forget(ship, weapon);

                record.DiveStops = record.DiveStops + 1;

                // One line per missile, not per tick: the game's lists take a moment to drop it.
                if (record.DiveStops > MaxDiveLogs) { busy = true; continue; }

                ModLog.Info("Defensive ECM: " + Name(ship) + " put " + name + " in the water - "
                            + (descent.Length > 0 ? descent + "; " : "") + splash);

                busy = true;
            }
            catch (Exception error)
            {
                ModLog.Error("Defensive ECM could not put a missile down", error);
            }
        }

        return busy;
    }

    /// <summary>How many dive lines one ship reports before it goes quiet. The dive keeps working.</summary>
    private const int MaxDiveLogs = 12;

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
        record.EcmTarget = null;

        ModLog.Info("Defensive ECM: " + Name(ship) + " stood down (" + why + ") - "
                    + (off ? note : "nothing to switch off; " + note));
    }

    /// <summary>
    /// The missile this ship's defensive jammer is holding, if any. Read by the map overlay, which
    /// needs it to draw the beam; null whenever the set is not engaged, so a stale target can never
    /// be drawn pointing at a missile that has gone.
    /// </summary>
    internal static object? TargetOf(ObjectBase? ship)
    {
        if (ship == null) return null;

        return UnitState.TryGet(ship, out UnitRecord record) && record.EcmEngaged
            ? record.EcmTarget
            : null;
    }

    /// <summary>The beams the map overlay draws for the defensive sets.</summary>
    internal static List<EcmBeam> Beams()
    {
        var beams = new List<EcmBeam>();

        foreach (ObjectBase ship in FleetTicker.Tracked())
        {
            try
            {
                if (!UnitState.TryGet(ship, out UnitRecord record) || !record.EcmEngaged) continue;

                beams.Add(new EcmBeam(ship, record.EcmTarget, true, false));
            }
            catch (Exception) { }
        }

        return beams;
    }

    /// <summary>One line describing this ship's state, for the menu tooltip.</summary>
    internal static string Describe(ObjectBase unit)
    {
        if (unit == null) return "no unit";

        UnitRecord record = UnitState.Of(unit);
        if (!record.DefensiveEcm) return "off";

        string state = record.EcmEngaged ? "armed, jamming now" : "armed, waiting for a vampire";
        return state + " (" + WeaponDive.Describe() + ")";
    }

    /// <summary>The missile's name and range, for the log line.</summary>
    private static string Name(object weapon, double? range)
    {
        string what;

        if (weapon is ObjectBase unit) what = Name(unit);
        else if (weapon is UnityEngine.Object asset && asset != null) what = asset.name;
        else what = weapon.GetType().Name;

        return range.HasValue ? what + " at " + range.Value.ToString("0.0") + " nm" : what;
    }

    private static string Name(ObjectBase? unit)
    {
        if (unit == null) return "<none>";

        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
