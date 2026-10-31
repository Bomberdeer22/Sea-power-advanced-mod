using System;
using System.Collections.Generic;
using System.Reflection;
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
///     Mk 41 VLS (32)
///       Cell 1   [x]
///       ...
///       Open all cells / Close all cells
///     Mk 141 Harpoon (4)
///       ...
///     Open all hatches / Close all hatches
///     Dump hatch data for this ship
/// </code>
///
/// The list is rebuilt every time the menu is built, so it always matches the selected unit.
/// </summary>
internal static class HatchMenu
{
    private const string MenuLabel = "Weapon hatches";

    internal static void Install(Harmony harmony)
    {
        MethodInfo? target = AccessTools.Method(typeof(ObjectBaseViewModel), "GetContextMenuItems");
        if (target == null)
        {
            ModLog.Warn("ObjectBaseViewModel.GetContextMenuItems was not found; the hatch menu is unavailable.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(HatchMenu), nameof(Postfix)));
        ModLog.Info("Hatch menu installed.");
    }

    private static void Postfix(ObjectBase ____objectBase, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            if (!ModSettings.Enabled || ____objectBase == null || __result == null) return;
            if (!IsControllable(____objectBase)) return;

            List<HatchGroup> groups = HatchBay.Read(____objectBase);
            if (groups.Count == 0) return;

            ContextMenuItem? orders = FindOrders(__result);
            if (orders?.Items == null) return;

            orders.Items.Insert(0, Build(____objectBase, groups));
        }
        catch (Exception error)
        {
            ModLog.Error("Could not build the hatch menu", error);
        }
    }

    private static bool IsControllable(ObjectBase unit)
    {
        if (unit == null || unit.IsDestroyed) return false;
        bool playerUnit = ReferenceEquals(unit._taskforce, Globals._playerTaskforce);
        return playerUnit || ModSettings.AllowEnemyUnits;
    }

    private static ContextMenuItem? FindOrders(IEnumerable<ContextMenuItem> roots)
    {
        string label;
        try { label = Singleton<LanguageResourceHandler>.Instance.getText("ContextMenu", "Orders"); }
        catch (Exception) { label = "Orders"; }

        foreach (ContextMenuItem item in roots)
        {
            if (item?.Items != null && string.Equals(item.Label as string, label, StringComparison.Ordinal))
                return item;
        }
        return null;
    }

    private static ContextMenuItem Build(ObjectBase unit, List<HatchGroup> groups)
    {
        var items = new TrulyObservableCollection<ContextMenuItem>();

        foreach (HatchGroup group in groups) items.Add(BuildGroup(unit, group));

        items.Add(new ContextMenuItem("Open all hatches", null,
            new DelegateCommand((object _) => HatchControl.ApplyAll(unit, groups, true))));
        items.Add(new ContextMenuItem("Close all hatches", null,
            new DelegateCommand((object _) => HatchControl.ApplyAll(unit, groups, false))));

        if (ModSettings.ShowDiagnostics)
        {
            items.Add(new ContextMenuItem("Dump hatch data for this ship", null,
                new DelegateCommand((object _) => UnitDump.Write(unit))));
        }

        return new ContextMenuItem(MenuLabel, items);
    }

    private static ContextMenuItem BuildGroup(ObjectBase unit, HatchGroup group)
    {
        var items = new TrulyObservableCollection<ContextMenuItem>();

        if (group.HasCells)
        {
            for (int index = 0; index < group.Cells.Count; index++)
            {
                int cell = index;
                bool open = HatchState.IsOpen(HatchState.CellKey(unit, group, cell));
                items.Add(new ContextMenuItem(HatchBay.CellLabel(group, cell), Check(open),
                    new DelegateCommand((object _) => Toggle(unit, group, cell))));
            }

            items.Add(new ContextMenuItem("Open all cells", null,
                new DelegateCommand((object _) => HatchControl.ApplyAll(unit, new[] { group }, true))));
            items.Add(new ContextMenuItem("Close all cells", null,
                new DelegateCommand((object _) => HatchControl.ApplyAll(unit, new[] { group }, false))));
        }
        else
        {
            // Fixed launchers and guns: one cover over the whole mount.
            items.Add(new ContextMenuItem("Open", null,
                new DelegateCommand((object _) => HatchControl.ApplyAll(unit, new[] { group }, true))));
            items.Add(new ContextMenuItem("Close", null,
                new DelegateCommand((object _) => HatchControl.ApplyAll(unit, new[] { group }, false))));
        }

        return new ContextMenuItem(group.Label, items);
    }

    private static void Toggle(ObjectBase unit, HatchGroup group, int cell)
    {
        string key = HatchState.CellKey(unit, group, cell);
        HatchControl.ApplyCell(unit, group, cell, !HatchState.IsOpen(key));
    }

    private static ReadOnlyReactiveProperty<bool> Check(bool value) =>
        new ReactiveProperty<bool>(value).ToReadOnlyReactiveProperty();
}
