# Changelog

## 0.12.0 — blink asks which radar, and the traffic panel explains itself

### Radar blink: you choose the set

The entry is now a submenu - **air search radar**, **surface search radar** or **both** - because the
two do different jobs and only the air-search set is what an anti-radiation shot homes on. A hull
with no radar is not offered the entry at all, and stopping restores each set to the state it was
found in rather than assuming it was on.

### Civilian traffic: the actual fault, and a report that names it

- **The route list was assumed, not found.** The generator required
  `MissionManager._civilianRoutes`, which is very likely null in the mission editor - the manager is
  what *runs* a mission, and the editor is holding the one being *edited*. `Civilian/RouteContainers`
  now searches the mission manager, then the editor's view model, then the mission it is editing, and
  adds to whichever list it finds. The panel's message now says which container it used.
- **The panel no longer fails in silence.** It used to `return` with an empty catch when the editor
  view model was null or threw, so the feature could be broken with nothing in the log at all. Every
  hidden reason is now logged once, and written to the report.
- **`traffic-report.txt`** lists all seven prerequisites with an ok/-- mark - editor open, state
  manager, drawing layer, unit database, civilian classes, neutral taskforce, route list - plus the
  zone's point count and the outcome of the last attempt. Written on every spawn and by the panel's
  new **Check setup** button.
- **F8 is read from the GUI event as well as from `Input`**, so the panel is reachable on builds that
  run the new Input System only, where `Input.GetKeyDown` throws.
- A redundant null test on a value-type polygon point is gone (CS0472).

## 0.11.0 — the ECM cones, and a manual transmit order

- **The beams are on the map.** Every set that is aimed at something draws a long, narrow cone:
  cyan from an offensive jammer to the ship it is locked on (thin while locked, thick and labelled
  while transmitting), amber from a defensive set to the missile it is tracking, and a small cyan box
  on a selected ship that carries ECM but has nothing locked. `Features/ShowEcmCone`, default on.
- **Locking and transmitting are now two orders.** A lock reads the target, holds the track and aims
  the beam; it does not switch anything off. **`Jam now - transmit on the lock`** does that, and
  **`Jamming: on - click to stop transmitting`** stops it while keeping the lock. The same two
  buttons appear on the contact's own menu once that contact is locked, next to the order that acted
  on it.
- **The overlay never guesses a screen position.** `Interop/MapProjection.cs` looks for the game's own
  `GeoPosition`-to-world conversion first, then falls back to each unit's Unity transform through the
  active camera. If neither exists it draws nothing, says so on screen, and writes `ecm-cone.txt`
  with what it looked for. A cone in the wrong place would be worse than no cone.
- **Cost control, because this draws every frame**: only on repaint, the beam list rebuilds five times
  a second rather than every GUI event, and `GameMain` is now cached in `GameWorld` instead of being
  found by a scene walk on every query.
- Audit: `tools/check-source.py` clean across 55 files, brace balance clean, every build script's
  labels resolve, and the new Unity types are on the checker's allow-list so it cannot mistake
  `Color` for a game type.

## 0.10.1 — ECM runs silent, and only where it fits

Four corrections after playing it, all of them about the same thing: the mod was offering things to
units and in modes that did not suit them.

- **Offensive ECM no longer switches the radar on.** It runs on EMCON - the attacking ship radiates
  nothing while it jams. The air-search requirement was copied from the defensive feature, where it
  belongs: shooting at a missile needs a fire-control bearing, jamming a contact does not.
  `EcmControl.Engage` now takes that decision as a parameter instead of assuming it.
- **A contact's menu gets one entry, not twenty.** Right-clicking a hostile or neutral contact now
  adds **Lock with ECM (<ship>, N nm)** at the top level and nothing else; the game's own
  identification entries are untouched. The ship is whichever of yours carries ECM, is nearest, and
  is in range - so the player never has to work out which hull has the jammer. If nothing of yours is
  in range, the entry is not added.
- **Only units that can do a thing are offered it.** ECM entries appear on hulls the game says carry
  ECM (and on surface hulls where it does not say), and never on submarines. Active sonar and towed
  array follow the game's own `HasActiveSonar` / `HasTowedSonar`. Helicopter patterns need a flight
  deck or hangar. Replenishment is surface hulls only. Where the game does not answer, the mod shows
  the entry rather than hiding a feature it could not check.
- **"Partially blind" replaced with what it actually means:** *NOT all sensors off - <sensor> would
  not switch off on this build*. The old wording read like a mod state; it was a missing game member.

`Interop/UnitFit.cs` holds the per-unit answers, caches its lookups, and logs what it decided for the
first contact menu it augments, so a menu that looks wrong can be explained from the log.

## 0.10.0 — offensive ECM, in two modes

The ship's jammer, used deliberately. Standard stays the default and changes nothing; signature lock
is the focused mode.

- **Standard** is the game's own ECM behaviour. The mod does not touch the set and does not radiate
  for it. Switching back to it releases any lock.
- **Signature lock** reads one contact's sensor fit before jamming anything - which radar families
  it has and whether each is up, towed array, active sonar, ESM and its own jammer, and what its hull
  signature reads as. That read *is* the lock: nothing is jammed until the mod knows what the target
  radiates with.
- With the read in hand, every one of those families is switched off **on that contact only**, and
  re-asserted ten times a second for as long as the lock is held, because the target's crew can
  switch a set back on in between. Sustained suppression, not a one-off toggle.
- The jammer ship radiates air search for the whole engagement - the same honest cost as the
  defensive feature - and gets it back as it was found when the lock is released, the target is
  sunk, or it runs beyond `Features/JamRangeNm` (default 40 nm).
- **"Fully blind" is a claim about evidence, not intent.** It is only reported when every sensor
  family the mod could reach was switched off *and read back as off*. A family this build refuses to
  switch is reported as *partially blind*, named in the log and listed as `STILL` in
  `offensive-ecm.txt`. The mod does not edit anyone's picture to hide the gap.

### How it reads another ship

`Interop/SensorSet.cs` is the reading half: it enumerates a unit's sensor families from the state the
game exposes (`IsAirSearchRadarsOn`, `IsSurfaceSearchRadarsOn`, `HasTowedSonar`, `HasActiveSonar`),
finds ESM and jammer switches by name, and reads the hull signature out of the unit's parameter block.
The same code switches them off, so the read and the jam can never disagree about what a "sensor" is.

The contact list comes from the ship's own picture - `_threats`, `_spottedObjects`,
`_dangerousMissiles` - filtered to hostiles inside jam range, so the mod never invents a target the
force cannot see.

## 0.9.0 — automatic defensive ECM

Every ship that carries a jammer now defends itself and its formation with it, without being asked
twice. Armed per ship or for the whole task force, and automatic from there.

- **A vampire wakes the jammer.** Any weapon the game tracks against this ship - or against a
  task-force ship within 25 nm - triggers it. The missile *instance* is handed to the game's own ECM
  target member, so the set points at the seeker rather than at a bearing the mod computed, and the
  lock is re-pointed ten times a second for as long as the missile is tracked, so a lock the game
  clears is immediately re-established.
- **Air search radars go on, deliberately.** A jammer needs a fire-control quality bearing, and the
  price of that bearing is an emission the other side can see. The feature is loud on purpose; that
  is the trade, and it is why it is armed rather than always on.
- **Power to maximum** where this build exposes a power or mode member whose units the mod can
  trust. A bare number with unknown units is left alone rather than set to a guess.
- **Stands down on its own** six seconds after the last vampire, stops jamming, and restores the
  air-search radar to exactly the state it was found in.
- **Honest limit, stated in the menu and the docs:** the mod points the game's jammer at the game's
  missile and holds it there. Whether the seeker breaks lock is the simulation's answer. No mod that
  cannot see the jamming model should claim a missile "almost certainly" misses, and this one does
  not.

### How the interface was found

ECM is not in any dump this project has, and every game type named in code from memory has failed to
compile. So `Interop/EcmControl.cs` discovers the interface instead: it scans the game's unit type
for members whose names mention `ecm` or `jam`, sorts them by shape (switch, target holder, power or
mode), and uses whatever is there. `Interop/Inbound.cs` holds the single definition of "inbound",
now shared with the auto-ARM feature so the two can never disagree.

- **`ecm-report.txt`** is written at startup and on demand: every ECM-like member on the game's
  unit type, every type in `Seapower-Scripts.dll` whose name mentions ecm/jam/decoy, and what the
  mod does with each. If this build names its jammers something else, that file says what - it is
  read from the installed game, so it is ground truth rather than a guess.
- If nothing is found, the menu says so plainly instead of pretending, and the report is the file to
  send. `diagnose.cmd` now collects it alongside `capabilities.txt`.

## 0.8.2 — 0.8.1's compile errors

0.8.1 got past the `Capability` and `using` errors and stopped on twelve more, all of the same two
kinds: a game type named in code, and a member that no longer exists. Both are now fixed at the
cause rather than one at a time.

- **No game enum is named in code any more.** `WaypointHeightState` (threat axis, helo patterns),
  `UnitRoles` and `MissionType` (flight deck) are unreachable from this project's namespaces —
  naming them is CS0246 every time, exactly like `WeaponStatus`. Two new wrappers find the methods
  by name and build their arguments from their own signatures:
  `Interop/WaypointControl.cs` (a waypoint on any unit) and `Interop/FlightDeckControl.cs` (launch a
  helicopter, after asking the game whether one is on deck). If a build's call has a shape the mod
  cannot fill, the menu says so and the log prints the signature instead of guessing.
- **`Capabilities.ParametersOf` is back.** A rewrite had removed it while `Readiness`,
  `MerchantDisguise` kept calling it — CS0117 in three places. It is now a one-liner over
  `unit._obp`, and the checker below looks for member references so a rewrite cannot silently take
  it again.
- **`get_ThreateningMissile()` → the property, read by reflection.** Calling an accessor by name is
  CS0571. A value type there is treated as "no answer" rather than "inbound", because a ship whose
  radars can never come back on is worse than one that misses a warning.
- **`LaunchChaff(bool)` returns `void`,** so the breakaway sequence no longer assigns it to a bool;
  it goes through `SensorControl`, which reports whether the call was made.
- **Dead state removed.** Four `UnitRecord` fields nothing ever wrote, and three more that were
  written but never read. The two that had a use now have one: `Blink.IsOn` also consults the record,
  the disguise drop logs how long it was up, and `SalvoPlan.Release` frees anything the records say
  is waiting, not only what survived in a static list.

### The checker now catches all five

`tools/check-source.py` gained: a game type named in a type position that no build has proved this
project can see; a member called on a mod type that has no such member (`CS0117`); an accessor called
by name (`CS0571`); and `UnitRecord` fields that are never assigned or never read (`CS0649`). Each is
fault-injected and verified. `check-source.cmd` still runs it in a second and writes
`check-source.txt`.

## 0.8.1 — the build compiles again

0.8.0 did not compile, so nothing after it could run. This is a compile fix, not a behaviour change:
the menu visibility work from the previous commit is still the thing to look for in game, and it has
never been observed working yet.

- **`Capability` restored.** Rewriting `Interop/Capabilities.cs` had deleted the class that eleven of
  its members use, which reads as correct in the file itself and fails with eleven `CS0246`s. The
  type now lives in its own file, `Interop/Capability.cs`, so a future rewrite of the reporter cannot
  take it with it.
- **`using SeapowerUI;`, `using SeapowerUI.ViewModels;`, `using UniRx;`** added to `FleetMenu.cs`,
  `MenuRouting.cs` and `FleetMenuPatch.cs`. `ContextMenuItem`, `MapUnitViewModel` and
  `TrulyObservableCollection` do not resolve from `using SeaPower;` alone.
- **The weapons-status enum is never named in code.** `WeaponStatus` failed to compile even though it
  is the parameter type of `ObjectBase.SetWeaponStatus`, so it is now taken from that method's own
  signature at runtime (`Interop/WeaponStatusControl.cs`) and the salvo planner and readiness feature
  both go through it. If a future game build renames the enum, the feature reports that it cannot hold
  weapons instead of failing to build.
- **`UnitRecord.BlinkPhase`** added — `Blink.cs` read a member that no longer existed.

### The check that should have caught all of that

- **`tools/check-source.py`** finds, without compiling: a symbol used in a file whose namespace is not
  imported, a mod type referenced but declared nowhere, and a `UnitRecord` member read by a feature
  but not declared. It exits non-zero and can `--fix` the usings it is sure about.
- **`check-source.cmd`** runs it from a double-click: writes `check-source.txt`, pauses, uses no
  PowerShell. Add `--fix` to add the missing usings.
- **`build.cmd`** runs it as step `[4/6]`, between checking BepInEx and compiling. It is skipped when
  Python is absent, and a finding is a warning rather than a refusal: the compiler stays the judge.
  A four-second check cannot be wrong in a way that blocks a good build.

## Unreleased — diagnostics and plain-text docs

For an install problem where the console closes before its output can be copied.

- **`diagnose.cmd`**: one double-click collects system info, the game folder it finds, which mod
  and BepInEx files are present, `BepInEx\LogOutput.log`, the mod's own log, and a run of the
  analysis tools into `SeaPowerAdvanced-diagnose.txt`. Uses only `cmd.exe` — never PowerShell — and
  writes the file as it goes, so a window closing cannot lose the evidence.
- `survey.cmd` now tees every run into `run-log.txt` for the same reason, and `analyze.py` writes a
  full traceback to `analysis-error.txt` if it crashes.
- **`README.txt` / `INSTALL.txt`**: plain-text twins of the Markdown, generated by
  `tools/md2txt.py` (wrapped to 78 columns, ASCII only, CRLF, tables rendered as labelled blocks so
  they read in Notepad).
- **Install guide corrected.** It still described the 0.4-era `StreamingAssets\SeaPowerAdvanced`
  location in step 3, its expected output, the uninstall steps and the file tree, while step 4 said
  `BepInEx\plugins\SeaPowerAdvanced`. Anyone following it end to end could install to the wrong
  folder and see the mod never load. Also fixed the stale log paths in `docs/usage.md`.

## Unreleased — feature readiness probe

Planning tooling for the next batch of features (sensor/EMCON, tactics, formation, logistics).

- `tools/analysis features.py` + `analyze.py readiness`: probes each planned feature's decisive
  capabilities against the installed assembly and reports READY / PARTIAL / BLOCKED, with the
  matched member names as evidence. Exposed as `survey.cmd readiness`.
- Each feature records the guess made *before* the check, so a surprise stays visible.
- `docs/feature-plan.md`: the twelve requested features triaged by dependency, with the build order
  and an explicit list of what is probably not possible.
- Performance work found by testing on real assemblies: metadata table lookups are cached (they were
  being re-materialised inside loops, turning name scans into minutes), member labels are built
  lazily, and compiler-generated member names are excluded from capability matches.
- Three real bugs caught by running the probe on F# and Android assemblies rather than on the fixture:
  a `\b` missing from a "towed array" pattern matched `sonAr` inside "Compari**sonAr**b", a regex
  without word boundaries matched `ToArray`, and `types`/`member` failed on short type names.

## 0.6.0 — civilian traffic zones (mission editor)

New feature: random civilian traffic, spawned into an area you draw yourself.

- **Zone tool.** Press **F8** in the mission editor for the *Civilian traffic* panel, then
  **Draw zone**. That hands the mouse to the game's own polygon tool (the one the editor uses for
  map drawing): left-click adds a point, middle-click finishes, points snap to markers. The mod
  reads the finished polygon from that state instead of reimplementing map input.
- **Min/max for ships and planes.** Four number fields, per type: the mod rolls a count inside
  your range each time you press **Spawn traffic**.
- **Variety.** Each generate creates 1–6 *lanes* — one lane is one spawn point plus one course —
  and every lane gets its own random slice of the civilian unit classes in your install, read from
  `UnitMetadataProvider.UnitData` (every class flagged `_isCivilian`). So one lane can be
  container ships and the next fishing boats, and the class list follows whatever units you have.
- **Real civilian traffic.** Routes are created as the game's own `SeaPower.CivilianRoute` objects
  and appended to `MissionManager._civilianRoutes`, which is the list the mission file is written
  from, so **saving the mission keeps them** and playing it spawns them. No custom file format.
- **Remove generated** takes the routes this session created back out again.
- New `[CivilianTraffic]` config section: panel visibility, hotkey, default numbers, lane count
  and classes per lane.
- New reference: `Noesis.NoesisGUI` (only because the editor's polygon state is declared there;
  the mod never draws through Noesis), plus optional Unity module references for IMGUI input.

See [docs/civilian-traffic.md](docs/civilian-traffic.md) for the mission-file format and the
verified API details.

## 0.5.1 — reference UnityEngine

Build fix for 0.5.0: `BaseUnityPlugin` derives from `MonoBehaviour`, which lives in the legacy
monolithic `UnityEngine.dll`, not in `UnityEngine.CoreModule.dll`. Only the latter was referenced, so
the BepInEx entry point failed with `CS0012: The type 'MonoBehaviour' is defined in an assembly that
is not referenced`.

- Added the `UnityEngine` reference (game's `Managed\UnityEngine.dll`), plus a build-time check with
  a readable message if it is missing.
- Verified against BepInEx 5.4.23.2's source that `BaseUnityPlugin : MonoBehaviour`, that it declares
  no `Awake` (so this plugin's `Awake` is not a hiding override), and that it requires `[BepInPlugin]`
  on the class, which is present.

## 0.5.0 — load as a BepInEx plugin

Confirmed from the user's game log: AnchorChain logged `Loaded assembly SeaPowerAdvanced,
Version=0.4.0.0` but never `Loaded plugin Sea Power Advanced`, no entry point output appeared and the
mod's own log file was never created. The same log shows **no** `Loaded plugin` line for any of the
other 28 AnchorChain mods either, so AnchorChain's trigger loop is not starting mods on that install.

Rather than depend on it:

- The DLL is now also a native BepInEx plugin (`[BepInPlugin]`, `BaseUnityPlugin.Awake`), and
  `-Install` puts it in `BepInEx\plugins\SeaPowerAdvanced\`, where BepInEx itself loads it. BepInEx's
  startup log then shows `Loading [Sea Power Advanced 0.5.0]`.
- Both entry points call one idempotent `Boot.Run`, so a double installation cannot patch anything
  twice, and the AnchorChain route still works where that loader functions.
- `Boot.Run` writes the log file before anything else, so the file's existence is proof the code
  ran; it prints the entry point, assembly path, log path, resolved settings and each patch target.
- The installer warns when a second copy is still present in `StreamingAssets`, which would be
  loaded twice.
- Docs updated: no mod-manager checkbox on the BepInEx route, and how to verify both ways.

## 0.4.0 — the map menu is a different code path

Reported: right-clicking a ship showed Orders with only "Identify". The cause was found in the
assembly: a unit's map right-click is built by `MapUnitViewModel.GetContextMenuItems()`, which
constructs its own list (it only calls `get_Unit`, `get_Identified`, `get_Value` and three `Add`s)
and never delegates to `ObjectBaseViewModel`. Patching only `ObjectBaseViewModel` therefore never
touched the menu a player actually uses.

- Patches **both** menu surfaces: `MapUnitViewModel.GetContextMenuItems` (map right-click, taking the
  unit from `MapUnitViewModel.Unit.BaseObject`) and `ObjectBaseViewModel.GetContextMenuItems`.
- `TryAdd` is idempotent (skips if a `Weapon hatches` entry is already present), so a shared
  collection cannot produce duplicates.
- Each surface logs `Context menu patch is live (map menu)` / `(unit menu)` the first time it runs,
  which distinguishes "patch never called" from "patch called and declined".
- Logs to its **own file** as well as BepInEx: `StreamingAssets\SeaPowerAdvanced\seapoweradvanced.log`.
  "Is the mod running at all?" is now answerable without reading another mod's log.
- The entry point announces itself: `=== Sea Power Advanced 0.4.0 entry point reached ===`.

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
