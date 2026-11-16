Plain-text copy of README.md, kept in step with it by tools/md2txt.py.
The Markdown version is the one that renders on GitHub; this one is for Notepad.

==============================================================================
SEA POWER ADVANCED
==============================================================================

An advanced mod for Sea Power: Naval Combat in the Missile Age, built as an
Anchor Chain (https://github.com/SeaPower-Modders/AnchorChain) code mod and
loadable as a plain BepInEx plugin.

Version 0.6.0  -  two features:

1. Manual hatch control

Right-click one of your ships and open Orders -> Weapon hatches to open or
close individual silo cells, VLS hatches, box-launcher covers and torpedo tube
shutters without launching a weapon.

    Orders
      > Weapon hatches
          Mk 41 VLS  (32)
            > Cell 1            [x]
              Cell 2            [ ]
              ...
              Open all cells
              Close all cells
          Mk 141 Harpoon
            > Hatch             [ ]
          Open all hatches
          Close all hatches
          Log hatch report

2. Civilian traffic zones (mission editor)

Press F8 in the mission editor for a small panel: draw a polygon on the map,
type min/max ships and min/max planes, press Spawn traffic. The mod creates
real civilian routes in that area, each lane drawing from your install's own
civilian unit classes for variety, and they are saved with the mission.

    Civilian traffic
      Zone: 6 points  ~14 800 nm2      [Draw zone]
      Ships (vessels, alive at once)    min [2]  max [6]
      Planes (aircraft, alive at once)  min [0]  max [2]
      [Spawn traffic]  [Remove generated]
      Classes: 23 ship class(es), 9 aircraft class(es)   [Refresh]

How it works, and the verified mission-file format: docs/civilian-traffic.md.
Hatch API: docs/hatch-api.md.

Something not working? Run diagnose.cmd  -  one double-click, one text file to
send. Plain-text copies of this file and of the install guide are included:
README.txt, INSTALL.txt.

------------------------------------------------------------------------------

REQUIREMENTS
------------

  Sea Power 0.8.x
      Why: Built against the 0.8 generation of Seapower-Scripts.dll.
      Who installs it: You

  BepInEx + the Anchor Chain preloader
      Why: Without them the game loads no code mods at all.
      Who installs it: build.cmd  -  downloaded into the game folder for you

  Anchor Chain itself (Workshop)
      Why: Optional. A second loader for StreamingAssets mods.
      Who installs it: Nobody, unless you want it

The only thing you provide is a Sea Power install. build.cmd fetches the
compiler and the loader by itself, straight into the folders the game expects.

INSTALL
-------

Full walkthrough: INSTALL.md  -  every step, from a stock install to a working
Weapon hatches menu, including the two loader pieces and how to confirm it
loaded.

The short version, once BepInEx + Anchor Chain are in place:

    build.cmd

Double-click it. It finds your game, fetches a portable .NET SDK if you have
none, installs BepInEx and the Anchor Chain preloader into the game folder if
they are missing, builds the mod against your own game DLLs and copies it into
BepInEx\plugins\SeaPowerAdvanced\. It pauses at the end, and writes build-
log.txt plus build-output.txt so a closing window never loses an error.

It uses no PowerShell  -  curl and tar, both built into Windows, do the
fetching and unpacking. That makes it the route to use if PowerShell is
restricted on your PC (is not digitally signed, or a PowerShell window that
closes itself).

Prefer PowerShell? tools\build-portable.ps1 -Install does the same job when it
is permitted.

The DLL goes into BepInEx\plugins\SeaPowerAdvanced\, so BepInEx loads it
directly  -  there is no mod-manager checkbox on this route. BepInEx's log
then shows Loading [Sea Power Advanced 0.6.0], and the mod writes its own
seapoweradvanced.log next to the DLL.

USE IT
------

Hatch control

  1. Right-click one of your ships.
  2. Orders -> Weapon hatches.
  3. Pick a mount, then a cell. The check mark shows whether that cell's
     hatches are open right now, and each click logs exactly what happened:
     Sea Power Advanced: Open Ticonderoga / Cell 7: openHatches -> started
     [open=yes, hatches=1]

Open all hatches does the whole ship. Because the list is rebuilt from live
game state, the check marks stay truthful even when the game opens a hatch
itself while firing.

Civilian traffic

  1. Open the mission editor on a mission (new or existing).
  2. Press F8. The Civilian traffic panel appears; drag it by its title bar.
  3. Draw zone -> left-click around the area on the map -> middle-click to
     finish, or press Finish.
  4. Set the four numbers and press Spawn traffic.
  5. Save the mission. The routes are part of it now and spawn when the
     mission is played.

Remove generated takes back the routes this session created, in case you want
a different roll.

Full walkthrough: docs/usage.md.

HOW IT WORKS
------------

The game has the code for this already; it just never exposed it. Verified
from Seapower-Scripts.dll (types, signatures, access flags and optional-
parameter flags  -  see docs/hatch-api.md):

  * each mount is a WeaponSystem whose _containers are its cells,
  * each cell is a WeaponContainer that owns its hatch models (_hatches) and
    can move them: csharp cell.openHatches();          // returns bool
    cell.closeHatches();         // returns bool cell._areHatchesOpen
    // live state, used for the check mark  These are the same methods the
    game's launch sequence calls, so nothing is faked or simulated.
  * mounts with one cover instead of per-cell hatches use
    WeaponSystem.openSystem() / closeSystem(delay).

Two details that matter in practice:

  * After a launch the game parks a pending auto-close on the cell
    (_triggerCloseAnimation). A manual order clears it, otherwise the hatch
    would shut again a moment later.
  * WeaponContainer.update() keeps running the animation and its sound, so
    opening and closing look and sound exactly like the game's own.

STATUS AND LIMITS
-----------------

  * Per cell, not per lid. Granularity is one container. A cell with four
    hatch models (_numberOfHatchesPerContainers) opens all four together  -
    that is what the game's own openHatches() does.
  * The game still owns the hatches. If the ship launches, the launch sequence
    opens and closes what it needs and may override your last manual order.
    That is expected; the check marks will reflect the result.
  * Not tested in game yet. The API is read from the actual shipping assembly
    rather than guessed, but this build has not been flown. Log hatch report
    and LogOutput.log are there to make the first session conclusive.
  * Gun and CIWS mounts have no silo hatches and are hidden unless
    General/IncludeGuns is on.

CONFIGURATION
-------------

BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg, created on first
run.

  General / Enabled
      Default: true
      Meaning: Master switch.

  General / IncludeGuns
      Default: false
      Meaning: Also list guns and CIWS (no silo hatch; only a mount cover if
        any).

  General / AllowEnemyUnits
      Default: false
      Meaning: Show the menu on enemy/neutral ships too.

  Menu / ShowDiagnosticsItem
      Default: true
      Meaning: Adds "Log hatch report".

  Diagnostics / VerboseLog
      Default: true
      Meaning: Log every hatch action with the game method used and its
        result.

  CivilianTraffic / ShowPanel
      Default: true
      Meaning: Show the panel in the mission editor.

  CivilianTraffic / ToggleKey
      Default: F8
      Meaning: Key that toggles the panel (a UnityEngine.KeyCode name).

  CivilianTraffic / MinShips / MaxShips
      Default: 2 / 6
      Meaning: Default numbers for vessels; the panel remembers what you
        last used.

  CivilianTraffic / MinPlanes / MaxPlanes
      Default: 0 / 2
      Meaning: Default numbers for aircraft.

  CivilianTraffic / MaxLanes
      Default: 4
      Meaning: How many separate traffic lanes one generate may make (1-6).
        More lanes means traffic from more places.

  CivilianTraffic / ClassesPerRoute
      Default: 6
      Meaning: How many different civilian classes one lane picks from
        (1-40).

TROUBLESHOOTING
---------------

  No "Weapon hatches" entry
      Check: Ticked in the mod manager? Game restarted (code loads once per
        launch)? Does the ship actually have launchers?

  Entry present, submenu empty for a ship
      Check: That mount may have no hatch models (some kitbashes). Log hatch
        report shows hatches=0 per cell.

  Click does nothing
      Check: LogOutput.log names the method and its result, e.g. openHatches
        -> no change. Attach that line plus hatch-report.txt.

  Hatch opens then closes again
      Check: The game's own launch/reload cycle re-took control. Note what
        the ship was doing.

  "Orders" not found in the log
      Check: The localised menu label differs. Send the log.

  Game CTD on startup
      Check: Remove the mod folder and send LogOutput.log; every path in the
        mod is guarded, so a crash here matters.

hatch-report.txt (mod folder, written by the menu item) lists per ship: each
mount, its cells, hatch counts, live open state, ammo and pending auto-close
flags.

BUILD FROM SOURCE
-----------------

The mod compiles against your game's DLLs, so the output matches your Sea
Power version.

Route A  -  nothing installed (portable SDK)

Downloads the official .NET SDK into .dotnet/ inside this repo (about 200 MB,
once), builds, and lays out the mod. No installer, no admin rights, no PATH or
registry changes; delete .dotnet/ to undo it.

    # Windows
    Copy-Item Directory.Build.local.props.example Directory.Build.local.props
    notepad Directory.Build.local.props          # set SeaPowerDir and AnchorChainDir
    .\tools\build-portable.ps1 -Install

    # Linux / Steam Proton
    cp Directory.Build.local.props.example Directory.Build.local.props
    $EDITOR Directory.Build.local.props
    ./tools/build-portable.sh --install

Route B  -  if you already have the .NET SDK

    .\build.ps1 -Install

    ./build.sh --install

  src/SeaPowerAdvanced/SeaPowerAdvanced.csproj
      Purpose: The build. References the game DLLs with Private=false.

  Directory.Build.props
      Purpose: Default Steam paths; overridden by
        Directory.Build.local.props.

  mod/_info.ini
      Purpose: Name and description shown in the mod manager.

  build.ps1 / build.sh
      Purpose: Build, then lay out dist/ (and optionally install).

  tools/build-portable.*
      Purpose: Fetch a portable SDK into .dotnet/ and run the build script.

  build.cmd
      Purpose: Build and install, as a batch file. No PowerShell, so policy
        cannot block it.

  check-features.cmd
      Purpose: Double-click: checks which planned features this build of the
        game can support.

  docs/usage.md
      Purpose: How to use the mod once it is installed.

  diagnose.cmd
      Purpose: One double-click: collects system, game folder, mod files and
        game logs into SeaPowerAdvanced-diagnose.txt. Send that file when
        something breaks.

  README.txt, INSTALL.txt
      Purpose: Plain-text copies of this file and the install guide, for
        Notepad. Regenerate with python tools/md2txt.py README.md README.txt.

  tools/analysis/
      Purpose: Read the game's API out of Seapower-Scripts.dll  -  types,
        signatures, access flags and who calls what. Self-tested against
        ILDasm. See tools/analysis/README.md.

  tools/read-metadata.py, tools/dump-game-api.ps1
      Purpose: Older, narrower metadata dumps (type and member names only).

Reference paths are never copied into StreamingAssets: Anchor Chain would load
the game's own DLLs a second time.

CHECKING THE API BEFORE WRITING CODE
------------------------------------

Every feature here is written against the shipped assembly rather than against
guesswork, because "does this member exist, what is its signature, and what
does the game call it from?" decides whether a feature is a five-line change
or impossible. That reader lives in tools/analysis (tools/analysis/README.md):

    check-features.cmd                      :: double-click: which planned features are buildable
    tools\analysis\survey.cmd              :: full API survey -> tools\analysis\analysis-report.txt
    tools\analysis\survey.cmd readiness    :: the same check from a command prompt
    tools\analysis\survey.cmd selftest     :: verify the reader still works

check-features.cmd is the one to double-click: it writes feature-readiness.txt
next to itself, saying feature by feature whether the game exposes what each
one needs (READY, PARTIAL or BLOCKED) and printing the member names it
matched, so a wrong verdict can be seen rather than believed. It needs Python
3 once; if that is missing it says so and needs nothing else. It reads
Directory.Build.local.props for the game path, so it works wherever Steam is
installed.

The next batch of features is triaged in docs/feature-plan.md: what depends on
what, what order to build it in, and which parts the game may not model at
all.

It reads metadata and IL only  -  nothing is loaded or run  -  and it also
answers one-off questions:

    python analyze.py member SeaPower.CivilianRoute
    python analyze.py find "_radarsActive" --callsites

LAYOUT
------

    src/SeaPowerAdvanced/
      Plugin.cs                 Anchor Chain entry point
      Menu/HatchMenu.cs         postfix on ObjectBaseViewModel.GetContextMenuItems -> Orders submenu
      Hatches/HatchBay.cs       reads _obp._weaponSystems -> _containers (_hatches per cell)
      Hatches/HatchControl.cs   open/close through WeaponContainer / WeaponSystem
      Diagnostics/ShipReport.cs per-ship report (hatch-report.txt)
      ModSettings.cs            BepInEx config
      ModActivation.cs          honours the mod-manager checkbox
      ModFolders.cs             mod folder + diagnostic file location
      Civilian/CivilianCatalogue.cs  civilian classes read from the game's unit database
      Civilian/TrafficZone.cs        polygon testing + random points/paths inside it
      Civilian/RouteFactory.cs       builds SeaPower.CivilianRoute objects (reflection, field-verified)
      Civilian/TrafficGenerator.cs   rolls counts, splits them into lanes, adds the routes
      Civilian/ZoneTool.cs           drives the game's own map polygon tool
      UI/TrafficPanel.cs             the IMGUI panel (F8 in the mission editor)
      ModInfo.cs, ModLog.cs     identity and logging

ROADMAP
-------

  * [x] Open and close individual silo hatches without launching
  * [ ] Per-cell salvo selection: open cells 1-8 only, remembered for the next
    launch
  * [ ] Hatch state in the bottom bar next to the weapon list
  * [ ] Pre-launch warm-up: open selected cells ahead of an order so the first
    missile leaves sooner
  * [ ] Radar/mast/sonar raising as an explicit order
  * [ ] Damage-control and flooding control per compartment

CREDITS
-------

Built on BepInEx, HarmonyX and Anchor Chain. Menu injection follows the
pattern used by public Sea Power mods (Bulk Identification Requests, Real Air
Combat Tactics). Not affiliated with Triassic Games; use at your own risk in
single player.
