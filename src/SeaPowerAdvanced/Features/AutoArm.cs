using System;
using System.Collections.Generic;
using System.Reflection;
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
/// ObjectBase.TracksIncomingWeapons()
/// ObjectBase.DisableAllRadars() / EnableAllRadars(bool)
/// </code>
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
    /// True when the game is tracking a weapon against this unit. Both the public missile list and
    /// the private weapon list are consulted, so either one being present is enough.
    /// </summary>
    private static bool HasInbound(ObjectBase unit)
    {
        try
        {
            if (unit._dangerousMissiles != null && unit._dangerousMissiles.Count > 0) return true;
            if (unit._incomingWeapons != null && unit._incomingWeapons.Count > 0) return true;
        }
        catch (Exception) { }

        // The documented getter is get_ThreateningMissile(), and calling that explicitly is a
        // compile error (CS0571: you cannot call an accessor by name), so the property is read
        // through reflection instead. A value type can never be null there, so one is treated as
        // "no answer" rather than as "a missile is inbound" - otherwise the ship would sit with its
        // radars cut for ever, which is worse than missing one warning.
        try
        {
            MemberInfo? missile = MemberProbe.Value(typeof(ObjectBase), "ThreateningMissile");
            if (missile == null) return false;

            Type type = missile is PropertyInfo property ? property.PropertyType
                        : ((FieldInfo)missile).FieldType;

            if (type.IsValueType && Nullable.GetUnderlyingType(type) == null) return false;

            return MemberProbe.Read(missile, unit) != null;
        }
        catch (Exception) { return false; }
    }

    private static string Name(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
