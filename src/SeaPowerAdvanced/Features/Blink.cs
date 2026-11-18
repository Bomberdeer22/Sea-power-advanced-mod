using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Radar blink: switch the surface-search radar on for a sweep, then off again.
///
/// A real ship does this so a passive anti-radiation seeker has a moving, intermittent target
/// instead of a steady beacon. The game's own sensors make it meaningful: the radar has to be on to
/// see, and off to be hard to home on.
///
/// Scope is one ship at a time, chosen from that ship's own menu - not a hidden global mode.
/// </summary>
/// <summary>Which radar set to cycle. The player picks it when starting a blink.</summary>
internal enum BlinkRadar
{
    AirSearch,
    SurfaceSearch,
    Both,
}

internal static class Blink
{
    private static readonly HashSet<ObjectBase> Cycling = new();

    /// <summary>How each set was found when the blink started, so stopping puts it back.</summary>
    private static readonly Dictionary<ObjectBase, (bool Air, bool Surface)> WasOn = new();

    /// <summary>
    /// Flips waiting to be checked, and what state the radar was asked for.
    ///
    /// A switch does not take effect the instant it is thrown - the game warms a radar up over
    /// several ticks - so a read straight afterwards would be a false alarm. In a multiplayer session
    /// a flip can also be refused outright, and a refusal looks exactly like a radar that did not
    /// follow its switch. Checking a few seconds later tells the two apart, and either way pretending
    /// to blink would be worse than stopping.
    /// </summary>
    private static readonly Dictionary<ObjectBase, (float At, bool Expect)> Verify = new();

    /// <summary>How long a flip is given to take effect before it is judged.</summary>
    private const float VerifySeconds = 4f;

    /// <summary>
    /// True while this unit's radar is cycling. The record is consulted as well as the set, so a
    /// unit stays "blinking" in the menu even if its entry was lost from the ticker's list.
    /// </summary>
    internal static bool IsOn(ObjectBase unit)
    {
        if (unit == null) return false;
        if (Cycling.Contains(unit)) return true;
        return UnitState.TryGet(unit, out UnitRecord record) && record.Blink;
    }

    /// <summary>
    /// Starts cycling the set the player chose.
    ///
    /// Radar blink means switching a radar on for a few seconds to sweep, then off again, so a
    /// passive seeker gets an intermittent target instead of a steady beacon. Which radar to do it
    /// with is the player's decision, and it matters: air search is the set an anti-radiation shot
    /// homes on, surface search is the one that finds ships. Hence the choice rather than a guess.
    /// </summary>
    internal static bool Start(ObjectBase unit, BlinkRadar radar, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        if (Cycling.Contains(unit))
        {
            report = "already blinking - stop it first";
            return false;
        }

        bool air = radar is BlinkRadar.AirSearch or BlinkRadar.Both;
        bool surface = radar is BlinkRadar.SurfaceSearch or BlinkRadar.Both;

        bool airWasOn = Read(() => SensorControl.GetAirSearchOn(unit));
        bool surfaceWasOn = Read(() => SensorControl.GetSurfaceSearchOn(unit));

        WasOn[unit] = (airWasOn, surfaceWasOn);
        Cycling.Add(unit);

        UnitRecord record = UnitState.Of(unit);
        record.Blink = true;
        record.NextBlinkAt = 0f;
        record.BlinkAir = air;
        record.BlinkSurface = surface;

        report = Describe(radar) + " now cycling every " + Interval() + "s" + SessionNote();
        return true;
    }

    /// <summary>Stops cycling and puts the radar back the way it was found.</summary>
    internal static bool Stop(ObjectBase unit, out string report)
    {
        if (unit == null || !Cycling.Contains(unit))
        {
            report = "not blinking";
            return false;
        }

        Cycling.Remove(unit);

        UnitRecord record = UnitState.Of(unit);
        record.Blink = false;

        WasOn.TryGetValue(unit, out (bool Air, bool Surface) remembered);
        bool airWasOn = remembered.Air;
        bool surfaceWasOn = remembered.Surface;
        WasOn.Remove(unit);
        Verify.Remove(unit);

        bool airOk = !record.BlinkAir || SensorControl.SetAirSearch(unit, airWasOn);
        bool surfaceOk = !record.BlinkSurface || SensorControl.SetSurfaceSearch(unit, surfaceWasOn);

        bool ok = airOk && surfaceOk;
        report = ok
            ? "stopped; the radar(s) put back exactly as they were found"
            : "stopped, but a radar did not respond - check it in the Sensors menu";
        return ok;
    }

    internal static void Tick()
    {
        if (Cycling.Count == 0) return;

        float interval = Interval();
        float now = Time.unscaledTime;

        foreach (ObjectBase unit in new List<ObjectBase>(Cycling))
        {
            try
            {
                if (unit == null) continue;

                if (unit.IsDestroyed)
                {
                    Cycling.Remove(unit);
                    WasOn.Remove(unit);
                    Verify.Remove(unit);
                    continue;
                }

                UnitRecord record = UnitState.Of(unit);

                if (Flipped(unit, record, now))
                {
                    StopRefused(unit, record);
                    continue;
                }

                if (now < record.NextBlinkAt) continue;

                record.NextBlinkAt = now + interval;
                record.BlinkPhase = !record.BlinkPhase;

                if (record.BlinkAir) SensorControl.SetAirSearch(unit, record.BlinkPhase);
                if (record.BlinkSurface) SensorControl.SetSurfaceSearch(unit, record.BlinkPhase);

                if (Multiplayer.InSession) Verify[unit] = (now + VerifySeconds, record.BlinkPhase);

                if (ModSettings.VerboseLog && record.BlinkPhase)
                {
                    ModLog.Info("Blink: " + HatchName(unit) + " "
                                + (record.BlinkAir && record.BlinkSurface ? "both radars on"
                                   : record.BlinkAir ? "air search on"
                                   : "surface search on"));
                }
            }
            catch (Exception error)
            {
                Cycling.Remove(unit);
                ModLog.Error("Blink stopped for a unit after an error", error);
            }
        }
    }

    /// <summary>
    /// The half-cycle: how long the radar stays in each state.
    ///
    /// Two seconds is the mod's own floor, but a session sets a higher one. The multiplayer mod drops
    /// a repeat radar toggle on the same unit and radar group inside ten seconds, and a dropped
    /// toggle does not move the switch at all - so a six second blink would leave the radar stuck
    /// wherever the throttle caught it. The setting is therefore a floor, not a ceiling.
    /// </summary>
    private static float Interval() =>
        Mathf.Clamp(ModSettings.BlinkSeconds, Multiplayer.RadarToggleFloorSeconds, 120f);

    /// <summary>True when a flip that should have taken effect has not.</summary>
    private static bool Flipped(ObjectBase unit, UnitRecord record, float now)
    {
        if (Verify.Count == 0) return false;
        if (!Verify.TryGetValue(unit, out (float At, bool Expect) check)) return false;
        if (now < check.At) return false;

        Verify.Remove(unit);

        // A session that ended while the check was outstanding: there is no host left to blame, and
        // second-guessing the game in single player is not this feature's job.
        if (!Multiplayer.InSession) return false;

        if (record.BlinkAir && SensorControl.GetAirSearchOn(unit) is bool air && air != check.Expect)
            return true;

        if (record.BlinkSurface && SensorControl.GetSurfaceSearchOn(unit) is bool surface
            && surface != check.Expect)
            return true;

        return false;
    }

    /// <summary>
    /// Stops blinking a unit whose switch did not take, and says why. In a session the usual reason
    /// is the host - it refused the order, or it reverted the switch a frame later - and a player
    /// watching a radar that never goes dark needs to be told, not left guessing.
    /// </summary>
    private static void StopRefused(ObjectBase unit, UnitRecord record)
    {
        Cycling.Remove(unit);
        WasOn.Remove(unit);
        record.Blink = false;

        string why = Multiplayer.InSession
            ? "the switch was refused or reverted - in a session the host decides whether a radar "
              + "radiates, and it has had the last word here"
            : "the radar did not follow the switch";

        ModLog.Warn("Radar blink stopped for " + HatchName(unit) + ": " + why);
        Multiplayer.Note("radar blink on " + HatchName(unit) + " stopped: " + why);
    }

    /// <summary>The sentence appended to the start report when a session has raised the floor.</summary>
    private static string SessionNote()
    {
        if (!Multiplayer.InSession || ModSettings.BlinkSeconds >= Multiplayer.RadarToggleFloorSeconds)
            return "";

        return " (raised from " + ModSettings.BlinkSeconds + "s: the multiplayer mod refuses a "
               + "second radar toggle on the same ship inside "
               + (int)Multiplayer.ToggleFloorSeconds + "s)";
    }

    private static bool Read(Func<bool?> getter)
    {
        try { return getter() ?? true; }
        catch (Exception) { return true; }
    }

    /// <summary>What the player will see in the label: which set is cycling.</summary>
    internal static string Describe(BlinkRadar radar) => radar switch
    {
        BlinkRadar.AirSearch => "air search radar",
        BlinkRadar.SurfaceSearch => "surface search radar",
        _ => "both radars",
    };

    /// <summary>What this unit is cycling right now, for the menu label.</summary>
    internal static string State(ObjectBase unit)
    {
        if (!IsOn(unit)) return "off";

        if (!UnitState.TryGet(unit, out UnitRecord record)) return "on";

        if (record.BlinkAir && record.BlinkSurface) return "both radars";
        if (record.BlinkAir) return "air search";
        return "surface search";
    }

    private static string HatchName(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
