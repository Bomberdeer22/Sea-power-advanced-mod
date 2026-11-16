# Test plan

Run this once per game version, and after any change to `HatchControl`, `HatchMenu`,
`TrafficGenerator` or `RouteFactory`.

Version under test: **0.11.0**. Mod id `io.github.bomberdeer22.seapoweradvanced`.

**Read the log, do not guess.** Everything below is confirmed by two files:

| File | What it proves |
|---|---|
| `BepInEx\LogOutput.log` | BepInEx listed the plugin, Harmony patched the game. |
| `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log` | The mod's own log. If this file exists, the code ran. |

If `seapoweradvanced.log` is missing, nothing else in this plan can pass — stop and send
`SeaPowerAdvanced-diagnose.txt` (from `diagnose.cmd`).

## 0. Before the first test

- [ ] `check-source.cmd` says **PASS**. It costs a second and catches the `using`/type mistakes that
      otherwise show up as `CS0246` two minutes into a build. `build.cmd` runs the same check as step
      `[4/6]`; it never blocks the build.

- [ ] BepInEx + the Anchor Chain preloader in the game folder (run `build.cmd` once and it puts them
      there); the game does *not* say "The Anchor Chain preloader is not running!"
- [ ] `SeaPowerAdvanced.dll` + `_info.ini` in `BepInEx\plugins\SeaPowerAdvanced\` (written by
      `build.cmd`, step `[install]`)
- [ ] **No** leftover copy in `Sea Power_Data\StreamingAssets\SeaPowerAdvanced\` — a double install
      loads every patch twice
- [ ] Game fully restarted after the build (code loads once per launch)
- [ ] `LogOutput.log` open in a second window

## 1. Load

In `LogOutput.log`, BepInEx's own banner should appear:

- [ ] `Loading [Sea Power Advanced 0.11.0]`

Then the mod's own startup lines:

- [ ] `Sea Power Advanced: ================ Sea Power Advanced 0.11.0 ================`
- [ ] `Sea Power Advanced: Entry point:  BepInEx plugin`
- [ ] `Sea Power Advanced: Settings: enabled=True includeGuns=False ...`
- [ ] `Sea Power Advanced: Patched MapUnitViewModel.GetContextMenuItems.`
- [ ] `Sea Power Advanced: Context menu patch is live (...)`
- [ ] `Sea Power Advanced: Civilian traffic panel ready (F8 toggles it inside the mission editor).`
- [ ] `Sea Power Advanced: Ready. Right-click a ship > Orders > Weapon hatches.`
- [ ] No `Could not patch`, no `Startup failed`

If `Entry point:` says `AnchorChain mod`, the DLL is being loaded from `StreamingAssets` — that is the
double-install case; remove that folder.

Anchor Chain's Workshop copy is optional: without it the build produces the BepInEx-only plugin and
the startup line reads `Anchor Chain not installed - building the BepInEx-only plugin. This is fine.`
Builds with Anchor Chain installed also compile an `AnchorChain mod` entry point, which is unused on
the BepInEx route.

## 2. Hatch menu

- [ ] Right-click a player ship with launchers (Spruance, Ticonderoga, Burke, Kirov, Slava)
- [ ] **Orders → Weapon hatches** exists
- [ ] Mounts are named as the game names them (`Mk 41 VLS  (32)`), not `WeaponSystemLauncher`
- [ ] Two mounts with the same name show ` #2`
- [ ] A ship with no launchers shows `This ship has no launchers - click for a report`
- [ ] Enemy and neutral ships show nothing (unless `AllowEnemyUnits`)
- [ ] Vanilla Orders entries are unchanged and still work

## 3. One cell (the feature)

- [ ] **Cell 1** → the hatch opens; log reads `Open <mount> / Cell 1: openHatches -> started`
- [ ] Reopen the menu: Cell 1 is ticked
- [ ] Click Cell 1 again → closes, unticked, log reads `closeHatches -> started`
- [ ] Move the camera close: the hatch animates and the hatch sound plays
- [ ] Repeat on a cell in the middle of the mount (not just the first)

`-> already running` or `-> state set` are also correct; `-> no-op` is the one worth reporting.

## 4. Whole mount / whole ship

- [ ] **Open all cells** opens every cell of that mount, all ticked
- [ ] **Open all hatches** cycles every mount; log ends with
      `Open all hatches on <ship>: N/M mounts responded.`
- [ ] **Close all hatches** closes them
- [ ] A 16+ cell VLS completes smoothly, no stutter
- [ ] A rail launcher without per-cell hatches (Mk 141) offers **Open cover / Close cover**, and the
      cover moves

## 5. Coexistence with the game

- [ ] Open cells manually, then launch: the launch is normal, salvo/reload counters unchanged
- [ ] Let the ship fire with cells manually opened: note whether the game reopened/closed cells
      (expected — the game owns the cycle)
- [ ] Hatch stays open afterwards (the mod clears `_triggerCloseAnimation`; if it slams shut, note the
      ship and what it was doing)
- [ ] Order a reload, a turn, a sensor change: nothing regresses
- [ ] Save, then load: the menu still works (check marks come from live state, so they must survive)

## 6. Hatch robustness

- [ ] Toggle a cell twice quickly
- [ ] Open hatches on a small boat with one mount
- [ ] **Log hatch report** → `hatch-report.txt` appears in the mod folder, per-cell lines look sane
- [ ] No `NullReferenceException` or `TypeLoadException` in `LogOutput.log`
- [ ] Ten minutes of play: no crash, log does not grow without bound

## 6b. The advanced features (right-click menu)

Full description of each: [features.md](features.md). Right-click one of your ships.

- [ ] The features are spread across the menu's own sections, not stacked under Orders:
      Sensors, Engage, Rules of engagement, Formation, Air operations, Damage control
- [ ] **Breakaway** is at the top level of the right-click menu, one click away
- [ ] `menu-report.txt` (mod folder) lists the section labels found and **where each entry went** -
      check it against the list above; it is the file to send if an entry is in the wrong place
- [ ] `capabilities.txt` lists the gameplay levers found in this build, and `ecm-report.txt` lists
      every ECM member and type the scan found
- [ ] Each toggle shows its state in the label and flips when clicked
- [ ] **Sensors and EMCON → Radars off** actually darkens the ship; `Radars on` brings them back
- [ ] **Radar blink** cycles the surface-search radar; stopping puts it back as it was
- [ ] **Countermeasures → Breakaway** launches chaff and turns the ship
- [ ] **Countermeasures → Auto-ARM shutoff** goes dark when a missile is inbound, then recovers
- [ ] **Sensors → Defensive ECM: ... - click to arm** arms one ship; the label then reads
      *armed, waiting for a vampire*
- [ ] **Sensors → Defensive ECM for the whole task force** arms every ship at once
- [ ] With a missile fired at the task force: the log shows
      `Defensive ECM: <ship> engaging a missile inbound to <ship> - air search on, lock on the
      missile via <member>, jamming`, the ship's air-search radar comes on, and six seconds after
      the sky clears the log shows `stood down` and the radar goes back as it was
- [ ] If this build exposes no ECM at all, the menu says so instead of pretending, and
      `ecm-report.txt` lists what the scan looked at - **send that file**, it is the missing piece
- [ ] **Sensors → ECM: what this build exposes** rewrites `ecm-report.txt` on demand
- [ ] **Sensors → Offensive ECM: standard - click for signature lock** switches mode; the lock
      submenu then appears and the label becomes *Offensive ECM: signature lock*
- [ ] After locking, a **long narrow blue beam** runs from the jammer to the contact and stops
      there - thin while locked; **`Jam now - transmit on the lock`** appears and thickens it when
      pressed; `ecm-cone.txt` records how positions are projected
- [ ] With a defensive set armed and a missile inbound, an **amber beam** runs to the missile
- [ ] If no beam appears, the bottom of the screen says why and `ecm-cone.txt` names what was tried
- [ ] **Offensive ECM lock** lists hostile contacts with ranges; picking one logs
      `Offensive ECM: <ship> locked on <target> (N nm, hull <sig>). Read ...`, and
      **`offensive-ecm.txt`** appears in the mod folder with a per-sensor verdict table
- [ ] The target's radars go dark and stay dark while the lock is held (watch its emissions); the
      log repeats `holding <target> ... blind` only when the picture changes
- [ ] **Release the lock** (or switching back to standard) stops the suppression, logs
      `released <target> after N assertion(s)`, and puts the jammer ship's air search back as found
- [ ] The lock runs **on EMCON**: the attacking ship's air-search radar does *not* come on, and the
      log says `on EMCON (no radar)` in the jammer path
- [ ] If a sensor cannot be switched off, the menu says *NOT all sensors off - <sensor> would not
      switch off on this build*, and `offensive-ecm.txt` shows `STILL ...` for that family - **that
      line is the one to send**, it names the member the next build needs
- [ ] Right-click an **enemy contact**: the menu shows the game's own identification entries plus
      **Lock with ECM (<ship>, N nm)** - and nothing else from this mod
- [ ] Right-click one of your **submarines**: no ECM entries at all
- [ ] Right-click a ship **without** a flight deck: no helicopter entries
- [ ] **Readiness → Action stations** shows fatigue climbing; reloads shorten
- [ ] **Manoeuvres → Sprint and drift** sets the doctrine (check the log for the cavitation speed)
- [ ] **Manoeuvres → Turn the screen onto the threat** re-stations the ships on one bearing
- [ ] **Air operations → ladder/square/sector** flies a helicopter pattern
- [ ] **Strike → Deconflict the salvo** holds ships and releases them one at a time
- [ ] **Deception → Commercial disguise** goes dark and slow, and says plainly what it cannot do
- [ ] **Logistics → Replenish alongside** moves ammunition when a supply ship is within 2 nm
- [ ] Nothing above throws an exception; `seapoweradvanced.log` names each action and its result

## 7. Civilian traffic (mission editor)

This is the feature that has never run, so expect to find something here.

- [ ] Open the **mission editor** on a new mission
- [ ] Log reads `Mission editor detected; the civilian traffic panel is available.`
- [ ] Press **F8** → a *Civilian traffic* window appears; drag it by the title bar
- [ ] `Classes:` line shows non-zero counts, and the log reads
      `Civilian catalogue: N civilian vessel class(es), M civilian aircraft class(es).`
      (0/0 means nothing in this install is flagged civilian — send the log)
- [ ] Press **Draw zone**, left-click 4–6 points around open sea, then **Finish**
      (or middle-click). Log reads `Zone tool: polygon mode started. ...`
- [ ] The `Zone:` line shows the point count and an area in nm²
- [ ] Set min/max ships to `2`/`4`, planes to `0`/`0`, press **Spawn traffic**
- [ ] Log reads `Civilian traffic: Added N ship lane(s) (up to 4 vessel(s) at a time) and 0 aircraft
      lane(s) ...`
- [ ] **Save the mission**, then play it: the ships appear inside the drawn area, not outside it
- [ ] They keep coming back after finishing their run (`_maxUnits` is a concurrency cap, not a total)
- [ ] **Remove generated** takes them back out; log reads `removed N generated route(s).`

Then repeat with planes set to `1`/`2` and ships `0`/`0`.

- [ ] Draw a zone that crosses land: units should stay off the land, or report what they did
- [ ] Draw a tiny zone (a few km): the panel should refuse it, not spawn one unit on top of another
- [ ] Reload the saved mission: does the traffic survive the round trip through the mission file?

## 8. Disable

- [ ] Up the `General/Enabled` toggle in
      `BepInEx\config\io.github.bomberdeer22.seapoweradvanced.cfg`, restart: log reads
      `General/Enabled is false; nothing will be patched.` and no `Weapon hatches` entry appears
- [ ] Set it back to `true`, restart: it comes back

## What to send back

- **Which numbered checks failed** — just the numbers, that is enough.
- `seapoweradvanced.log` (mod folder) for anything that failed, and the matching `LogOutput.log` lines.
- `hatch-report.txt` for anything hatch-related that did not move.
- For traffic: the ship class and mount for any hatch that did not move, and whether the units
  appeared in the right place, off the coast, or not at all.

`diagnose.cmd` collects system, game folder, mod files, build logs and game logs into one file. If in
doubt, run it and send `SeaPowerAdvanced-diagnose.txt`.
