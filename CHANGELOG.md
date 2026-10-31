# Changelog

## 0.1.0 — first build

**Manual hatch control**

- New **Orders → Weapon hatches** submenu on player units: per launcher, per cell open/close, plus
  open/close all cells and open/close all hatches on the ship.
- Cells come from the game's own `WeaponSystem._containers`, so a 32-cell VLS really is 32 entries.
- Opening a hatch uses a layered strategy chain (container member → Unity Animator → blueprint code
  animation → mount member), and logs the strategy that worked.
- Per-unit, per-cell open state remembered for the session, shown as check marks.

**Diagnostics**

- `hatch-api-dump.txt` at startup: every hatch-ish member in the game assembly, plus full member
  lists for container, launcher, weapon and code-animation types.
- **Dump hatch data for this ship** in the menu: code animations, weapon systems, cells and the
  Unity animators (with parameters) on each cell's model.

**Plumbing**

- Anchor Chain plugin, HarmonyX patch on `ObjectBaseViewModel.GetContextMenuItems`.
- Honours the mod-manager checkbox; all game internals reached by name so a game update degrades
  to a logged warning instead of a failed load.
- BepInEx config, Windows and POSIX build scripts, test plan and API notes.
