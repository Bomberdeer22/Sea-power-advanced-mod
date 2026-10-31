using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SeaPower;

namespace SeaPowerAdvanced.Hatches;

/// <summary>
/// What the player has asked for, per unit, per system, per cell.
///
/// The game may also open and close covers on its own while it fires, so this is the mod's own
/// intent (shown as the check mark in the menu), not a mirror of the model. Creating a state entry
/// is what makes the menu remember "this silo is open" between right-clicks.
/// </summary>
internal static class HatchState
{
    private static readonly ConcurrentDictionary<string, bool> Requested = new();

    internal static string CellKey(ObjectBase unit, HatchGroup group, int cellIndex) =>
        UnitKey(unit) + "/" + group.Index + "/" + cellIndex;

    internal static bool IsOpen(string key) => Requested.TryGetValue(key, out bool open) && open;

    internal static void Set(string key, bool open) => Requested[key] = open;

    /// <summary>Stable identity for a unit instance across a mission.</summary>
    internal static string UnitKey(ObjectBase unit)
    {
        object? id = R.Get(unit, "Id");
        if (id != null) return id.ToString() ?? string.Empty;
        return RuntimeHelpers.GetHashCode(unit).ToString();
    }

    internal static void Clear() => Requested.Clear();
}
