using System;
using HarmonyLib;
using SeaPowerAdvanced.Menu;

namespace SeaPowerAdvanced;

/// <summary>
/// The real startup, shared by both entry points.
///
/// The mod can be loaded two ways:
///
/// <list type="bullet">
/// <item><b>BepInEx plugin</b> — <see cref="BepInExEntry"/>, picked up from
/// <c>BepInEx\plugins\SeaPowerAdvanced\</c>. This is the preferred route: BepInEx lists the plugin
/// in its own startup log, so "did it load?" is answered by the first few lines of the log.</item>
/// <item><b>AnchorChain mod</b> — <see cref="Plugin"/>, picked up from
/// <c>SeaPower_Data\StreamingAssets\</c> for Workshop distribution.</item>
/// </list>
///
/// Only one of them is ever installed; <see cref="Run"/> is idempotent anyway, so a double
/// installation cannot patch anything twice.
/// </summary>
internal static class Boot
{
    private static bool started;
    private static readonly object Gate = new();

    internal static void Run(string source, bool honourModManager)
    {
        lock (Gate)
        {
            if (started)
            {
                ModLog.Info("Already started; ignoring a second entry point (" + source + ").");
                return;
            }
            started = true;
        }

        // First thing: put something on disk. If this file exists, the code ran.
        ModLog.Info("================ Sea Power Advanced " + ModInfo.Version + " ================");
        ModLog.Info("Entry point:  " + source);
        ModLog.Info("Assembly:     " + (typeof(Boot).Assembly.Location ?? "<in memory>"));
        ModLog.Info("Log file:     " + (ModLog.FilePath ?? "<writer unavailable>"));
        ModLog.Info("Time:         " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        Harmony? harmony = null;
        try
        {
            ModSettings.Bind();
            ModLog.Info("Settings: enabled=" + ModSettings.Enabled
                        + " includeGuns=" + ModSettings.IncludeGuns
                        + " allowEnemyUnits=" + ModSettings.AllowEnemyUnits
                        + " showWhenNoMounts=" + ModSettings.ShowWhenNoMounts);

            if (!ModSettings.Enabled)
            {
                ModLog.Warn("General/Enabled is false; nothing will be patched.");
                return;
            }

            if (honourModManager && !ModActivation.IsEnabled(ModFolders.Package))
            {
                ModLog.Info("Disabled in the mod manager; no patches installed.");
                return;
            }

            harmony = new Harmony(ModInfo.Id);
            HatchMenu.Install(harmony);

            ModLog.Info("Ready. Right-click a ship > Orders > Weapon hatches.");
        }
        catch (Exception error)
        {
            ModLog.Error("Startup failed; hatch control is unavailable", error);
            try { harmony?.UnpatchSelf(); }
            catch (Exception rollback) { ModLog.Error("Patch rollback failed", rollback); }
        }
    }
}
