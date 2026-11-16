using System;
using HarmonyLib;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;
using SeaPowerAdvanced.Menu;
using SeaPowerAdvanced.UI;

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
/// <item><b>AnchorChain mod</b> — <c>Plugin</c>, picked up from
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

            // Find out what this build of the game actually exposes, before anything tries to use
            // it. Reflection, so a renamed or non-public member is a logged miss, not a crash.
            Capabilities.Discover();
            CapabilityReport.Write();

            // The ECM interface is discovered before anything uses it, and written down: if this
            // build names its jammers differently, ecm-report.txt says what they are called.
            EcmControl.Discover();
            EcmControl.WriteReport();

            // The multiplayer mod, if it is there: which side of the session this machine is on
            // decides what the automatic features are allowed to promise. Written to
            // multiplayer.txt before any menu can be opened.
            ModLog.Info("Multiplayer: " + Multiplayer.Describe());
            Multiplayer.WriteReport();
            ModLog.Info("Game interface: " + CapabilityReport.Summary());

            harmony = new Harmony(ModInfo.Id);
            HatchMenu.Install(harmony);

            // Its own patch, so a hatch-menu skip cannot take the features with it.
            FleetMenuPatch.Install(harmony);
            TrafficPanel.Install();

            // The map cones: a beam from every jammer to what it is holding.
            EcmConeOverlay.Install();

            // Timers only: blink, auto-ARM, salvo release, readiness clocks. No interface of its own;
            // every feature is reached through the game's right-click menus.
            FleetTicker.Install();

            ModLog.Info("Ready. Right-click a ship > Orders > Weapon hatches.");
            ModLog.Info("Mission editor: press " + ModSettings.TrafficToggleKey
                        + " for the Civilian traffic panel.");
            ModLog.Info("Advanced features: " + (ModSettings.FeaturesEnabled ? "on" : "OFF")
                        + " (Features/Enabled). They are added to the right-click menu the first time "
                        + "a menu is opened; menu-report.txt records where each one went.");
        }
        catch (Exception error)
        {
            ModLog.Error("Startup failed; hatch control and civilian traffic are unavailable", error);
            try { harmony?.UnpatchSelf(); }
            catch (Exception rollback) { ModLog.Error("Patch rollback failed", rollback); }
        }
    }
}
