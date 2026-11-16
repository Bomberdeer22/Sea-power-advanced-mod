using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace SeaPowerAdvanced;

/// <summary>
/// Settings, stored in <c>BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg</c>.
/// Every read falls back to a default, so a missing or broken config can never stop the mod.
/// </summary>
internal static class ModSettings
{
    private static ConfigEntry<bool>? enabled;
    private static ConfigEntry<bool>? includeGuns;
    private static ConfigEntry<bool>? allowEnemyUnits;
    private static ConfigEntry<bool>? showDiagnostics;
    private static ConfigEntry<bool>? showWhenNoMounts;
    private static ConfigEntry<bool>? verboseLog;

    private static ConfigEntry<bool>? trafficShowPanel;
    private static ConfigEntry<string>? trafficToggleKey;
    private static ConfigEntry<int>? trafficMinShips;
    private static ConfigEntry<int>? trafficMaxShips;
    private static ConfigEntry<int>? trafficMinPlanes;
    private static ConfigEntry<int>? trafficMaxPlanes;
    private static ConfigEntry<int>? trafficMaxLanes;
    private static ConfigEntry<int>? trafficClassesPerRoute;

    private static ConfigEntry<bool>? fleetShowPanel;
    private static ConfigEntry<string>? fleetToggleKey;
    private static ConfigEntry<float>? fleetBlinkSeconds;

    internal static bool Enabled => enabled?.Value ?? true;
    internal static bool IncludeGuns => includeGuns?.Value ?? false;
    internal static bool AllowEnemyUnits => allowEnemyUnits?.Value ?? false;
    internal static bool ShowDiagnostics => showDiagnostics?.Value ?? true;
    internal static bool ShowWhenNoMounts => showWhenNoMounts?.Value ?? true;
    internal static bool VerboseLog => verboseLog?.Value ?? true;

    internal static bool FleetShowPanel => fleetShowPanel?.Value ?? true;
    internal static string FleetToggleKey => fleetToggleKey?.Value ?? "F7";

    /// <summary>Seconds between radar blink flips. Short enough to see in a test mission.</summary>
    internal static float FleetBlinkSeconds => fleetBlinkSeconds?.Value ?? 6f;

    internal static bool TrafficShowPanel => trafficShowPanel?.Value ?? true;
    internal static string TrafficToggleKey => trafficToggleKey?.Value ?? "F8";

    /// <summary>Ship/plane counts persist so the panel opens with the numbers last used.</summary>
    internal static int TrafficMinShips
    {
        get => trafficMinShips?.Value ?? 2;
        set { if (trafficMinShips != null) trafficMinShips.Value = Math.Clamp(value, 0, 200); }
    }

    internal static int TrafficMaxShips
    {
        get => trafficMaxShips?.Value ?? 6;
        set { if (trafficMaxShips != null) trafficMaxShips.Value = Math.Clamp(value, 0, 200); }
    }

    internal static int TrafficMinPlanes
    {
        get => trafficMinPlanes?.Value ?? 0;
        set { if (trafficMinPlanes != null) trafficMinPlanes.Value = Math.Clamp(value, 0, 200); }
    }

    internal static int TrafficMaxPlanes
    {
        get => trafficMaxPlanes?.Value ?? 2;
        set { if (trafficMaxPlanes != null) trafficMaxPlanes.Value = Math.Clamp(value, 0, 200); }
    }

    internal static int TrafficMaxLanes => Math.Clamp(trafficMaxLanes?.Value ?? 4, 1, 6);
    internal static int TrafficClassesPerRoute => Math.Clamp(trafficClassesPerRoute?.Value ?? 6, 1, 40);

    internal static void Bind()
    {
        try
        {
            var file = new ConfigFile(Path.Combine(Paths.ConfigPath, ModInfo.Id + ".cfg"), true);

            enabled = file.Bind("General", "Enabled", true,
                "Master switch for the mod.");

            includeGuns = file.Bind("General", "IncludeGuns", false,
                "Also list guns and CIWS in the hatch menu. Off by default: they have no silo hatch, "
                + "so only their mount cover (if any) can open.");

            allowEnemyUnits = file.Bind("General", "AllowEnemyUnits", false,
                "Show the hatch menu on enemy and neutral ships as well as your own.");

            showDiagnostics = file.Bind("Menu", "ShowDiagnosticsItem", true,
                "Add a 'Log hatch report' entry to the hatch menu.");

            showWhenNoMounts = file.Bind("Menu", "ShowWhenNoMounts", true,
                "Show the Weapon hatches entry even on units with no launchers, so it is obvious the "
                + "mod is running. Set to false for a cleaner menu once everything works.");

            verboseLog = file.Bind("Diagnostics", "VerboseLog", true,
                "Log every hatch action with the game method used and its result.");

            trafficShowPanel = file.Bind("CivilianTraffic", "ShowPanel", true,
                "Show the Civilian traffic panel while the mission editor is open.");

            trafficToggleKey = file.Bind("CivilianTraffic", "ToggleKey", "F8",
                "Keyboard key that shows and hides the Civilian traffic panel (a UnityEngine.KeyCode name).");

            trafficMinShips = file.Bind("CivilianTraffic", "MinShips", 2,
                "Default minimum number of civilian vessels placed in a zone.");

            trafficMaxShips = file.Bind("CivilianTraffic", "MaxShips", 6,
                "Default maximum number of civilian vessels placed in a zone.");

            trafficMinPlanes = file.Bind("CivilianTraffic", "MinPlanes", 0,
                "Default minimum number of civilian aircraft placed in a zone.");

            trafficMaxPlanes = file.Bind("CivilianTraffic", "MaxPlanes", 2,
                "Default maximum number of civilian aircraft placed in a zone.");

            trafficMaxLanes = file.Bind("CivilianTraffic", "MaxLanes", 4,
                "How many separate traffic lanes one generate can create per type (1-6). Lanes are what "
                + "make traffic come from more than one place.");

            fleetShowPanel = file.Bind("Fleet", "ShowPanel", true,
                "Show the fleet sensor panel. The hotkey below only works while this is on.");

            fleetToggleKey = file.Bind("Fleet", "ToggleKey", "F7",
                "Keyboard key that shows and hides the fleet sensor panel (a UnityEngine.KeyCode name).");

            fleetBlinkSeconds = file.Bind("Fleet", "BlinkSeconds", 6f,
                "Seconds between radar blink flips (2-120). Blink cycles the surface-search radar so "
                + "an anti-radiation seeker has less to home on.");

            trafficClassesPerRoute = file.Bind("CivilianTraffic", "ClassesPerRoute", 6,
                "How many different civilian unit classes one lane may pick from (1-40). Higher means "
                + "more variety inside a single lane.");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not load the config; defaults are used", error);
        }
    }
}
