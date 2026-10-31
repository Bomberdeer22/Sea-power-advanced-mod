# Sea Power Advanced

An advanced mod for **Sea Power: Naval Combat in the Missile Age**, built as an
[Anchor Chain](https://github.com/SeaPower-Modders/AnchorChain) code mod.

**Feature 1 (this release): manual hatch control.** Right-click one of your ships and open
**Orders → Weapon hatches** to open or close individual silo cells, VLS hatches, box-launcher
covers and torpedo tube shutters *without* launching a weapon — the thing you'd do before a
launch, now on your own terms.

```
Orders
  ▸ Weapon hatches
      Mk 41 VLS  (32)
        ▸ Cell 1        [x]
          Cell 2        [ ]
          ...
          Open all cells
          Close all cells
      Mk 141 Harpoon  (4)
        ▸ Cell 1        [ ]
          ...
      Open all hatches
      Close all hatches
      Dump hatch data for this ship
```

Version 0.1.0 — experimental. See [Status and limits](#status-and-limits) before you judge it.

---

## Requirements

| Thing | Why |
|---|---|
| Sea Power 0.8.x | Built against the 0.8 generation of `Seapower-Scripts.dll`. |
| [BepInEx](https://github.com/BepInEx/BepInEx) (via Anchor Chain) | Loads the DLL. |
| **Anchor Chain + its preloader** | The mod-loader. Without the preloader the game shows *"The Anchor Chain preloader is not running!"* and this mod's code never loads. |

## Install

1. Install Anchor Chain following its own guide (it is the one manual step: the preloader goes in
   `BepInEx/plugins/`).
2. Copy the `SeaPowerAdvanced` folder (it holds `SeaPowerAdvanced.dll` and `_info.ini`) into

   ```
   <Steam>\steamapps\common\Sea Power\Sea Power_Data\StreamingAssets\
   ```

3. Start the game, tick **Sea Power Advanced** in the mod manager, and restart the game.

`build.ps1 -Install` and `build.sh --install` do step 2 for you.

## Use it

1. Right-click one of your ships.
2. **Orders → Weapon hatches**.
3. Pick a mount, then a cell. Each click toggles that cell and logs what it actually did:

   ```
   Sea Power Advanced: Open Mk 41 VLS / Cell 7: cell 7 animator Fore_Mk41 [Hatch_Open:Bool] (named parameters)
   ```

**Open all hatches** does the whole ship at once. The check marks are the mod's own record of what
you asked for, so they survive closing and reopening the menu.

## Status and limits

Sea Power does not document how a ship opens a hatch, and **no public mod had touched it** when this
was written, so this release is built to *find* the mechanism at runtime rather than assume one:

* It works through a chain of strategies, most specific first — a per-cell `WeaponContainer`
  member, then a Unity **Animator** on the cell's model, then a blueprint **code animation**
  (`_obp._codeAnimations`), then a mount-level member/animation. Each success is logged.
* If a hatch does nothing, it does **not** crash or spam: you get one warning in
  `BepInEx/LogOutput.log` and the menu keeps working.
* The game may re-apply its own hatch state (for example while a launcher is reloading); in that
  case a hatch can snap back. That means the hatch is driven by game state we have not found yet.

So: **expected to work on at least some ships, and the first person to run it tells us where the
rest are.** The mod ships with the tools to do that in one pass:

* `SeaPowerAdvanced/hatch-api-dump.txt` — written at startup, lists every member in the game
  assembly whose name looks like a hatch, plus the full member list of the container, launcher and
  animation types.
* `hatch-unit-dump.txt` — from **Orders → Weapon hatches → Dump hatch data for this ship**, lists
  the selected ship's code animations, every weapon system, every cell, and the Unity animators and
  their parameters on each cell's model.

Send either file back and the hatch strategy gets pointed at the exact member. Details in
[docs/hatch-api.md](docs/hatch-api.md).

## Build from source

The mod is compiled against **your** game's DLLs, so the DLL always matches your Sea Power version.
Two ways in; the first one installs nothing.

### Route A — nothing installed (portable SDK)

One command downloads the official .NET SDK into `.dotnet/` inside this repo (about 200 MB, once),
builds, and lays out the mod. No installer, no admin rights, no PATH or registry changes; delete
`.dotnet/` to undo it entirely.

```powershell
# Windows
Copy-Item Directory.Build.local.props.example Directory.Build.local.props
notepad Directory.Build.local.props          # set SeaPowerDir and AnchorChainDir
.\tools\build-portable.ps1 -Install
```

```bash
# Linux / Steam Proton
cp Directory.Build.local.props.example Directory.Build.local.props
$EDITOR Directory.Build.local.props          # set the two paths
./tools/build-portable.sh --install
```

`-Install` / `--install` copies the result straight into
`StreamingAssets/SeaPowerAdvanced/`. Without it you get `dist/SeaPowerAdvanced/` and can copy it
yourself.

### Route B — if you already have the .NET SDK

```powershell
.\build.ps1 -Install
```

```bash
./build.sh --install
```

Either way, the first build is the interesting one: if the compiler rejects a line, the game's
signature moved and the error message names it exactly — send it over and it gets fixed in minutes.

`dist/SeaPowerAdvanced/` then holds exactly what goes into `StreamingAssets`.

| File | Purpose |
|---|---|
| `src/SeaPowerAdvanced/SeaPowerAdvanced.csproj` | The build. References the game DLLs with `Private=false`. |
| `Directory.Build.props` | Default Steam paths; overridden by `Directory.Build.local.props`. |
| `mod/_info.ini` | Name and description shown in the mod manager. |
| `build.ps1` / `build.sh` | Build, then lay out `dist/` (and optionally install). |
| `tools/build-portable.*` | Fetch a portable SDK into `.dotnet/` and run the build script. |

Reference paths are never copied into `StreamingAssets`: Anchor Chain would load the game's own DLLs
a second time.

## Configuration

`BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg`, created on first run.

| Section | Key | Default | Meaning |
|---|---|---|---|
| General | `Enabled` | `true` | Master switch. |
| General | `IncludeGuns` | `false` | Also list guns and CIWS (they have no silo hatch). |
| General | `AllowEnemyUnits` | `false` | Show the menu on enemy/neutral ships too. |
| Menu | `ShowDiagnosticsItem` | `true` | Adds "Dump hatch data for this ship". |
| Diagnostics | `VerboseLog` | `true` | Log which strategy opened each hatch. |
| Diagnostics | `DumpApiOnLoad` | `true` | Write `hatch-api-dump.txt` at startup. |
| Animation | `AnimatorBoolNames` | `Open,IsOpen,Hatch,Hatch_Open,Door,Cover,Silo` | Animator bool names tried when the parameter list can't be read. |

## Troubleshooting

| Symptom | Check |
|---|---|
| No "Weapon hatches" entry | Is the mod ticked in the mod manager? Did you **restart** the game (code loads once per launch)? Does the ship actually have launchers? |
| Entry present but nothing moves | Open `LogOutput.log`: the line after each click names the strategy it used. Run the ship dump and send it. |
| "Orders" not found warning | The menu label lookup failed (a localisation or version difference). Send the log. |
| Game CTD on startup | Remove the mod folder, then send `LogOutput.log`. Everything in this mod is wrapped in try/catch, so a crash here is important information. |

## How it works

```
src/SeaPowerAdvanced/
  Plugin.cs                 Anchor Chain entry point; installs the menu, writes the startup dump
  R.cs                      cached reflection helpers (the game's hatch members are not public API)
  ModSettings.cs            BepInEx config
  ModActivation.cs          honours the mod-manager checkbox (Anchor Chain loads every DLL)
  ModFolders.cs             where the mod and its diagnostic files live
  Menu/HatchMenu.cs         postfix on ObjectBaseViewModel.GetContextMenuItems → Orders submenu
  Hatches/HatchBay.cs       reads _obp._weaponSystems → _vwp._systemName and _containers per ship
  Hatches/HatchState.cs     per unit / system / cell open intent (the check marks)
  Hatches/HatchControl.cs   the strategy chain that actually opens a hatch
  Diagnostics/ApiDump.cs    assembly member dump (startup)
  Diagnostics/UnitDump.cs   one-ship dump (menu)
```

Every game member is reached by name through `R` instead of being bound at compile time, so a game
update that renames something degrades to "this strategy stopped working" and is logged, rather
than a `TypeLoadException` that stops the mod from loading at all.

## Roadmap

Feature 1 is the foundation; the same plumbing (menu injection, per-cell state, runtime discovery)
is what the rest of the advanced mod needs.

- [ ] Point the hatch strategies at the exact game members (needs one dump from a real install)
- [ ] Per-cell salvo selection: open cells 1–8 only, and remember it for the next launch
- [ ] Hatch state surfaced in the bottom bar next to the weapon list
- [ ] Pre-launch warm-up: open selected cells ahead of an order so the first missile leaves sooner
- [ ] Radar/mast/sonar raising as an explicit order (the same code-animation mechanism)
- [ ] Damage-control and flooding control per compartment

## Credits

Built on BepInEx, HarmonyX and Anchor Chain. The menu-injection approach follows the patterns used
by public Sea Power mods (Bulk Identification Requests, Real Air Combat Tactics, Torpedo Search
Modes); the game members used here were identified from those projects' documentation of
`Seapower-Scripts.dll`.

Not affiliated with Triassic Games. Use at your own risk in single player.
