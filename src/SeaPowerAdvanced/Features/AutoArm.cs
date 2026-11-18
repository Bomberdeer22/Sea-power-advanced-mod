using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Auto-ARM shutoff: the moment an anti-radiation weapon is inbound, go dark.
///
/// The idea is old and it is not a cheat - it is what a real ship does, and it costs the ship its
/// own picture of the air. The game gives exactly the material needed:
///
/// <code>
/// ObjectBase._incomingWeapons        List&lt;WeaponBase&gt;  - weapons tracked against this unit
/// ObjectBase._dangerousMissiles      List&lt;Missile&gt;
/// ObjectBase.ThreateningMissile      read by reflection - the accessor cannot be called
///                                          (see Interop/Inbound.cs, shared with the ECM feature)
/// ObjectBase.TracksIncomingWeapons()
/// ObjectBase.DisableAllRadars() / EnableAllRadars(bool)
/// </code>
///
/// If defensive ECM is also armed on the same ship, it takes precedence while it is jamming: the
/// jammer cannot hold a lock with the radar dark, so going dark would defeat both features.
///
/// Because the game does not label a missile as "anti-radiation" in any member this mod can read,
/// the rule is deliberately conservative: any inbound weapon triggers a dark period. That leaves
/// the ship blind for a few seconds whether or not the shot was homing on the radar, which is the
/// same trade a real commanding officer faces, and it is why the feature is opt-in per ship.
/// </summary>
internal static class AutoArm
{
    private const float DarkSeconds = 8f;

    internal static bool IsOn(ObjectBase unit) => unit != null && UnitState.Of(unit).AutoArm;

    internal static bool Toggle(ObjectBase unit, out string report)
    {
        if (unit == null)
        {
            report = "no unit";
            return false;
        }

        // Cutting the radars is a plain radar switch, so in a session it is the same switch the
        // multiplayer mod throttles and the host can refuse. Arming it here would promise something
        // this machine cannot deliver.
        string? refused = Multiplayer.RefusedTo("Auto-ARM shutoff");
        if (refused != null)
        {
            report = refused;
            return false;
        }

        UnitRecord record = UnitState.Of(unit);
        record.AutoArm = !record.AutoArm;

        if (!record.AutoArm && record.AutoArmCut)
        {
            SensorControl.SetAllRadars(unit, true);
            record.AutoArmCut = false;
            report = "auto-ARM off; radars restored";
            return true;
        }

        report = record.AutoArm
            ? "auto-ARM on: radars cut for " + DarkSeconds + "s when a weapon is inbound"
            : "auto-ARM off";
        return true;
    }

    internal static void Tick()
    {
        if (Multiplayer.IsClient) return;

        float now = Time.unscaledTime;

        foreach (ObjectBase unit in FleetTicker.Tracked())
        {
            try
            {
                UnitRecord record = UnitState.Of(unit);

                if (record.AutoArmCut && now >= record.AutoArmRestoreAt)
                {
                    SensorControl.SetAllRadars(unit, true);
                    record.AutoArmCut = false;
                    ModLog.Info("Auto-ARM: " + Name(unit) + " radars back on after the threat passed");
                    continue;
                }

                if (!record.AutoArm || record.AutoArmCut) continue;

                // Contradictory tactics, one ship: defensive ECM needs the air-search radar
                // radiating to keep its lock, so it wins for as long as a jammer is up. Going dark
                // would blind the jammer and defeat both features at once.
                if (record.EcmEngaged) continue;

                if (!HasInbound(unit)) continue;

                if (SensorControl.SetAllRadars(unit, false))
                {
                    record.AutoArmCut = true;
                    record.AutoArmRestoreAt = now + DarkSeconds;
                    ModLog.Info("Auto-ARM: " + Name(unit) + " went dark - weapon inbound");
                }
            }
            catch (Exception error)
            {
                ModLog.Error("Auto-ARM failed for a unit", error);
            }
        }
    }

    /// <summary>
    /// True when the game is tracking a weapon against this unit. The definition of "inbound" lives
    /// in <see cref="Inbound"/> and is shared with the defensive ECM feature, so the two can never
    /// disagree about whether a vampire is in the air.
    /// </summary>
    private static bool HasInbound(ObjectBase unit) => Inbound.Any(unit);

    private static string Name(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
