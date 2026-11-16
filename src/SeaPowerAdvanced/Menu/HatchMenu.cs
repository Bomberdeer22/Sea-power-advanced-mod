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
        // Two different builders produce a right-click menu in this game:
        //   MapUnitViewModel.GetContextMenuItems    - the map right-click on a unit (this is the
        //                                             one a player actually uses, and it builds its
        //                                             own list rather than delegating)
        //   ObjectBaseViewModel.GetContextMenuItems - the unit panel / bottom bar path
        // Patching only the second one leaves the map menu untouched, which is why the entry did
        // not appear. Both are patched, and TryAdd is idempotent so a shared collection is safe.
        Patch(harmony, typeof(MapUnitViewModel), nameof(MapUnitViewModel.GetContextMenuItems),
            nameof(MapPostfix));
        Patch(harmony, typeof(ObjectBaseViewModel), nameof(ObjectBaseViewModel.GetContextMenuItems),
            nameof(UnitPostfix));
    }

    private static void Patch(Harmony harmony, Type type, string method, string postfix)
    {
        try
        {
            var target = AccessTools.Method(type, method);
            if (target == null)
            {
                ModLog.Warn($"Patch target not found: {type.Name}.{method} (game version change?)");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(HatchMenu), postfix));
            ModLog.Info($"Patched {type.Name}.{method}.");
        }
        catch (Exception error)
        {
            ModLog.Error($"Could not patch {type.Name}.{method}", error);
        }
    }

    /// <summary>Map right-click on a unit: the path a player uses to give orders.</summary>
    private static void MapPostfix(MapUnitViewModel __instance, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            ObjectBase? unit = __instance?.Unit?.BaseObject;
            TryAdd(unit, __result, "map menu");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not add the hatch menu to the map menu", error);
        }
    }

    /// <summary>Unit panel / bottom-bar menu.</summary>
    private static void UnitPostfix(ObjectBaseViewModel __instance, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            TryAdd(__instance?._objectBase, __result, "unit menu");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not add the hatch menu to the unit menu", error);
        }
    }

    private static readonly HashSet<string> SourcesSeen = new(StringComparer.Ordinal);

    /// <summary>Logged once, so the first menu built shows where everything landed.</summary>
    private static bool FleetPlacementLogged;

    private static void TryAdd(ObjectBase? unit, TrulyObservableCollection<ContextMenuItem>? result, string source)
    {
        if (result == null) return;

        if (!SourcesSeen.Contains(source))
        {
            SourcesSeen.Add(source);
            ModLog.Info($"Context menu patch is live ({source}).");
        }

        if (unit == null) return;

        if (!ModSettings.Enabled)
        {
            MenuLog.Once(unit, "skipped: General/Enabled is false");
            return;
        }

        if (unit.IsDestroyed)
        {
            MenuLog.Once(unit, "skipped: unit is destroyed");
            return;
        }

        if (!IsControllable(unit, out string reason))
        {
            MenuLog.Once(unit, "skipped: " + reason);
            return;
        }

        // Both builders can be involved in one menu: never add the entry twice.
        foreach (ContextMenuItem existing in result)
        {
            if (string.Equals(existing?.Label as string, MenuLabel, StringComparison.Ordinal)) return;
        }

        AddFleetMenus(unit, result);

        List<HatchGroup> groups = HatchBay.Read(unit);
        MenuLog.Once(unit, groups, HatchBay.Summary(unit, groups));

        if (groups.Count == 0 && !ModSettings.ShowWhenNoMounts) return;

        ContextMenuItem menu = Build(unit, groups);

        ContextMenuItem? orders = FindOrders(result);
        if (orders?.Items != null)
        {
            orders.Items.Insert(0, menu);
            return;
        }

        // No Orders submenu in this menu: show the feature at the top level rather than vanishing.
        ModLog.Warn($"No Orders submenu in the {source} for {HatchControl.UnitName(unit)}; "
                    + "adding the hatch menu at the top level instead.");
        result.Insert(0, menu);
    }

    /// <summary>
    /// Adds the twelve advanced features as game-style submenus, once per menu.
    ///
    /// Kept here rather than in its own Harmony patch so both features share one injection point:
    /// two patches on the same method would each need their own de-duplication, and one of them
    /// would eventually add its entries twice.
    /// </summary>
    private static void AddFleetMenus(ObjectBase unit, TrulyObservableCollection<ContextMenuItem> result)
    {
        try
        {
            if (!ModSettings.FeaturesEnabled) return;

            List<ContextMenuItem> roots = MenuRouting.Roots(result);
            if (FleetMenu.Present(roots)) return;

            int sections = FleetMenu.Install(unit, roots);

            if (sections > 0 && !FleetPlacementLogged)
            {
                FleetPlacementLogged = true;
                ModLog.Info("Advanced features placed in " + sections
                            + " menu section(s): " + MenuRouting.Describe());
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Could not add the advanced feature entries", error);
        }
    }

    /// <summary>
    /// Player units only, unless the setting says otherwise. Written as a series of explicit cases
    /// so that a null taskforce on either side cannot quietly hide the whole feature.
    /// </summary>
    private static bool IsControllable(ObjectBase unit, out string reason)
    {
        if (ModSettings.AllowEnemyUnits)
        {
            reason = "allowed by AllowEnemyUnits";
            return true;
        }

        Taskforce? player = Globals._playerTaskforce;
        if (player == null)
        {
            // Nothing to compare against; showing the menu is the more useful failure.
            reason = "player taskforce unavailable, allowing";
            return true;
        }

        Taskforce? own = unit._taskforce;
        if (own == null)
        {
            reason = "unit has no taskforce";
            return false;
        }

        if (ReferenceEquals(own, player))
        {
            reason = "player unit";
            return true;
        }

        reason = "not a player unit";
        return false;
    }

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
                tooltip: "Hatch control found no weapon mounts on this unit.  (Sea Power Advanced "
                         + ModInfo.Version + ")");
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
            tooltip: "Open or close silo hatches, launcher covers and torpedo tube shutters without "
                     + "launching.  (Sea Power Advanced " + ModInfo.Version + ")");
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
