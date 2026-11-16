using System;
using System.Collections.Generic;
using HarmonyLib;
using SeaPower;
using SeaPowerAdvanced.Interop;
using SeapowerUI;
using SeapowerUI.ViewModels;
using UniRx;

namespace SeaPowerAdvanced.Menu;

/// <summary>
/// Injects the advanced features into the game's own right-click menus.
///
/// This is a separate Harmony patch from <see cref="HatchMenu"/>, on purpose. It used to be called
/// from inside the hatch menu's own logic, which meant every reason the hatch menu skipped a unit -
/// no launchers, a mod-manager state, a destroyed hull - also silently removed all twelve features.
/// Two features that have nothing to do with each other should not share a failure mode.
///
/// The patch is a postfix on both menu builders the game uses, and does nothing to the collection if
/// it has already been augmented.
/// </summary>
internal static class FleetMenuPatch
{
    internal static void Install(Harmony harmony)
    {
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
                ModLog.Warn("Feature menu patch target not found: " + type.Name + "." + method);
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(FleetMenuPatch), postfix));
            ModLog.Info("Patched " + type.Name + "." + method + " for the feature menus.");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not patch " + type.Name + "." + method + " for the feature menus", error);
        }
    }

    private static void MapPostfix(MapUnitViewModel __instance, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            ObjectBase? unit = __instance?.Unit?.BaseObject;
            Inject(unit, __result, "map menu");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not add the feature menus to the map menu", error);
        }
    }

    private static void UnitPostfix(ObjectBaseViewModel __instance, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            Inject(__instance?._objectBase, __result, "unit menu");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not add the feature menus to the unit menu", error);
        }
    }

    private static void Inject(ObjectBase? unit, TrulyObservableCollection<ContextMenuItem>? result, string source)
    {
        if (result == null || unit == null) return;
        if (!ModSettings.Enabled || !ModSettings.FeaturesEnabled) return;

        // The guard is the collection object itself: once this exact menu has been augmented, stop.
        // Deciding that from the labels was the bug that hid every feature - the game has its own
        // entry called "Local control", which looked like ours.
        if (FleetMenu.Present(result)) return;

        if (unit.IsDestroyed) return;

        // A contact is not one of ours. It gets the jammer and nothing else - the game's own
        // identification entries are already in that menu and are the right ones for it.
        if (!UnitFit.IsPlayer(unit))
        {
            if (FleetMenu.InstallContact(unit, MenuRouting.Roots(result), result) > 0)
                MenuRouting.MarkHandled(result);

            if (ModSettings.VerboseLog && !contactLogged)
            {
                contactLogged = true;
                ModLog.Info("Contact menu: the mod adds only an ECM lock entry. "
                            + UnitFit.Describe(unit));
            }
            return;
        }

        List<ContextMenuItem> roots = MenuRouting.Roots(result);
        int sections = FleetMenu.Install(unit, roots, result);
        MenuRouting.MarkHandled(result);

        if (!logged)
        {
            logged = true;
            ModLog.Info("Feature menus added to the " + source + ": " + sections + " section(s) - "
                        + MenuRouting.Describe());
            MenuReport.Write();
        }

        if (ModSettings.VerboseLog)
        {
            ModLog.Info("Feature menus for " + SafeName(unit) + ": "
                        + MenuRouting.Placements.Count + " entr(ies)");
        }
    }

    private static bool logged;

    private static bool contactLogged;

    private static string SafeName(ObjectBase unit)
    {
        try { return unit.DisplayNameShort; }
        catch (Exception) { return "<unit>"; }
    }
}
