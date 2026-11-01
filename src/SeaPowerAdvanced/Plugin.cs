using System;
using AnchorChain;
using HarmonyLib;
using SeaPowerAdvanced.Menu;

namespace SeaPowerAdvanced;

/// <summary>
/// Anchor Chain entry point. Anchor Chain creates this type and calls
/// <see cref="TriggerEntryPoint"/> once, shortly after the game starts.
/// </summary>
[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
    private static bool initialized;

    public void TriggerEntryPoint()
    {
        if (initialized) return;
        initialized = true;

        var harmony = new Harmony(ModInfo.Id);
        try
        {
            ModLog.Info("Loading " + ModInfo.Name + " " + ModInfo.Version);
            ModLog.Info("Package folder: " + ModFolders.Package);
            ModSettings.Bind();
            ModLog.Info("Settings: enabled=" + ModSettings.Enabled
                        + " includeGuns=" + ModSettings.IncludeGuns
                        + " allowEnemyUnits=" + ModSettings.AllowEnemyUnits);

            if (!ModActivation.IsEnabled(ModFolders.Package))
            {
                ModLog.Info("Disabled in the mod manager; no patches installed.");
                return;
            }

            HatchMenu.Install(harmony);

            ModLog.Info("Ready. Right-click a ship > Orders > Weapon hatches.");
        }
        catch (Exception error)
        {
            try { harmony.UnpatchSelf(); }
            catch (Exception rollback) { ModLog.Error("Patch rollback failed", rollback); }
            ModLog.Error("Initialization failed; hatch control is unavailable", error);
        }
    }
}
