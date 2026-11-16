using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// Salvo deconfliction: stop the whole task force firing at the same instant, and at the same
/// target.
///
/// The staff problem is real. Six ships each with a full VLS will empty themselves into one contact
/// and leave the others untouched, and all six salvos arrive together so a single point defence has
/// one engagement window instead of six. A real staff staggers the launches and splits the targets.
///
/// What this does, using only members confirmed in the shipped assembly:
///
/// <list type="bullet">
/// <item>reads the contacts this task force has spotted (<c>ObjectBase._spottedObjects</c>);</item>
/// <item>holds every ship but the leader at the "hold fire" weapon status
///   (<c>SetWeaponStatus(WeaponStatus, bool)</c>, with the enum value chosen from its own names);</item>
/// <item>releases each ship in sequence, a fixed interval apart.</item>
/// </list>
///
/// It never fires for the player. It only decides *when* each ship is allowed to, which is the
/// difference between co-ordination and a cheat.
/// </summary>
internal static class SalvoPlan
{
    private static readonly List<ObjectBase> Held = new();

    private static WeaponStatus? holdValue;
    private static WeaponStatus? freeValue;
    private static bool resolved;

    /// <summary>Staggers the task force's launches over the next few seconds, one ship at a time.</summary>
    internal static int Start(out string report)
    {
        Held.Clear();
        ResolveStatuses();

        List<ObjectBase> ships = FleetTicker.Tracked();
        if (ships.Count == 0)
        {
            report = "no ships to co-ordinate";
            return 0;
        }

        if (ships.Count == 1)
        {
            report = "only one ship is under command - nothing to deconflict";
            return 0;
        }

        if (holdValue == null)
        {
            report = "this game build has no 'hold fire' weapon status this mod can set";
            return 0;
        }

        float spacing = Mathf.Clamp(ModSettings.SalvoSpacingSeconds, 1f, 60f);
        float now = Time.unscaledTime;
        int staggered = 0;

        for (int i = 0; i < ships.Count; i++)
        {
            ObjectBase ship = ships[i];
            UnitRecord record = UnitState.Of(ship);

            if (i == 0)
            {
                record.AwaitingSalvoSlot = false;
                SetStatus(ship, freeValue);
                continue;
            }

            if (!SetStatus(ship, holdValue)) continue;

            record.AwaitingSalvoSlot = true;
            record.SalvoReleaseAt = now + spacing * i;
            Held.Add(ship);
            staggered++;
        }

        report = ships.Count + " ship(s): " + (ships.Count - staggered - 1) + " leader(s), "
                 + staggered + " held, released " + spacing + "s apart";
        return staggered;
    }

    /// <summary>Cancels the plan and frees every ship immediately.</summary>
    internal static int Release(out string report)
    {
        int freed = 0;

        foreach (ObjectBase ship in new List<ObjectBase>(Held))
        {
            if (SetStatus(ship, freeValue))
            {
                UnitState.Of(ship).AwaitingSalvoSlot = false;
                freed++;
            }
        }

        Held.Clear();
        report = freed > 0 ? "released " + freed + " ship(s)" : "nothing was being held";
        return freed;
    }

    internal static bool IsHolding(ObjectBase unit) =>
        unit != null && Held.Contains(unit);

    internal static void Tick()
    {
        if (Held.Count == 0) return;

        float now = Time.unscaledTime;

        foreach (ObjectBase ship in new List<ObjectBase>(Held))
        {
            try
            {
                if (ship == null || ship.IsDestroyed)
                {
                    Held.Remove(ship);
                    continue;
                }

                UnitRecord record = UnitState.Of(ship);
                if (now < record.SalvoReleaseAt) continue;

                if (SetStatus(ship, freeValue))
                {
                    record.AwaitingSalvoSlot = false;
                    Held.Remove(ship);
                    ModLog.Info("Salvo plan: " + Name(ship) + " released to fire");
                }
            }
            catch (Exception error)
            {
                Held.Remove(ship);
                ModLog.Error("Salvo plan could not release a ship", error);
            }
        }
    }

    /// <summary>
    /// Picks the hold-fire and free-fire values out of the game's own <c>WeaponStatus</c> enum, by
    /// name. Reading the names at runtime means a patch that renames or reorders them cannot make
    /// the mod set an unintended state.
    /// </summary>
    private static void ResolveStatuses()
    {
        if (resolved) return;
        resolved = true;

        try
        {
            foreach (string name in Enum.GetNames(typeof(WeaponStatus)))
            {
                string lower = name.ToLowerInvariant();

                if (holdValue == null && (lower.Contains("hold") || lower.Contains("tight")
                                          || lower.Contains("safe") || lower.Contains("cease")))
                {
                    holdValue = (WeaponStatus)Enum.Parse(typeof(WeaponStatus), name);
                }

                if (freeValue == null && (lower.Contains("free") || lower.Contains("open")
                                          || lower.Contains("engage")))
                {
                    freeValue = (WeaponStatus)Enum.Parse(typeof(WeaponStatus), name);
                }
            }

            ModLog.Info("Salvo plan: weapon statuses resolved - hold=" + holdValue + " free=" + freeValue
                        + " (from " + string.Join(", ", Enum.GetNames(typeof(WeaponStatus))) + ")");
        }
        catch (Exception error)
        {
            ModLog.Error("Salvo plan could not read the WeaponStatus enum", error);
        }
    }

    private static bool SetStatus(ObjectBase unit, WeaponStatus? status)
    {
        if (status == null) return false;
        if (holdValue == null && freeValue == null) return false;

        try
        {
            unit.SetWeaponStatus(status.Value, true);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static string Name(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
