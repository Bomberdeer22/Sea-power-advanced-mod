using System;
using System.Collections.Generic;
using HarmonyLib;
using SeaPower;
using SeaPowerAdvanced.Diagnostics;
using SeaPowerAdvanced.Hatches;
using SeapowerUI;
using SeapowerUI.ViewModels;
using UniRx;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// Adds "Weapon hatches" to the right-click Orders menu of a unit:
///
/// <code>
/// Orders
///   Weapon hatches
///     Mk 41 VLS  (32)
///       Cell 1  [x]
///       ...
///       Open all cells / Close all cells
///     Mk 141 Harpoon
///       Hatch
///     Open all hatches / Close all hatches
///     Log hatch report
/// </code>
///
/// The menu is rebuilt from live game state every time it opens, so the check marks always match
/// the actual hatches. Mounts whose ship data has no hatch animation are listed and labelled rather
/// than hidden, and if a ship has nothing at all the submenu still appears when
/// <c>Menu/ShowWhenNoMounts</c> is on — during development that tells "mod is running but this ship
/// has no hatch data" apart from "mod is not running".
///
/// Every <see cref="ContextMenuItem"/> is built with named arguments: the type has two constructors
/// whose parameters are all optional, so positional calls are ambiguous.
/// </summary>
internal static class HatchMenu
{
    internal const string MenuLabel = "Weapon hatches";

    internal static void Install(Harmony harmony)
    {
        try
        {
            harmony.Patch(
                AccessTools.Method(typeof(ObjectBaseViewModel), nameof(ObjectBaseViewModel.GetContextMenuItems)),
                postfix: new HarmonyMethod(typeof(HatchMenu), nameof(Postfix)));
            ModLog.Info("Hatch menu installed.");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not patch the context menu; hatch control is unavailable", error);
        }
    }

    private static void Postfix(ObjectBaseViewModel __instance, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            if (!ModSettings.Enabled || __result == null) return;

            ObjectBase? unit = __instance?._objectBase;
            if (unit == null || unit.IsDestroyed) return;
            if (!IsControllable(unit)) return;

            List<HatchGroup> groups = HatchBay.Read(unit);
            MenuLog.Once(unit, groups, HatchBay.Summary(unit, groups));

            if (groups.Count == 0 && !ModSettings.ShowWhenNoMounts) return;

            ContextMenuItem menu = Build(unit, groups);

            ContextMenuItem? orders = FindOrders(__result);
            if (orders?.Items != null)
            {
                orders.Items.Insert(0, menu);
                return;
            }

            // The localised "Orders" entry could not be identified. Still show the feature rather
            // than vanishing silently, and say so once.
            ModLog.Warn("No Orders submenu found on " + HatchControl.UnitName(unit)
                        + "; adding the hatch menu at the top level instead.");
            __result.Insert(0, menu);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not build the hatch menu", error);
        }
    }

    private static bool IsControllable(ObjectBase unit) =>
        ReferenceEquals(unit._taskforce, Globals._playerTaskforce) || ModSettings.AllowEnemyUnits;

    private static ContextMenuItem? FindOrders(IEnumerable<ContextMenuItem> roots)
    {
        string label;
        try { label = Singleton<LanguageResourceHandler>.Instance.getText("ContextMenu", "Orders"); }
        catch (Exception) { label = "Orders"; }

        foreach (ContextMenuItem item in roots)
        {
            if (item?.Items == null) continue;
            if (string.Equals(item.Label as string, label, StringComparison.Ordinal)) return item;
        }

        // Fall back to a case-insensitive match, then to any submenu whose label mentions orders.
        foreach (ContextMenuItem item in roots)
        {
            if (item?.Items == null) continue;
            if (string.Equals(item.Label as string, label, StringComparison.OrdinalIgnoreCase)) return item;
        }
        foreach (ContextMenuItem item in roots)
        {
            if (item?.Items == null) continue;
            string? text = item.Label as string;
            if (text != null && text.IndexOf("order", StringComparison.OrdinalIgnoreCase) >= 0) return item;
        }
        return null;
    }

    private static ContextMenuItem Build(ObjectBase unit, List<HatchGroup> groups)
    {
        var items = new TrulyObservableCollection<ContextMenuItem>();

        foreach (HatchGroup group in groups) items.Add(BuildGroup(unit, group));

        if (groups.Count == 0)
        {
            items.Add(Action("This ship has no launchers - click for a report",
                "Writes the full weapon-mount list for this unit into LogOutput.log and hatch-report.txt.",
                _ => ShipReport.Write(unit, groups)));
            return new ContextMenuItem(label: MenuLabel, items: items,
                tooltip: "Hatch control: no weapon mounts found on this unit.");
        }

        items.Add(Action("Open all hatches", "Open every hatch on this ship without firing.",
            _ => HatchControl.SetAll(unit, groups, true)));
        items.Add(Action("Close all hatches", "Close every hatch on this ship.",
            _ => HatchControl.SetAll(unit, groups, false)));

        if (ModSettings.ShowDiagnostics)
        {
            items.Add(Action("Log hatch report", "Write this ship's hatch data to hatch-report.txt in the mod folder.",
                _ => ShipReport.Write(unit, groups)));
        }

        return new ContextMenuItem(
            label: MenuLabel,
            items: items,
            tooltip: "Open or close silo hatches, launcher covers and torpedo tube shutters without launching.");
    }

    private static ContextMenuItem BuildGroup(ObjectBase unit, HatchGroup group)
    {
        var items = new TrulyObservableCollection<ContextMenuItem>();

        if (group.HasHatchData)
        {
            for (int index = 0; index < group.Cells.Count; index++)
            {
                int cell = index;
                if (!HatchControl.HasHatches(group.Cells[cell])) continue;

                items.Add(new ContextMenuItem(
                    label: HatchBay.CellLabel(group, cell) + HatchBay.CellDetail(group, cell),
                    items: null,
                    command: new DelegateCommand(_ => ToggleCell(unit, group, cell)),
                    isChecked: Check(HatchControl.IsOpen(group.Cells[cell]))));
            }

            items.Add(Action("Open all cells", "Open every cell of this mount.",
                _ => LogSetGroup(unit, group, true)));
            items.Add(Action("Close all cells", "Close every cell of this mount.",
                _ => LogSetGroup(unit, group, false)));
        }
        else if (group.HasMountCover)
        {
            items.Add(Action("Open cover", "Open this mount's cover without launching.",
                _ => LogSetGroup(unit, group, true)));
            items.Add(Action("Close cover", "Close this mount's cover.",
                _ => LogSetGroup(unit, group, false)));
        }
        else
        {
            items.Add(Action("No hatch data in this ship's files",
                "This mount has no hatch animation or hatch models in the unit's INI data. "
                + "Click to write a report for it.",
                _ => ShipReport.Write(unit, new List<HatchGroup> { group })));
        }

        return new ContextMenuItem(
            label: group.Label + (group.CanDrive ? string.Empty : "  -  no hatch data"),
            items: items,
            tooltip: group.Status + " | " + group.Kind);
    }

    private static void ToggleCell(ObjectBase unit, HatchGroup group, int cell)
    {
        bool open = !HatchControl.IsOpen(group.Cells[cell]);
        HatchControl.SetCell(unit, group, cell, open);
    }

    private static void LogSetGroup(ObjectBase unit, HatchGroup group, bool open)
    {
        bool changed = HatchControl.SetGroup(group, open, out string report);
        string message = $"{(open ? "Open" : "Close")} {group.Name} on {HatchControl.UnitName(unit)}: {report}";
        if (changed) ModLog.Info(message);
        else ModLog.Warn(message);
    }

    private static ContextMenuItem Action(string label, string tooltip, Action<object> action) =>
        new ContextMenuItem(label: label, items: null, command: new DelegateCommand(action), tooltip: tooltip);

    private static ReadOnlyReactiveProperty<bool> Check(bool open) =>
        new ReactiveProperty<bool>(open).ToReadOnlyReactiveProperty();
}
