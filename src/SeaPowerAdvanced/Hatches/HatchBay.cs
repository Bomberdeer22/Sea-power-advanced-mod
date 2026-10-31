using System;
using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Hatches;

/// <summary>One weapon system that can be opened: a VLS, a box launcher, a torpedo tube nest.</summary>
internal sealed class HatchGroup
{
    internal HatchGroup(object system, int index, string name, string kind, int duplicateSuffix, List<object> cells)
    {
        System = system;
        Index = index;
        Name = name;
        Kind = kind;
        DuplicateSuffix = duplicateSuffix;
        Cells = cells;
    }

    /// <summary>The game's weapon system object (a WeaponSystem, WeaponSystemLauncher, ...).</summary>
    internal object System { get; }

    /// <summary>Position in <c>_obp._weaponSystems</c>. Stable for the life of the ship, used as a state key.</summary>
    internal int Index { get; }

    internal string Name { get; }
    internal string Kind { get; }
    internal int DuplicateSuffix { get; }

    /// <summary>The game's containers (WeaponContainer): one per silo cell, tube or barrel.</summary>
    internal List<object> Cells { get; }

    internal bool HasCells => Cells.Count > 0;

    internal string Label
    {
        get
        {
            string suffix = DuplicateSuffix > 1 ? " #" + DuplicateSuffix : string.Empty;
            return HasCells ? Name + suffix + "  (" + Cells.Count + ")" : Name + suffix;
        }
    }
}

/// <summary>Reads the openable systems of a unit. Read-only: all reflection lives here.</summary>
internal static class HatchBay
{
    internal static List<HatchGroup> Read(ObjectBase unit)
    {
        var groups = new List<HatchGroup>();
        if (unit == null) return groups;

        object? blueprint = R.Get(unit, "_obp");
        List<object> systems = R.Items(R.Get(blueprint, "_weaponSystems"));
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < systems.Count; index++)
        {
            object system = systems[index];
            try
            {
                Type type = system.GetType();
                bool launcher = type.Name.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0;
                List<object> cells = R.Items(R.Get(system, "_containers"));

                // Missile/torpedo launchers and anything with cells are always interesting.
                // Guns only when the player asks for them: a gun barrel has no silo hatch.
                if (!launcher && cells.Count == 0 && !ModSettings.IncludeGuns) continue;

                string name = ReadSystemName(system) ?? type.Name;
                string kind = launcher ? "launcher"
                    : type.Name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0 ? "gun"
                    : "other";

                nameCounts.TryGetValue(name, out int seen);
                nameCounts[name] = ++seen;

                groups.Add(new HatchGroup(system, index, name, kind, seen, cells));
            }
            catch (Exception error)
            {
                ModLog.Error("Could not read weapon system " + index, error);
            }
        }

        return groups;
    }

    /// <summary>The display name the game uses for the mount, e.g. "Mk 41 VLS" or "Mk 141 Harpoon".</summary>
    private static string? ReadSystemName(object system)
    {
        object? vwp = R.Get(system, "_vwp");
        foreach (string member in new[] { "_systemName", "systemName", "_name", "Name" })
        {
            if (R.Get(vwp, member) is string fromMount && fromMount.Trim().Length > 0) return fromMount.Trim();
            if (R.Get(system, member) is string fromSystem && fromSystem.Trim().Length > 0) return fromSystem.Trim();
        }
        return null;
    }

    /// <summary>Best label for a single cell: the game's own name when it has one, otherwise "Cell N".</summary>
    internal static string CellLabel(HatchGroup group, int cellIndex)
    {
        object cell = group.Cells[cellIndex];
        foreach (string member in new[] { "_name", "_cellName", "name", "_id" })
        {
            if (R.Get(cell, member) is string text && text.Trim().Length > 0) return text.Trim();
        }

        if (group.Cells.Count == 1) return "Open / close";
        return "Cell " + (cellIndex + 1);
    }
}
