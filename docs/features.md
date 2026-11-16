# The advanced features

There are fourteen now: the twelve original ones, automatic defensive ECM, and offensive ECM in
two modes. All of them live in
the game's own right-click menu, each in the section a player would look in first - not stacked into
one submenu. There is no mod window and no hotkey.

| Feature | Where it appears |
|---|---|
| Radars, air/surface search only, active sonar, towed array, EMCON profiles | **Sensors** |
| Radar blink, auto-ARM shutoff, **defensive ECM**, **offensive ECM**, commercial disguise | **Sensors** (they are emission decisions) |
| **ECM cones on the map** | Not a menu entry - drawn over the map whenever a set is aimed |
| Salvo deconfliction | **Engage** (it is about who may fire, and when) |
| Action stations, cold cruise, crew fatigue | **Rules of engagement** |
| Sprint and drift, turn the screen onto the threat | **Formation** |
| Helicopter ladder / square / sector | **Air operations** |
| Local control, replenish alongside | **Damage control** (replenishment falls back to logistics) |
| Breakaway with chaff and hard rudder | **Top level, one click** - it is the emergency button |

Sections are found by keyword at runtime, because the game's exact menu labels are not in the API
dump. If a section is missing in your build, the entry moves up to Orders rather than disappearing,
and `capabilities.txt` lists the section labels that were actually found - so the routing can be
corrected from a single log rather than a guess. This document says what each one does, which game
member it drives, and - where it matters - what it honestly cannot do.

## How to test them

Right-click one of your ships → **Orders**. You should see **Weapon hatches** and the feature
entries, spread across Sensors, Engage, Rules of engagement, Formation, Air operations, Damage
control and Logistics. Each toggle states its own state in the label ("Radar blink: on - click to stop"), and
each action reports through the game's order-feedback text as well as `seapoweradvanced.log`.

Anything that goes wrong is written down. `capabilities.txt` in the mod folder lists every lever the
mod found in your build of the game, and `ecm-report.txt` lists every ECM member and type it found.

---

## 1. Radar blink cycling — `Sensors and EMCON`

Switches a radar on for a few seconds, then off again, on a timer. A real ship does this so a passive
anti-radiation seeker has to track an intermittent, moving source instead of a steady beacon.

**You choose which set.** The entry is a submenu:

| Choice | What cycles | Why you would pick it |
|---|---|---|
| Air search radar | The air-search set only | This is the set an anti-radiation missile homes on |
| Surface search radar | The surface-search set only | Still finds ships, far harder to home on |
| Both radars | Both, on one timer | Two intermittent emissions, one control |

A hull with no radar is not offered the entry. Stopping puts every set back exactly as it was found -
if the radar was off before, it stays off.

| Drives | `EnableSurfaceSearchRadars(bool,bool)` / `DisableSurfaceSearchRadars(bool)`, state read from `IsSurfaceSearchRadarsOn` |
|---|---|
| Interval | `Features/BlinkSeconds`, default 6 s |
| Reversible | Yes - stopping puts each radar back exactly as it was found |

## 2. Auto-ARM shutoff — `Countermeasures`

When a weapon is tracked against the ship, the radars go dark for eight seconds, then come back.
This is what an anti-radiation missile forces a ship to do, and it costs the ship its own air
picture, which is why it is off by default and opt-in per ship.

| Drives | `DisableAllRadars()` / `EnableAllRadars(bool)` |
|---|---|
| Sensors used | `_incomingWeapons`, `_dangerousMissiles`, `ThreateningMissile` (read by reflection: you cannot call the `get_` accessor, that is CS0571) |
| Honest limit | The game does not label a missile as anti-radiation in any readable member, so **any** inbound weapon goes dark. That is conservative on purpose. |
| With defensive ECM | Defensive ECM wins while it is jamming: a jammer cannot hold a lock with the radar dark. The two features do not fight each other on the same ship. |

## 2b. Automatic defensive ECM — `Sensors and EMCON`

Every ship that carries a jammer also defends itself and its formation with it, without being
asked twice. When a vampire - a weapon the game is tracking against this ship, or against any ship
of the task force within 25 nm - appears, the mod:

1. **puts the air-search radar on.** A jammer needs a fire-control quality bearing to know where to
   point, and that bearing comes from radiating. This is the price, and it is not optional: while
   the jammer works, the ship is the loudest thing in the water. It is the same trade a real
   commanding officer makes, and it is why the feature is armed deliberately rather than always on.
2. **hands the missile itself to the jammer** as its target, so the set is pointing at the seeker
   rather than at a bearing the mod computed.
3. **raises power or mode**, where this build exposes a control whose units the mod can trust.
4. **holds the lock** - the target is re-pointed four times a second for as long as the missile is
   tracked, so a lock that the game clears is immediately re-established.

When the sky has been clear for six seconds it stands down, stops jamming, and puts the air-search
radar back exactly as it was found.

| Drives | The game's own ECM switch, ECM target member and power/mode member - all found at runtime by name, never guessed (see `Interop/EcmControl.cs`) |
|---|---|
| Sensors | Air-search radar on for the duration; that is the deliberate cost |
| Range | Defends the ship and any task-force ship within 25 nm of it |
| Honest limit | **The mod cannot promise the missile misses.** It points the game's jammer at the game's missile and keeps it there; whether the seeker breaks lock is the simulation's answer. "Almost impossible to resist" is not something a mod can guarantee without knowing the game's jamming model, and this one does not pretend to. |
| Honest limit | If this build names its jammers something the scan does not recognise, the feature says so in the menu and writes `ecm-report.txt` listing every ECM-like member it found. That file is ground truth from the installed game and is the fastest way to make the next build exact. |

### Which units get which entries

The mod only offers an option to a unit that can carry it out:

| Entry | Shown for |
|---|---|
| ECM (defensive, offensive, mode and lock) | hulls the game says carry ECM, and surface hulls where it does not say. **Never on submarines** |
| Active sonar, towed array | hulls that report the fit, or where the game does not report one |
| Helicopter search patterns | hulls with a flight deck or hangar, or surface hulls where the game does not say |
| Replenish alongside | surface hulls |
| **Lock with ECM** on a contact | hostile and neutral contacts, when one of your ships with ECM is in range. Nothing else is added to a contact's menu |

| Setting | What it does |
|---|---|
| Sensors → *Defensive ECM: off - click to arm automatic jamming* | Arms this ship |
| Sensors → *Defensive ECM for the whole task force* | Arms every ship under command |
| Sensors → *ECM: what this build exposes* | Writes `ecm-report.txt` again on demand |

## 2c. Offensive ECM, two modes — `Sensors and EMCON`

The ship's jammer, used deliberately. There are two modes and the default is the one that changes
nothing.

### Standard (default)

The game's own ECM behaviour. The mod does not touch the set, does not radiate for it, and switches
back to this the moment you pick it - which also releases any lock it was holding. If you never open
this menu, nothing about your jamming changes.

### Signature lock

Two ways in, and both end in the same place:

- **Right-click the enemy contact itself → Lock with ECM.** That is the only entry the mod adds to a
  contact's menu; the game's own identification entries (mark hostile/neutral, request identify) are
  untouched. The mod picks whichever of your ships with ECM is nearest and inside range, and names it
  in the label, so you do not have to work out which hull carries the jammer. If nothing of yours is
  in range, the entry is not added at all.
- **Right-click your own ship → Sensors → Offensive ECM lock** and pick a contact from the list (the
  contacts that ship's own picture holds, nearest first, max 40 nm).

Either way the mod then:

1. **reads that contact's sensor fit** - which radar families it has and whether each is up, whether
   it has a towed array or active sonar, whether it is running ESM or its own jammer, and what its
   hull signature reads as. This read is the lock: a jammer that has not identified what the other
   ship is radiating with has nothing to aim at. **Locking does not transmit.** The beam appears on
   the map and the target carries on as it was.
2. **`Jam now - transmit on the lock`** is a second, manual order. Only then are every one of those
   families switched off on that contact, and only that contact.
3. **holds them down** - re-asserted ten times a second while transmitting, because the target's crew
   can switch a set back on in between. Sustained suppression, not a one-off toggle.
4. **`Jamming: on - click to stop transmitting`** stops the jam and keeps the lock, so it can be
   resumed. Nothing is put back on the radar front, because this mode never touched it.
5. **stands the lock down** when the target is sunk, runs beyond range, you release it, or you go
   back to standard.

| Drives | The target's own switches: `DisableAllRadars()`, `DisableAirSearchRadars`, `DisableSurfaceSearchRadars`, `TowedSonarsSetActive(false)`, `DisableActiveSonars`, plus any ESM / jammer switch it exposes |
|---|---|
| Reads | Its radiator state (`IsAirSearchRadarsOn`, `IsSurfaceSearchRadarsOn`), its sensor fit (`HasTowedSonar`, `HasActiveSonar`), its hull signature from its parameter block |
| Range | `Features/JamRangeNm`, default 40 nm |
| Cost | **None in emissions.** This mode runs on EMCON: it never switches the attacking ship's radar on, so the jammer gives away nothing while it works. (The defensive feature does need air search - it is locking onto a missile, not a contact.) |
| Result you will see | *every sensor off*, or *NOT all sensors off - <sensor> would not switch off on this build*. The second is not a bug in the lock: it means the game gives no way to reach that one sensor family, so it is still working. The menu names it and `offensive-ecm.txt` lists it as `STILL`. The mod does not edit the target's picture to hide that. |
| Honest limit | The effect lasts as long as the lock is held. It is not a permanent state change to the other ship's data, and it stops the moment the lock is released - which is why the mod re-asserts rather than setting it once. |

`offensive-ecm.txt` in the mod folder records every lock, appended rather than overwritten: the
signatures read off the target, what was silenced, what was not, and the jammer path used. It is the
file to send if the target does not go quiet when it should.

## 2d. The ECM cones on the map — `Sensors and EMCON`

A jammer is directional and a menu cannot show you which way it is pointing, so the mod draws it:

| State | What you see |
|---|---|
| Offensive set, locked, not transmitting | a long, narrow cyan beam from the jammer to the ship it is locked on, thin and dim |
| Offensive set, jamming | the same beam, bright and thicker, with `ECM JAMMING` on the far end |
| Defensive set, engaged | an amber beam to the missile it is tracking, with a marker on the missile |
| A ship with ECM selected, nothing aimed | a small cyan box on the ship and `ECM ready` |

The beams are deliberately long and narrow - they are bearings, not areas, and a cone wide enough to
look like a search fan would be telling you something that is not true.

| How it is drawn | IMGUI, the same way the civilian traffic panel is - no assets, no XAML |
|---|---|
| Where the ends come from | `Interop/MapProjection.cs`: the game's own `GeoPosition`-to-world conversion if one exists, otherwise the unit's Unity transform, projected through the active camera |
| If neither exists | nothing is drawn, one line appears at the bottom of the screen saying so, and `ecm-cone.txt` records exactly what was looked for. A cone in the wrong place would be worse than none |
| Setting | `Features/ShowEcmCone`, default true |
| Follows the selected unit | the game's own selection member if the mod can find one, otherwise the unit whose right-click menu you opened last |

If the cones do not appear, `ecm-cone.txt` in the mod folder says which of the two projection routes
was tried and what the camera was - that file is the thing to send.

## 2e. Getting civilian traffic to work — `mission editor`

If the panel's **Spawn traffic** does nothing, **`traffic-report.txt`** in the mod folder now says
which link is missing. The feature needs five things, and the file tests each one:

| Line | What it means if it says `--` |
|---|---|
| mission editor open | No mission is open in the editor; the panel cannot do anything |
| editor state manager | The editor reports no interaction state manager, so the zone tool cannot draw |
| editor drawing layer | The map reports no drawing layer, so the polygon has nowhere to go |
| unit database | The game's unit database was not readable, so there are no civilian classes |
| civilian classes | Nothing in this install is flagged civilian, so a lane has nothing to spawn |
| neutral taskforce | Civilian traffic needs a side to belong to; load a mission first |
| civilian route list | The mod could not find where this build keeps its routes - **send this line** |

The route list is now **looked for rather than assumed**. It used to require
`MissionManager._civilianRoutes`, which in the mission editor is very likely null, because the manager
is what runs a mission and the editor is holding the one being edited. The mod now searches the
mission manager, then the editor's view model, then the mission it is editing, and writes down every
place it looked.

Also fixed: the panel used to return silently when it could not draw, so "it does not work" left
nothing in the log. It now logs the reason once and writes it into `traffic-report.txt`. The F8 key is
read from the GUI event as well as from Unity's `Input` class, so it works on builds that use the new
Input System only.

## 2f. Using it with the multiplayer mod

The multiplayer mod (`SeapowerMultiplayer`, `com.seapowermultiplayer.plugin`) is supported: the mod
detects it, works out whether this machine is the host or the client, and changes what it promises
accordingly. It is reached by reflection, so nothing changes for players who do not have it, and
nothing breaks if it is not installed.

**`multiplayer.txt`** in the mod folder says what was found and which rules followed from it. It is
rewritten when a session comes up or ends.

Three of the multiplayer mod's rules matter here:

| Rule | What it does to this mod |
|---|---|
| It throttles radar switches to one per ship per radar group per 10 seconds, and a throttled switch does not move locally either | **Radar blink** raises its half-cycle to 12 seconds in a session, whatever `BlinkSeconds` says. A six second blink would lose every second flip and leave the radar stuck |
| The client does not decide its own emissions - the host's radar picture is re-imposed every frame | **Radar blink** checks each flip four seconds later. If the switch did not take, the blink **stops itself and says so** rather than pretending. **Auto-ARM** and **defensive ECM** are not offered on a client at all, and the menu says why |
| Jamming is an order the host runs, not a member write | **Offensive ECM** on a client sends the game's own jam order, which the multiplayer mod forwards. The signature lock's switch-off assertion is not attempted from a client: the target is simulated on the host, so there is nothing there to switch off |

What still works everywhere:

- **Everything on the host**, and in single player without a change: this machine simulates the world.
- **The ECM cones** - they draw from whatever jamming the game reports on this machine, including
  jamming the session replicated in from the other side.
- **The menus, hatches, salvo planner, readiness, formations, helo patterns** - these go through the
  game's own orders, which the multiplayer mod already forwards.

Two limits worth stating plainly, because a player will otherwise read them as bugs:

1. **A jam started from a client cannot be stopped from that client.** The multiplayer mod forwards a
   jam order but has no "stop jamming" order, so the host's jam task runs until it ends by itself.
   The menu says so when you stop it.
2. **Damage, hits and sensor outcomes are the host's.** Nothing here claims a different result from the
   one the authoritative machine reached.

## 3. Commercial signature spoofing — `Deception`

The ship stops radiating, drops to a merchant's telegraph setting, and presents a civilian-looking
signature string (`MV <hull>`).

| Drives | `DisableAllRadars()`, `setEMCON(bool,bool)`, `setTelegraph(int)`, the signature member on the hull's parameters |
|---|---|
| Honest limit | **The mod cannot make an enemy's radar read our signature.** That needs a patch on the detection routine. Treat this as a reduced emission profile, and the log says so every time it is used. |

## 4. Salvo deconfliction — `Strike`

Holds every ship but the leader at hold-fire, then releases them one at a time so the task force's
launches arrive as several engagements instead of one.

| Drives | `SetWeaponStatus(WeaponStatus, bool)` |
|---|---|
| Enum values | Read from `WeaponStatus`'s own names at runtime - "hold/tight/safe" and "free/open/engage" - so a renamed enum cannot make it set the wrong state |
| Spacing | `Features/SalvoSpacingSeconds`, default 8 s |
| Never | Fires anything. It only decides when each ship may fire. |

## 5. Breakaway with chaff and hard rudder — `Countermeasures`

Full ahead, rudder hard over, chaff away, noisemaker - all in one action, at the moment a missile is
actually tracked.

| Drives | `setTelegraph(int)`, `setRudderAngle(float)`, `LaunchChaff(bool)`, `LaunchNoisemaker(string)` |
|---|---|

## 6. Degraded local control — `Countermeasures`

Ships the guns and CIWS onto local control for when the fire-control picture is lost.

| Drives | Any member on the hull whose enum type is named `*Discipline*` (the game has `CiwsDisciplineState`), set to the value whose name mentions local/manual/autonomous |
|---|---|
| Honest limit | If this build exposes no such member, the menu reports that the game was not told rather than claiming success. |

## 7. Cold cruise vs battle stations — `Readiness`

Closes the ship up: reloads run about 15% faster while held, and the crew starts to tire.

| Drives | `WeaponSystem._remainingReloadTime` is shortened on every mount that is reloading |
|---|---|
| Also | `SetWeaponStatus(<its own enum>, bool)` for the weapons posture, reached through `WeaponStatusControl` |
| Honest limit | General quarters is **not** a game concept - the mod keeps it, and expresses it through these two levers. |

## 8. Threat-axis screen reorientation — `Manoeuvres`

Finds the nearest tracked threat, computes the bearing to it, and re-stations the task force onto
that axis.

| Drives | `setWaypointTask(GeoPosition, string, <height enum>)` on each ship, through `WaypointControl` |
|---|---|
| Reads | `_threats`, `_dangerousMissiles`, `_spottedObjects`, `getGeoPosition()` |
| Detail | Bearing is corrected for latitude, so it is right in the North Atlantic rather than skewed |

## 9. Sprint-and-drift ASW — `Manoeuvres`

Toggles the game's own `SprintAndDriftDoctrine`, per ship or for the whole task force at once, and
streams the towed array. Reports the ship's real cavitation threshold from `CavitationOnsetKts()`.

| Drives | `SprintAndDriftDoctrine` (ReactiveProperty), `TowedSonarsSetActive(bool)` |
|---|---|
| Why not write it ourselves | The behaviour already exists and is already tested by the developers. The gap the mod fills is doing it fleet-wide, which the game's UI does not offer |

## 10. Helicopter search patterns — `Air operations`

Ladder, expanding square, or sector search. Launches an ASW helicopter if one is on deck and flies
the pattern; if one is already airborne, it just flies it.

| Drives | `FlightDeckHasUnit(...)`, `FlightDeckLaunchUnit(...)`, `setWaypointTask(...)` - all through `FlightDeckControl` and `WaypointControl` |
|---|---|
| Enum values | `UnitRoles` and `MissionType` are never named in code. The methods are found by name and their arguments are built from their own signatures; the role is picked by matching names like `ASW`, and the launch arguments are written to the log |
| Honest limit | If this build's flight-deck call has a shape the mod cannot fill, the menu says so and logs the signature instead of guessing at it |
| Honest limit | Launch is asynchronous, so the pattern is queued and flown once the aircraft appears - the log says "launching" rather than pretending it already flew |

## 11. Functional underway replenishment — `Logistics`

Takes on ammunition from a supply ship within 2 nm.

| Drives | `RearmAll(bool)`, `ReplenishAmmo(float,float)`, `ReplenishAccountableAmmo(float, Dictionary)` |
|---|---|
| Supply ships found by | Name containing supply / oiler / replenish / AOE / AOR / tanker |
| Honest limit | **Fuel is not transferred** - this game has no fuel-transfer-at-sea member the mod can find, so only ammunition moves, and the log says so |

## 12. Battle stations fatigue — `Readiness`

Fatigue accumulates while the ship is at action stations: 15 minutes of general quarters makes a
crew fully tired, and a tired crew gives back up to 25% of the reload saving. Standing down recovers
more slowly than tiring, so there is a real cost to leaving the fleet closed up all mission.

| Drives | The same `_remainingReloadTime` shave, with the penalty subtracted |
|---|---|
| Shown | The current state and fatigue percentage in the Readiness submenu |
| Honest limit | Crew fatigue is entirely the mod's model. The game has crew *skill*, not crew *tiredness*. |

---

## Settings

`BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg`, section `Features`:

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Add the eight submenus at all |
| `BlinkSeconds` | `6` | Seconds between radar blink flips |
| `SalvoSpacingSeconds` | `8` | Seconds between ships released in a salvo |

## What to send back if something misbehaves

`seapoweradvanced.log` (mod folder), `capabilities.txt` and `ecm-report.txt` (same folder). The log
names every feature and its result per ship, so a single failed action is easy to find; the two
report files say what the game actually exposes.

In a multiplayer session, add **`multiplayer.txt`**: it names the multiplayer mod's version, which
side of the session this machine is on, and every rule that followed from it.
