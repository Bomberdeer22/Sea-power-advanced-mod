using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using SeaPowerAdvanced.Interop;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Readiness: cold cruise, action stations, and the cost of staying closed up.
///
/// The readiness report was right that **general quarters is not a settable state** in this game -
/// there is no such member. So the mod keeps it, and makes it *mean* something through members that
/// do exist:
///
/// <code>
/// ObjectBase.SetWeaponStatus(...)                  the game's own weapons status (resolved at runtime)
/// WeaponSystem._remainingReloadTime                public float, the live reload clock
/// WeaponSystem._isReloading / StartReload(bool)
/// </code>
///
/// At action stations the reload clock is shaved by a small percentage while it runs, which is what
/// a fully manned mount actually does. In cold cruise nothing is shaved and fatigue decays.
///
/// Fatigue is the counterweight, and it is entirely the mod's own model: it accumulates while the
/// ship is closed up and applies a growing penalty to the same reload clock. That keeps the trade
/// honest - you cannot leave the fleet at action stations for an hour for free.
/// </summary>
internal static class Readiness
{
    /// <summary>How much of a reload action stations takes off, at zero fatigue.</summary>
    private const float GqBonus = 0.15f;

    /// <summary>How much fatigue adds back, at full exhaustion.</summary>
    private const float FatiguePenalty = 0.25f;

    /// <summary>Seconds of action stations that make a crew fully tired.</summary>
    private const float FatigueFullSeconds = 900f;

    internal static float Fatigue(ObjectBase unit) => Math.Clamp(UnitState.Of(unit).Fatigue, 0f, 1f);

    internal static bool AtActionStations(ObjectBase unit) => UnitState.Of(unit).GeneralQuarters;

    /// <summary>Closes the ship up (or stands it down) and applies the weapons status behind it.</summary>
    internal static bool SetGeneralQuarters(ObjectBase unit, bool on, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        UnitRecord record = UnitState.Of(unit);
        record.GeneralQuarters = on;

        // On a client the reload clock belongs to the host: a shave written here is reverted by the
        // host's own weapon update, so the bonus is not claimed and not applied. The weapons posture
        // still goes out as the game's own call, which is the half that can survive a session.
        bool here = !Multiplayer.IsClient;
        record.GqReloadBonus = on && here ? GqBonus : 0f;

        bool status = false;
        try
        {
            status = ApplyWeaponStatus(unit, on);
        }
        catch (Exception) { }

        report = on
            ? (here
                ? "action stations: reloads " + (int)(GqBonus * 100) + "% faster while held"
                : "action stations: weapons status sent, but the reload speed-up is the host's "
                  + "clock in a session, so it is not applied here")
              + (status ? "" : " (weapon status not available in this build)")
            : "stood down: cold cruise, fatigue recovering";

        // ok is the weapons status, not "the method returned": that is the only half of readiness a
        // session can carry, so it is the only half that can be shown as taking.
        Multiplayer.Attempt("readiness", unit, status, "action stations " + (on ? "on" : "off")
                                                     + (here ? "" : " (reload speed-up skipped)")
                                                     + (status ? "" : " (no weapon status in this build)"));
        return true;
    }

    /// <summary>
    /// Uses the game's own weapons status for the readiness change, through
    /// <see cref="WeaponStatusControl"/>. The enum is deliberately not named here: it is taken from
    /// the game's own method signature, because naming it failed to compile.
    /// </summary>
    private static bool ApplyWeaponStatus(ObjectBase unit, bool atActionStations) =>
        WeaponStatusControl.Set(unit, atActionStations);

    /// <summary>Shaves or stretches the reload clock on every mount of this unit.</summary>
    private static void AdjustReloads(ObjectBase unit, float fraction)
    {
        if (Math.Abs(fraction) < 0.001f) return;

        object? parameters = Capabilities.ParametersOf(unit);
        if (parameters == null) return;

        MemberInfo? systems = MemberProbe.Value(parameters.GetType(),
            "_weaponSystems", "WeaponSystems", "weaponSystems");
        if (MemberProbe.Read(systems, parameters) is not System.Collections.IEnumerable list) return;

        foreach (object? item in list)
        {
            if (item is not WeaponSystem mount) continue;

            try
            {
                if (!mount._isReloading) continue;

                float remaining = mount._remainingReloadTime;
                if (remaining <= 0.01f) continue;

                mount._remainingReloadTime = remaining * (1f - fraction);
            }
            catch (Exception) { }
        }
    }

    /// <summary>Called every couple of seconds: accrues fatigue and applies it to reloads.</summary>
    internal static void Tick()
    {
        foreach (ObjectBase unit in FleetTicker.Tracked())
        {
            try
            {
                UnitRecord record = UnitState.Of(unit);

                if (record.GeneralQuarters)
                {
                    record.BattleStationsSeconds += 2f;
                    record.Fatigue = Math.Clamp(record.BattleStationsSeconds / FatigueFullSeconds, 0f, 1f);
                }
                else
                {
                    // Recovering is slower than tiring, which is the point of standing down early.
                    record.BattleStationsSeconds = Math.Max(0f, record.BattleStationsSeconds - 1f);
                    record.Fatigue = Math.Clamp(record.BattleStationsSeconds / FatigueFullSeconds, 0f, 1f);
                }

                // Fatigue is still tracked and shown on a client - it is the mod's own model and
                // costs nothing - but the reload clock it would change is not ours to change.
                if (Multiplayer.IsClient) continue;

                float net = record.GqReloadBonus - record.Fatigue * FatiguePenalty;
                AdjustReloads(unit, net);
            }
            catch (Exception error)
            {
                ModLog.Error("Readiness update failed for a unit", error);
            }
        }
    }

    internal static string Describe(ObjectBase unit)
    {
        UnitRecord record = UnitState.Of(unit);
        return (record.GeneralQuarters ? "action stations" : "cold cruise")
               + ", fatigue " + (int)(Fatigue(unit) * 100) + "%";
    }
}
