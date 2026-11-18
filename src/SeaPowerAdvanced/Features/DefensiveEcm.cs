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
/// And it finishes the job at close range. A sea-skimmer whose seeker loses lock does not fly on
/// towards the ship - it loses its height reference, stops correcting, and goes down. So once a
/// missile is inside <see cref="DiveNm"/>, the jammer's lock on it is used for what it is worth: the
/// weapon's own lock is broken, its height is flown down, and when it arrives the weapon is handed to
/// the game's own end-of-flight routes - fuel, life, its own state machine. That is
/// <see cref="WeaponDive"/>, and it is the one place this mod writes to a weapon rather than to a
/// ship's sensors - deliberately, because a missile inside the dive range is a missile that has
/// already been fired at somebody.
///
/// The game does **not** remove a weapon that reaches the surface. 0.19.0 assumed it did; the test
/// showed the missile held at fifty feet and left there. Hence the end-of-flight handover.
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
    /// How close a missile gets before the jam is finished with it and it is flown down.
    ///
    /// Ten miles, because the descent has to be visible and the weapon has to be dealt with before it
    /// is overhead: at six hundred knots a sea-skimmer covers two miles in twelve seconds. Ten gives
    /// it about a minute, and the descent itself is capped at forty seconds.
    /// </summary>
    internal static float DiveNm => Mathf.Clamp(ModSettings.EcmDiveNm, 0f, 30f);

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

        // Weapons the game has thrown away without telling anybody: forget them, or the dive's own
        // bookkeeping holds every missile of the mission for ever.
        WeaponDive.Sweep();

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
    /// Points every missile that has closed inside <see cref="DiveNm"/> at the sea.
    ///
    /// It is steered, not killed: the lock is broken and the nose is flown down, far enough out that
    /// the descent finishes well before the ship, and at the bottom the weapon is handed to the
    /// game's own end-of-flight routes. Nothing here destroys the object and nothing sets the warhead
    /// off; the previous builds did both, and produced an explosion every tick and then a
    /// NullReferenceException inside the game's own update.
    ///
    /// <b>Three things keep it from being a wall</b>, and all three are how the real thing works:
    /// the ship has a limited number of <b>guidance channels</b> (<c>EcmDiveChannels</c>), a new dive
    /// may only start every <c>EcmDiveIntervalSeconds</c>, and a missile whose seeker is not a radar
    /// is left alone entirely, because jamming cannot blind a heat seeker
    /// (<c>EcmDiveRadarSeekersOnly</c>). A four-missile salvo therefore gets through.
    ///
    /// Returns true when a weapon is being brought down, which tells the caller this ship has
    /// nothing else to do this tick.
    /// </summary>
    private static bool Sink(ObjectBase ship, UnitRecord record)
    {
        bool busy = false;
        float reach = DiveNm;

        // Zero means the dive is switched off: the jam still works, the missile is left alone.
        if (reach <= 0f) return false;

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

        float now = Time.unscaledTime;
        int channels = ModSettings.EcmDiveChannels;
        int held = 0;
        bool fullLogged = false;

        foreach (object weapon in weapons)
        {
            try
            {
                // A weapon whose range cannot be read is still brought down: it is on this ship's own
                // inbound list, so the game has already decided it is close enough to matter.
                double? range = Inbound.RangeNm(ship, weapon as ObjectBase);
                if (range.HasValue && range.Value > reach) continue;

                // Has it finished? Either the game removed it at the surface, or it is sitting on
                // the water and this build does not remove it. One line, one time.
                if (WeaponDive.Finished(weapon, out string finished))
                {
                    if (record.DiveStops < MaxDiveLogs) ModLog.Info("Defensive ECM: " + finished);
                    continue;
                }

                bool already = WeaponDive.IsDiving(weapon);

                // 1. A seeker that radio jamming cannot touch is not this feature's business. This
                //    is physics, not difficulty: an infrared or anti-radiation seeker does not care
                //    what the jammer is transmitting, and one of them is homing on it.
                if (!WeaponDive.CanBeDecoyed(weapon, out string seeker))
                {
                    if (already) held++;
                    if (record.DiveStops < MaxDiveLogs && seeker.Length > 0
                        && ModSettings.VerboseLog)
                        ModLog.Info("Defensive ECM: " + Name(ship) + " cannot decoy "
                                    + Name(weapon, range) + " - " + seeker);
                    continue;
                }

                // 2. The ship's guidance channels are finite. A jammer already holding its limit
                //    watches the rest of the salvo arrive, which is the whole point of a salvo.
                if (!already && held >= channels)
                {
                    if (!fullLogged)
                    {
                        fullLogged = true;
                        record.DiveStops = record.DiveStops + 1;

                        if (record.DiveStops <= MaxDiveLogs)
                            ModLog.Warn("Defensive ECM: " + Name(ship) + " has all " + channels
                                        + " guidance channel(s) committed - the rest of this salvo "
                                        + "is not being decoyed");
                    }
                    continue;
                }

                // 3. The crew needs a moment between solutions.
                if (!already && now < record.DiveNextAt) continue;

                if (already) held++;

                string name = Name(weapon, range);

                // A new dive: this channel is now in use, and the next one has to wait.
                if (!already)
                {
                    held++;
                    record.DiveChannels = held;
                    record.DiveNextAt = now + ModSettings.EcmDiveIntervalSeconds;
                }

                // 1. It cannot correct what comes next.
                WeaponDive.BreakLock(weapon, out _);

                // 2. Point it down, gradually, from here.
                bool diving = WeaponDive.Steer(weapon, out string descent, out bool atSurface);

                if (atSurface)
                {
                    WeaponDive.AtSurface(weapon);

                    if (WeaponDive.Announce(weapon))
                    {
                        record.DiveStops = record.DiveStops + 1;

                        if (record.DiveStops <= MaxDiveLogs)
                        {
                            ModLog.Info("Defensive ECM: " + Name(ship) + " has " + name
                                        + " at the waterline - " + (descent.Length > 0
                                            ? descent + "; " : "")
                                        + "the game's own water-dip removal takes it from here");
                        }
                    }

                    busy = true;
                    continue;
                }

                if (WeaponDive.Announce(weapon))
                {
                    record.DiveStops = record.DiveStops + 1;

                    if (record.DiveStops <= MaxDiveLogs)
                    {
                        ModLog.Info("Defensive ECM: " + Name(ship) + " is flying " + name
                                    + " down - " + (descent.Length > 0 ? descent
                                        : "nothing the mod can steer; the guidance link is broken "
                                          + "and it is coasting"));
                    }
                }

                busy |= diving;
            }
            catch (Exception error)
            {
                ModLog.Error("Defensive ECM could not put a missile down", error);
            }
        }

        record.DiveChannels = busy ? Math.Min(held, channels) : 0;

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

        // How much of the ship's attention is in use, so the player can see a salvo overwhelming it
        // rather than wondering why some missiles get through.
        int channels = ModSettings.EcmDiveChannels;
        int used = Math.Min(record.DiveChannels, channels);

        if (used > 0) state += ", " + used + "/" + channels + " channels";

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
