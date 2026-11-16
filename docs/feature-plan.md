# Feature plan: sensor, tactics, formation, logistics

The twelve requested features, triaged against what is actually knowable before code is written.

**To check these against your own install:** double-click `check-features.cmd` in the project
folder. It writes `feature-readiness.txt`, which answers each feature with `READY`, `PARTIAL` or
`BLOCKED` against your `Seapower-Scripts.dll` and shows the member names it matched. That file is
worth more than this document: the triage below is reasoning from the API, the report is evidence
from the exact build you are running. Send it back and the buildable ones get built first.

**Status of this document:** a plan, not a promise. Each feature lists the one capability that
decides whether it can exist. Those capabilities are checked mechanically by
`tools/analysis` — see [Checking, not guessing](#checking-not-guessing) — so the verdicts in
`feature-readiness.txt` have evidence behind them instead of confidence.

---

## The shape of the problem

These twelve features are not twelve equal pieces of work. They are three things:

1. **One shared primitive, asked for three times.** Radar blink cycling, auto-ARM shutoff and
   signature spoofing are all the same underlying capability: *switch a ship's emitters on and off
   from code, and know whether they are on*. Build that once and three features become thin
   policies on top of it. Build them separately and the same discovery is paid for three times.
2. **Things that need weapon internals.** Salvo deconfliction, readiness states and degraded local
   control all need to know how a mount decides it is ready and how it is told to fire.
3. **Things that need a resource model to exist at all.** Underway replenishment and crew fatigue
   assume the game tracks fuel and crew endurance. If it does not, these are not "hard" — they are
   not buildable, and no amount of cleverness changes that.

That third group is where most of the risk sits, so the probe exists to fail them fast.

---

## The features

`prior` is my guess before checking. The probe either confirms it or shows it was wrong, and a
wrong guess is worth more than a right one.

### Sensor & EMCON

| # | Feature | Decisive capability | Prior |
|---|---|---|---|
| 1 | **Radar blink cycling** | an emitter can be switched on/off from code | likely |
| 2 | **Auto-ARM shutoff** | anti-radiation missiles are a recognisable kind, and inbound weapons are visible to the ship | maybe |
| 3 | **Commercial signature spoofing** | a unit's radar cross-section is writable at runtime | unlikely |

**1** is the anchor. If emitters can be toggled, blink cycling is a timer with real tactical
meaning: 10–15 seconds of radiation every few minutes builds the picture without handing the enemy
a continuous bearing. It also gives the plumbing features 2 and 3 need.

**2** depends on two things that may not exist: a way to tell that an inbound weapon is
anti-radiation rather than just anti-ship, and a way to see inbound weapons per ship. If detection
exists but the missile type does not, a degraded version is still useful — cut emitters whenever
*any* missile is inbound on your bearing, which is a worse tactical decision but the same code path.

**3** is the weakest. Radar cross-section in this game is most likely read from unit data at load,
not held per ship. If it is not writable, the honest fallback is much smaller: mimic the *speed
profile* of a merchant (slow, steady, predictable) which affects how an observer's threat logic
reads you, without changing the signature itself. Worth stating plainly: a cosmetic "looks like a
trawler on the map" is not the feature that was asked for, and would not be shipped as if it were.

### Tactical

| # | Feature | Decisive capability | Prior |
|---|---|---|---|
| 4 | **Salvo deconfliction** | a mount can fire without the UI's fire command | maybe |
| 5 | **Chaff and hard rudder** | chaff can be released from code; a hard turn can be ordered | maybe |
| 6 | **Degraded local control** | fire-control directors are separately damageable | maybe |
| 7 | **Readiness states** | reload/readiness is a per-mount timer that can be read and influenced | maybe |

**4** is the one that decides whether this is a coordination feature or an advisory feature. If fire
can be triggered per mount, the mod assigns the shot to the ship with the best solution and holds
the others. If it cannot, the same logic still helps as a *recommendation* — the mod tells you which
escort should take the shot and marks the rest "hold" — but it is a different, smaller feature and
should be described as such rather than quietly downgraded.

**5** splits into two halves that fail independently. Ordering a hard turn is almost certainly
possible (the game orders courses). Releasing chaff from code may be tied to the AI's own defensive
logic. If chaff cannot be triggered, the manoeuvre half still works on its own.

**6** is only real if damage is modelled per subsystem. If a destroyed director takes the whole mount
with it, there is nothing to fall back to and the feature is cosmetic.

**7** is the most interesting and the most likely to be misjudged: a readiness timer that the game
does not already have would have to be owned entirely by the mod, which means the mod would have to
intercept firing rather than observe it. That is a far bigger job than adding a timer, and worth
knowing before committing.

### Formation & ASW

| # | Feature | Decisive capability | Prior |
|---|---|---|---|
| 8 | **Threat-axis screen reorientation** | formation stations are addressable, and a missile's bearing is readable | maybe |
| 9 | **Sprint-and-drift ASW** | speed can be set from code, and a quiet/cavitation speed exists | likely |
| 10 | **Helicopter search patterns** | flight deck launches can be called from code, and waypoints can be pushed onto aircraft | likely |

**9** is the best value-to-risk in the whole list. Sprint-and-drift is a speed schedule: listen at
the hull's quiet speed, sprint to catch up, repeat. It needs only speed control and a sonar, both of
which very likely exist, and it is genuinely how ASW escorts behave.

**10** is close behind, and partly de-risked already: the civilian traffic work showed that
waypoints can be set on a unit (`setWaypointTask` is called from `CivilianRoute.ApplyRouteWaypoints`
for every waypoint it applies). What is unverified is the launch half.

**8** depends on formations being data the mod can write to, rather than an AI behaviour. If
formations are opaque, the same effect can be approached from the other end — order the picket ship
to a computed position — which is cruder but achievable.

### Logistics

| # | Feature | Decisive capability | Prior |
|---|---|---|---|
| 11 | **Underway replenishment** | ships have a fuel quantity, and it can be transferred | unlikely |
| 12 | **Battle stations fatigue** | crew state exists as a modelled thing | unlikely |

Both of these need the game to model something it may not model. If there is no fuel number, UNREP
has nothing to move; if there is no crew state, fatigue has nothing to degrade. The probe checks
first so this is answered by evidence rather than by a build attempt.

If fuel is absent but **magazine counts are present**, there is a smaller honest version of 11:
replenishing *ammunition* alongside a supply ship. That is still a real tactic and still useful,
and it would be described as ammunition transfer rather than pretend to be full UNREP.

---

## Build order

Ordered by dependency, not by excitement. Each wave is a release.

| Wave | Features | Why here |
|---|---|---|
| **1** | 1 radar blink, 9 sprint-and-drift, 10 helo patterns | Independent of each other, likely supported, and wave 1 builds the emitter-control primitive that wave 2 needs. |
| **2** | 2 auto-ARM shutoff, 5 chaff and rudder, 8 threat-axis screen | All need "what is inbound and from where" on top of wave 1's emitter control. |
| **3** | 4 salvo deconfliction, 7 readiness, 6 degraded control | All need weapon internals: how a mount is told to fire, and how it reports readiness. |
| **4** | 3 signature spoofing, 11 UNREP, 12 fatigue | Highest chance of being impossible; build last, or replace with the honest reduced versions. |

Wave 1 is deliberately made of three features that do not share a code path, so a problem with one
does not stall the others.

---

## Checking, not guessing

```bat
tools\analysis\survey.cmd
tools\analysis\.venv\Scripts\python.exe tools\analysis\analyze.py readiness
```

The second command probes each feature's decisive capabilities against the installed build and
writes `tools\analysis\feature-readiness.txt`:

```
  [READY  ] Radar blink cycling
            prior guess: likely
              ok   an emitter can be switched on and off
                   SeaPower.SensorSystem::set_Active
              ok   a unit exposes the list of its own emitters
                   ...
  [BLOCKED] Battle stations fatigue
            prior guess: unlikely
             MISS  crew state exists as a modelled thing
```

`READY` means every required capability was found, `PARTIAL` that some were, `BLOCKED` that at least
one is missing. The examples are printed so a match can be judged rather than trusted: a member
called `set_Active` on a sensor is evidence; a member with a similar name on an unrelated type is
not, and that distinction has to be made by reading the names.

The probe is deliberately cheap to mislead. It looks for *names*, which is enough to answer "does
this concept exist here?" and not enough to answer "does it work the way we need?". A `READY` verdict
means "worth starting", not "will work".

---

## What is already verified

From the features already shipped, so these do not need re-checking:

| Capability | Evidence |
|---|---|
| Inject entries into both right-click menus | shipped in 0.3–0.4; `MapUnitViewModel` and `ObjectBaseViewModel` both patched |
| Open/close weapon hatches per cell | shipped in 0.2–0.5; `WeaponContainer.openHatches/closeHatches` |
| Read a unit's weapon mounts and cells | `ObjectBase._obp._weaponSystems` → `_containers` |
| Set waypoints on a unit | `ObjectBase.setWaypointTask` is called per waypoint by `CivilianRoute.ApplyRouteWaypoints` |
| Read unit data (class, civil/military flag, type) | `UnitMetadataProvider.UnitData` → `SimpleObjectParameters` |
| Drive the map's polygon tool | shipped in 0.6; `PolygonInteractionState` via `MissionEditorViewModel.StateManager` |
| Create entities in the world | `CivilianRoute.SpawnUnit` → `SceneCreator.createShip/createAircraft` |

That last row is the useful one for reinforcements and for any feature that needs to produce a
unit rather than move one.
