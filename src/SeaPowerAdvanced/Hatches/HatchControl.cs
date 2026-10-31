using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Hatches;

/// <summary>
/// Opens and closes hatches through the game's own methods, without launching anything.
///
/// Per cell, the game's exact behaviour (from the IL of <see cref="WeaponContainer"/>):
///
/// <code>
/// openHatches():
///   if (_openAnimation == null)                          -> nothing happens, false
///   if (_areHatchesOpen)  { _areHatchesOpen = true;      -> false  (already open)
///   if (_playOpenAnimation)                              -> nothing, false (already running)
///   _openAnimation.playAnim(); play hatch sound;
///   _playOpenAnimation = true;                           -> true
///
/// update():  steps the animation; when it finishes it sets _playOpenAnimation = false and
///            _areHatchesOpen = true (and shows the weapons in the cells)
/// </code>
///
/// So the boolean return means "started something", not "is now open" — the state lives in
/// <see cref="WeaponContainer._areHatchesOpen"/> plus the two in-flight flags, which is what the
/// menu shows and what toggling uses.
/// </summary>
internal static class HatchControl
{
    /// <summary>True when this cell can be opened: the game needs an animation to play.</summary>
    internal static bool CanOpen(WeaponContainer? cell) => cell?._openAnimation != null;

    internal static bool CanClose(WeaponContainer? cell) => cell?._closeAnimation != null;

    /// <summary>Drop this cell from the menu when it has no hatch animation at all.</summary>
    internal static bool HasHatches(WeaponContainer? cell) =>
        CanOpen(cell) || CanClose(cell) || (cell?._hatches?.Count ?? 0) > 0;

    /// <summary>
    /// Open, opening, or mid-open. Closing counts as closed, so a second click during a close
    /// starts opening again instead of stopping half way.
    /// </summary>
    internal static bool IsOpen(WeaponContainer? cell)
    {
        if (cell == null) return false;
        bool open = cell._areHatchesOpen || cell._playOpenAnimation;
        return open && !cell._playCloseAnimation;
    }

    /// <summary>Opens or closes one cell (one silo, one tube or one lid).</summary>
    internal static bool SetCell(WeaponContainer cell, bool open, out string report)
    {
        if (cell == null)
        {
            report = "no cell";
            return false;
        }

        if (open ? !CanOpen(cell) : !CanClose(cell))
        {
            report = open ? "cell has no open animation" : "cell has no close animation";
            return false;
        }

        bool before = IsOpen(cell);

        if (open)
        {
            // The game parks a pending auto-close on the cell after firing. A manual order has to
            // clear it or the hatch shuts again on the next update.
            cell._triggerCloseAnimation = false;
        }

        bool started;
        try
        {
            started = open ? cell.openHatches() : cell.closeHatches();
        }
        catch (System.Exception error)
        {
            report = "threw " + error.GetType().Name;
            return false;
        }

        bool running = open ? cell._playOpenAnimation : cell._playCloseAnimation;
        bool now = IsOpen(cell);
        bool success = running || now == open;

        report = (open ? "openHatches" : "closeHatches")
                 + " -> " + (started ? "started" : running ? "already running" : now == before ? "no-op" : "state set")
                 + $" [open={(now ? "yes" : "no")}, hatches={cell._hatches?.Count ?? 0}]";
        return success;
    }

    /// <summary>Opens or closes every cell of one mount, then its cover if it has no cells.</summary>
    internal static bool SetGroup(HatchGroup group, bool open, out string report)
    {
        int cells = 0;

        foreach (WeaponContainer cell in group.Cells)
        {
            if (!HasHatches(cell)) continue;
            cells++;
            SetCell(cell, open, out _);
        }

        if (cells > 0)
        {
            // The mount has cells the player can drive: it responded, even if a cell was already
            // in the requested state (openHatches/closeHatches report false for that case).
            report = $"{cells} cell{(cells == 1 ? "" : "s")} {(open ? "opened" : "closed")}";
            return true;
        }

        return TryMountCover(group, open, out report);
    }

    /// <summary>The mount-level cover, for launchers whose one lid covers every cell.</summary>
    private static bool TryMountCover(HatchGroup group, bool open, out string report)
    {
        try
        {
            if (open)
            {
                if (group.Weapon._openSystemAnimation == null)
                {
                    report = "no hatch on this mount";
                    return false;
                }

                bool started = group.Weapon.openSystem();
                report = "openSystem -> " + (started ? "started" : "no change");
                return started;
            }

            group.Weapon.closeSystem(0f);
            report = "closeSystem -> ok";
            return true;
        }
        catch (System.Exception error)
        {
            report = "mount cover threw " + error.GetType().Name;
            return false;
        }
    }

    /// <summary>Every mount of a unit, with one summary line.</summary>
    internal static void SetAll(ObjectBase unit, IEnumerable<HatchGroup> groups, bool open)
    {
        int mounts = 0, moved = 0;

        foreach (HatchGroup group in groups)
        {
            mounts++;
            if (SetGroup(group, open, out string report))
            {
                moved++;
                if (ModSettings.VerboseLog) ModLog.Info($"{(open ? "Opened" : "Closed")} {group.Name}: {report}");
            }
            else if (ModSettings.VerboseLog)
            {
                ModLog.Warn($"{(open ? "Open" : "Close")} {group.Name}: {report}");
            }
        }

        ModLog.Info($"{(open ? "Open" : "Close")} all hatches on {UnitName(unit)}: {moved}/{mounts} mounts responded.");
    }

    internal static void SetCell(ObjectBase unit, HatchGroup group, int cellIndex, bool open)
    {
        if (cellIndex < 0 || cellIndex >= group.Cells.Count) return;

        bool changed = SetCell(group.Cells[cellIndex], open, out string report);
        string message = $"{(open ? "Open" : "Close")} {group.Name} / {HatchBay.CellLabel(group, cellIndex)}: {report}";

        if (changed || ModSettings.VerboseLog) ModLog.Info(message);
        else ModLog.Warn(message);
    }

    internal static string UnitName(ObjectBase? unit)
    {
        if (unit == null) return "this unit";
        try
        {
            string? display = unit.get_DisplayNameShort();
            if (!string.IsNullOrWhiteSpace(display)) return display!.Trim();
            string? plain = unit.getName();
            if (!string.IsNullOrWhiteSpace(plain)) return plain!.Trim();
        }
        catch (System.Exception) { }
        return "this unit";
    }
}
