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

    private static ConfigEntry<bool>? featuresEnabled;
    private static ConfigEntry<float>? blinkSeconds;
    private static ConfigEntry<float>? salvoSpacingSeconds;
    private static ConfigEntry<float>? jamRangeNm;
    private static ConfigEntry<float>? jamAirRangeNm;
    private static ConfigEntry<bool>? showEcmCone;
    private static ConfigEntry<bool>? missileShield;

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

    /// <summary>
    /// Whether the missile shield starts switched on.
    ///
    /// Off by default, and the only setting in the mod that asks the game to be something other than
    /// a simulation. It is a cheat, in plain words: guided weapons aimed at your units have their
    /// lock broken every tick and are taken down when they close. Everything else here works within
    /// the rules; this does not, so it is the player's decision and it is recorded.
    /// </summary>
    internal static bool MissileShield => missileShield?.Value ?? false;

    /// <summary>Seconds between ships being released in a deconflicted salvo.</summary>
    internal static float SalvoSpacingSeconds => salvoSpacingSeconds?.Value ?? 8f;

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

            missileShield = file.Bind("Features", "MissileShield", false,
                "The missile shield: off by default. When on, every guided weapon aimed at one of "
                + "your units has its lock broken every tick, and is taken down once the game flags "
                + "it as dangerous or it closes inside 2 nm. Guns and rockets are not covered. THIS "
                + "IS A CHEAT - it is the one feature that does not play by the simulation's rules. "
                + "The menu entry toggles the same thing.");

            salvoSpacingSeconds = file.Bind("Features", "SalvoSpacingSeconds", 8f,
                "Seconds between ships being released to fire in a deconflicted salvo (1-60).");

        }
        catch (Exception error)
        {
            ModLog.Error("Could not load the config; defaults are used", error);
        }
    }
}
