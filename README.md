# Sea Power Advanced

An advanced mod for **Sea Power: Naval Combat in the Missile Age**, built as an
[Anchor Chain](https://github.com/SeaPower-Modders/AnchorChain) code mod.

**Feature 1 (this release): manual hatch control.** Right-click one of your ships and open
**Orders → Weapon hatches** to open or close individual silo cells, VLS hatches, box-launcher
covers and torpedo tube shutters *without* launching a weapon.

```
Orders
  ▸ Weapon hatches
      Mk 41 VLS  (32)
        ▸ Cell 1            [x]
          Cell 2            [ ]
          ...
          Open all cells
          Close all cells
      Mk 141 Harpoon
        ▸ Hatch             [ ]
      Open all hatches
      Close all hatches
      Log hatch report
```

Version 0.2.0 — hatch control implemented against the game's real API (see
[docs/hatch-api.md](docs/hatch-api.md) for the verified signatures).

---

## Requirements

| Thing | Why |
|---|---|
| Sea Power 0.8.x | Built against the 0.8 generation of `Seapower-Scripts.dll`. |
| [BepInEx](https://github.com/BepInEx/BepInEx) (via Anchor Chain) | Loads the DLL. |
| **Anchor Chain + its preloader** | The mod loader. Without the preloader the game shows *"The Anchor Chain preloader is not running!"* and this mod's code never loads. |

## Install

**Full walkthrough: [INSTALL.md](INSTALL.md)** — every step, from a stock install to a working
`Weapon hatches` menu, including the two loader pieces and how to confirm it loaded.

The short version, once BepInEx + Anchor Chain are in place:

```powershell
Copy-Item Directory.Build.local.props.example Directory.Build.local.props   # set the game paths
.\tools\build-portable.ps1 -Install        # build + copy into StreamingAssets
```

Blocked by PowerShell's execution policy (`is not digitally signed`)? Use `build.cmd install`
instead — batch files are not affected by it.

Then tick **Sea Power Advanced** in the mod manager and restart the game.

## Use it

1. Right-click one of your ships.
2. **Orders → Weapon hatches**.
3. Pick a mount, then a cell. The check mark shows whether that cell's hatches are open right now,
   and each click logs exactly what happened:

   ```
   Sea Power Advanced: Open Ticonderoga / Cell 7: openHatches -> started [open=yes, hatches=1]
   ```

**Open all hatches** does the whole ship. Because the list is rebuilt from live game state, the
check marks stay truthful even when the game opens a hatch itself while firing.

Full walkthrough, including what the menu is telling you and what the mod does not control:
**[docs/usage.md](docs/usage.md)**.

## How it works

The game has the code for this already; it just never exposed it. Verified from
`Seapower-Scripts.dll` (types, signatures, access flags and optional-parameter flags — see
[docs/hatch-api.md](docs/hatch-api.md)):

* each mount is a `WeaponSystem` whose `_containers` are its cells,
* each cell is a `WeaponContainer` that owns its hatch models (`_hatches`) and can move them:

  ```csharp
  cell.openHatches();          // returns bool
  cell.closeHatches();         // returns bool
  cell._areHatchesOpen         // live state, used for the check mark
  ```

  These are the same methods the game's launch sequence calls, so nothing is faked or simulated.
* mounts with one cover instead of per-cell hatches use `WeaponSystem.openSystem()` /
  `closeSystem(delay)`.

Two details that matter in practice:

* After a launch the game parks a pending auto-close on the cell (`_triggerCloseAnimation`). A
  manual order clears it, otherwise the hatch would shut again a moment later.
* `WeaponContainer.update()` keeps running the animation and its sound, so opening and closing
  look and sound exactly like the game's own.

## Status and limits

* **Per cell, not per lid.** Granularity is one container. A cell with four hatch models
  (`_numberOfHatchesPerContainers`) opens all four together — that is what the game's own
  `openHatches()` does.
* **The game still owns the hatches.** If the ship launches, the launch sequence opens and closes
  what it needs and may override your last manual order. That is expected; the check marks will
  reflect the result.
* **Not tested in game yet.** The API is read from the actual shipping assembly rather than
  guessed, but this build has not been flown. `Log hatch report` and `LogOutput.log` are there to
  make the first session conclusive.
* Gun and CIWS mounts have no silo hatches and are hidden unless `General/IncludeGuns` is on.

## Configuration

`BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg`, created on first run.

| Section | Key | Default | Meaning |
|---|---|---|---|
| General | `Enabled` | `true` | Master switch. |
| General | `IncludeGuns` | `false` | Also list guns and CIWS (no silo hatch; only a mount cover if any). |
| General | `AllowEnemyUnits` | `false` | Show the menu on enemy/neutral ships too. |
| Menu | `ShowDiagnosticsItem` | `true` | Adds "Log hatch report". |
| Diagnostics | `VerboseLog` | `true` | Log every hatch action with the game method used and its result. |

## Troubleshooting

| Symptom | Check |
|---|---|
| No "Weapon hatches" entry | Ticked in the mod manager? Game **restarted** (code loads once per launch)? Does the ship actually have launchers? |
| Entry present, submenu empty for a ship | That mount may have no hatch models (some kitbashes). `Log hatch report` shows `hatches=0` per cell. |
| Click does nothing | `LogOutput.log` names the method and its result, e.g. `openHatches -> no change`. Attach that line plus `hatch-report.txt`. |
| Hatch opens then closes again | The game's own launch/reload cycle re-took control. Note what the ship was doing. |
| "Orders" not found in the log | The localised menu label differs. Send the log. |
| Game CTD on startup | Remove the mod folder and send `LogOutput.log`; every path in the mod is guarded, so a crash here matters. |

`hatch-report.txt` (mod folder, written by the menu item) lists per ship: each mount, its cells,
hatch counts, live open state, ammo and pending auto-close flags.

## Build from source

The mod compiles against **your** game's DLLs, so the output matches your Sea Power version.

### Route A — nothing installed (portable SDK)

Downloads the official .NET SDK into `.dotnet/` inside this repo (about 200 MB, once), builds, and
lays out the mod. No installer, no admin rights, no PATH or registry changes; delete `.dotnet/` to
undo it.

```powershell
# Windows
Copy-Item Directory.Build.local.props.example Directory.Build.local.props
notepad Directory.Build.local.props          # set SeaPowerDir and AnchorChainDir
.\tools\build-portable.ps1 -Install
```

```bash
# Linux / Steam Proton
cp Directory.Build.local.props.example Directory.Build.local.props
$EDITOR Directory.Build.local.props
./tools/build-portable.sh --install
```

### Route B — if you already have the .NET SDK

```powershell
.\build.ps1 -Install
```

```bash
./build.sh --install
```

| File | Purpose |
|---|---|
| `src/SeaPowerAdvanced/SeaPowerAdvanced.csproj` | The build. References the game DLLs with `Private=false`. |
| `Directory.Build.props` | Default Steam paths; overridden by `Directory.Build.local.props`. |
| `mod/_info.ini` | Name and description shown in the mod manager. |
| `build.ps1` / `build.sh` | Build, then lay out `dist/` (and optionally install). |
| `tools/build-portable.*` | Fetch a portable SDK into `.dotnet/` and run the build script. |
| `build.cmd` | Same thing as a batch file, for machines where PowerShell policy blocks `.ps1`. |
| `docs/usage.md` | How to use the mod once it is installed. |
| `tools/read-metadata.py`, `tools/dump-game-api.ps1` | Read the game's API out of `Seapower-Scripts.dll` — no game, no compiler. |

Reference paths are never copied into `StreamingAssets`: Anchor Chain would load the game's own DLLs
a second time.

## Layout

```
src/SeaPowerAdvanced/
  Plugin.cs                 Anchor Chain entry point
  Menu/HatchMenu.cs         postfix on ObjectBaseViewModel.GetContextMenuItems → Orders submenu
  Hatches/HatchBay.cs       reads _obp._weaponSystems → _containers (_hatches per cell)
  Hatches/HatchControl.cs   open/close through WeaponContainer / WeaponSystem
  Diagnostics/ShipReport.cs per-ship report (hatch-report.txt)
  ModSettings.cs            BepInEx config
  ModActivation.cs          honours the mod-manager checkbox
  ModFolders.cs             mod folder + diagnostic file location
  ModInfo.cs, ModLog.cs     identity and logging
```

## Roadmap

- [x] Open and close individual silo hatches without launching
- [ ] Per-cell salvo selection: open cells 1–8 only, remembered for the next launch
- [ ] Hatch state in the bottom bar next to the weapon list
- [ ] Pre-launch warm-up: open selected cells ahead of an order so the first missile leaves sooner
- [ ] Radar/mast/sonar raising as an explicit order
- [ ] Damage-control and flooding control per compartment

## Credits

Built on BepInEx, HarmonyX and Anchor Chain. Menu injection follows the pattern used by public Sea
Power mods (Bulk Identification Requests, Real Air Combat Tactics). Not affiliated with Triassic
Games; use at your own risk in single player.
