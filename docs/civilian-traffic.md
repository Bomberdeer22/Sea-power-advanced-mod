# Civilian traffic zones — how it works

This document records what the game actually does, because the mod leans on three of its
internal systems: the map polygon tool, the unit metadata database, and the civilian route
simulation. Everything below was read out of `Seapower-Scripts.dll` (the metadata and IL of
`SeaPower.CivilianRoute`, `SeaPower.SceneCreator`, `SeaPower.SaveLoadManager`,
`SeaPower.UnitMetadataProvider`, `SeapowerUI.ViewModels.PolygonInteractionState`).

## 1. What "civilian traffic" is in this game

Civilian traffic is **not** a normal unit group. It is simulated by `SeaPower.CivilianRoute`
objects held in one list:

```csharp
// SeaPower.MissionManager — public field
public List<CivilianRoute> _civilianRoutes;
```

`MissionManager.OnUpdate()` ticks every route; each route spawns units from a pool of allowed
unit classes until it has spawned `_maxUnits` of them, then stops (or repeats).

Routes come from the mission file. `SeaPower.SceneCreator.LoadCivilianRoutes(IniHandler)`
clears the list and rebuilds it from the INI, and `SeaPower.SaveLoadManager.WriteCivilianRoutes`
writes the list back out. So a route added to `_civilianRoutes` while a mission is open is
saved by the game itself — no custom file format, no patching of the save path.

### Mission file format (as read by `LoadCivilianRoutes`)

```ini
[Mission]
NumberOfCivilianRoutes=2

[CivilianRoute1]
OperationKey=            ; non-empty ⇒ the mission editor authored this route
Name=
Description=
Taskforce=Neutral       ; Neutral | <player side> | <enemy side>
SpawnFrom=              ; pv key of a port/airport object; empty ⇒ spawn in the open
DespawnAt=              ; pv key of an object; empty ⇒ despawn at the end of the route
UnitType=Vessel         ; Vessel | Aircraft | LandUnit
AllowedUnits=a,b,c      ; unit INI names, comma separated, picked at random per spawn
Squadron=
AllowedNations=all
AllowedLoadouts=all
MaxUnits=5
SpawnInGroupsOf=1
RadarsActive=false
StartTime=0             ; seconds of delay before the first spawn
SpawnInterval=30,90     ; min,max seconds between spawns
Disabled=false
Repeatable=true
EnableWeaponsAtWaypoint=-1
Waypoints=...           ; the game's waypoint blob
RuntimeStartTime=...    ; runtime state, only meaningful in a save file
RuntimeRemainingUnits=...
[...]
```

Notes that matter:

* `AllowedUnits` entries are **unit INI file names** (`SpawnUnit` looks the name up in
  `UnitMetadataProvider.UnitData` under the folder `vessels` / `aircraft` / `land_units`)
  and logs `Can not find <name>.ini in <folder>` for anything it cannot resolve.
* Section names are generated from the loop index: `CivilianRoute1`, `CivilianRoute2`, … So the
  numbering must stay dense and in list order, which is what `WriteCivilianRoutes` preserves.
* An empty `SpawnFrom` sets `_requiresHomeBase = false`, and then `CreateUnit()` calls
  `SpawnUnit()` directly at `_spawnGeoPosition` — exactly what a zone generator wants.
* `_allowedNations[0] == "All"` skips the nation/variant filtering branch entirely; a civilian
  route does not need a nation at all because it is spawned into the neutral taskforce.
* `OnUpdate()` starts with
  `if (GameTime.missionElapsedTime - _startTime < _startDelay) return;`
  so `_startTime = 0` and `_startDelay = 0` means "start immediately".

## 2. Unit catalogue

```csharp
var provider = SeaPower.Singleton<SeaPower.UnitMetadataProvider>.Instance;
provider.EnsureStart();

// key   = (folder, ini name)          e.g. ("vessels", "ContainerShip")
// value = (id, SimpleObjectParameters)
foreach (var kv in provider.UnitData)
{
    var sop = kv.Value.Item2;
    bool civilian = sop._isCivilian;          // public field
    var type = sop._objectType;               // public field, ObjectType
    var ini  = sop._objectIniName;            // public field, what AllowedUnits wants
}
```

`ObjectType` numeric values (read from the Constant table): `Other=0, Vessel=1, Submarine=2,
Aircraft=3, Helicopter=4, LandUnit=5, Any=19`.

This is where the "loads of variety" comes from: ship routes draw from every civilian vessel
class the install defines, air routes from every civilian aircraft class, and each route gets
its own random subset.

## 3. Drawing the zone

The mission editor already owns a polygon tool:

```csharp
var editor  = SeapowerUI.ViewModels.MissionEditorViewModel.Instance;   // public static field
var manager = editor.StateManager;                                     // public property
var layer   = editor.Map.DrawingLayer;                                 // public property

var state = new SeapowerUI.ViewModels.PolygonInteractionState(manager, layer);
manager.PushState(state);
// user left-clicks to add points; middle-click pops the state (the game's own exit)
IList<GeoPosition> points = state.Polygon?.Locations;
```

`PolygonDrawing.Update()` rebuilds `Locations` from `Points` on every mouse action, so reading
`Locations` after any click gives the polygon in geographic order. `PolygonDrawing` renders its
points as markers on the layer passed to the constructor, which is why the zone stays visible
while you draw it and after you finish.

## 4. What the mod does

1. You open the mission editor; the *Civilian traffic* panel appears (F8 toggles it).
2. **Draw zone** pushes the polygon state. Click around the area, middle-click to finish.
3. Set min/max ships and min/max planes, press **Spawn traffic**.
4. For each type the mod rolls a count in `[min, max]`, splits it into 1–4 routes, picks a
   random spawn point, 2–5 waypoints and a random set of civilian classes per route, then
   creates `CivilianRoute` objects and appends them to `MissionManager.Instance._civilianRoutes`.
5. Save the mission. `WriteCivilianRoutes` writes them into the mission file, so they survive a
   reload and spawn when the mission is played.

Routes created by the mod are remembered in the panel, so **Remove generated** can take them
back out of the list again while the editor session lasts.
