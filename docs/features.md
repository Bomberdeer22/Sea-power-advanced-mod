# The advanced features

There are thirteen now: the twelve original ones and automatic defensive ECM. All of them live in
the game's own right-click menu, each in the section a player would look in first - not stacked into
one submenu. There is no mod window and no hotkey.

| Feature | Where it appears |
|---|---|
| Radars, air/surface search only, active sonar, towed array, EMCON profiles | **Sensors** |
| Radar blink, auto-ARM shutoff, **defensive ECM**, commercial disguise | **Sensors** (they are emission decisions) |
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

Cycles the surface-search radar on and off on a timer. A real ship does this so a passive
anti-radiation seeker has to track an intermittent, moving source instead of a steady beacon.

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

| Setting | What it does |
|---|---|
| Sensors → *Defensive ECM: off - click to arm automatic jamming* | Arms this ship |
| Sensors → *Defensive ECM for the whole task force* | Arms every ship under command |
| Sensors → *ECM: what this build exposes* | Writes `ecm-report.txt` again on demand |

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
