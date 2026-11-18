using System;
using System.Collections.Generic;
using System.Text;
using SeaPower;
using SeaPowerAdvanced.Hatches;

namespace SeaPowerAdvanced.Diagnostics;

/// <summary>
/// Logs one ship's hatch picture: every mount, its cells, how many hatch GameObjects each cell
/// owns and whether they are open right now.
///
/// This is the file to attach to a bug report: it shows exactly what the mod sees, and the same
/// data the menu uses.
/// </summary>
internal static class ShipReport
{
    internal static void Write(ObjectBase unit, List<HatchGroup>? groups = null)
    {
        try
        {
            groups ??= HatchBay.Read(unit);
            var text = new StringBuilder();

            text.AppendLine("Sea Power Advanced " + ModInfo.Version + " - hatch report");
            text.AppendLine("Ship:     " + HatchControl.UnitName(unit));
            text.AppendLine("Class:    " + Safe(() => unit.ClassNameFull));
            text.AppendLine("When:     " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Mounts:   " + groups.Count);
            text.AppendLine("Summary:  " + HatchBay.Summary(unit, groups));
            text.AppendLine();

            foreach (HatchGroup group in groups)
            {
                WeaponParameters? vwp = group.Weapon._vwp;
                text.AppendLine($"## {group.Name}  [{group.Kind}]  {group.Cells.Count} cell(s), {group.HatchCount} hatch(es)");
                text.AppendLine("   type:          " + group.Weapon.GetType().FullName);
                text.AppendLine("   hatches/cell:  " + (vwp?._numberOfHatchesPerContainers ?? 0));
                text.AppendLine("   mount cover:   " + (group.Weapon._openSystemAnimation != null ? "yes" : "no")
                                + " (open animation: " + (vwp?._openSystemAnimation ?? "-") + ")");

                for (int index = 0; index < group.Cells.Count; index++)
                {
                    WeaponContainer cell = group.Cells[index];
                    if (cell == null) continue;
                    text.AppendLine($"   cell {index + 1}: index={cell._containerIndex}"
                                    + $" hatches={cell._hatches?.Count ?? 0}"
                                    + $" anim={(cell._openAnimation != null ? "open" : "-")}"
                                    + $"/{(cell._closeAnimation != null ? "close" : "-")}"
                                    + $" open={cell._areHatchesOpen}"
                                    + $" ammo={cell._initialAmmoCount}"
                                    + $" pendingClose={cell._triggerCloseAnimation}");
                }
                text.AppendLine();
            }

            foreach (string line in text.ToString().Split('\n'))
            {
                if (line.Trim().Length > 0) ModLog.Info(line.TrimEnd());
            }

            string path = ModFolders.WriteText("hatch-report.txt", text.ToString());
            ModLog.Info("Hatch report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Hatch report failed", error);
        }
    }

    private static string Safe(Func<string> read)
    {
        try { return read() ?? "-"; }
        catch (Exception) { return "-"; }
    }
}
