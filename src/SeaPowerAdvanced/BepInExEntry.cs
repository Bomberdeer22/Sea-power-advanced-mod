using BepInEx;

namespace SeaPowerAdvanced;

/// <summary>
/// BepInEx entry point, used when the DLL is installed in
/// <c>BepInEx\plugins\SeaPowerAdvanced\</c>.
///
/// This is the reliable route: BepInEx itself enumerates the plugin ("Loading [Sea Power Advanced
/// ...]" in the startup log) and calls <see cref="Awake"/>, with no mod-manager checkbox and no
/// third-party loader in between.
///
/// A separate class is used rather than deriving the AnchorChain plugin from
/// <see cref="BaseUnityPlugin"/> so that neither loader's base class leaks into the other's path,
/// and so the mod still loads if AnchorChain is not installed at all.
/// </summary>
[BepInPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class BepInExEntry : BaseUnityPlugin
{
    private void Awake()
    {
        // BepInEx only loads plugins that are present, so there is no checkbox to honour here.
        Boot.Run("BepInEx plugin", honourModManager: false);
    }
}
