# Test plan

Run this once per game version, and after any change to `HatchControl`.

## 0. Before the first test

- [ ] Anchor Chain and its preloader installed; the mod manager does *not* say "The Anchor Chain
      preloader is not running!"
- [ ] `SeaPowerAdvanced/` (DLL + `_info.ini`) in `StreamingAssets`, ticked in the mod manager
- [ ] Game fully restarted after the build (code loads once per launch)
- [ ] `BepInEx/LogOutput.log` open in a second window

## 1. Load

- [ ] Log contains `Sea Power Advanced: Loading Sea Power Advanced 0.1.0`
- [ ] Log contains `Hatch menu installed.`
- [ ] Log contains `API dump written to …hatch-api-dump.txt`
- [ ] The file exists and its section 1 is non-empty

If any of these is missing, stop: the mod is not running and nothing below can pass.

## 2. Menu

- [ ] Right-click a player ship with launchers (e.g. a Spruance, Ticonderoga or Kirov)
- [ ] **Orders → Weapon hatches** exists and is at the top of Orders
- [ ] The submenu lists the ship's launchers with sensible names and cell counts
      (`Mk 41 VLS (32)`), not `WeaponSystemLauncher (32)`
- [ ] A ship with no launchers (a gunboat) does not show the entry — or shows only guns when
      `General/IncludeGuns` is on
- [ ] Right-clicking an enemy or neutral ship shows nothing (unless `AllowEnemyUnits`)
- [ ] The menu still opens and closes normally, and the vanilla Orders entries are unchanged

## 3. Opening one cell

- [ ] **Orders → Weapon hatches → Mk 41 VLS → Cell 1** → the log names the strategy used
- [ ] The model visibly opens that cell's hatch (the feature working)
- [ ] Re-open the menu: Cell 1 shows a check mark
- [ ] Click Cell 1 again → it closes and the check mark clears

If nothing moves, record which strategy the log named (or `no hatch control found`) — that is the
key datum for the next iteration.

## 4. Whole mount / whole ship

- [ ] **Open all cells** opens every cell of that mount, and every cell gets a check mark
- [ ] **Open all hatches** does every launcher on the ship in one click
- [ ] **Close all hatches** closes them again
- [ ] A ship with 16+ cells completes in well under a second and stays at a normal frame rate

## 5. Coexistence with the game

- [ ] Launch a missile from a manually opened cell: the launch is normal, the salvo/reload counters
      in the bottom bar are unchanged, and the mod logs nothing unexpected
- [ ] Manually open cells, then let the ship fire: note in the dump whether the game's own launch
      opens *more* cells or closes the ones you opened (that tells us whether the game owns the state)
- [ ] Order a reload, a turn, and a sensor change: nothing regresses
- [ ] Pause, save, reload the save: the menu still works afterwards; check marks may reset
      (expected — the mod's state is per session)

## 6. Robustness

- [ ] Open a hatch on a large ship with the camera zoomed in, then on a small boat with none
- [ ] Click a hatch toggle twice quickly
- [ ] Run the dump on a ship with no launchers: log shows a warning, no exception
- [ ] No `NullReferenceException` or `TypeLoadException` in `LogOutput.log` at any point
- [ ] Run a mission for 10 minutes with the mod on: no crash, no growing log spam

## 7. Disable

- [ ] Untick the mod in the mod manager, restart: log says `Disabled in the mod manager; no patches
      installed.` and no `Weapon hatches` entry appears
- [ ] Re-enable, restart: it comes back

## What to send back

`hatch-unit-dump.txt` and `hatch-api-dump.txt` from the mod folder, plus the lines in
`LogOutput.log` between `Loading Sea Power Advanced` and the last hatch action. Note the ship class
for each hatch that did nothing.
