using AnchorChain;

namespace SeaPowerAdvanced;

/// <summary>
/// AnchorChain entry point, used when the DLL is installed in
/// <c>Sea Power_Data\StreamingAssets\SeaPowerAdvanced\</c> for Workshop-style distribution.
///
/// AnchorChain creates this type and calls <see cref="TriggerEntryPoint"/> once at startup, but only
/// if its own loader recognises the mod. If the mod seems to do nothing on this route, check that
/// the game log contains <c>Loaded plugin Sea Power Advanced</c>. The BepInEx route
/// (<see cref="BepInExEntry"/>) does not depend on that step, which is why it is the documented
/// default.
/// </summary>
[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
    public void TriggerEntryPoint() => Boot.Run("AnchorChain mod", honourModManager: true);
}
