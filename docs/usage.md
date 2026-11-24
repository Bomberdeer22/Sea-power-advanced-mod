# Using Sea Power Advanced

How to open hatches by hand. Thirty seconds of clicking, plus what the menu is telling you.

The advanced features - radar blink, the two ECM features, the force datalink, salvo deconfliction, the decoy screen,
readiness, formations, air operations and the rest - are documented feature by feature, with the settings and the
limits, in [features.md](features.md). What to check after installing is in [testing.md](testing.md).

## Before you start

The mod has to be installed **and loaded**:

- `SeaPowerAdvanced\` is in `BepInEx\plugins\` (or the older `Sea Power_Data\StreamingAssets\`
  location, if that is the route you installed), and
- the game has been **restarted** since installing it (mod code loads once per launch).

To confirm, search `BepInEx\LogOutput.log` for `Loading [Sea Power Advanced` and
`Hatch menu installed.`, or read the mod's own `seapoweradvanced.log` next to the DLL.

## The short version

1. **Right-click one of your ships.**
2. Open **Orders → Weapon hatches**.
3. Pick a mount, then click a cell. It opens. Click again to close.

That is the whole feature.

## What the menu looks like

```
Orders
  ▸ Weapon hatches
      Mk 41 VLS  (32)
        ▸ Cell 1                 [x]
          Cell 2                 [ ]
          Cell 3                 [ ]
          ...
          Open all cells
          Close all cells
      Mk 141 Harpoon  (4)
        ▸ Cell 1                 [ ]
          ...
      Open all hatches
      Close all hatches
      Log hatch report
```

- **The first level** is one entry per weapon mount, named the way the game names it, with the
  number of cells in brackets.
- **A check mark** means those hatches are open right now.
- **The last three** act on the whole ship, plus a diagnostic.

## Opening one cell

Click the cell. The hatch animates open, the game plays its hatch sound, and you get a line in
`LogOutput.log`:

```
Sea Power Advanced: Open Ticonderoga / Cell 7: openHatches -> started [open=yes, hatches=1]
```

Read it left to right: what was asked, which mount and cell, which game method ran, and the
resulting state (`open=yes`) with how many hatch models that cell owns.

Click it again to close it.

## Whole mounts and whole ships

- **Open all cells / Close all cells** — every cell of that one mount.
- **Open all hatches / Close all hatches** — every mount on the ship, one click. The log ends with a
  summary such as `Open all hatches on Ticonderoga: 6/6 mounts responded.`

Useful before a deliberate big launch: open the ship up, confirm the cells you want are ticked, then
give the attack order.

## Granularity: cells, not lids

The menu's unit is the **cell** — one silo tube, one launch box, one torpedo tube. If a cell has
several hatch models (a four-lid box launcher), they open together, because that is what the game's
own `openHatches()` does for a container. Individual lids inside one cell are not separable.

## What the mod does not control

- **The game still owns the hatches during a launch.** When a ship fires, its launch sequence opens
  and closes what it needs and can override your last manual order. If a hatch you opened shuts by
  itself while the ship is firing or reloading, that is the game, not a bug. The check marks will
  show the outcome next time you open the menu.
- **A mount with no hatch animation cannot be opened.** Some ships and kitbashes have no hatch
  animation data on their containers. Those mounts are still listed, marked `no hatch data` (their
  tooltip says why), so an empty-looking feature is never mistaken for a broken install. Clicking
  such an entry writes a report for it.
- **Guns and CIWS are hidden by default** — they have no silo hatch. See settings below.

## Log hatch report

**Orders → Weapon hatches → Log hatch report** writes `hatch-report.txt` next to the mod DLL
(`BepInEx\plugins\SeaPowerAdvanced\`) and prints the same thing into `LogOutput.log`.

It lists, per ship: every mount, its type, cells, hatch counts, live open state, ammo count and the
game's pending auto-close flag. That file is what makes a report actionable:

```
## Mk 41 VLS  [launcher]  32 cell(s), 32 hatch(es)
   hatches/cell:  1
   mount cover:   no (open animation: )
   cell 1: index=0 hatches=1 open=True ammo=1 pendingClose=False
```

## Settings that change the menu

In `BepInEx\config\io.github.bomberdeer22.seapoweradvanced.cfg` (edit while the game is closed):

| Key | Default | Effect |
|---|---|---|
| `General/IncludeGuns` | `false` | Also list gun and CIWS mounts. They have no silo hatch, so only a mount cover can move. |
| `General/AllowEnemyUnits` | `false` | Show the menu on enemy and neutral ships too. |
| `General/Enabled` | `true` | Master switch, without unticking the mod. |
| `Menu/ShowDiagnosticsItem` | `true` | Show "Log hatch report". |
| `Diagnostics/VerboseLog` | `true` | Log every action, including ones that did nothing. |

## Good ships to try first

Ships whose model data has obvious hatch animations, so you can see the feature working:

- **US**: Ticonderoga, Arleigh Burke, Spruance (Mk 41 VLS), Perry and Knox (Mk 13/box launchers)
- **Soviet**: Kirov, Slava, Sovremenny (big visible VLS covers)
- **Torpedo tubes**: destroyers and submarines with trainable tube nests

If the submenu for a mount is empty or the cell does nothing, that ship's data has no hatch
animation for it. Run **Log hatch report** and send the file.

## Quick troubleshooting

| What you see | What it means |
|---|---|
| No `Weapon hatches` entry at all | Check `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log` first: it records the entry point, both patch targets and every menu built. No file at all means the mod never ran. |
| Entry says "This ship has no launchers" | That unit genuinely has no launcher mounts (or guns only, with `IncludeGuns` off). |
| A mount says `no hatch data` | That ship's files define no hatch animation for the mount, so the game cannot open it. |
| Cell click logs `cell has no open animation` | That cell's data cannot be opened in this game build. Note the ship class. |
| Hatch opens, then shuts again | The game's launch/reload cycle took it back. Note what the ship was doing. |
| Nothing in the log after clicking | The cell entry is a leftover from an older menu state — reopen the menu. |

---

