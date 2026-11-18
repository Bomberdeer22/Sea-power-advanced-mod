# Sea Power Advanced

An advanced mod for **Sea Power: Naval Combat in the Missile Age**, built as an
[Anchor Chain](https://github.com/SeaPower-Modders/AnchorChain) code mod and loadable as a plain
BepInEx plugin.

**Version 0.23.0** — the first public release: fourteen advanced features for your own ships,
plus per-cell hatch control. Everything is reached through the game's own right-click menus; there
is no mod window and no hotkey.

### 1. Manual hatch control

Right-click one of your ships and open **Orders → Weapon hatches** to open or close individual silo
cells, VLS hatches, box-launcher covers and torpedo tube shutters *without* launching a weapon.

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

### 2. Fourteen advanced features, in the right-click menu

Right-click one of your ships. Each feature appears in the section of the menu where a player would
look for it, rather than in one long submenu:

```
Orders                      Weapon hatches (feature 1)
Sensors                     radars, air/surface search only, active sonar, towed array,
                            EMCON profiles, radar blink, auto-ARM, defensive ECM,
                            offensive ECM (two modes, works on unidentified contacts, and a
                            suppression budget with a time limit), commercial disguise, and the
                            close-in dive that flies jammed missiles down
Engage                      deconflict the salvo, release everyone
Rules of engagement         action stations / cold cruise, crew fatigue status
Formation                   sprint and drift (this ship or the whole force),
                            turn the screen onto the threat
Air operations              helicopter ladder / expanding square / sector search
Damage control              local control, replenish alongside
(top level)                 Breakaway - chaff and hard rudder in one click
```

Sections are matched by name at runtime; if your build has no such section the entry moves up to
Orders instead of vanishing. **`menu-report.txt`** in the mod folder lists the section labels that
were actually found and where every entry landed - it is the first file to read if something appears
in an unexpected place.

Every entry that is a toggle shows its current state in the label, and every one reports what
happened through the game's own order-feedback text as well as the log.

**Defensive ECM** arms itself. Arm it on a ship (or on the whole task force) and its jammer
comes up automatically the moment a vampire is in the air - pointing at the missile itself and
holding the lock, at the price of radiating air search while it works, because a jammer needs a
fire-control quality bearing to know where to point. It stands down six seconds after the sky is
clear and puts the radar back as it found it. What it cannot promise is that the missile misses -
that is the game's own jamming model, not the mod's - but inside ten miles it does two more things: it
breaks the weapon's guidance link the way the game itself does, and flies it into the water over a
planned 2.5 to 20 seconds with the nose stopping at ninety degrees - using the flight model's own
numbers, its orientation and velocity vector, not a dragged model. At the waterline the game's own
water-dip path removes it. It is limited the way a real ship is - three guidance channels, two seconds
between engagements, one second to react, and radar seekers only, because jamming cannot blind a heat
seeker - and all four are settings, documented in `docs/features.md` under "Making it less powerful".
Nothing in the feature nulls a reference, destroys a weapon, or sets a warhead off; `ecm-report.txt` and `ecm-dive.txt` say exactly which members your install exposes and
what the mod does with them, down to the weapon type's whole member list.

**Offensive ECM** is the deliberate half. Right-click an enemy contact → **Jam with ECM** and the
nearest of your ships that carries a jammer points it at that contact through the game's own jam
order - identified or not, because an unidentified track is jammed on its bearing, which is the order
the game provides for exactly that case. It runs on EMCON: the attacking ship never switches its own
radar on. Ships are reached at 150 nm and aircraft at 150 nm. A second, separate order - *hold its
sensors down* - switches the contact's own radar, sonar and towed array off and keeps them off, and
that half is limited twice over: **two contacts held blind at a time across the task force**, and a
**one minute window per hold**, after which the contact's crew get their sensors back for fifteen
seconds before it can be held down again. Both are settings (`EcmJamChannels`, `EcmJamHoldSeconds`,
`EcmJamRecoverySeconds`) and both are stated in the menu before you click. The game's own jam keeps
running on every lock whether or not it has a channel.

### Multiplayer

Works with the community multiplayer mod (`SeapowerMultiplayer`), host or client, and is unchanged
without it. The one thing worth knowing: the multiplayer mod throttles radar switches to one per ship
per radar group every ten seconds, so **radar blink slows to a 12 second half-cycle in a session**, and
on a client the host still has the final say on whether a radar radiates. `multiplayer.txt` in the mod
folder records which side of the session this machine is on and every rule that followed from it.

**Something not working? Run `diagnose.cmd`** — one double-click, one text file to send. Then open an
issue here and attach it; the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md) lists the
report files and what each one answers, so a report can be acted on rather than guessed at.

Plain-text copies of this file and of the install guide are included: [README.txt](README.txt),
[INSTALL.txt](INSTALL.txt).

---

## Requirements

| Thing | Why | Who installs it |
|---|---|---|
| Sea Power (Steam) | The mod compiles against **your** install's own DLLs, so it matches whatever
version you run. It is written against the 0.8 generation of `Seapower-Scripts.dll`. | You |
| **BepInEx + the Anchor Chain preloader** | Without them the game loads no code mods at all. | **`build.cmd`** — downloaded into the game folder for you |
| Anchor Chain itself (Workshop) | Optional. A second loader for `StreamingAssets` mods. | Nobody, unless you want it |

The only thing you provide is a Sea Power install. `build.cmd` fetches the compiler and the loader
by itself, straight into the folders the game expects.

## Install

**Full walkthrough: [INSTALL.md](INSTALL.md)** — every step, from a stock install to a working
`Weapon hatches` menu, including the two loader pieces and how to confirm it loaded.

The short version, once BepInEx + Anchor Chain are in place:

```bat
build.cmd
```

**Double-click it.** It finds your game, fetches a portable .NET SDK if you have none, **installs
BepInEx and the Anchor Chain preloader into the game folder if they are missing**, builds the mod
against your own game DLLs and copies it into `BepInEx\plugins\SeaPowerAdvanced\`. It pauses
at the end, and writes `build-log.txt` plus `build-output.txt` so a closing window never loses an
error.

It uses **no PowerShell** — `curl` and `tar`, both built into Windows, do the fetching and unpacking.
That makes it the route to use if PowerShell is restricted on your PC (`is not digitally signed`,
or a PowerShell window that closes itself).

Prefer PowerShell? `tools\build-portable.ps1 -Install` does the same job when it is permitted.

**Updating** an installation that is already in the game folder is `update.cmd`: it reads the version
that is there, does only what that version still needs, and writes down every decision in
`update.txt`. **Uninstalling** is `uninstall.cmd`, which keeps a copy of everything it removes.
Both are described in [docs/updating.md](docs/updating.md).

The DLL goes into `BepInEx\plugins\SeaPowerAdvanced\`, so **BepInEx loads it directly** — there is
no mod-manager checkbox on this route. BepInEx's log then shows `Loading [Sea Power Advanced 0.23.0]`,
and the mod writes its own `seapoweradvanced.log` next to the DLL.

## Use it

### Hatch control

1. Right-click one of your ships.
2. **Orders → Weapon hatches**.
3. Pick a mount, then a cell. The check mark shows whether that cell's hatches are open right now,
   and each click logs exactly what happened:

   ```
   Sea Power Advanced: Open Ticonderoga / Cell 7: openHatches -> started [open=yes, hatches=1]
   ```

**Open all hatches** does the whole ship. Because the list is rebuilt from live game state, the
check marks stay truthful even when the game opens a hatch itself while firing.

Full walkthrough: **[docs/usage.md](docs/usage.md)**.

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
* **Tested in game, on the developer's install.** Every feature has been exercised in a live
  mission through 0.23.0, and each one has its own report file so a different install can be
  compared line by line. Where something is still a guess it is named in
  [docs/features.md](docs/features.md) under *Honest limit* rather than left to be discovered.
* Gun and CIWS mounts have no silo hatches and are hidden unless `General/IncludeGuns` is on.
* **The advanced features have limits, and every one is written down.** They are in
  [docs/features.md](docs/features.md) under *Honest limit*, and the ones a player will notice first
  are: the ECM reach of 150 nm is the mod's reach, not a promise the jam bites that far; radar blink
  slows to a 12 second half-cycle in a session because the multiplayer mod throttles radar switches;
  only missiles with a radar seeker can be talked out of the sky; replenishment moves ammunition and
  no fuel, because this game has no fuel-transfer-at-sea member; and a jam started from a client
  cannot be stopped from that client.

## Configuration

`BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg`, created on first run.

| Section | Key | Default | Meaning |
|---|---|---|---|
| General | `Enabled` | `true` | Master switch. |
| General | `IncludeGuns` | `false` | Also list guns and CIWS (no silo hatch; only a mount cover if any). |
| General | `AllowEnemyUnits` | `false` | Show the menu on enemy/neutral ships too. |
| Menu | `ShowDiagnosticsItem` | `true` | Adds "Log hatch report". |
| Menu | `ShowWhenNoMounts` | `true` | Show the hatch entry even on ships with no launchers, so it is obvious the mod is loaded. Set `false` once everything works. |
| Diagnostics | `VerboseLog` | `true` | Log every hatch action with the game method used and its result. |

The advanced features have their own section, and every one of the limits quoted above is here:

| Key | Default | Meaning |
|---|---|---|
| `Features/Enabled` | `true` | Add the advanced features to the menus at all |
| `Features/BlinkSeconds` | `6` | Seconds between radar blink flips (12 in a multiplayer session) |
| `Features/SalvoSpacingSeconds` | `8` | Seconds between ships released in a deconflicted salvo |
| `Features/JamRangeNm` | `150` | Reach of the focused offensive jammer against a ship (5-300) |
| `Features/JamAirRangeNm` | `150` | The same against an aircraft or helicopter (5-300) |
| `Features/ShowEcmCone` | `true` | Draw the ECM cones on the map |
| `Features/EcmJamChannels` | `2` | How many contacts the task force may hold sensors down on at once |
| `Features/EcmJamHoldSeconds` | `60` | How long one hold lasts. **0 = unlimited** |
| `Features/EcmJamRecoverySeconds` | `15` | Seconds before the same contact can be held down again. **0 = free to renew** |
| `Features/EcmDiveNm` | `10` | How far out an armed jammer starts putting an inbound missile in the sea. **0 = off** |
| `Features/EcmDiveChannels` | `3` | How many missiles one ship's jammer can put down at once |
| `Features/EcmDiveIntervalSeconds` | `2` | Seconds between one descent starting and the next being allowed |
| `Features/EcmDiveReactionSeconds` | `1` | Seconds between a missile crossing `EcmDiveNm` and its descent starting |
| `Features/EcmDiveRadarSeekersOnly` | `true` | Only radar-seeking missiles can be decoyed; IR, optical and anti-radiation are immune |

Setting the old "unlimited" values (99 / 0 / 0 for the offensive limits, 99 / 0 / 0 / false for the
dive) is how you get the pre-limit behaviour back. Every default is argued for in
[docs/features.md](docs/features.md), in the two sections called "Making it less powerful".

## Troubleshooting

| Symptom | Check |
|---|---|
| No "Weapon hatches" entry | Ticked in the mod manager? Game **restarted** (code loads once per launch)? Does the ship actually have launchers? |
| Entry present, submenu empty for a ship | That mount may have no hatch models (some kitbashes). `Log hatch report` shows `hatches=0` per cell. |
| Click does nothing | `LogOutput.log` names the method and its result, e.g. `openHatches -> no change`. Attach that line plus `hatch-report.txt`. |
| Hatch opens then closes again | The game's own launch/reload cycle re-took control. Note what the ship was doing. |
| "Orders" not found in the log | The localised menu label differs. Send the log. |
| Game CTD on startup | Remove the mod folder and send `LogOutput.log`; every path in the mod is guarded, so a crash here matters. |
| No advanced features in the right-click menu | `Features/Enabled` in the config is `true`? The entries appear the first time a menu is opened after a ship is selected - `menu-report.txt` lists where each one landed. |
| An ECM entry missing or refused | `ecm-report.txt` and `offensive-ecm.txt` say which members your install exposes and which limit refused the click. In a session, `multiplayer.txt` explains what the host owns. |
| A jammed missile did not go down | `ecm-dive.txt` names the weapon type, its seeker, and whether it was left alone on purpose (an IR, optical or anti-radiation seeker is immune by design). |
| Anything else | Run `diagnose.cmd` and send `SeaPowerAdvanced-diagnose.txt`. |

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
| `build.cmd` | Build and install, as a batch file. No PowerShell, so policy cannot block it. |
| `check-features.cmd` | Double-click: checks which planned features this build of the game can support. |
| `tools/check-source.py` | The mistakes that have actually broken a build: a missing `using`, a member that no longer exists, a game enum named in code. Runs in CI, and from `build.cmd` before it compiles. |
| `tools/check-batch.py` | The same idea for the `.cmd` files: every label resolves, no unclosed block, no `%VAR%` that expands stale inside a block. Runs in CI. |
| `tools/check-versions.py` | Fails when the version number disagrees between the source, the build, the changelog, the README, the install guide and the test document. |
| `tools/check-docs.py` | Fails when a `.txt` twin has drifted from the Markdown it was generated from. `--write` regenerates them. |
| `docs/capability-layer.md` | Why "missing" usually means misnamed, and how the mod finds levers at runtime. |
| `docs/usage.md` | How to use the mod once it is installed. |
| `docs/features.md` | Every feature, the member it drives, its settings and its honest limit. |
| `docs/testing.md` | What to check in a mission, one line per thing worth looking at. |
| `docs/releasing.md` | How a release is cut: the four checks, the version bump, the tag, and what must never be attached. |
| `diagnose.cmd` | One double-click: collects system, game folder, mod files and game logs into `SeaPowerAdvanced-diagnose.txt`. Send that file when something breaks. |
| `README.txt`, `INSTALL.txt` | Plain-text copies of this file and the install guide, for Notepad. Regenerate with `python tools/md2txt.py README.md README.txt`. |
| `tools/analysis/` | **Read the game's API out of `Seapower-Scripts.dll`** — types, signatures, access flags and who calls what. Self-tested against ILDasm. See [tools/analysis/README.md](tools/analysis/README.md). |
| `tools/read-metadata.py`, `tools/dump-game-api.ps1` | Older, narrower metadata dumps (type and member names only). |

Reference paths are never copied into `StreamingAssets`: Anchor Chain would load the game's own DLLs
a second time.

## Checking the API before writing code

Every feature here is written against the shipped assembly rather than against guesswork, because
"does this member exist, what is its signature, and what does the game call it from?" decides whether
a feature is a five-line change or impossible. That reader lives in
[tools/analysis](tools/analysis/README.md):

```bat
check-features.cmd                      :: double-click: which planned features are buildable
tools\analysis\survey.cmd              :: full API survey -> tools\analysis\analysis-report.txt
tools\analysis\survey.cmd readiness    :: the same check from a command prompt
tools\analysis\survey.cmd selftest     :: verify the reader still works
```

**`check-features.cmd`** is the one to double-click: it writes `feature-readiness.txt` next to
itself, saying feature by feature whether the game exposes what each one needs (`READY`, `PARTIAL`
or `BLOCKED`) and printing the member names it matched, so a wrong verdict can be seen rather than
believed. It needs Python 3 once; if that is missing it says so and needs nothing else. It reads
`Directory.Build.local.props` for the game path, so it works wherever Steam is installed.

The next batch of features is triaged in [docs/feature-plan.md](docs/feature-plan.md): what depends
on what, what order to build it in, and which parts the game may not model at all.

It reads metadata and IL only — nothing is loaded or run — and it also answers one-off questions:

```bat
python analyze.py member SeaPower.ObjectBase
python analyze.py find "_radarsActive" --callsites
```

## Layout

```
src/SeaPowerAdvanced/
  Plugin.cs                 Anchor Chain entry point
  BepInExEntry.cs           BepInEx plugin entry point (the documented route)
  Boot.cs                   the startup both entry points share; writes the log banner
  Menu/HatchMenu.cs         postfix on ObjectBaseViewModel.GetContextMenuItems → Orders submenu
  Hatches/HatchBay.cs       reads _obp._weaponSystems → _containers (_hatches per cell)
  Hatches/HatchControl.cs   open/close through WeaponContainer / WeaponSystem
  Diagnostics/ShipReport.cs per-ship report (hatch-report.txt)
  ModSettings.cs            BepInEx config
  ModActivation.cs          honours the mod-manager checkbox
  ModFolders.cs             mod folder + diagnostic file location
  Interop/Capabilities.cs        finds the game's emitter/throttle/decoy levers by reflection
  Interop/MemberProbe.cs         name + signature lookup, so a rename degrades instead of breaking
  Interop/UnitState.cs           per-unit state for what the game never modelled (fatigue, GQ)
  Interop/SensorControl.cs       radar/sonar/EMCON/doctrine levers, named from the real assembly
  Interop/GameWorld.cs           the game's own list of live units (private, so read by reflection)
  Interop/CapabilityReport.cs    writes capabilities.txt: what this game build exposes
  Interop/Capability.cs          one named lever the game exposes, with how it is called
  Interop/WeaponStatusControl.cs the weapons-status enum, taken from the game's method signature
  Interop/WaypointControl.cs     setWaypointTask, with its arguments built from its own signature
  Interop/FlightDeckControl.cs   the flight-deck launch call, likewise - no game enum is named
  Interop/Inbound.cs             the vampires: what is inbound to a ship or to the ships near it
  Interop/EcmControl.cs          discovers the ECM switch, target and power by name; ecm-report.txt
  Interop/SensorSet.cs           reads a unit's sensor fit and signatures, and silences them
  Interop/UnitFit.cs             per-unit answers: does this hull carry ECM, can it fly, is it ours
  Interop/MapProjection.cs       game position -> screen, found at runtime; ecm-cone.txt if not
  Interop/Selection.cs           the selected unit, from the game if it can, else the last one touched
  UI/EcmConeOverlay.cs           draws the beams: long, narrow, and only where they belong
  Menu/FleetMenu.cs              the features, distributed across the game's sections
  Menu/MenuRouting.cs            puts each entry in the section a player would look in first
  Menu/FleetMenuPatch.cs         its own Harmony patch, so a hatch-menu problem cannot hide it
  Menu/MenuReport.cs             menu-report.txt: which label went where in the live menus
  Features/Blink.cs              radar blink cycling
  Features/AutoArm.cs            anti-radiation defence: go dark on an inbound weapon
  Features/SalvoPlan.cs          staggered release of the task force's launches
  Features/Breakaway.cs          chaff, hard rudder and full ahead, at once
  Features/LocalControl.cs       degraded local fire control
  Features/Readiness.cs          action stations, cold cruise and crew fatigue
  Features/ThreatAxis.cs         re-station the screen onto the threat bearing
  Features/SprintDrift.cs        the game's own sprint-and-drift doctrine, per ship or fleet-wide
  Features/HeloPatterns.cs       one-click helicopter search patterns
  Features/MerchantDisguise.cs   reduced emissions and a civilian-looking signature
  Features/Replenishment.cs      ammunition from a supply ship alongside
  Features/DefensiveEcm.cs       automatic defensive jamming of missiles inbound to ship or force
  Features/OffensiveEcm.cs       two modes: the game's own, or focused jamming of one contact
  Interop/WeaponDive.cs          a missile's lock broken and its height driven down, from 10 nm
  Interop/OffensiveJam.cs        the game's jammer (a sensor system) and its own jam order
  Interop/ContactPicture.cs      the player's contacts, identified or not (plotting table)
  Interop/Multiplayer.cs         host or client, and the rules that follow from it (multiplayer.txt)
  Features/EcmBeam.cs            one beam's geometry, shared by the overlay and the reports
  Menu/MenuLog.cs                what the menu did, line by line
  Features/FleetTicker.cs        the timers behind the timed features (no interface of its own)
  ModInfo.cs, ModLog.cs     identity and logging

tools/
  check-source.py           pre-flight check: missing usings, missing types, UnitRecord members,
                            out-of-scope locals, calls to methods nothing declares
  check-batch.py            the same idea for the .cmd files: unresolvable labels, unclosed ( )
                            blocks, `%VAR%` read inside the block that set it, a for /f that would
                            silently read its quoted path as a string
  md2txt.py                 writes the .txt twins of the documentation
```

Outside `src/` and `tools/`, the scripts a player double-clicks:

```
build.cmd        find the game, fetch a compiler, build, install   (first install)
update.cmd       read the installed version, apply what the changelog says changed, rebuild
uninstall.cmd    remove the mod, keeping a backup in uninstalled\
diagnose.cmd     one file with everything needed to answer "why does it not work"
check-source.cmd the C# and .cmd checks, without building
check-features.cmd  what the game's own assembly can support
```

## Roadmap

- [x] Open and close individual silo hatches without launching
- [x] The fourteen advanced features, each in the game's own menu section
- [x] Defensive ECM that jams a missile and flies it into the sea
- [x] Offensive ECM through the game's own jam order, on unidentified contacts
- [ ] Per-cell salvo selection: open cells 1–8 only, remembered for the next launch
- [ ] Hatch state in the bottom bar next to the weapon list
- [ ] Pre-launch warm-up: open selected cells ahead of an order so the first missile leaves sooner
- [ ] Radar/mast/sonar raising as an explicit order
- [ ] Damage-control and flooding control per compartment

## License

MIT - see [LICENSE](LICENSE). The mod is compiled against your own copy of the game and ships no
game files.

## Credits

Built on BepInEx, HarmonyX and Anchor Chain. Menu injection follows the pattern used by public Sea
Power mods (Bulk Identification Requests, Real Air Combat Tactics). Not affiliated with Triassic
Games; use at your own risk. Multiplayer support is compatibility with the community multiplayer
mod, not an official feature of either.
