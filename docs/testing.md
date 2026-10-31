# Test plan

Run this once per game version, and after any change to `HatchControl` or `HatchMenu`.

## 0. Before the first test

- [ ] Anchor Chain and its preloader installed; the mod manager does *not* say "The Anchor Chain
      preloader is not running!"
- [ ] `SeaPowerAdvanced/` (DLL + `_info.ini`) in `StreamingAssets`, ticked in the mod manager
- [ ] Game fully restarted after the build (code loads once per launch)
- [ ] `BepInEx/LogOutput.log` open in a second window

## 1. Load

- [ ] Log contains `Sea Power Advanced: Loading Sea Power Advanced 0.2.0`
- [ ] Log contains `Hatch menu installed.`
- [ ] Log contains `Ready. Right-click a ship > Orders > Weapon hatches.`
- [ ] No exception or `Could not patch` line

If any of these is missing, stop: the mod is not running and nothing below can pass.

## 2. Menu

- [ ] Right-click a player ship with launchers (Spruance, Ticonderoga, Burke, Kirov, Slava)
- [ ] **Orders → Weapon hatches** exists, at the top of Orders
- [ ] Mounts are named as the game names them (`Mk 41 VLS  (32)`), not `WeaponSystemLauncher`
- [ ] Two mounts with the same name show ` #2`
- [ ] A ship with no launchers shows no entry (or only guns, if `IncludeGuns` is on)
- [ ] Enemy and neutral ships show nothing (unless `AllowEnemyUnits`)
- [ ] Vanilla Orders entries are unchanged and still work

## 3. One cell (the feature)

- [ ] **Cell 1** → the hatch opens; log reads `openHatches -> ok (N hatch…)`
- [ ] Reopen the menu: Cell 1 is ticked
- [ ] Click Cell 1 again → closes, unticked, log reads `closeHatches -> ok`
- [ ] Move the camera close: the hatch animates and the hatch sound plays
- [ ] Repeat on a cell in the middle of the mount (not just the first)

## 4. Whole mount / whole ship

- [ ] **Open all cells** opens every cell of that mount, all ticked
- [ ] **Open all hatches** cycles every mount; log ends with
      `Open all hatches on <ship>: N/M mounts responded.`
- [ ] **Close all hatches** closes them
- [ ] A 16+ cell VLS completes smoothly, no stutter, no log spam beyond the summary
- [ ] A rail launcher without per-cell hatches (Mk 141) offers **Open cover / Close cover** and it
      moves the cover

## 5. Coexistence with the game

- [ ] Open cells manually, then launch: the launch is normal, salvo/reload counters unchanged
- [ ] Let the ship fire with cells manually opened: note in `hatch-report.txt` whether the game
      reopened/closed cells (expected — the game owns the cycle)
- [ ] Hatch stays open afterwards (the mod clears `_triggerCloseAnimation`; if it slams shut, note
      the ship and what it was doing)
- [ ] Order a reload, a turn, a sensor change: nothing regresses
- [ ] Save, then load: the menu still works (check marks come from live state, so they must survive)

## 6. Robustness

- [ ] Toggle a cell twice quickly
- [ ] Open hatches on a small boat with one mount
- [ ] Run `Log hatch report`: `hatch-report.txt` appears in the mod folder, per-cell lines look sane
- [ ] No `NullReferenceException` or `TypeLoadException` in `LogOutput.log`
- [ ] Ten minutes of play with the mod on: no crash, log does not grow without bound

## 7. Disable

- [ ] Untick the mod, restart: log says `Disabled in the mod manager; no patches installed.` and no
      `Weapon hatches` entry appears
- [ ] Re-enable, restart: it comes back

## What to send back

Whether it opened (yes/no), the ship class and mount for any hatch that did not move, and
`hatch-report.txt` plus the matching `LogOutput.log` lines.
