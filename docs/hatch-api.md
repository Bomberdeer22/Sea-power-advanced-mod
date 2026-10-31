# How a hatch opens — research notes

This is the technical background for feature 1. It separates what is **known** from what the mod
has to **discover at runtime**, because that distinction is the whole design.

## What is known

Sea Power's gameplay code lives in `Sea Power_Data/Managed/Seapower-Scripts.dll`. Code mods attach
to it with HarmonyX under Anchor Chain. Verified against public mod sources for game 0.8.x:

| Member | Meaning | Seen in |
|---|---|---|
| `ObjectBase._obp` | the unit's blueprint instance (live objects, not just data) | several mods |
| `_obp._weaponSystems` | the unit's weapon systems | SeaPowerRadio, seapower-survivors |
| `_obp._codeAnimations` | dictionary of named model animations; values have `setClosedState()` | seapower-survivors |
| `_obp._magazineSystems`, `_obp._launcherWeapons`, `_obp._gunWeapons` | other mount collections | seapower-survivors |
| `WeaponSystem._vwp` | the visual weapon part (mount) — `_systemName`, `_magazineReloadTime`, `_fireRatePerMinute`, `_preLaunchDelay` | several mods |
| `WeaponSystem._containers` | list of **`WeaponContainer`** — one per cell/tube/barrel; `_initialAmmunition`, `_weapons`, `_initialAmmoCount`, `RefillAmmo` | seapower-survivors |
| `WeaponSystemLauncher` | launcher subclass (also `WeaponSystemGun`, `WeaponSystemCIWS`, `WeaponSystemChaff`) | seapower-survivors |
| `WeaponSystem.EngageState.TargetTooFarToOpenSystem` | the game has a concept of a system being **opened** | SeaPowerRadio |

`_containers` is the key one: it is per **cell**, which is exactly the granularity "open individual
silos" needs. A 32-cell Mk 41 shows up as a launcher with 32 containers.

The unit `.ini` format does not reference hatches: a weapon system declares `ContainerBase=`,
`Container1=`, `Container1_Position`, `AreContainersRotatable`, `FiringArcs`, `AssociatedMagazine`
and a collider. So a hatch is a property of the *model* (an animation), not of the data file.

## What is not known

**Which member actually plays the hatch animation.** No public mod opens a hatch, the Anchor Chain
docs do not cover gameplay internals, and the game has no published API reference. Candidates, in
the order this mod probes them:

1. a named member on `WeaponContainer` — `Open()`, `OpenHatch()`, `IsOpen`, `_hatchOpen`, …
2. a Unity `Animator` on the cell's model GameObject, with a bool/trigger parameter whose name
   contains `hatch`, `cover`, `lid`, `silo`, `door`, `canopy`, `vls`, `tube` or `cell`
3. a `_obp._codeAnimations` entry whose name matches both a hatch word and the mount's name
   (e.g. `Mk41_Hatch_Open`), driven with `setOpenState()` / `setClosedState()`
4. the same member/animator search on the mount (`WeaponSystem` / `_vwp`), for launchers that open
   one cover over all cells

The mod tries 1 → 2 → 3 per cell, then 4 for the mount, and stops at the first that works. It logs
the winner in `LogOutput.log` and in `hatch-unit-dump.txt`.

Deliberate safety choices:

* A code animation is only driven when it unambiguously belongs to the mount by name, **or** when
  the ship has exactly one hatch-like animation. Otherwise it is skipped — opening a random radar
  mast would be worse than doing nothing.
* Ambiguous container members are not tried in bulk; the first candidate that exists wins and is
  logged, so it can be corrected in the next release.

## The two diagnostic files

**`hatch-api-dump.txt`** (mod folder, written at startup when `Diagnostics/DumpApiOnLoad` is on)

* every member of every type in the game assembly whose *name* contains hatch/cover/lid/silo/door/
  canopy/cell — the section that would reveal `WeaponContainer._hatchAnimator` style members
* the full member list of every container, launcher, weapon, code-animation and blueprint type
* the resolved `UnityEngine.Animator` type

**`hatch-unit-dump.txt`** (mod folder, from the menu's *Dump hatch data for this ship*)

* the ship's `_codeAnimations`: every key, and the fields of its value object
* every openable weapon system: type, display name, cell count, fields of the mount
* every cell: its fields, plus the animators found on its model and each animator's parameters

## If a hatch does nothing

1. Run *Dump hatch data for this ship* on that ship class.
2. Send `hatch-unit-dump.txt` and `hatch-api-dump.txt` (or the relevant lines of
   `BepInEx/LogOutput.log`).
3. The next step is mechanical: whichever handle appears is added at the front of the strategy
   chain in `Hatches/HatchControl.cs`. If a handle is per-mount rather than per-cell, the menu
   already has the mount-level path for it.

If the dump shows **no** hatch-ish member and **no** animator on the cells, then the game drives
hatch animation entirely from animation *clips* (no named parameters). In that case the remaining
approach is to play the clip directly (`Animation.Play("...")`) or to hook the game's own launch
routine and reuse whatever it calls — both need the game's launch method, which the log will name
once a launch happens with `VerboseLog` on.
