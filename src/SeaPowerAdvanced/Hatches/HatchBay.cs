using System;
using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Hatches;

/// <summary>
/// One openable weapon system on a ship: a VLS, a box launcher, a torpedo tube nest.
///
/// The game models this as a <see cref="WeaponSystem"/> whose <see cref="WeaponSystem._containers"/>
/// are the individual cells. Each <see cref="WeaponContainer"/> owns the hatch GameObjects for its
/// cell (<see cref="WeaponContainer._hatches"/>) and can open and close them itself.
/// </summary>
internal sealed class HatchGroup
{
    internal HatchGroup(WeaponSystem system, int index, string name, string kind, int duplicateSuffix)
    {
        Weapon = system;
        Index = index;
        Name = name;
        Kind = kind;
        DuplicateSuffix = duplicateSuffix;
        Cells = system._containers ?? new List<WeaponContainer>();
    }

    internal WeaponSystem Weapon { get; }

    /// <summary>Position in <c>_obp._weaponSystems</c>; stable for the life of the ship.</summary>
    internal int Index { get; }

    internal string Name { get; }
    internal string Kind { get; }
    internal int DuplicateSuffix { get; }

    /// <summary>The cells (silo hatches, tubes, barrels) of this mount.</summary>
    internal IReadOnlyList<WeaponContainer> Cells { get; }

    internal int HatchCount
    {
        get
        {
            int total = 0;
            foreach (WeaponContainer cell in Cells)
            {
                if (cell?._hatches != null) total += cell._hatches.Count;
            }
            return total;
        }
    }

    /// <summary>True when at least one cell has a hatch the player can drive.</summary>
    internal bool HasHatches
    {
        get
        {
            foreach (WeaponContainer cell in Cells)
            {
                if (HatchControl.HasHatches(cell)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// A mount with no per-cell hatches but a mount-level cover: rail launchers such as the
    /// Mk 141 or a box launcher whose single lid opens through the system animation.
    /// </summary>
    internal bool HasMountCover => !HasHatches && Weapon._openSystemAnimation != null;

    internal string Label
    {
        get
        {
            string suffix = DuplicateSuffix > 1 ? " #" + DuplicateSuffix : string.Empty;
            int cells = Cells.Count;
            if (cells > 1) return Name + suffix + "  (" + cells + ")";
            return Name + suffix;
        }
    }
}

/// <summary>Reads the openable systems of a unit. Read-only; never changes game state.</summary>
internal static class HatchBay
{
    internal static List<HatchGroup> Read(ObjectBase unit)
    {
        var groups = new List<HatchGroup>();
        ObjectBaseParameters? blueprint = unit?._obp;
        List<WeaponSystem>? systems = blueprint?._weaponSystems;
        if (systems == null) return groups;

        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < systems.Count; index++)
        {
            WeaponSystem? system = systems[index];
            if (system == null) continue;

            try
            {
                string name = NameOf(system);
                var group = new HatchGroup(system, index, name, KindOf(system), 1);

                // Keep anything with hatches, anything that can open a mount cover, and (only when
                // asked for) plain guns and CIWS, which have neither.
                if (!group.HasHatches && !group.HasMountCover && !ModSettings.IncludeGuns) continue;

                nameCounts.TryGetValue(name, out int seen);
                nameCounts[name] = ++seen;

                groups.Add(new HatchGroup(system, index, name, group.Kind, seen));
            }
            catch (Exception error)
            {
                ModLog.Error("Could not read weapon system " + index, error);
            }
        }

        return groups;
    }

    /// <summary>The display name the game uses for the mount, e.g. "Mk 41 VLS".</summary>
    internal static string NameOf(WeaponSystem system)
    {
        string? name = system._vwp?._systemName;
        if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();

        name = system._vwp?._moduleName;
        if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();

        return system.GetType().Name;
    }

    internal static string KindOf(WeaponSystem system) => system switch
    {
        WeaponSystemLauncher => "launcher",
        WeaponSystemGun => "gun",
        WeaponSystemCIWS => "ciws",
        _ => "system"
    };

    /// <summary>Label for one cell: the hatch count when a cell holds several lids.</summary>
    internal static string CellLabel(HatchGroup group, int index)
    {
        WeaponContainer cell = group.Cells[index];
        int number = cell._containerIndex >= 0 ? cell._containerIndex + 1 : index + 1;

        if (group.Cells.Count == 1) return "Hatch";
        return "Cell " + number;
    }

    internal static string CellDetail(HatchGroup group, int index)
    {
        WeaponContainer cell = group.Cells[index];
        int hatches = cell._hatches?.Count ?? 0;
        return hatches > 1 ? "  (" + hatches + " hatches)" : string.Empty;
    }
}
