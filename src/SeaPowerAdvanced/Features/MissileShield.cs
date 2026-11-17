using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// The missile shield: nothing guided reaches the task force.
///
/// This is the one feature in the mod that is deliberately not a simulation. Everywhere else the mod
/// drives the game's own mechanisms and then says honestly that the outcome is the simulation's to
/// decide - defensive ECM points a jammer, a lock holds a set down, a salvo is spaced out. This one
/// promises an outcome, and it is built to keep that promise:
///
/// <list type="number">
/// <item><b>Every tick</b>, each guided weapon the game is tracking against one of our units has the
///   member it is homing on written to null. The weapon keeps flying; it stops knowing where to. This
///   is the same state a missile is in when the ship it was fired at is sunk under it, so the game
///   already handles it.</item>
/// <item><b>When it matters</b> - the weapon is on the game's own dangerous list, or it is inside
///   <see cref="CloseNm"/> - the flight is ended: the game's terminal method if this build has one,
///   otherwise a plain destroy, and the ship's inbound list is cleaned up afterwards.</item>
/// </list>
///
/// What it does <b>not</b> cover, stated here rather than discovered in a mission: guns and rockets.
/// A shell is ballistic and has no lock to break, so it is not touched. The feature is called a
/// missile shield and not an invulnerability toggle for exactly that reason.
///
/// It is off until the player switches it on, it is refused outright on a multiplayer client
/// (weapons are simulated by the host), and every weapon it touches is written to
/// <c>missile-shield.txt</c>.
/// </summary>
internal static class MissileShield
{
    /// <summary>
    /// How close a weapon has to be before its flight is ended regardless of what the game's own
    /// dangerous list says. Two nautical miles is inside any terminal defence envelope and far
    /// enough out that a ship is never touched.
    /// </summary>
    private const float CloseNm = 2f;

    /// <summary>
    /// How many weapons one unit will report on before this feature goes quiet. The count is only
    /// for the log; the shield keeps working either way.
    /// </summary>
    private const int MaxLogsPerUnit = 12;

    /// <summary>Null until the player toggles it, so the setting supplies the first answer.</summary>
    private static bool? armed;

    private static float nextSweep;

    internal static bool IsArmed => armed ?? ModSettings.MissileShield;

    /// <summary>Switches the shield on or off for the whole task force, and says what it will do.</summary>
    internal static bool Toggle(out string report)
    {
        string? refused = Multiplayer.RefusedTo("The missile shield");
        if (refused != null)
        {
            report = refused;
            ModLog.Warn("Missile shield: " + report);
            return false;
        }

        armed = !IsArmed;

        if (!armed.Value)
        {
            report = "missile shield off - guided weapons are the game's again";
            ModLog.Info("Missile shield: " + report);
            return true;
        }

        report = "missile shield on for the whole task force: every guided weapon aimed at one of "
                 + "your units has its lock broken, and is taken down if it is on the game's "
                 + "dangerous list or inside " + (int)CloseNm + " nm. Guns and rockets are not "
                 + "covered.";
        ModLog.Info("Missile shield: " + report);

        // Written at the moment it is switched on, so the file exists even in a quiet mission.
        WeaponGuard.WriteReport();
        return true;
    }

    /// <summary>
    /// Called from the ticker. Sweeps every player unit - ships, submarines, aircraft and
    /// helicopters - because a missile does not care which of them it is aimed at.
    /// </summary>
    internal static void Tick()
    {
        if (!ModSettings.FeaturesEnabled) return;
        if (!IsArmed) return;
        if (Multiplayer.IsClient) return;

        // 20 Hz is faster than any seeker's own update and cheap: an inbound list is a handful of
        // entries, and a null write is one reflection set.
        float now = Time.unscaledTime;
        if (now < nextSweep) return;
        nextSweep = now + 0.05f;

        List<ObjectBase> units;
        try { units = GameWorld.PlayerUnits(); }
        catch (Exception) { return; }

        if (units.Count == 0) return;

        var handled = new HashSet<object>();

        foreach (ObjectBase unit in units)
        {
            try
            {
                if (unit == null || unit.IsDestroyed) continue;

                // Everything the game is tracking against this unit...
                foreach (object weapon in Inbound.WeaponsAgainst(unit))
                {
                    if (!handled.Add(weapon)) continue;
                    Guard(unit, weapon, false);
                }

                // ...and the ones it has flagged as dangerous, which is the hard-kill trigger.
                foreach (object weapon in Inbound.DangerousAgainst(unit))
                {
                    if (!handled.Add(weapon)) continue;
                    Guard(unit, weapon, true);
                }
            }
            catch (Exception error)
            {
                ModLog.Error("Missile shield failed for a unit", error);
            }
        }
    }

    /// <summary>One weapon against one unit.</summary>
    private static void Guard(ObjectBase unit, object weapon, bool dangerous)
    {
        UnitRecord record = UnitState.Of(unit);

        // The cap is on the log only. The shield keeps working whatever the count says - a quiet
        // log is a convenience, and it must never be the thing that stops a missile.
        bool quiet = record.ShieldStops >= MaxLogsPerUnit;

        double? range = Inbound.RangeNm(unit, weapon as ObjectBase);
        bool close = dangerous || (range.HasValue && range.Value <= CloseNm);

        bool broke = WeaponGuard.Break(weapon, out string breakNote);

        string outcome = broke ? breakNote : "NOT broken - " + breakNote;

        if (close)
        {
            bool ended = WeaponGuard.Finish(weapon, out string finishNote);

            if (ended) Inbound.Forget(unit, weapon);

            outcome = (broke ? breakNote : "no lock broken") + "; "
                      + (ended ? finishNote : "NOT taken down - " + finishNote);
        }

        record.ShieldStops = record.ShieldStops + 1;
        if (quiet) return;

        string where = range.HasValue ? " at " + range.Value.ToString("0.0") + " nm" : "";

        ModLog.Info("Missile shield: " + Name(weapon) + where + " from " + Name(unit) + " - "
                    + outcome);

        if (!broke)
        {
            ModLog.Warn("Missile shield: " + Name(weapon) + " - " + breakNote
                        + ". missile-shield.txt lists what this weapon type does expose.");
        }
    }

    /// <summary>The line the menu shows.</summary>
    internal static string Describe()
    {
        if (!IsArmed) return "off";

        string refused = Multiplayer.IsClient ? " - the host simulates weapons in a session" : "";

        return "on for the task force: " + WeaponGuard.Describe()
               + (refused.Length > 0 ? refused : ", " + (int)CloseNm + " nm close guard");
    }

    private static string Name(object? thing)
    {
        if (thing is ObjectBase unit)
        {
            try { return unit.DisplayNameShort; }
            catch (Exception) { }
        }

        if (thing is UnityEngine.Object asset && asset != null) return asset.name;

        return thing == null ? "<none>" : thing.GetType().Name;
    }
}
