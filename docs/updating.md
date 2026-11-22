# Updating and uninstalling

Two batch files, both of them the "other direction" of `build.cmd`:

| File | What it is for |
|---|---|
| `update.cmd` | Bring an installation that is **already in the game folder** up to the version in this folder, doing only the changes that version still needs - and write down what it did in `update.txt` |
| `uninstall.cmd` | Take the mod **back out** of the game folder, keeping a copy of everything it removed |

Neither one uses PowerShell, needs admin rights, or asks a question it cannot answer itself.

```
update.cmd                 find the version, apply what is missing, rebuild, verify
update.cmd check           say what would be done, change nothing
update.cmd nobuild         apply the file changes from dist\ without compiling
update.cmd force           rebuild and reinstall even if already current
update.cmd selftest        check the version maths on this PC, touch nothing

uninstall.cmd              remove the mod, keep the settings file
uninstall.cmd check        say what would be removed, change nothing
uninstall.cmd clean        remove the mod and the settings file as well
```

---

## 1. Why `update.cmd` exists

`build.cmd` assumes it is starting from nothing: it finds the game, installs BepInEx if it has to,
compiles, copies two files in, and stops. That is right for a first install and wasteful for the
seventh.

An update is a different question - **what is already in the game folder, and what does that version
still need?** - and the changelog is the list of answers. So `update.cmd`:

1. finds the game folder the same way `build.cmd` does;
2. reads the version that is actually installed;
3. walks the changelog from that version to this one and does only what applies;
4. runs `build.cmd install nopause`, so there is one compiler and one install routine in the
   project, not two;
5. verifies the result and copies `update.txt` into the mod folder as well as next to the script.

## 2. How the installed version is read

In this order, because each one knows it more directly than the next:

| Source | Where | When it is there |
|---|---|---|
| `version.txt` | `BepInEx\plugins\SeaPowerAdvanced\version.txt` | Every install made by this build of `build.cmd`. The most reliable: it says what was *installed*, whether or not the game has been started since |
| The mod's own log | `...\seapoweradvanced.log`, the `================ Sea Power Advanced 0.24.0 ================` banner | Every install that has been started at least once. The last banner is the build that actually ran |
| BepInEx's log | `BepInEx\LogOutput.log`, `Loading [Sea Power Advanced 0.24.0]` | Any install the game has loaded |
| nothing | - | A copy put in by hand, or an install from before any of this existed. Treated as **0.1.0** and every change is checked, because each one looks for the files it would alter and skips when they are not there |

`version.txt` did not exist before this change, so an older install falls through to the two logs,
which is why both are still read.

The version *in this folder* comes from `src/SeaPowerAdvanced/ModInfo.cs`, with `CHANGELOG.md`'s
newest heading as the fallback - the same number written twice, for the assembly and for people.

## 3. What it changes, release by release

Everything below is taken from `CHANGELOG.md`. Only the rows with a file action touch the disk;
the rest are notes written into `update.txt` so the report explains the version jump rather than
just announcing it.

| Since | Release | On disk | What `update.cmd` does |
|---|---|---|---|
| < 0.5.0 | the DLL moved to `BepInEx\plugins\SeaPowerAdvanced` | `Sea Power_Data\StreamingAssets\SeaPowerAdvanced\` may still hold an old copy | **Moves it**: copies it to `uninstalled\legacy-StreamingAssets-<date>\` and deletes the folder. Two copies are loaded twice |
| < 0.10.0 | offensive ECM | nothing | note only |
| < 0.15.1 | civilian traffic removed | nothing - it never wrote a file | note only |
| < 0.17.1 | the ECM cones draw | nothing - all in the DLL | note only |
| < 0.18.0 | the missile shield removed | nothing - it never wrote a file | note only |
| < 0.18.1 | jam ranges became 150 nm | a settings file may still say `JamRangeNm = 40` / `JamAirRangeNm = 90` | **Reports them.** The mod moves the two old defaults to 150 itself when it starts; anything you chose yourself is left alone |
| < 0.18.3 | the dive stops detonating the missile | nothing | note only |
| < 0.19.0 | the dive starts at 10 nm and the game removes the missile | a settings file may still say `EcmDiveNm = 2` | **Reports it.** The mod moves exactly `2` to `10` at load. `EcmDiveNm = 0` means "you switched the dive off", and that is left exactly as it is |
| < 0.20.0 | the dive is timed, the nose stops at 90 degrees, the flight is ended through the game's own routes | nothing - all in the DLL | note only |
| < 0.21.0 | the dive uses the game's own lock-loss state and never nulls a reference | nothing - all in the DLL | note only |
| < 0.22.0 | the dive is limited by guidance channels, an engagement interval, a reaction delay and a radar-seeker filter | nothing - all in the DLL. **New settings appear with defaults** and are read on the first start | note only |
| < 0.22.1 | the shipped dive limits became 3 channels / 2 s / 1 s | a settings file may still hold `EcmDiveChannels = 2`, `EcmDiveIntervalSeconds = 4`, `EcmDiveReactionSeconds = 2` | **Reports them.** The mod moves exactly those old figures to 3 / 2 / 1 at load; anything you chose yourself is left alone |
| < 0.23.0 | offensive ECM's sensor hold is limited by a task-force channel budget and a hold window with a recovery period | nothing - all in the DLL. **New settings appear with defaults** (`EcmJamChannels` 2, `EcmJamHoldSeconds` 60, `EcmJamRecoverySeconds` 15) and are read on the first start | note only |
| < 0.24.0 | the Sensors menu is two submenus (*Radars and sonar*, *ECM*) instead of twenty-odd entries; every multiplayer client path is declared in one table and reports whether it was tried; **and ECM is only offered on a hull whose own sensor list carries a jammer** | nothing - all in the DLL, and no setting changes meaning | note only. **A ship that was armed for defensive ECM and has no jammer is disarmed by the mod itself**, and its ECM entries disappear from the menu |
| < 0.24.1 | a build fix with no behaviour change (the 0.24.0 DLL did not compile) | nothing | nothing beyond the DLL | note only |
| < 0.26.0 | **new: the laser rangefinder.** A hull with an optical or laser director in its own sensor list can look at contacts the force already holds, with its radars silent | new settings (`LidarEnabled` on, `LidarRangeNm` 12, `LidarArcDegrees` 90, `LidarSeconds` 4) | nothing beyond the DLL | `update.cmd` says so. **It never looks for a contact the force has not already seen**, and most hulls have no director - `lidar.txt` shows what each one really carries |
| < 0.25.0 | **new: the force datalink** between a co-op host and guest. In a mission with a side per player the two pictures are joined: contacts this force holds are handed to the other, position only | a new setting appears with its default (`CoopDatalink` true, `CoopDatalinkSeconds` 3) | nothing beyond the DLL | `update.cmd` says so. **Nothing is shared in a PvP session, and nothing outside one** - the switch is in *Sensors and EMCON → Datalink* |
| < 0.24.2 | *Defensive ECM for the whole task force* is a switch: the same entry disarms | nothing - all in the DLL, and no setting changes meaning | nothing beyond the DLL | `update.cmd` says so. **A force left armed for defensive ECM stays armed** - the new entry will read *on for N of M* and clears it in one click |
| every release | - | `SeaPowerAdvanced.dll`, `_info.ini` | **Replaces them**, after copying the old DLL to `uninstalled\before-<version>_<date>\` |

## 4. The settings file is never rewritten - on purpose

`BepInEx\config\io.github.bomberdeer22.seapoweradvanced.cfg` holds your choices, and BepInEx writes
a `## description` block above every key. Rewriting that file from a batch script means echoing lines
that contain `!`, `&`, `(` and `)` back through `cmd.exe`, and one mangled line costs you every
setting in the file.

So the script only ever **reads** it, to tell you which figures are about to move, and the mod does
the moving: `ModSettings.Bind` compares the stored value with the old default and replaces only an
exact match. That is the same rule, applied in the one place that has a real parser.

## 5. `update.txt`

Written every run, next to the script, and copied into the mod folder at the end so `diagnose.cmd`
picks it up. It records:

- the mode it ran in, and the date and time;
- the game folder;
- the installed version **and which of the three sources said so**;
- the version in this folder;
- the decision: older, already current, unknown, or newer (a newer install stops the update - that
  would be a downgrade);
- every migration, marked `APPLY`, `WOULD REMOVE`, `ok` or `NOTE`, with the reason;
- what the update gains you, from the changelog headings;
- the backup folder;
- the version found in the game folder afterwards, and whether it matches this folder.

`update.cmd selftest` is worth running once. It feeds the version comparison and the version-shape
check twelve cases with known answers - `0.9.0` older than `0.10.0`, `0.18.9` older than `0.18.10`,
`0.5` equal to `0.5.0`, `1.2` not a version - because those two routines decide everything else and
a wrong answer to either is silent. It touches nothing.

## 6. Rolling back

Nothing is destroyed. Anything replaced sits in `uninstalled\`:

```
uninstalled\
  before-0.18.3_31-10-2026_14-22-08-44\
    SeaPowerAdvanced.dll          the build that was replaced
    _info.ini
    version.txt
  legacy-StreamingAssets_31-10-2026_14-22-08-44\
    SeaPowerAdvanced.dll          the pre-0.5.0 copy, moved out of the game
```

To go back: close the game, copy `SeaPowerAdvanced.dll` and `_info.ini` from the backup folder into
`BepInEx\plugins\SeaPowerAdvanced\`, then start the game. Delete `uninstalled\` when you are happy.

## 7. Uninstalling

`uninstall.cmd` removes, in this order:

1. `BepInEx\plugins\SeaPowerAdvanced\` - the DLL, `_info.ini`, `version.txt` and every report the mod
   wrote (`capabilities.txt`, `ecm-report.txt`, `ecm-cone.txt`, `offensive-ecm.txt`, `ecm-dive.txt`,
   `multiplayer.txt`, `menu-report.txt`, `hatch-report.txt`, `seapoweradvanced.log`, `update.txt`);
2. `Sea Power_Data\StreamingAssets\SeaPowerAdvanced\`, if an old release left one there;
3. the settings file - **only** with `uninstall.cmd clean`.

Everything is copied to `uninstalled\` before it is deleted, so a mistake is a copy back.

It refuses to run while Sea Power is open: a loaded DLL cannot be deleted, and a half-removed mod is
worse than an installed one. If a file is still locked anyway it says **LEFT BEHIND** and lists what
is there, rather than reporting a success it did not have.

**Left alone, deliberately:** `BepInEx\`, `winhttp.dll` and `doorstop_config.ini`. Other mods need
them, and removing the loader to remove one plugin would be a surprise. Unsubscribe from Anchor Chain
and delete those three by hand if that is what you want.

## 8. Limits, stated plainly

- **It updates an installation it can find.** The game folder is found through
  `Directory.Build.local.props`, Steam's registry entry, then the usual library names. A copy
  installed somewhere else entirely has to be removed by hand - `uninstall.cmd` prints where to look.
- **It rebuilds from this folder.** `update.cmd` is not a downloader: the version it updates *to* is
  the one in `src\SeaPowerAdvanced`. That is why a newer install in the game folder stops it.
- **It cannot compile without the game's assemblies.** `nobuild` exists for exactly that: it copies
  the package `dist\SeaPowerAdvanced\` that a working machine produced.
- **Only the mod's own files.** It will not touch BepInEx, Anchor Chain, or another mod's folder.
- **Multiplayer is unchanged by any of this.** The updater edits files on disk while the game is
  closed; nothing in it runs inside a session.
