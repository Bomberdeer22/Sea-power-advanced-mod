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
/// the actual hatches — including ones the game opened itself while firing.
///
/// Every <see cref="ContextMenuItem"/> is constructed with named arguments: the type has two
/// constructors whose parameters are all optional, so positional calls are ambiguous.
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
            if (unit == null || unit.IsDestroyed || !IsControllable(unit)) return;

            List<HatchGroup> groups = HatchBay.Read(unit);
            if (groups.Count == 0) return;

            ContextMenuItem? orders = FindOrders(__result);
            if (orders?.Items == null) return;

            orders.Items.Insert(0, Build(unit, groups));
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
        return null;
    }

    private static ContextMenuItem Build(ObjectBase unit, List<HatchGroup> groups)
    {
        var items = new TrulyObservableCollection<ContextMenuItem>();

        foreach (HatchGroup group in groups) items.Add(BuildGroup(unit, group));

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

        if (group.HasHatches)
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
        else
        {
            items.Add(Action("Open cover", "Open this mount's cover without launching.",
                _ => LogSetGroup(unit, group, true)));
            items.Add(Action("Close cover", "Close this mount's cover.",
                _ => LogSetGroup(unit, group, false)));
        }

        return new ContextMenuItem(
            label: group.Label,
            items: items,
            tooltip: group.HatchCount + " hatch(es) on " + group.Cells.Count + " cell(s)");
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

    /// <summary>Snapshot of a cell's real state, for the check mark next to the entry.</summary>
    private static ReadOnlyReactiveProperty<bool> Check(bool open) =>
        new ReactiveProperty<bool>(open).ToReadOnlyReactiveProperty();
}
