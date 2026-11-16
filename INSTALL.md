# Installing Sea Power Advanced

A complete walkthrough from a stock Sea Power install. Allow 20 minutes the first time; most of it
is the one-time .NET SDK download in step 2.

You do this once. After that, updating the mod is step 2 again (`build.cmd`) and a game restart.

---

## Before you start

- **Sea Power** installed through Steam.
- Windows. (Linux/Steam Proton works too — see [Linux notes](#linux--steam-proton) at the end.)
- **No installer and no admin rights are needed.** The .NET SDK the build uses is downloaded into
  this repo folder in step 2, not into Windows.

Two things get installed around the mod, and **`build.cmd` installs both by itself** — there is
nothing to download by hand:

| Thing | What it is | Why |
|---|---|---|
| **BepInEx** | Unity's mod loader | Loads code mods at all. |
| **Anchor Chain preloader** | Bootstrap that puts BepInEx into Sea Power | Without it the game never loads BepInEx. |

They ship together as `ACPreloader.zip` on Anchor Chain's releases page, and step 2 fetches it
straight into the game folder. **Anchor Chain's own Workshop entry is optional** — this mod loads
as a plain BepInEx plugin and does not need it.

---

## Step 1 — Nothing to download

Earlier versions of this guide asked you to download `ACPreloader.zip`, extract it, copy
`winhttp.dll`, `doorstop_config.ini` and the `BepInEx` folder into the game by hand, then subscribe
to Anchor Chain on the Workshop.

**Skip all of that.** `build.cmd` does it in step 2: when those files are not in the game folder it
fetches `ACPreloader.zip` from Anchor Chain's latest release and unpacks it in the right place. It
checks for the files first, so running it again does nothing.

The Workshop subscription is **not needed** either. The mod is a BepInEx plugin: BepInEx finds it in
`BepInEx\plugins\SeaPowerAdvanced\` and loads it. Anchor Chain is a second, alternative loader — if
`AnchorChain.dll` happens to be installed, it is supported and used, but nothing breaks without it.

> If you would rather install the preloader yourself, `INSTALL.md` in older releases described the
> manual copy, and it still works. Just have `winhttp.dll`, `doorstop_config.ini` and `BepInEx\`
> next to `Sea Power.exe` before running `build.cmd`.

---

## Step 2 — Build the mod

The mod is compiled against **your** game's own DLLs, so it always matches your Sea Power version.
There is no prebuilt download; the build takes about two minutes and downloads a compiler the first
time.

### 2a. Double-click `build.cmd`

That is the whole step. It:

1. finds a .NET SDK — either one already installed, or it downloads a portable one into `.dotnet\`
   (about 220 MB, once, no installer, no admin rights),
2. finds your game folder by itself (Steam registry, then the usual library locations),
3. **installs BepInEx and the Anchor Chain preloader into the game** if they are not there yet
   (about 650 KB),
4. checks the source for the mistakes that have actually broken builds — a missing `using`, a type
   or member that no longer exists, a game enum named in code, an accessor called by name (skipped
   if you have no Python: the build does not need it),
5. compiles the mod against **your** `Seapower-Scripts.dll`,
6. copies it into `BepInEx\plugins\SeaPowerAdvanced\`.

Expected output ends with:

```
[3/6] Checking BepInEx in the game folder...
      already installed - nothing to do.
[4/6] Checking the source before building...
      clean: 43 file(s), no missing namespaces, UnitRecord consistent
[5/6] Building the mod...
      build succeeded.
[6/6] Packaging...
      package ready: ...\dist\SeaPowerAdvanced
[install] Copying into the game...
       copied to C:\Program Files (x86)\Steam\steamapps\common\Sea Power\BepInEx\plugins\SeaPowerAdvanced
```

On a PC where the first run has to install BepInEx, step 3 of the list reads instead:

```
[3/6] Checking BepInEx in the game folder...
      not installed yet - fetching it into the game folder.
      fetching https://github.com/SeaPower-Modders/AnchorChain/releases/latest/download/ACPreloader.zip
      extracting into the game folder...
      BepInEx and the Anchor Chain preloader are now in place.
```

It **pauses** when it finishes, so the window stays open. Two files are written next to it:

| File | What it is |
|---|---|
| `build-log.txt` | Which steps ran, and the result of each. Survives the window closing. |
| `build-output.txt` | The compiler's full output. This is the file to send if the build fails. |
| `check-source.txt` | Step 4's findings. A problem here is a warning: the build still runs, and the compiler stays the judge. |

Step 4 can be run on its own at any time by double-clicking **`check-source.cmd`** — it takes about
a second, writes `check-source.txt`, and pauses. Use it after editing any `.cs` file instead of
waiting two minutes to be told a `using` is missing.

`build.cmd` uses **no PowerShell at all** — it downloads with `curl` and unpacks with `tar`, both of
which are part of Windows 10 and 11. That matters on a machine where PowerShell is restricted: the
errors `is not digitally signed`, or a PowerShell window that closes itself, cannot happen here.

To build without installing: `build.cmd dist`. To install without rebuilding, just run it again.

### If it could not find the game

It writes `Directory.Build.local.props` for you and prints the paths it needs. Open that file in
Notepad, set the two paths, save, and run `build.cmd` again:

```xml
<SeaPowerDir>C:\Program Files (x86)\Steam\steamapps\common\Sea Power</SeaPowerDir>
<AnchorChainDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\1286220\3380210757</AnchorChainDir>
```

`AnchorChainDir` is the Workshop folder that contains `AnchorChain.dll` — its number
(`3380210757`) is the same on every PC.

### The PowerShell route (optional)

If you prefer PowerShell and it works on your machine:

```powershell
.\tools\build-portable.ps1 -Install
```

It does the same job. **If PowerShell is restricted on your PC, this is the wrong route** — use
`build.cmd`, which never invokes it.

### "is not digitally signed" — why it happens, and what to do

Windows marks files downloaded from the internet, and PowerShell's **execution policy** then refuses
to run `.ps1` files that are not signed. It is not a problem with the mod, and nothing about your PC
needs changing.

The only reliable fix on a locked-down machine is to not use a PowerShell script at all:

**Use `build.cmd`.** Batch files are not covered by the execution policy, so it works even when
`MachinePolicy` or `UserPolicy` is set to `AllSigned` by IT, where
`-ExecutionPolicy Bypass` is ignored and `Unblock-File` changes nothing.

---

## Step 3 — Start the game

`-Install` puts the DLL in `BepInEx\plugins\SeaPowerAdvanced\`, so **BepInEx loads it directly**.
There is no mod-manager checkbox to tick on this route: the plugin is present, so it runs.

If you previously installed it into `StreamingAssets\SeaPowerAdvanced\`, **delete that folder**. A
copy in both places is loaded twice; the installer prints a warning when it sees one.

Why this route: a copy in `StreamingAssets` is only started if AnchorChain's own loader recognises
and triggers it, and on some installs that trigger never happens — the game log shows the assembly
being loaded but never says `Loaded plugin Sea Power Advanced`. The BepInEx route has no such step.

## Step 4 — Check it loaded

Two things to look at.

**1. BepInEx's plugin list**, near the top of `BepInEx\LogOutput.log`. It must say:

```
[Info   :   BepInEx] Loading [Sea Power Advanced 0.12.0]
```

**2. The mod's own log**, `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log`. This file only
exists if the code actually ran, which makes "did it load?" unambiguous:

```
================ Sea Power Advanced 0.12.0 ================
Entry point:  BepInEx plugin
Settings: enabled=True includeGuns=False allowEnemyUnits=False showWhenNoMounts=True
Patched MapUnitViewModel.GetContextMenuItems.
Patched ObjectBaseViewModel.GetContextMenuItems.
Hatch menu installed.
Ready. Right-click a ship > Orders > Weapon hatches.
```

If you see `no patches installed` instead, the box in the Mod Manager is not ticked (or the game was
not restarted). If you see nothing at all, Anchor Chain is not loading the DLL — re-check step 1.

---

## Step 5 — Use it

1. Start a mission with a ship that has launchers — a Ticonderoga, Arleigh Burke, Spruance or Kirov
   is ideal.
2. **Right-click the ship → Orders → Weapon hatches.**
3. Pick a mount, then click a cell. It opens, the check mark appears, and the log records it:

   ```
   [Info] Sea Power Advanced: Open Ticonderoga / Cell 7: openHatches -> started [open=yes, hatches=1]
   ```

4. Click the same cell again to close it. **Open all hatches** does the ship in one click.

Useful to know:

- The menu lists **cells**, not individual lids. A cell with several hatch models opens them
  together — that is what the game's own `openHatches()` does.
- The game still owns the hatches during a launch; if the ship fires, it may reopen or close what it
  needs and your last manual order can be overridden. The check marks always show the truth.
- **Log hatch report** writes `hatch-report.txt` next to the DLL, in
  `BepInEx\plugins\SeaPowerAdvanced\` — that is the file to send if something does not move.

---

## Updating or rebuilding

```bat
build.cmd
```

then **fully restart** the game. Close Sea Power before rebuilding, or the DLL is locked.

## Uninstalling

1. Delete `BepInEx\plugins\SeaPowerAdvanced\`.
2. If an older release was installed into `Sea Power_Data\StreamingAssets\SeaPowerAdvanced\`,
   delete that folder too — leaving it means the mod loads twice.
3. Optional cleanup: delete `.dotnet\`, `dist\` and `BepInEx\config\io.github.bomberdeer22.seapoweradvanced.cfg`.

To remove Anchor Chain itself, unsubscribe on Steam and delete `winhttp.dll`,
`doorstop_config.ini` and the `BepInEx` folder from the game directory.

---

## If something goes wrong

**Run `diagnose.cmd`** (double-click it in the repo folder). It writes one file,
`SeaPowerAdvanced-diagnose.txt`, containing the game folder it found, which files are present, the
game's own `LogOutput.log`, the mod's log, and a check of the analysis tools. **Send that file** —
it answers almost every "it doesn't work" question without anyone needing to copy console text.

It uses only `cmd.exe`: no PowerShell, no admin rights, nothing changed.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `dotnet : The term ... is not recognized` | You ran `build.ps1` directly without a .NET SDK. Use `build.cmd` instead — it fetches one. |
| `Sea Power not found. Set SeaPowerDir in Directory.Build.local.props` | You skipped 3a, or the path has a typo. It must contain `Sea Power_Data\Managed`. |
| `AnchorChain.dll not found` | Only a note now: the build continues with the BepInEx-only plugin. Set `AnchorChainDir` if you want the Anchor Chain entry point compiled in. |
| `error CS0117: ... does not contain a definition` | The game's API moved in an update. Send me the full error — it names the member exactly. |
| `.ps1 cannot be loaded ... is not digitally signed` | Execution policy. **Use `build.cmd`** — it runs no PowerShell at all. See [why](#is-not-digitally-signed--why-it-happens-and-what-to-do). |
| The build window opens and closes instantly | Fixed: `build.cmd` now pauses. If it still happens, open `build-log.txt` — it records every step, and `build-output.txt` has the compiler's own errors. |
| PowerShell itself crashes or closes when building | That is why `build.cmd` exists. It uses `curl` and `tar` (both built into Windows) instead of any script host. |
| Build succeeds, but no `Weapon hatches` in game | Read `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log`. No file = the plugin never ran (check that step 5 shows `Loading [Sea Power Advanced ...]`). File present = the code ran, so send it. |
| `The Anchor Chain preloader is not running!` | Step 1: `winhttp.dll` + `doorstop_config.ini` + `BepInEx` must be **in the game folder**. |
| Menu entry appears but the submenu is empty | That mount has no hatch animation of its own. Run **Log hatch report** and send it. |
| Click does nothing, log says `cell has no open animation` | Real result: that cell cannot be opened in this game build. Note the ship class. |
| Hatch opens then shuts again immediately | The game's launch/reload cycle took it back. Note what the ship was doing and send the log. |

### Linux / Steam Proton

Build anywhere (the same `./tools/build-portable.sh --install` works, with `SEAPOWER_DIR` set), or
build on another machine and copy `dist/SeaPowerAdvanced/` across. Then copy that folder into
`.../steamapps/common/Sea Power/BepInEx/plugins/`.

For BepInEx under Proton you may also need the standard `winhttp` override:

```bash
protontricks 1286220 --force winecfg   # Libraries → add "winhttp" as native,builtin
```

---

## What was installed, at a glance

```
Sea Power\                                  <- game folder
├── winhttp.dll                             <- step 2 (installed automatically)
├── doorstop_config.ini                     <- step 2 (installed automatically)
└── BepInEx\
    ├── LogOutput.log                       <- where to look when something is wrong
    ├── config\io.github.bomberdeer22.seapoweradvanced.cfg   <- settings
    └── plugins\
        └── SeaPowerAdvanced\               <- step 2 (the mod)
            ├── SeaPowerAdvanced.dll        <- loaded by BepInEx at startup
            ├── _info.ini                   <- name and description
            └── seapoweradvanced.log        <- the mod's own log
```
