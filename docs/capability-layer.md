# The capability layer: what "missing" really meant

The readiness report said four features were blocked and two were partial. Almost all of that was
a naming problem, not a missing feature. This document is the rule for telling the two apart, and
what the mod now does about each.

## The three cases

When the report said something like "a unit exposes the list of its own emitters — MISS", it was
really saying one of three things, and they need different answers.

### 1. The member is there and public

Nothing to do. Call it. This was never the problem.

### 2. The member is there but named differently, private, or on another type

The offline scan looks for the names I guessed. The game may use a name I did not guess, or keep it
`private`, or hang it off `Ship` instead of `ObjectBase`. That is **not** a missing capability — it
is a capability the scan could not see.

`ObjectBase::TowedSonarsSetActive` is the proof. It is real, it does exactly what the ASW feature
needs, and its name also tells us what the radar switch is probably called.

**Answer: `Interop/Capabilities.cs`.** The mod looks members up by reflection at startup, tries a
list of plausible names, checks the signature (`bool` in, `void` out), and uses whatever it finds.
Consequences:

- a wrong guess costs a **logged miss**, not a compile error and another round trip through a build;
- a game patch that renames one drops that capability instead of breaking the DLL;
- everything found is written to `capabilities.txt`, so the next question is answered by the game
  itself rather than by another offline scan.

Reflection is used because the alternative — calling members I have not seen the signature of —
already produced four compile errors once. That trade is deliberate: slower and more verbose, but
it cannot stop the mod from loading.

### 3. The game genuinely has no such concept

Crew fatigue. General quarters as a settable state. These are not hiding anywhere; nobody modelled
them. No amount of reflection will find them.

**Answer: `Interop/UnitState.cs`.** The mod keeps its own per-unit state:

| Field | Feature that needs it |
|---|---|
| `GeneralQuarters` | cold cruise vs battle stations |
| `Fatigue` | battle stations fatigue |
| `SprintDrift`, `SprintPhase`, `NextPhaseAt` | sprint-and-drift ASW |
| `Blink`, `NextBlinkAt`, `BlinkRestoreOn` | radar blink cycling |

It is held in a `ConditionalWeakTable` keyed on the unit, so it lives exactly as long as the unit,
needs no save format, and cannot leak or go stale.

## The one honest limit

Mod-owned state can only ever **drive levers the game already has**: the reload timer, an emitter
switch, the throttle. It cannot make the game's own detection maths read a number it never stored.

So:

- "general quarters makes reloads 20% faster" — **possible**, if the game exposes a reload time we
  can write. The report says reload timers exist.
- "a spoofed radar signature fools an enemy's radar" — **not possible** by state alone. It needs a
  patch on the specific method that reads `SensorData::RCS`, so that it returns our value instead.
  That also points at exactly what the member dump is needed for: naming that method.

That distinction is the whole reason the two files (`capabilities.txt` from the running game,
`member-dump.txt` from the offline scan) are worth having before a feature is written.

## What is now unblocked

| Feature | Was | Now |
|---|---|---|
| Sprint-and-drift ASW | READY (evidence solid) | Buildable: towed sonar switch confirmed, throttle discovered at runtime, `UnitState` holds the phase. |
| Radar blink cycling | READY, but on junk evidence | Buildable if the radar switch exists under any name; the mod finds it at runtime and puts radars back as it found them. If it does not exist, blink falls back to mod-owned state and is reported as such. |
| Helo search patterns | READY | Buildable: `setWaypointTask` is already proven by the civilian traffic feature. |
| Chaff and hard rudder | PARTIAL (course ordering) | Chaff reachable (`decoySystems`); heading order is looked up at runtime among four plausible names. |
| Cold cruise / general quarters | PARTIAL | Unblocked by `UnitState`; the *effect* needs the reload timer. |
| Battle stations fatigue | PARTIAL | Unblocked as state; effect likewise. |
| UNREP, signature spoofing | READY (UNREP on junk) | Still the hardest: both need the game's own maths to consult our values. |

## What the real dump changed

The member dump answered the two open questions and overturned one plan:

| Question | Answer from the dump |
|---|---|
| Is there a radar switch? | **Yes, a whole family.** `EnableAllRadars(bool)`, `DisableAllRadars()`, `EnableAirSearchRadars(bool,bool)`, `DisableSurfaceSearchRadars(bool)`, `EnableActiveSonars(bool,bool,bool)`, `TowedSonarsSetActive(bool)`, `setEMCON(bool,bool)`, plus live state in `IsAirSearchRadarsOn` / `IsSurfaceSearchRadarsOn` and `HasRadar` / `HasTowedSonar`. |
| Does sprint-and-drift need inventing? | **No.** `SprintAndDriftDoctrine` is already a per-unit flag, and `CavitationOnsetKts()` is the game's own quiet-speed threshold. |
| Is anti-radiation defence possible? | **Yes.** `_incomingWeapons`, `_dangerousMissiles`, `TracksIncomingWeapons()`, `get_ThreateningMissile()`. |
| Chaff from code? | **Yes.** `LaunchChaff(bool)`, `LaunchNoisemaker(string)`. |
| Reload timer writable? | **Yes.** `WeaponSystem._remainingReloadTime` is a public field, with `StartReload` / `ForceReload`. |
| General quarters? | **Still absent** as a concept — but `SetWeaponStatus(WeaponStatus, bool)` is the nearest real thing, and `UnitState.GeneralQuarters` covers the rest. |

The one thing this changes is scope, and it is worth being blunt about: **implementing sprint-and-drift
from scratch would duplicate behaviour the developers already wrote.** The mod's job is the part the
game does not do - setting the doctrine, the EMCON state and the radar sets for a whole task force at
once, from one panel, mid-mission.

## Verifying it

`capabilities.txt` is written next to the DLL on every start. Open it after the next launch and it
answers, in one screen, which of the seven levers this build of the game actually provides.
