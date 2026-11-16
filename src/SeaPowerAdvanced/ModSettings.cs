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

    private static ConfigEntry<bool>? featuresEnabled;
    private static ConfigEntry<float>? blinkSeconds;
    private static ConfigEntry<float>? salvoSpacingSeconds;
    private static ConfigEntry<float>? jamRangeNm;
    private static ConfigEntry<float>? jamAirRangeNm;
    private static ConfigEntry<bool>? showEcmCone;

    internal static bool Enabled => enabled?.Value ?? true;
    internal static bool IncludeGuns => includeGuns?.Value ?? false;
    internal static bool AllowEnemyUnits => allowEnemyUnits?.Value ?? false;
    internal static bool ShowDiagnostics => showDiagnostics?.Value ?? true;
    internal static bool ShowWhenNoMounts => showWhenNoMounts?.Value ?? true;
    internal static bool VerboseLog => verboseLog?.Value ?? true;

    /// <summary>Master switch for the menu features (hatch control is unaffected).</summary>
    internal static bool FeaturesEnabled => featuresEnabled?.Value ?? true;

    /// <summary>Seconds between radar blink flips. Short enough to see in a test mission.</summary>
    internal static float BlinkSeconds => blinkSeconds?.Value ?? 6f;

    /// <summary>Draw the ECM cones on the map.</summary>
    internal static bool ShowEcmCone => showEcmCone?.Value ?? true;

    /// <summary>How far the focused (signature-lock) offensive jammer reaches, in nautical miles.</summary>
    internal static float JamRangeNm => jamRangeNm?.Value ?? 40f;

    /// <summary>
    /// How far the same jammer reaches against something in the air.
    ///
    /// This is not generosity, it is geometry. A jammer has to be inside the other radar's horizon,
    /// and the radio horizon runs to about 1.23 x (root of height in feet) nautical miles for each
    /// end. A masthead at 20 m is 4.5 nm; an aircraft at 15,000 ft is 65 nm on its own, so the pair
    /// see each other at roughly 70 nm - and further still for a high patrol. The 40 nm figure is
    /// for ships and sea-skimming missiles, where both ends are close to the water.
    /// </summary>
    internal static float JamAirRangeNm => jamAirRangeNm?.Value ?? 90f;

    /// <summary>Seconds between ships being released in a deconflicted salvo.</summary>
    internal static float SalvoSpacingSeconds => salvoSpacingSeconds?.Value ?? 8f;

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

            featuresEnabled = file.Bind("Features", "Enabled", true,
                "Add the advanced features to the game's right-click menus: sensors and EMCON, "
                + "countermeasures, readiness, manoeuvres, air operations, strike, deception, logistics.");

            blinkSeconds = file.Bind("Features", "BlinkSeconds", 6f,
                "Seconds between radar blink flips (2-120). Blink cycles the surface-search radar so "
                + "an anti-radiation seeker has less to home on.");

            showEcmCone = file.Bind("Features", "ShowEcmCone", true,
                "Draw a long, narrow cone from each jammer to the missile or ship it is holding. "
                + "Cyan for the offensive set, amber for the defensive one.");

            jamRangeNm = file.Bind("Features", "JamRangeNm", 40f,
                "How far the focused offensive jammer reaches against a ship, in nautical miles "
                + "(5-200). Beyond this the lock drops and the target's crew can bring its sensors "
                + "back up.");

            jamAirRangeNm = file.Bind("Features", "JamAirRangeNm", 90f,
                "How far the same jammer reaches against an aircraft or a helicopter, in nautical "
                + "miles (5-300). Longer than the ship figure because both ends are high above the "
                + "radio horizon - roughly 70 nm for a masthead and an aircraft at 15,000 ft.");

            salvoSpacingSeconds = file.Bind("Features", "SalvoSpacingSeconds", 8f,
                "Seconds between ships being released to fire in a deconflicted salvo (1-60).");

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
