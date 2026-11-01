# Changelog

## 0.3.1 — say why, never silently

Reported: hovering Orders showed only the vanilla "Identify", with no hatch entry. That is the
0.2.0 filter bug (fixed in 0.3.0), but a missing entry should never require guessing which build is
installed.

- The submenu tooltip now carries the mod version, so hovering proves which DLL is loaded.
- Logs "Context menu patch is live" the first time any context menu is built, which separates "the
  patch is not installed" from "the patch ran and declined".
- Every skipped unit is logged once with the reason: destroyed, not a player unit, no taskforce,
  or `General/Enabled` false in the config.
- A missing player taskforce no longer hides the feature; it is treated as "cannot compare, allow".

## 0.3.0 — the menu shows up

Reported: after installing and enabling, the Weapon hatches section never appeared. Cause: the
mount filter required hatch data, and hatches are only built for launchers whose unit data declares
them (in `WeaponSystemLauncher`'s constructor), so on many ships every mount was filtered out and the
whole submenu was skipped.

- Every launcher is now listed whether or not its data has hatches, and mounts without hatch data
  say so in the label and tooltip instead of disappearing.
- The submenu appears even on units with no mounts (`Menu/ShowWhenNoMounts`, default on), with a
  report action, so "mod is running but this ship has nothing to open" is distinguishable from "mod
  is not running".
- If the localised Orders submenu cannot be identified, the menu is added at the top level and the
  reason is logged, instead of failing silently.
- Logs what it found per unit, once per distinct result: number of weapon systems, launchers, mounts
  with hatch control and hatch models, plus a status line per mount.
- Logs the package folder and the active settings at startup, to make activation problems obvious.
- `hatch-report.txt` now records whether each cell has open/close animations, which is the usual
  reason a hatch cannot move.

## 0.2.0 — real API

The game's hatch code was located in `Seapower-Scripts.dll` (see `docs/hatch-api.md`), so the
feature is now implemented against it directly instead of probing for a handle at runtime.

- Per-cell open/close uses `WeaponContainer.openHatches()` / `closeHatches()` — the same methods the
  game's launch sequence calls — and reads live state from `_areHatchesOpen` for the check marks.
- Mount covers (rail launchers and box launchers without per-cell hatches) use
  `WeaponSystem.openSystem()` / `closeSystem(delay)`.
- A manual order clears the game's pending auto-close (`_triggerCloseAnimation`) so a hatch does not
  slam shut a moment after opening.
- Menu items use named constructor arguments: `ContextMenuItem` has two constructors with only
  optional parameters, which makes positional calls ambiguous.
- Cells are labelled from the game's own `_containerIndex`, with the hatch count when a cell holds
  several lids.
- "Log hatch report" replaces the previous discovery dumps and writes `hatch-report.txt`.
- Removed the reflective probing layer, the assembly-wide API dump and the animator heuristics they
  fed; roughly a third of the code is gone.

## 0.1.0 — first build

- **Orders → Weapon hatches** submenu: per mount, per cell open/close, plus whole-mount and
  whole-ship actions.
- Cells derived from the game's own `WeaponSystem._containers`.
- Layered strategy chain for the unknown hatch handle (container member, Unity Animator, blueprint
  code animation, mount member), each step logged.
- Startup assembly dump and per-ship dump to identify the members.
- Anchor Chain plugin, HarmonyX patch on `ObjectBaseViewModel.GetContextMenuItems`, honours the
  mod-manager checkbox, BepInEx config, Windows and POSIX build scripts, portable SDK route.
