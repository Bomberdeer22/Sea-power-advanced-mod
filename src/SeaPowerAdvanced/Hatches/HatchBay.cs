using System;
using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Hatches;

/// <summary>
/// One weapon mount on a ship that could have hatches: a VLS, a box launcher, a torpedo tube nest.
///
/// A mount is a <see cref="WeaponSystem"/> whose <see cref="WeaponSystem._containers"/> are its
/// cells. Whether a mount can actually open depends on the ship's data: the game builds the
/// per-cell animations and hatch models in <see cref="WeaponSystemLauncher"/>'s constructor, from
/// the animation names in the unit's INI. Mounts whose data has none are still listed, so the menu
/// always reflects reality instead of silently disappearing.
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

    /// <summary>The cells (silo tubes, launch boxes, barrels) of this mount.</summary>
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

    internal int AnimatedCells
    {
        get
        {
            int total = 0;
            foreach (WeaponContainer cell in Cells)
            {
                if (cell != null && (cell._openAnimation != null || cell._closeAnimation != null)) total++;
            }
            return total;
        }
    }

    internal int OpenableCells
    {
        get
        {
            int total = 0;
            foreach (WeaponContainer cell in Cells)
            {
                if (HatchControl.HasHatches(cell)) total++;
            }
            return total;
        }
    }

    /// <summary>True when at least one cell has a hatch animation or hatch model to drive.</summary>
    internal bool HasHatchData => OpenableCells > 0;

    /// <summary>A mount-level cover, for launchers whose single lid covers every cell.</summary>
    internal bool HasMountCover => Weapon._openSystemAnimation != null;

    /// <summary>Anything can be made to move on this mount.</summary>
    internal bool CanDrive => HasHatchData || HasMountCover;

    internal bool IsLauncher => Kind == "launcher";

    internal string Label
    {
        get
        {
            string suffix = DuplicateSuffix > 1 ? " #" + DuplicateSuffix : string.Empty;
            string count = Cells.Count > 1 ? "  (" + Cells.Count + ")" : string.Empty;
            return Name + suffix + count;
        }
    }

    /// <summary>Why this mount is listed but cannot open, for the menu and the log.</summary>
    internal string Status
    {
        get
        {
            if (HasHatchData) return HatchCount > 0
                ? HatchCount + " hatch model(s) on " + AnimatedCells + "/" + Cells.Count + " cell(s)"
                : AnimatedCells + "/" + Cells.Count + " cell(s) animated";
            if (HasMountCover) return "mount cover only";
            return "no hatch data";
        }
    }
}

/// <summary>Reads the mounts of a unit. Read-only; never changes game state.</summary>
internal static class HatchBay
{
    /// <summary>
    /// Mounts the player can see in the menu: every launcher (a silo or tube mount, whether or not
    /// its data has hatches), anything else that does have hatch data, and — only when asked for —
    /// plain guns and CIWS.
    /// </summary>
    internal static List<HatchGroup> Read(ObjectBase? unit)
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
                var probe = new HatchGroup(system, index, NameOf(system), KindOf(system), 1);
                if (!probe.IsLauncher && !probe.HasHatchData && !ModSettings.IncludeGuns) continue;

                nameCounts.TryGetValue(probe.Name, out int seen);
                nameCounts[probe.Name] = ++seen;

                groups.Add(new HatchGroup(system, index, probe.Name, probe.Kind, seen));
            }
            catch (Exception error)
            {
                ModLog.Error("Could not read weapon system " + index, error);
            }
        }

        return groups;
    }

    /// <summary>One-line summary of what a ship offered, for the log.</summary>
    internal static string Summary(ObjectBase? unit, List<HatchGroup> groups)
    {
        int launchers = 0, driveable = 0, hatches = 0;
        foreach (HatchGroup group in groups)
        {
            if (group.IsLauncher) launchers++;
            if (group.CanDrive) driveable++;
            hatches += group.HatchCount;
        }

        int systems = unit?._obp?._weaponSystems?.Count ?? 0;
        return $"{systems} weapon system(s), {groups.Count} listed ({launchers} launcher(s)), "
               + $"{driveable} with hatch control, {hatches} hatch model(s)";
    }

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

    internal static string CellLabel(HatchGroup group, int index)
    {
        WeaponContainer cell = group.Cells[index];
        int number = cell._containerIndex >= 0 ? cell._containerIndex + 1 : index + 1;
        return group.Cells.Count == 1 ? "Hatch" : "Cell " + number;
    }

    internal static string CellDetail(HatchGroup group, int index)
    {
        WeaponContainer cell = group.Cells[index];
        int hatches = cell._hatches?.Count ?? 0;
        bool animated = cell._openAnimation != null || cell._closeAnimation != null;
        if (hatches > 1) return "  (" + hatches + " hatches)";
        if (!animated && hatches == 0) return "  (no data)";
        return string.Empty;
    }
}
