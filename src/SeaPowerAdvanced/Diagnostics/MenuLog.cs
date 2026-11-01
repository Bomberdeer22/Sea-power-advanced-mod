using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SeaPower;
using SeaPowerAdvanced.Hatches;

namespace SeaPowerAdvanced.Diagnostics;

/// <summary>
/// Logs what the context menu found for a unit, once per unit per distinct result.
///
/// Right-clicking is frequent, so logging every build would flood the log; logging none makes a
/// missing menu impossible to diagnose. This logs when the answer changes (a ship gained or lost
/// mounts, or the mod started seeing a different picture), which is exactly when it matters.
/// </summary>
internal static class MenuLog
{
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);

    internal static void Once(ObjectBase? unit, List<HatchGroup> groups, string summary)
    {
        try
        {
            string key = Key(unit) + "|" + groups.Count + "|" + summary;

            if (Seen.Contains(key))
            {
                if (ModSettings.VerboseLog) ModLog.Info("Menu for " + HatchControl.UnitName(unit) + ": " + summary);
                return;
            }

            if (Seen.Count > 500) Seen.Clear();
            Seen.Add(key);

            ModLog.Info("Menu for " + HatchControl.UnitName(unit) + ": " + summary);

            foreach (HatchGroup group in groups)
            {
                ModLog.Info($"   {group.Label} [{group.Kind}] {group.Status}");
            }

            if (groups.Count == 0)
            {
                ModLog.Warn("   No weapon mounts matched. The ship may have no launchers, or its mounts "
                            + "have no hatch data. Use 'Log hatch report' for the full picture.");
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Menu logging failed", error);
        }
    }

    internal static void Reset() => Seen.Clear();

    private static string Key(ObjectBase? unit)
    {
        if (unit == null) return "-";
        try
        {
            int id = unit.Id;
            return id.ToString();
        }
        catch (Exception)
        {
            return RuntimeHelpers.GetHashCode(unit).ToString();
        }
    }
}
