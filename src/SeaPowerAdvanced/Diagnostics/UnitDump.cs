using System;
using System.Collections.Generic;
using System.Text;
using SeaPower;
using SeaPowerAdvanced.Hatches;

namespace SeaPowerAdvanced.Diagnostics;

/// <summary>
/// Writes <c>hatch-unit-dump.txt</c> for one ship: its code animations, every openable weapon
/// system, every cell, and the Unity animators found on each cell's model with their parameters.
///
/// This is the file to send along with a bug report: it shows precisely which members and
/// animations a given ship class exposes, so the hatch strategy can be pointed at the right one.
/// </summary>
internal static class UnitDump
{
    internal static void Write(ObjectBase unit)
    {
        try
        {
            var text = new StringBuilder();
            object? blueprint = R.Get(unit, "_obp");

            text.AppendLine("Sea Power Advanced " + ModInfo.Version + " - hatch dump for one ship");
            text.AppendLine("Written:  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Unit:     " + UnitName(unit));
            text.AppendLine("Type:     " + unit.GetType().FullName);
            text.AppendLine("Blueprint:" + R.TypeOf(blueprint)?.FullName);
            text.AppendLine();

            List<KeyValuePair<string, object?>> animations =
                R.DictionaryEntries(R.Get(blueprint, "_codeAnimations"));
            text.AppendLine("== Code animations (" + animations.Count + ") ==");
            foreach (KeyValuePair<string, object?> animation in animations)
            {
                text.AppendLine("  " + animation.Key + " -> " + R.Format(animation.Value));
                if (animation.Value != null) text.AppendLine("      " + R.Describe(animation.Value, 8));
            }
            text.AppendLine();

            List<HatchGroup> groups = HatchBay.Read(unit);
            text.AppendLine("== Openable weapon systems (" + groups.Count + ") ==");

            foreach (HatchGroup group in groups)
            {
                text.AppendLine();
                text.AppendLine("## " + group.Name + "   [" + group.Kind + "]   "
                                + group.Cells.Count + " cells   type " + group.System.GetType().FullName);
                text.AppendLine("   system:   " + R.Describe(group.System, 10));
                text.AppendLine("   mount:    " + R.Describe(R.Get(group.System, "_vwp"), 10));
                text.AppendLine("   animators on mount: " + HatchControl.DescribeAnimators(group.System));

                for (int index = 0; index < group.Cells.Count; index++)
                {
                    object cell = group.Cells[index];
                    text.AppendLine("   cell " + (index + 1) + " (" + HatchBay.CellLabel(group, index) + "): " + R.Describe(cell, 16));
                    text.AppendLine("      animators: " + HatchControl.DescribeAnimators(cell));
                }
            }

            string path = ModFolders.WriteText("hatch-unit-dump.txt", text.ToString());
            ModLog.Info("Hatch dump for " + UnitName(unit) + " written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Hatch dump failed", error);
        }
    }

    private static string UnitName(ObjectBase unit)
    {
        if (R.Get(unit, "Name") is string name && name.Trim().Length > 0) return name.Trim();
        if (R.Get(unit, "_name") is string internalName && internalName.Trim().Length > 0) return internalName.Trim();
        return "(unnamed unit)";
    }
}
