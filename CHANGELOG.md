# Changelog

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
