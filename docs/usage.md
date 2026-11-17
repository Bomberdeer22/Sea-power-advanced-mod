# Using Sea Power Advanced

How to open hatches by hand. Thirty seconds of clicking, plus what the menu is telling you.

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

# Civilian traffic zones

Second feature: random civilian traffic in an area **you choose**, with the amounts **you choose**.
This one lives in the **mission editor**, not in a running mission.

## Where it is

**Right-click any unit in the mission editor → Civilian traffic.** It is a section of the game's own
context menu, the same one every other feature in this mod uses.

There is also an F8 panel, but its buttons cannot be clicked: the game's own interface layer consumes
the mouse before IMGUI sees it. The menu is the way in.

## The workflow

1. Open the mission editor (new mission or an existing one) and place at least one unit.
2. Right-click a unit → **Civilian traffic**.
3. Make a zone, either way:
   * **Use the map area around your units** - the sea your units are already in, plus a margin. No
     drawing, nothing to go wrong.
   * **Draw a zone on the map** - hands the mouse to the game's own polygon tool; click points, then
     **Finish the zone** (the entry changes to that while drawing) or middle-click.
4. Click **Ships at a time** and **Aircraft at a time** until they show what you want. They cycle.
5. **Spawn traffic now**.
6. **Save the mission.** That is what makes them part of the mission.

Every action answers twice: a line of the game's own order feedback on the unit you right-clicked,
and the **`Last:`** entry in the submenu, which is still there when you open it again.

## Drawing the zone, if you want it

The panel does not have its own map tools — it borrows the mission editor's polygon tool, so
points snap to markers and the shape is drawn on the map exactly like a map annotation. Click as
many points as you like; three is the minimum a shape needs. The panel shows the point count and
the approximate area while you draw.

If you are halfway through and change your mind, **Finish** leaves the tool and keeps the points;
press **Draw zone** again to start a new shape.

## The four numbers

| Stepper | Meaning |
|---|---|
| Ships min / max | Civilian **vessels** that will be alive in the zone at any moment. The mod rolls a number in the range each time you press *Spawn traffic*. |
| Planes min / max | The same for civilian **aircraft**. Set max to 0 to skip a type. |

"At once" matters: a route keeps topping itself up as ships finish their course and leave, so the
zone stays busy instead of emptying out. Ten ships in a busy shipping lane is a lot of ships.

## What "variety" means here

Each press of **Spawn traffic**:

* rolls a count inside your min/max,
* splits that count over 1–6 **lanes** (a lane = one spawn point and one course through the zone),
* gives each lane a different random slice of the civilian unit classes your install defines,
* staggers the lanes so they do not all appear at the same second.

The class list is read from the game's unit database (`UnitMetadataProvider`), filtered to
everything flagged civilian — so the variety follows your game and any unit mods you have, rather
than a hard-coded list that would go stale. The panel shows how many classes it found; press
**Refresh** if you started the game with the mission editor already open and it shows zero.

## Saving and playing

The routes are real `SeaPower.CivilianRoute` objects in the mission, which is the same list the
mission file is written from. So:

* **Save** the mission → the routes are in the `.ini` and survive a reload;
* **Play** the mission → the game spawns the traffic itself;
* **Remove generated** in the same session → the routes this mod created are deleted again.

They are neutral, so they do not fight and nobody shoots at them unless you do. Shooting them has
the game's usual civilian penalty — that is the game's rule, not the mod's.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `CivilianTraffic/ShowPanel` | `true` | Show the panel in the mission editor. |
| `CivilianTraffic/ToggleKey` | `F8` | Key that toggles it. Any `UnityEngine.KeyCode` name. |
| `CivilianTraffic/MinShips`, `MaxShips` | `2`, `6` | Defaults; the panel remembers the last numbers you used. |
| `CivilianTraffic/MinPlanes`, `MaxPlanes` | `0`, `2` | Same for aircraft. |
| `CivilianTraffic/MaxLanes` | `4` | Upper bound on separate lanes per generate (1–6). |
| `CivilianTraffic/ClassesPerRoute` | `6` | Different classes one lane may pick from (1–40). |

## If something does not work

| What you see | What it means |
|---|---|
| No panel in the mission editor | Check `BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log` for `Civilian traffic panel ready`. If the line is missing the mod did not load; if it is there, the panel may have been toggled off — set `CivilianTraffic/ShowPanel` to `true`. |
| F8 does nothing | The key is read from the GUI event as well as from `Input`, so this should not happen; if it does, change `CivilianTraffic/ToggleKey` or start with `ShowPanel` in the config. |
| Panel says "Open a mission in the mission editor" | It is telling you where it works, rather than not appearing at all. |
| **Draw zone** does nothing | Use **Map area** instead - it needs none of the editor's drawing machinery. |
| Spawn adds nothing | The log line says why, and **Check setup** writes `traffic-report.txt` with the route list's exact type. |
| "No civilian vessel classes" | The unit database was not ready yet. Press **Refresh** once a mission is loaded. |
| "The mission manager is not running yet" | Load a mission first — the panel needs the mission that the routes will belong to. |
| "That zone is too small" | The clicks were nearly collinear; draw a wider area. |
| Traffic appears but looks sparse | Raise *max*, raise `MaxLanes`, or draw a bigger zone: lanes spawn at a point and sail out. |
| Saved mission has no traffic | Check the mission was saved **after** spawning. The log line `Civilian traffic: Added …` confirms the routes were added. |
