# Installing Sea Power Advanced

A complete walkthrough from a stock Sea Power install. Allow 20 minutes the first time; most of it
is the one-time .NET SDK download in step 3.

You do this once. After that, updating the mod is step 3 again (`-Install`) and a game restart.

---

## Before you start

- **Sea Power** installed through Steam.
- Windows. (Linux/Steam Proton works too — see [Linux notes](#linux--steam-proton) at the end.)
- **No installer and no admin rights are needed.** The .NET SDK the build uses is downloaded into
  this repo folder in step 3, not into Windows.

Two things get installed around the mod:

| Thing | What it is | Why |
|---|---|---|
| **BepInEx** | Unity's mod loader | Loads code mods at all. |
| **Anchor Chain** | Sea Power's mod loader, on top of BepInEx | Finds and loads DLL mods from `StreamingAssets` and the Workshop. |

Both arrive together in step 1.

---

## Step 1 — Install the Anchor Chain preloader

This is the only part with a manual file copy. It installs BepInEx **and** the Anchor Chain
preloader in one go.

1. Open the Anchor Chain releases page: <https://github.com/SeaPower-Modders/AnchorChain/releases>
2. Under the newest release, download **`ACPreloader.zip`**.
3. Extract the zip into a folder in your Downloads.
4. In that folder you will see `winhttp.dll`, `doorstop_config.ini` and a `BepInEx` folder. Select
   **everything inside** the folder (Ctrl+A), copy it (Ctrl+C).
5. Open your game folder: in Steam, right-click **Sea Power → Manage → Browse local files**.
   It is usually `C:\Program Files (x86)\Steam\steamapps\common\Sea Power`.
6. Paste (Ctrl+V). Say yes if Windows asks about merging folders.
7. Check the game folder now contains **`winhttp.dll`**, **`doorstop_config.ini`** and **`BepInEx`**,
   next to `Sea Power.exe`. If yes, this step is done.
8. Start Sea Power once and quit it. This lets BepInEx generate its folders and log file.

## Step 2 — Subscribe to Anchor Chain

1. Open the Workshop page: <https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757>
2. Click **Subscribe**. Steam downloads it.
3. Start Sea Power, open the **Mod Manager**, and check that **Anchor Chain** is ticked.

If the Mod Manager shows *"The Anchor Chain preloader is not running!"*, step 1 did not copy the
files into the right folder — revisit it before going further.

> Where it landed: Steam puts Workshop items in
> `<steam>\steamapps\workshop\content\1286220\3380210757\`. That is where this project looks for
> `AnchorChain.dll` when building.

---

## Step 3 — Build the mod

The mod is compiled against **your** game's own DLLs, so it always matches your Sea Power version.
There is no prebuilt download; the build takes about two minutes and downloads a compiler the first
time.

### 3a. Tell the build where the game is

In the project folder (the one containing this file):

```powershell
Copy-Item Directory.Build.local.props.example Directory.Build.local.props
notepad Directory.Build.local.props
```

Set the two paths inside it:

```xml
<SeaPowerDir>C:\Program Files (x86)\Steam\steamapps\common\Sea Power</SeaPowerDir>
<AnchorChainDir>C:\Program Files (x86)\Steam\steamapps\workshop\content\1286220\3380210757</AnchorChainDir>
```

If your Steam library is on another drive, find the second path quickly with:

```powershell
Get-ChildItem -Path D:\,E:\ -Filter AnchorChain.dll -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty DirectoryName
```

The values shipped in the file are correct for a **default** Steam install, so if Steam is in the
default place you may not need to change anything — but keep the copy step, it is what tells the
build to use these paths.

### 3b. Build and install in one command

```powershell
.\tools\build-portable.ps1 -Install
```

If Windows blocks that script — `is not digitally signed` — skip to
[the fix below](#if-windows-blocks-the-script-is-not-digitally-signed) and use `build.cmd install`.

What it does:

1. sees there is no .NET SDK in `.dotnet\`, downloads one (about 200 MB, **once**, no installer),
2. compiles the mod against `Seapower-Scripts.dll` from your game,
3. copies `SeaPowerAdvanced.dll` and `_info.ini` into
   `Sea Power_Data\StreamingAssets\SeaPowerAdvanced\`.

Expected last lines:

```
Build succeeded.
Package ready: ...\dist\SeaPowerAdvanced
Installed to: C:\Program Files (x86)\Steam\steamapps\common\Sea Power\Sea Power_Data\StreamingAssets\SeaPowerAdvanced
```

### If Windows blocks the script ("is not digitally signed")

Scripts downloaded from the internet are blocked by PowerShell's **execution policy**. It is not a
problem with the mod, and you do not have to change any system setting. Three fixes, easiest first:

**A. Unblock the downloaded files** (right fix for the common case):

```powershell
Get-ChildItem .\tools\*.ps1, .\*.ps1 | Unblock-File
.\tools\build-portable.ps1 -Install
```

This clears the "came from the internet" mark on those files. It changes no Windows settings.

**B. Run it with the policy bypassed for that one command:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-portable.ps1 -Install
```

**C. Use `build.cmd` instead — no PowerShell script at all.**

```bat
build.cmd install
```

Batch files are not covered by the execution policy, so this works even on a machine where the
policy is locked down by IT. It does the same thing: fetches the portable SDK into `.dotnet\`,
builds the mod, and copies it into `StreamingAssets`.

> Which fix you need depends on the policy: `Get-ExecutionPolicy -List` shows it. If `MachinePolicy`
> or `UserPolicy` says `AllSigned`, options A and B cannot override it — IT set it — so use
> **option C**.

---

## Step 4 — Start the game

`-Install` puts the DLL in `BepInEx\plugins\SeaPowerAdvanced\`, so **BepInEx loads it directly**.
There is no mod-manager checkbox to tick on this route: the plugin is present, so it runs.

If you previously installed it into `StreamingAssets\SeaPowerAdvanced\`, **delete that folder**. A
copy in both places is loaded twice; the installer prints a warning when it sees one.

Why this route: a copy in `StreamingAssets` is only started if AnchorChain's own loader recognises
and triggers it, and on some installs that trigger never happens — the game log shows the assembly
being loaded but never says `Loaded plugin Sea Power Advanced`. The BepInEx route has no such step.

## Step 5 — Check it loaded

Two things to look at.

**1. BepInEx's plugin list**, near the top of `BepInEx\LogOutput.log`. It must say:

```
[Info   :   BepInEx] Loading [Sea Power Advanced 0.6.0]
```

**2. The mod's own log**, `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log`. This file only
exists if the code actually ran, which makes "did it load?" unambiguous:

```
================ Sea Power Advanced 0.6.0 ================
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

## Step 6 — Use it

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
- **Log hatch report** writes `hatch-report.txt` into `StreamingAssets\SeaPowerAdvanced\` — that is
  the file to send if something does not move.

---

## Updating or rebuilding

```powershell
.\tools\build-portable.ps1 -Install
```

then **fully restart** the game. Close Sea Power before rebuilding, or the DLL is locked.

## Uninstalling

1. In the Mod Manager, untick **Sea Power Advanced** (or just delete its folder).
2. Delete `Sea Power_Data\StreamingAssets\SeaPowerAdvanced\`.
3. Optional cleanup: delete `.dotnet\`, `dist\` and `BepInEx\config\io.github.bomberdeer22.seapoweradvanced.cfg`.

To remove Anchor Chain itself, unsubscribe on Steam and delete `winhttp.dll`,
`doorstop_config.ini` and the `BepInEx` folder from the game directory.

---

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `dotnet : The term ... is not recognized` | You ran `build.ps1` directly without a .NET SDK. Use `tools\build-portable.ps1` instead. |
| `Sea Power not found. Set SeaPowerDir in Directory.Build.local.props` | You skipped 3a, or the path has a typo. It must contain `Sea Power_Data\Managed`. |
| `AnchorChain.dll not found` | Anchor Chain is not subscribed (step 2), or `AnchorChainDir` points at the wrong folder. |
| `error CS0117: ... does not contain a definition` | The game's API moved in an update. Send me the full error — it names the member exactly. |
| `.ps1 cannot be loaded ... is not digitally signed` | Execution policy. See [the three fixes](#if-windows-blocks-the-script-is-not-digitally-signed); `build.cmd install` always works. |
| Build succeeds, but no `Weapon hatches` in game | Read `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log`. No file = the plugin never ran (check that step 5 shows `Loading [Sea Power Advanced ...]`). File present = the code ran, so send it. |
| `The Anchor Chain preloader is not running!` | Step 1: `winhttp.dll` + `doorstop_config.ini` + `BepInEx` must be **in the game folder**. |
| Menu entry appears but the submenu is empty | That mount has no hatch animation of its own. Run **Log hatch report** and send it. |
| Click does nothing, log says `cell has no open animation` | Real result: that cell cannot be opened in this game build. Note the ship class. |
| Hatch opens then shuts again immediately | The game's launch/reload cycle took it back. Note what the ship was doing and send the log. |

### Linux / Steam Proton

Build anywhere (the same `./tools/build-portable.sh --install` works, with `SEAPOWER_DIR` set), or
build on another machine and copy `dist/SeaPowerAdvanced/` across. Then copy that folder into
`.../steamapps/common/Sea Power/Sea Power_Data/StreamingAssets/`.

For BepInEx under Proton you may also need the standard `winhttp` override:

```bash
protontricks 1286220 --force winecfg   # Libraries → add "winhttp" as native,builtin
```

---

## What was installed, at a glance

```
Sea Power\                                  <- game folder
├── winhttp.dll                             <- step 1 (BepInEx/Anchor Chain preloader)
├── doorstop_config.ini                     <- step 1
├── BepInEx\
│   ├── LogOutput.log                       <- where to look when something is wrong
│   └── config\io.github.bomberdeer22.seapoweradvanced.cfg   <- settings
└── Sea Power_Data\StreamingAssets\
    └── SeaPowerAdvanced\
        ├── SeaPowerAdvanced.dll            <- step 3 (the mod)
        ├── _info.ini                       <- name and description in the Mod Manager
        └── hatch-report.txt                <- written on demand from the menu
```
