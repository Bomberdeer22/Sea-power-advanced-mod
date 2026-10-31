using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace SeaPowerAdvanced;

/// <summary>
/// Settings, stored in <c>BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg</c>.
/// Every read goes through a nullable field with a sensible default, so a broken or missing
/// config file can never stop the mod from loading.
/// </summary>
internal static class ModSettings
{
    private static ConfigEntry<bool>? enabled;
    private static ConfigEntry<bool>? includeGuns;
    private static ConfigEntry<bool>? allowEnemyUnits;
    private static ConfigEntry<bool>? showDiagnostics;
    private static ConfigEntry<bool>? dumpApiOnLoad;
    private static ConfigEntry<string>? animatorBoolNames;
    private static ConfigEntry<bool>? verboseLog;

    internal static bool Enabled => enabled?.Value ?? true;
    internal static bool IncludeGuns => includeGuns?.Value ?? false;
    internal static bool AllowEnemyUnits => allowEnemyUnits?.Value ?? false;
    internal static bool ShowDiagnostics => showDiagnostics?.Value ?? true;
    internal static bool DumpApiOnLoad => dumpApiOnLoad?.Value ?? true;
    internal static bool VerboseLog => verboseLog?.Value ?? true;

    internal static string[] AnimatorBoolNames =>
        (animatorBoolNames?.Value ?? "Open,IsOpen,Hatch,Hatch_Open,Door,Cover,Silo")
            .Split(',')
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();

    internal static void Bind()
    {
        try
        {
            var file = new ConfigFile(Path.Combine(Paths.ConfigPath, ModInfo.Id + ".cfg"), true);

            enabled = file.Bind("General", "Enabled", true,
                "Master switch for the mod.");
            includeGuns = file.Bind("General", "IncludeGuns", false,
                "Also list guns and CIWS in the hatch menu. Off by default: guns have no silo hatch.");
            allowEnemyUnits = file.Bind("General", "AllowEnemyUnits", false,
                "Show the hatch menu for enemy and neutral units as well as your own.");

            showDiagnostics = file.Bind("Menu", "ShowDiagnosticsItem", true,
                "Add a 'Dump hatch data' entry to the hatch menu.");
            verboseLog = file.Bind("Diagnostics", "VerboseLog", true,
                "Log which hatch-opening method was used for each action.");
            dumpApiOnLoad = file.Bind("Diagnostics", "DumpApiOnLoad", true,
                "Write hatch-api-dump.txt next to the mod DLL at startup. Used to discover the "
                + "game's hatch members; harmless and small.");

            animatorBoolNames = file.Bind("Animation", "AnimatorBoolNames",
                "Open,IsOpen,Hatch,Hatch_Open,Door,Cover,Silo",
                "Animator bool parameter names tried when the animator's parameter list cannot be "
                + "read. Comma separated, first match wins.");
        }
        catch (Exception error)
        {
            ModLog.Error("Could not load the config; defaults are used", error);
        }
    }
}
