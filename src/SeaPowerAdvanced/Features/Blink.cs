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
internal static class Blink
{
    private static readonly HashSet<ObjectBase> Cycling = new();

    private static readonly Dictionary<ObjectBase, bool> WasOn = new();

    internal static bool IsOn(ObjectBase unit) => unit != null && Cycling.Contains(unit);

    /// <summary>Starts cycling. Returns how many ships were added (0 or 1).</summary>
    internal static bool Start(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        if (Cycling.Contains(unit))
        {
            report = "already blinking";
            return false;
        }

        bool? current = null;
        try { current = SensorControl.GetSurfaceSearchOn(unit); }
        catch (Exception) { }

        WasOn[unit] = current ?? true;
        Cycling.Add(unit);

        UnitRecord record = UnitState.Of(unit);
        record.Blink = true;
        record.NextBlinkAt = 0f;
        record.BlinkCancelled = false;

        report = "surface search radar now cycling every " + Interval() + "s";
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
        UnitState.Of(unit).Blink = false;

        bool restore = WasOn.TryGetValue(unit, out bool remembered) && remembered;
        WasOn.Remove(unit);

        bool ok = SensorControl.SetSurfaceSearch(unit, restore);
        report = ok
            ? "stopped; radar " + (restore ? "switched back on" : "left off")
            : "stopped; the radar did not respond";
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
                if (unit == null || unit.IsDestroyed)
                {
                    Cycling.Remove(unit);
                    WasOn.Remove(unit);
                    continue;
                }

                UnitRecord record = UnitState.Of(unit);
                if (now < record.NextBlinkAt) continue;

                record.NextBlinkAt = now + interval;
                record.BlinkPhase = !record.BlinkPhase;

                SensorControl.SetSurfaceSearch(unit, record.BlinkPhase);

                if (ModSettings.VerboseLog && record.BlinkPhase)
                    ModLog.Info("Blink: " + HatchName(unit) + " surface search on");
            }
            catch (Exception error)
            {
                Cycling.Remove(unit);
                ModLog.Error("Blink stopped for a unit after an error", error);
            }
        }
    }

    private static float Interval() => Mathf.Clamp(ModSettings.BlinkSeconds, 2f, 120f);

    private static string HatchName(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
