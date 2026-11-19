# Changelog

## 0.24.1 - build fix, and an archive that downloads

`Multiplayer.cs` grew a `List<ClientRule>` and a `Dictionary<string, Attempted>` in 0.24.0 and did not
grow the import for them, so the build stopped on `CS0246: The type or namespace name 'List<>' could
not be found` (and the same for `Dictionary<,>`), twice each in the error list.

Nothing else changed: the file already used `System.Reflection` and `System.Text`, so the missing line
was `using System.Collections.Generic;` and no code moved.

**Why the checks did not catch it.** `check-source.py` has a table of symbols mapped to the namespace
that declares them, and that table held the game's types, Harmony, BepInEx, Unity - but not the BCL, so
`List` and `Dictionary` were simply not on the list of things it looked for. They are now, along with
about thirty other unambiguous BCL types (`HashSet`, `IEnumerable`, `KeyValuePair`, `StringBuilder`,
`Regex`, `DateTime`, `Math`, `BindingFlags`, `MemberInfo`, `Path`, `File` and the rest). Two symbols
are deliberately left out - `Object` and `Debug` - because `System` and `UnityEngine` both declare
them, and a false alarm costs as much as a miss.

The gate was tested against this exact failure rather than assumed: with the import removed again it
reports all three symbols (`List`, `Dictionary`, `KeyValuePair`) and exits 1, and
`check-source.py --fix` puts the line back on its own.

```
python tools/check-source.py          :: clean: 53 file(s), no missing namespaces, ...
python tools/check-source.py --fix    :: same, and adds any import that is missing
```

A name a file declares itself is exempt: `ModInfo.cs` has `const string Version`, and that is not
`System.Version`, so the scan does not ask for an import it does not need.

### And the archive no longer trips a virus scanner

A download of 0.24.0's source zip stopped at **"Couldn't download - virus detected"**. Correct
behaviour on the scanner's part: the archive contained a compiled, unsigned executable -
`tools/analysis/testdata/hello.exe`, a 3.5 KB "hello world" used as a fixture by the offline assembly
reader's self-test. Nobody runs it; it existed so the reader could be pinned against ILDasm's own
output. An unsigned `.exe` inside a small project's zip is precisely what a browser is built to stop,
so it stopped the whole archive.

- **The fixture is not shipped at all.** The first attempt stored it as base64 text (`hello.exe.b64`),
  and the report came back unchanged - "still showing up as a virus" - which is the right answer:
  scanners look for an encoded `MZ` header as readily as for the `.exe` itself, so hiding the
  bytes only hid them from a person reading the file. `selftest.py` now ships the **expected output**
  (`hello.ildasm.txt`, MIT, text) and fetches the sample on demand with
  `python tools/analysis/selftest.py --fetch`, which checks its sha256 before use. With no sample the
  self-test skips and exits 2; with one, all nine checks pass.
- **The repository now contains no binary of any kind, encoded or not** - only `.cs`, `.cmd`, `.ps1`,
  `.md`, `.txt`, `.ini` and `.py`, all text that opens in Notepad.
- **`tools/build-portable.ps1` no longer runs a downloaded script.** It fetched `dotnet-install.ps1`
  from dot.net and executed it with `-ExecutionPolicy Bypass` - download-a-script-and-run-it is a
  dropper signature, and it was the one pattern in this repository that deserved to be flagged. It now
  downloads Microsoft's SDK **ZIP** and unpacks it with `Expand-Archive`, exactly what `build.cmd`
  already did with `curl` and `tar`.
- **`check-source.py` enforces it**: a committed `.exe`/`.dll`/`.zip`/`.msi` fails the check, a file
  whose first two bytes are `MZ` fails whatever its name, a text file containing a base64 `MZ` fails,
  and so does any script outside the tooling allowlist. Each one was tested by planting it.
- **`INSTALL.md` now explains the refusal** instead of leaving a player to guess: what was in the
  archive, what the tools actually do line by line, and three ways to get the files when a browser will
  not hand them over (a git clone, GitHub's own zip, or the individual files).

## 0.24.0 - a menu that can be read, and a client path that admits what it has not proved

Two debts paid, and neither of them is a new feature. Two reports from a live session came in
while they were being paid, and both are fixed here as well: a hull with **no jammer** being
given one (a Huak class), and **one client switching another client's ECM on** - which is why
defensive ECM appeared to arm itself. Those are the two sections after this one.

**The menu.** On a hull that carries a jammer, Sensors was carrying twenty-three entries - not a menu,
a hunt. There is now one rule, and it is deliberately not "a submenu once a section passes N": a named
group with **more than one entry** becomes one submenu inside the section it was already going into,
and everything else is placed exactly as before. So **Radars and sonar** and **ECM** are submenus in
Sensors, a ship with no ECM grows no ECM submenu at all - a group of one is not a group - and a hull without sonar gets a shorter radar
submenu rather than a differently shaped one. A feature can never move between one hull and the next
because of how many sensors it happens to carry, and nothing is ever hidden - if a section is missing,
the group goes up to Orders with the entries that would have been in it. The decision is made in one
place, from the entries themselves, and `menu-report.txt` records the section *and* the submenu each
entry landed in.

**The client.** The multiplayer behaviour used to be prose in three places - a paragraph in the report,
a sentence in the menu, and whatever each feature did in code - which is the arrangement that lets the
menu promise something the report denies. It is now a single table in `Interop/Multiplayer.cs`: one row
per feature, its route into the simulation (ORDER / CALL / READ / LOCAL), and what a client gets. The
menu's refusal text is generated from that row, the report prints the whole table, and `multiplayer.txt`
is the reader's copy.

Writing the table down found four paths that were never stated at all, and they are not all fine:

- **Readiness** was writing the reload speed-up (`WeaponSystem._remainingReloadTime`) on clients, where
  the host's own weapon update reverts it. The weapons posture is still sent; the bonus is now skipped
  on a client and the menu says why.
- **Commercial disguise** was writing the hull's signature string on clients, where the host's copy of
  the ship wins. The radars and the telegraph are still sent; the signature is left to the host.
- **Breakaway, replenishment, threat axis, helo patterns** were never classified. They now are.

And because a table of intentions is not evidence, **every path a client can act on records itself**
when it is used - count, unit, whether it reported success - and `multiplayer.txt` closes with how many
of the twelve are still untried this session. A client that flies three of them shows nine unproven.
That number is meant to be uncomfortable; it is the only honest way to present code that only a client
can prove.

### And a ship with no jammer is no longer given one

Reported against a base-game **Huak class**: a radar and an RWR, no ECM set - and defensive ECM armed
itself on it anyway, put its air-search radar on for the first inbound missile, and logged a jam that
was never transmitted.

The cause was the answer to "does this hull carry a jammer", and it was wrong in two places at once:

- `UnitFit.HasEcm` ended with **"it is a surface hull, so a jammer is fitted"**, so anything that did
  not positively deny it was treated as a jammer;
- the fitted-object probe before that matched by **name prefix**, and the first member on the unit type
  starting with `_ECM` is `_ECMSystemsTryingToJamMe` - the list of *other people's jammers pointed at
  this ship*. Every unit in the game has that field.

Both are gone. The question is now answered from **the ship's own sensor list**, read from the installed
game, and the answer "none" is an answer:

- **the sensor list is asked first** and its answer is final, including no;
- a **receiver is not a transmitter**: an ESM, RWR, intercept or warning entry in the list does not
  count as a jammer, whatever its class is called;
- and when the list and the game disagree - a set named like ECM that the game's own
  `isOffensiveEcmSystemAvailable` says cannot jam offensively - **the game wins**;
- the game's own `isOffensiveEcmSystemAvailable` / `HasSelfScreeningJammer` flags are the fallback for a
  build whose parameter block cannot be reached;
- and if nothing can be read, the answer is **unknown, which is not a yes** - the menu says the fit
  could not be read and **nothing is armed automatically**.

A hull with no set now gets no ECM entries at all in its menu, the same rule a submarine has always
had, plus one line - *ECM: no jammer on this hull, click for the report*. `ECM: what this build exposes`
writes `ecm-report.txt`, which now ends with **a jammer fit by hull**: one line per hull class in your
fleet, saying fitted / no jammer / unreadable, with the sensor families that decided it.

Two consequences worth stating rather than discovering:

- **The close-in dive is part of defensive ECM**, so a hull with no jammer no longer gets it. The dive
  is driven by the jammer's lock on the missile; without a set there is no lock to break.
- **Arming the whole task force now reports what it skipped**, and disarms a ship that was armed by an
  earlier build instead of leaving it armed and silently doing nothing.

### One client can no longer switch another client's ECM on

The second half of the report from the same session. Two things were happening at once:

- **Arming the whole task force armed everybody's ships.** The fleet list on a co-op client holds
  every friendly hull on that side, including the other player's command, and *Defensive ECM for the
  whole task force* walked all of them.
- **Which is why defensive ECM looked like it turned itself on.** The other player's screen showed
  their own ships' radar and jammer come on with nobody having clicked anything on that machine - the
  click had happened on yours.

The fix is the ownership rule the multiplayer mod already keeps, read rather than re-invented:

- **`UnitLockManager.BlocksOrdersFor(unit)`** - the other player has claimed that hull and the
  multiplayer mod refuses orders for it. Every ECM path here now asks the same question first:
  arming, engaging, jamming, holding, releasing, changing mode, and picking a jammer from the contact
  menu.
- **`TaskforceAssignmentManager.ClientMayControl(unit)`** - a client assigned one task force does not
  get to task another's ships. Same gate.
- **Neither readable** (older or newer build of that mod): per-ship clicks still work, because the
  player is clicking a ship on their own screen, but **fleet-wide arming is refused** with that
  reason rather than guessed at.

A ship this machine no longer commands is **disarmed on the next tick** and the log says so, so an
arm recorded before the other player claimed the hull cannot survive the handover.

And a direct answer to "defensive ECM turns on by itself": **it cannot**. There is no automatic arming
path in the mod - the two menu entries are the only way in - and `ecm-report.txt` now carries the
audit: how many ships are armed, and how many of those this machine does **not** command. That second
number should always be zero; if it is not, that file is the bug report.

## 0.23.0 - a jammer has channels, and a hold runs out

The player's verdict on the offensive ECM, asked for and answered honestly: the game's own jam is
fine, but the second half - switching an identified contact's whole sensor fit off and keeping it off -
had no limit at all. One click, a contact blind for as long as the lock was held, and a fleet's worth
of ships doing it to a fleet's worth of contacts at once. That is the same "too well" as the dive had
before 0.22.0, and it gets the same treatment.

Two limits, both settings, both stated in the menu before the click and in `offensive-ecm.txt`
afterwards:

- **`Features/EcmJamChannels`, default 2.** How many contacts the task force may hold **blind** at
  once. A ship's jammer already holds one contact - the game gives a set one assignment - so a
  per-ship figure above one would be a promise the simulation would not keep. What is finite is the
  expensive half: a few tracks held blind at a time, staffed from the force's electronic attack cell.
  **Every lock still gets the game's own jam**, so a lock with no channel free is a jam without the
  blinding, and the menu names the ships using them. `99` is the old version.
- **`Features/EcmJamHoldSeconds`, default 60, and `Features/EcmJamRecoverySeconds`, default 15.** A
  hold is now a burst: a minute of suppression, then the mod stops holding the sets down - it switches
  nothing back on for the enemy, the game's own crew does that - and the same contact cannot be held
  down again for fifteen seconds. Stopping a hold by hand and releasing the lock both start the same
  recovery window, so the limit cannot be skipped by clicking twice.

The mod also checks its own second limit. Five seconds after a hold ends it reads the contact's
sensors once and writes down whether anything came back up (`5 s after the hold on <target> ended: N
of M sensor famil(ies) radiating`). It will not switch them on for the enemy - that would be radiating
on someone else's behalf - so the target's own side has to, and if it does not, the window is buying
the target nothing and the log says so rather than leaving the limit looking like it works.

Also fixed: taking a second lock on the same ship used to overwrite the first without standing it
down, leaving the old assignment in place. It now releases the old lock properly - teardown and all -
before taking the new one. And *hold its sensors down* is no longer offered to a multiplayer client,
where it could never have done anything: the menu says the host simulates the target instead of
accepting an order that changed nothing.

Nothing else about offensive ECM changed: it still runs on EMCON, still works on unidentified contacts
through the game's bearing jam, still reaches 150 nm for ships and aircraft, and still reports every
lock to `offensive-ecm.txt`.

## 0.22.1 - 3 / 2 / 1 as the shipped setting

The player played 0.22.0's middle setting and chose a line: **three guidance channels, two seconds
between engagements, one second to react, radar seekers only**. That is the default now.

It is the setting that makes a salvo an exchange rather than a formality: a two-missile shot is
stopped, and a four-missile salvo loses three and puts the last one in. The old 2 / 4 / 2 is still
documented as "Practical" and the last-ditch 1 / 8 / 4 as "Strict", both one edit away in
`docs/features.md`.

A config written by 0.22.0 keeps whatever it stored, so the exact old figures are moved once on load -
the same rule as the jam-range and dive-range migrations: `EcmDiveChannels` 2 becomes 3,
`EcmDiveIntervalSeconds` 4 becomes 2, `EcmDiveReactionSeconds` 2 becomes 1. Anything the player set
themselves is left exactly as it is.

## 0.22.0 - it works, and it is too strong

The player's verdict on 0.21.0: **it works perfectly, maybe too well, it seems overpowered**. Agreed.
A jammer that kills every missile of every salvo does not just beat the enemy's anti-ship missiles, it
makes them pointless - and the feature was unlimited in exactly the way a real ship is not: infinite
guidance channels, no reaction time, no limit on how fast solutions could be settled, and it worked on
seekers that radio jamming physically cannot touch.

Four limits, all of them real, all of them settings:

- **`Features/EcmDiveChannels`, default 2.** A ship defends on a handful of guidance channels -
  illuminators and fire-control loops - not on unlimited attention. With two, a four-missile salvo
  gets through. 99 is the old behaviour, kept so it is reachable rather than deleted.
- **`Features/EcmDiveIntervalSeconds`, default 4.** The crew has to see how the first missile went
  before the next solution settles, so only one new descent may start every four seconds.
- **`Features/EcmDiveReactionSeconds`, default 2.** The jammer is on the missile the frame it is seen;
  the *descent* waits for the contact to be designated and the solution to settle. The log says
  `designating - the descent starts in 1.7 s` while it does.
- **`Features/EcmDiveRadarSeekersOnly`, default true.** This is the big one, and it is physics rather
  than difficulty: radio-frequency jamming does not blind an infrared, imaging or optical seeker, and
  an **anti-radiation** missile is homing on the emission - jamming it is helping it. The seeker is
  read from the game's own `_seeker._guidanceType` (name only, so a reordered enum cannot turn a heat
  seeker into a radar one), and a missile that is left alone says so once in the log.

The menu entry now shows the load - `armed, jamming now, 1/2 channels` - so a salvo overwhelming a
ship is visible rather than mysterious, and `ecm-dive.txt` prints the seeker type it read per weapon
type plus a count of missiles left alone because of their seeker.

Defaults are the middle setting. Two other combinations are documented in `docs/features.md`: the
0.21.0 wall (99 / 0 / 0 / false) and a **Strict** last-ditch (1 / 8 / 4 / true) under which a
two-missile salvo has a real chance.

## 0.21.0 - the game's own flight model, and no nulls anywhere

The player's test of 0.20.0: the exception warning came back, and the missile **stopped in mid-air and
spun on the spot**. Both symptoms had one cause, and the research this version is built on names it.

**What the game actually does**, read out of the decompiled assembly through two mods that work against
it (Real Air Combat Tactics, SeaPowerMultiplayer):

- `Missile.OnFixedUpdate` carries **motion integration, guidance, seeker, fuse and the state machine**.
  It runs every physics tick.
- `Missile.OnUpdateEveryFrame` carries the time-compression clamp and **a water-dip destruction
  path** - so this game does remove a weapon that gets into the water. 0.19.0 never put one there: it
  dragged the *model* down and left the simulation at its skimming height.
- The flight state is plain data: `ConnectionLost` (`ReactiveProperty<bool>`), `_lostGuidanceTime`,
  `_targetLost`, `_velocityVecInUnity`, `_velocityInUnity`, `_velocityInKnots`.
- **When the guidance link is lost the missile coasts on its last track** - the game's own
  `_lostGuidanceTime` extrapolation. That is the state a jammed missile is supposed to be in.

**The bug, in the game's terms.** 0.20.0 "broke the lock" by finding any member whose name mentioned a
target or a seeker and setting it to **null**. On this type that is `_seeker` and `CurrentTarget` -
both dereferenced in `OnFixedUpdate`. So the game threw a `NullReferenceException` every physics tick:
the repeated-exception warning; motion integration never ran again, so the missile froze; and the mod
kept rotating its transform, so it span where it stopped.

**So this build writes no null to any reference.** It does what the game does for a jammed missile -
`ConnectionLost = true` and `_lostGuidanceTime` stamped with the mission clock, re-asserted every frame
because the game's own update clears the flag - and then aims it at the water with the flight model's
own numbers: the orientation is *assigned* each frame (pitch towards the sea, heading held) and
`_velocityVecInUnity` is written to match, so the physics step integrates a real descent.

- **The descent is metred, not stepped.** The pitch is worked out from the height and the weapon's own
  speed, in the grid's units (about 67 m each - mixing the two is what turns a nudge into a vertical
  dive), and the height is walked down a straight line over 2.5 to 20 s. A sea-skimmer at 15 m noses
  over by about a degree; one at 5,000 m pitches much harder.
- **The nose stops at 90 degrees**, and gets there faster the higher it is: 6 deg/s below 1,000 m,
  18 deg/s to 5,000 m, 30 deg/s above. Orientation is assigned absolutely, so it can never wind itself
  up into the spin 0.20.0 produced.
- **The waterline is judged on the transform**, which is the map's own truth (the multiplayer mod
  converts it straight to latitude, longitude and height). The weapon's own height member is written
  as well and reported beside it, and if the game hands it back higher three times running, that is a
  **floor**, and `ecm-dive.txt` records the number.
- **The game removes it.** At the waterline the mod stops writing; `OnUpdateEveryFrame`'s water-dip
  path does the rest. Only if that has not happened after six seconds does the last resort run - fuel,
  life, the weapon's own state machine - and if those are refused, the totals say `left where this build
  put it` rather than the mod removing an object behind the game's back.
- **A half-metre of self-doubt**: if the missile stops moving while the mod is steering it, the log
  says so by name. That is 0.20.0's failure mode, and it should never be silent again.

`ecm-dive.txt` now prints every member of the weapon type, the guidance/velocity/altitude/scene members
found, the floor if there is one, and the reason for every decision.

## 0.20.0 - flown down, then handed to the game

The player's test of 0.19.0 found two things, and only one of them was a matter of taste.

**It teleported.** The descent rate was a flat 12 m/s, so a weapon found at fifty feet was in the
water in one and a quarter seconds. A dive nobody can see is not a dive.

**It did not die, and that was 0.19.0's whole premise.** The build was written on the assumption that
the game removes a weapon that reaches the surface, the way it removes a ship. It does not. The
missile was held at about **50 ft** - which is very probably this build's skimming floor - and it
stayed there, floating, while the mod had already stopped touching it because it believed the job was
done. Worse, the model had been dragged to sea level while the simulation still had the weapon at its
skimming height, so the thing that looked sunk and the thing on the ship's threat list were never the
same object at the same place.

So the dive is now:

- **A descent time, chosen once, at the start.** At least 2.5 s, at most 40 s, from the height it was
  found at; the height then follows a straight line over that time. A stutter cannot shorten it, a
  long frame cannot jump it, and a weapon at 10,000 m no longer needs fourteen minutes.
- **A nose that stops at 90 degrees.** It pitches at 6 deg/s, or **18 deg/s above 1,000 m** - a high
  weapon's nose has a long way to travel and the way down is the point - and it never turns past
  straight down.
- **A model that follows the simulation, never below it.** The scene object is only ever moved *to*
  the height the game reports. No more model in the water with the weapon at 50 ft.
- **A floor, detected and reported.** If the game hands back the height that was written, three
  times running, that is its skimming floor: `ecm-dive.txt` records it and the mod stops pretending it
  can get under it.

**And then the flight is ended by the game's own route.** At the waterline, or at that floor, the
weapon is handed to the ways a game ends a flight it owns - **fuel to empty** (almost empty: a
simulation handed an exact zero would divide by it), **life expired**, then **its own state machine**
put into a state whose name says finished - one candidate per 1.5 s, each checked before the next is
tried, warhead methods and `Destroy` still refused by name. Which routes exist, and what happened when
each was tried, is written to `ecm-dive.txt` along with **the weapon type's whole member list**, so the
next fix is written from fact rather than from another guess.

`ecm-dive.txt` also gains a sweep, so a weapon the game throws away without telling anybody is
forgotten instead of being held for the rest of the mission.

## 0.20.0 tooling - updating and uninstalling, in the other direction

Two files that are the opposite of `build.cmd`, added alongside the dive work above and **no mod code
of their own**: they operate on a finished install, so they work on any version already in the game
folder.

**`build.cmd` assumes it is starting from nothing.** That is right for a first install and wasteful
after that, and it says nothing useful about a version that is already in the game folder.
`update.cmd` asks the only question that matters - *what is installed, and what does that version
still need?* - and the changelog is the list of answers:

- **Which version is installed** is read from `version.txt` in the mod folder (new, written by
  `build.cmd` on every install), then the mod's own log banner, then BepInEx's `Loading [Sea Power
  Advanced x.y.z]` line, then "unknown", which is treated as 0.1.0 and checked against everything.
- **Only the changes that apply are made.** Pre-0.5.0 copies are moved out of `StreamingAssets`
  (two copies load twice); the DLL and `_info.ini` are replaced; the settings file is **never
  rewritten** - it is only read, to report that `JamRangeNm = 40`, `JamAirRangeNm = 90` or
  `EcmDiveNm = 2` are still in it, because the mod moves exactly those old defaults itself at load
  and a batch script echoing BepInEx's `##` description blocks back through cmd.exe is how a config
  file gets destroyed.
- **Everything is written down** in `update.txt`: the folder, the version and *which source said so*,
  the decision, each migration marked `APPLY` / `WOULD REMOVE` / `ok` / `NOTE`, what the jump adds,
  and the version in the game folder afterwards. It is copied into the mod folder as well.
- **`update.cmd selftest`** checks the version comparison against twelve cases with known answers
  (`0.9.0` older than `0.10.0`, `0.5` equal to `0.5.0`, `1.2` not a version). Those two routines
  decide everything else, and a wrong answer to either is silent.

**`uninstall.cmd`** removes the mod folder, any pre-0.5.0 `StreamingAssets` copy, and - only with
`clean` - the settings file. Everything is copied to `uninstalled\` first, so a mistake is a copy
back. It refuses to run while Sea Power is open, says **LEFT BEHIND** and lists the files rather than
claiming a success it did not have, and leaves `BepInEx`, `winhttp.dll` and `doorstop_config.ini`
alone because other mods need them.

- `build.cmd` writes `version.txt` next to the DLL and accepts `nopause`, so `update.cmd` can run it
  for the compile instead of there being two compilers to keep in step.
- `tools\check-batch.py` is the same idea as `check-source.py` for batch files, because these are
  written where no cmd.exe can be run: unresolved `goto`/`call` labels, a block left open, `%VAR%`
  read inside the block that set it, and `for /f ... in ("C:\path")` without `usebackq`, which
  silently reads the path as a string and loops zero times.
- `docs/updating.md` and its `.txt` twin: the release-by-release table, the version sources, what
  `update.txt` contains, how to roll back, and the limits - it updates an installation it can find,
  it rebuilds from *this* folder, and `nobuild` exists for a machine with no game assemblies.

## 0.19.0 — the missile is flown into the sea, not deleted

The player's report was two things at once, and both were right.

**`NullReferenceException` in `GameMain.FixedUpdate`.** The previous build removed the weapon with
`Destroy` (and, before that, set its warhead off). Deleting a live weapon out from under the game's
own update is exactly what that exception looks like, and what was left on screen - a missile sitting
on the water - was the object the *mod* had dropped while the game carried on without it.

**So the mod no longer touches a weapon's life at all.** It does what the player suggested: it flies
the missile into the water and lets the game register the impact, which is what already happens to
every missile that misses a ship.

- The lock is broken, so it cannot correct.
- Its nose is pitched down (about 6°/s) and its height walked down at 12 m/s - through the weapon's
  own height member where this build has one, its transform otherwise.
- At the waterline the mod stops. The game's own sea impact removes it. Nothing calls `Detonate`,
  `Explode`, `SelfDestruct`, or `Destroy` anywhere in this feature.

**From 10 nm instead of 2.** The descent has to be gradual to look real, and a sea-skimmer closing at
600 knots covers two miles in twelve seconds - too fast to come down safely. From ten it has about a
minute, and it cannot reach the ship however slow the descent turns out to be. `Features/EcmDiveNm`
defaults to **10** now (a stored `2` is moved up on load; anything else you chose is left alone), and
**0 switches the dive off** and leaves the jam working.

If this build turns out not to remove a weapon that reaches the surface, `ecm-dive.txt` says
`left on the water` rather than papering over it - and that file now names the two writes to suspect
if an exception ever appears again.

## 0.18.3 — the missile goes into the water, once

Two faults, and the second was the visible one.

**It was an explosion, not a splash.** The list of "ways to end a weapon's flight" included
`Detonate`, `Explode` and `SelfDestruct`, so putting a missile in the sea was implemented by setting
its warhead off two miles from the ship. The dive now uses only methods that end the flight quietly
(`Expire`, `Abort`, `Terminate`, ...) , or removes the object **at sea level** if the build has none.
Warhead methods are still found and listed in `ecm-dive.txt`, marked *warhead (unused)*, and are
never called.

**It exploded forever.** The take-down called the game's terminal method and trusted it to remove the
weapon. When it did not, the next tick found the missile still on the ship's inbound list and set it
off again - every tick, indefinitely. A weapon is now added to a *settled* set **before** anything is
done to it and is untouchable from then on, whatever the outcome: `WeaponDive.Done()` is checked
before the dive and before the jam bookkeeping, so one missile can produce at most one line and one
ending.

**And it was never going to be visible.** The descent was 60 metres per *call* at the ticker's rate -
more than a kilometre a second - so a twenty-metre sea-skimmer reached the waterline in a single
tick. It is now **15 m/s against real time**, with a one-second minimum before it may reach the
surface and a six-second deadline if nothing moves it, so the missile is seen to lose its height and
go in.

## 0.18.2 — build fix (the dive and the camera)

One error and three warnings from 0.18.0/0.18.1:

- **CS1061, `WeaponDive.cs`: `'Object' does not contain a definition for 'gameObject'`.** Right - and
  it is the one place the distinction matters. `UnityEngine.Object` is the base of both `Component`
  and `GameObject`, and only those two have a `gameObject`. The take-down now asks for the scene
  object with a helper that handles both cases, destroys that when it exists and the object itself
  when it does not. The descent route had the same switch inline; both now share the helper.
- **CS8604** and **CS8625** are nullability warnings, cleared: the weapon is checked for null before
  anything is read off it, and `MemberProbe.Read` now says in its signature what its callers already
  do - a static member is read with a null target, which is how the game's own globals are read.
- **CS0649**, a `Dive` field nothing assigned, removed. The dive's last known height is now used
  where it is useful: the log line says how far it had got before it went in.

Behaviour is unchanged from 0.18.1.

## 0.18.1 — offensive ECM reaches 150 nm

`JamRangeNm` and `JamAirRangeNm` both default to **150 nm** now, up from 40 and 90, and both go up to
300. That is the range at which the focused jammer will offer a contact, point at it and hold it.

**A config written before this build is moved with it.** BepInEx keeps whatever is already in the
`.cfg`, so a player who never edited these would have stayed at 40/90 and concluded nothing had
changed. On load, a stored value that is *exactly* the old default (40 or 90) is written up to 150;
any value you chose yourself is left alone. Change the key by hand if you want a different reach.

**The honest note, in the docs as well as here:** 150 nm ship-to-ship is well past the radio horizon.
The mod will point the jammer and hold the order at that range; if the game's own radar calculator
decides the jam does not degrade a set that far away, that is the simulation's answer. The second
half of the feature - holding the target's own sensors down - is unaffected by physics at range,
because it reaches the target's switches rather than the other radar's receiver.

## 0.18.0 — the shield is gone; the jammer puts missiles in the water instead

The missile shield is **removed**. It was an invulnerability switch wearing a feature's clothes: every
guided weapon aimed at you was taken off the board, whether or not anything about your ship justified
it. What replaces it is smaller, physical, and belongs to a feature that already exists.

**Defensive ECM now finishes the job at close range.** A sea-skimmer whose seeker loses lock two miles
out does not fly on towards the ship - it loses its height reference, stops correcting, and goes into
the water. So once an armed ship's jammer has a missile inside **`EcmDiveNm` (2 nm by default)**:

1. the weapon's own lock is broken - the member it is homing on is set to `null`, so it cannot
   correct what comes next;
2. its height is driven down every tick - through its own altitude member if this build has one,
   otherwise through its transform, which weapons do have here;
3. at the waterline it is taken down - the game's own terminal method if there is one, otherwise the
   object is removed. If neither the height member nor the transform moves it, a three second
   deadline does the same job.

There is no switch to turn on, no cheat label, and no separate feature: it is what *armed defensive
ECM* does at 2 nm on a hull that carries a jammer. Disarm the jammer and the dive stops with it. Guns
and rockets are still not touched - a shell has no lock to break - and every missile put down is
recorded in **`ecm-dive.txt`** with the route that did the work: `the weapon's own height member`,
`its transform`, or `nothing moved it down - removed at the end of the dive`.

Removed with the shield: `Features/MissileShield.cs`, `Interop/WeaponGuard.cs`, the top-level
**Missile shield** menu entries, and the `Features/MissileShield` setting. A `.cfg` that still has
that key keeps it harmlessly; the mod no longer reads it. The new key is `Features/EcmDiveNm`.

## 0.17.1 — the ECM cones can actually be drawn

The cones reported that they could not work. The cause was structural: the overlay needs to turn a
game position into a screen position, and it had two routes, neither of which this build offers.
There is no `GeoPosition`→`Vector3` helper to find, and the game's units are **ECS entities**, not
scene objects - so no unit has a Unity transform to project either.

The third route is the one the game itself uses, and it is now the first one tried: the world is laid
out in tiles of 0.2 degrees at 330.71396 world units each, offset by the tile the map is centred on
(`Globals._currentCenterTile`). That mapping is linear and exact, so a jammer's position lands where
the game draws the jammer.

Also in this build:

- **The camera is chosen by evidence, not by tag.** `Camera.main` is a tag, and a game with a UI
  camera will happily hand back the wrong one. The candidates are scored on whether a point at the
  world origin - where the map is centred - actually lands on screen. A camera that has been
  destroyed by a mission change is looked for again instead of switching the overlay off for good.
- **`ecm-cone.txt` now answers the question directly:** the centre tile, where `lat/lon 0,0` lands in
  world space, where that projects to on a screen of known size, the chosen camera, and every camera
  in the scene. That is what to send if a beam is in the wrong place - a different fault from a beam
  that never appears.
- **The on-screen note names the file properly:** `ecm-cone.txt` in the mod folder, beside
  `seapoweradvanced.log`, with the one-line reason in brackets.

From the ECM report a real install sent back, the jammer's own member names are now known and used:
`isOffensiveEcmSystemAvailable()` decides whether a hull carries ECM at all, `IsOffensivelyJamming`
answers whether it is transmitting, and `CeaseOffensiveJamming()` is the teardown - called on the unit
before the sensor-level fallbacks. `offensive-ecm.txt` lists which of those the build exposes.

## 0.17.0 — the missile shield

The one feature that does not play by the simulation's rules, built as one and labelled as one.

**What it does.** Every tick, for every unit of yours - ship, submarine, aircraft, helicopter - a
guided weapon the game is tracking against it has the member it is homing on written to `null`. The
weapon keeps flying and stops knowing where to; that is the same state a missile is in when the ship
under it is sunk, so the game already handles it. Then, when the weapon is on the game's own
`_dangerousMissiles` list or inside 2 nm, its flight is ended: the game's own terminal method
(`SelfDestruct`/`Detonate`/`Expire`/`Abort`...) if the build has one, otherwise `Destroy`, with the
ship's inbound list cleaned up afterwards.

**What it does not do.** Guns and rockets. A shell has no lock to break. The menu label and the
report both say "missile shield", not "invulnerable".

**Off by default.** `Features/MissileShield` in the `.cfg` (default `false`), and a top-level menu
entry on any of your units toggles it for the session: *Missile shield: off - click to switch on
(cheat)*. It is refused on a multiplayer client, where the host simulates weapons.

The per-unit log cap is a cap on the log only: after a dozen weapons a ship stops reporting, but the
shield keeps breaking locks and taking weapons down.

Everything about it is found by reflection, like the rest of the mod: the homing member and the
terminal method are read off the live weapon and written to **`missile-shield.txt`**, per weapon type.
That file is what to send if a missile ever gets through - it will name what the build actually
exposes, including the case where nothing readable is there.

## 0.16.1 — build fix

Three compiler errors in 0.16.0, all mine:

- `FleetMenu.cs`: the `AnyJammer()` helper that tells "no jammer in the fleet" apart from "none near
  enough" was referenced but never written - an edit script aborted before it reached that step.
  Added.
- `OffensiveJam.cs`: the teardown finder returned `MemberInfo?` while both call sites expect
  `MethodInfo?` (CS0266). It only ever returns a method, so it is typed `MethodInfo?` now.

Both gates clean again: `check-source.py` 52 files, tree-sitter 0 syntax problems. No behaviour change
from 0.16.0.

**The check that would have caught it.** `tools/check-source.py` now also flags a method called by
name that nothing in the project declares - the `AnyJammer` failure exactly (CS0103). It reads like
every other call, so no name check, brace count or scope check noticed it; asking "does any file
declare this?" does. Verified by deleting the helper again: the checker reports
`FleetMenu.cs:103: AnyJammer() is called but nothing in the project declares it (CS0103)`.

## 0.16.0 — offensive ECM actually jams, and works on unidentified contacts

Three things were wrong, and they had one root cause: the mod was looking for ECM and for contacts in
places the game does not keep them.

**A jammer is a sensor system, not a member of the unit.** It lives in the unit's parameter block
(`_obp._sensorSystems`) as an ECM entry with its own state. The old code scanned the *unit type* for
members called "ECM" or "jam", found none, and reported that this build had no ECM to drive - so the
contact menu returned before adding anything, and the lock refused with "this build exposes no ECM to
drive". `Interop/OffensiveJam.cs` now finds the sensor list on the type the game actually hands over,
finds the game's own jam orders (the unit form and the bearing form), and drives them.

**A contact picture lives on the task force, not on the unit.** It is
`PlottingTable.LocalVehicles` - the map - plus `_spottedObjects`. `Interop/ContactPicture.cs` reads
both. Nothing in it looks at identification to decide whether a contact exists, which is why
unidentified tracks now appear in the lock list and are marked as such.

**The jam is the game's own order.** `setOrder(Order.Type.Jam, target, displayOrderText)` for an
identified contact, the bearing form of the same order for one that is not - the order the weapons
panel issues, the one the game's radar model reads, and the one the multiplayer mod forwards to the
host. So this is not a mod-side imitation of jamming: it is the game's jamming. Writing
`_associatedTarget` and `_ECMSystemsTryingToJamMe` by hand is kept only as a fallback for a build
whose order cannot be found.

The second half of the feature - switching the target's own sensor families off and holding them down
- is unchanged in behaviour but is now honestly labelled as the extra: it needs the contact
identified, and the menu offers it only then. An unidentified contact gets the game's jam on its
bearing, which is what the game provides for exactly that case.

Also:

- The lock list is on offer whatever the mode toggle says. Hiding it behind the toggle was half of
  why the menu looked broken: the toggle changed a label and there was no way to pick a target.
- Taking a lock now points the jammer immediately, so a click does something visible.
- A contact menu with no jammer near enough says **ECM: no jammer within N nm** instead of adding
  nothing at all.
- The jam is re-issued only when the game's own state shows the assignment has gone, rather than ten
  times a second into the multiplayer mod's order forwarding.
- `offensive-ecm.txt` gains a contact-picture block: which source was read, how many foreign contacts
  were held, and how many of them were not identified.
- `capabilities.txt` names the jammer layer and what it found.

## 0.15.1 — civilian traffic removed

The feature is gone, completely: the panel, the menu section, the route builder, the zone tool, the
civilian catalogue, the diagnostics report, its settings, and every document that described it.

It never put traffic into a mission. Three builds went into it - looking for the route list instead of
assuming it, adding through `IList` instead of demanding a `List<T>`, making the field writes
best-effort, moving the whole thing into the game's own right-click menu - and it still produced
nothing. Leaving a feature in the mod that does not work is worse than not having it: it costs the
player time, and it makes the features that *do* work look doubtful.

**To be explicit about the state it was removed in:** the last build put a working **Civilian traffic**
section into the game's own context menu, so the buttons could at last be clicked. It was the
generation that still did not work, not the menu.

What stays: everything else. Hatch control, radar blink, auto-ARM, the three ECM features and their
cones, disaster-averting chaff, readiness and fatigue, threat axis, sprint-and-drift, helo patterns,
disguise, replenishment, local control, salvo deconfliction, the multiplayer work.

Removed from the config: the whole `CivilianTraffic` section. An existing `.cfg` keeps the stale keys
harmlessly; the mod no longer reads them.

## 0.15.0 — civilian traffic moved into the game's own menu

The panel drew, and nothing in it could be clicked. That is what a full-screen UI layer consuming
GUI events looks like: the window renders, the mouse never reaches it. Rather than fight it, the
whole feature is now a **section of the game's own right-click menu**, the same menu every other
feature in this mod uses and the one the player is already using in the mission editor.

```
Civilian traffic  ▸
    Zone: none - draw one or use the map area
    Draw a zone on the map  /  Finish the zone
    Use the map area around your units
    Clear the zone
    Ships at a time: 2-6 - click to change
    Aircraft at a time: 0-2 - click to change
    Spawn traffic now
    Remove generated traffic
    Last: nothing spawned yet this session
    Check setup - writes traffic-report.txt
```

The two things a context menu cannot do are handled instead of dropped:

- **No text fields.** The counts cycle through presets on click - ships `none → 1-2 → 2-4 → 2-6 →
  4-10 → 8-20 → 16-40`, aircraft `none → 1-2 → 2-4 → 4-8 → 8-16`. Saved to the config, so the
  choice survives a restart.
- **No pop-ups.** Every action reports through the game's own order feedback on the unit that was
  right-clicked, and leaves a **`Last:`** line in the submenu that is still there next time it opens.

The section is added in the **mission editor only**: traffic is written into the mission being
edited, so in a running battle the entry would be a dead end. It is added to every unit's menu -
including contacts, because in the editor the units on the map are often not the player's own.

The F8 panel stays as a convenience and is no longer required. It now runs early in Unity's
execution order, which is the one thing that can be done about the swallowed input from inside the
mod, and it consumes no GUI events except its own hotkey.

## 0.14.0 — civilian traffic rebuilt: panel, and the reasons it never spawned

### The panel

- **Absolute layout, not GUILayout.** A layout window measures its content, and in a game that draws
  its own UI that is how a panel ends up collapsed, reflowed or half off-screen. Every control now
  has a fixed rectangle.
- **Scaled from the screen height**, so it is the same physical size at 720p and 4K instead of tiny
  or enormous. Clamped 0.8x-2.2x.
- **No text fields.** Four of them fought the game for keyboard focus; every number is now a - / +
  button.
- **A visible log.** The last six results, newest first, in the window. Previously the result was one
  line that the next click overwrote and everything else went to a file.
- **Collapsible** to a title bar, draggable, and closable - it never has to cover the map.
- **Says why it is not working.** Opened outside the mission editor it draws a short notice with the
  reason instead of nothing at all.

### Why it never spawned - four separate faults

1. **The route list had to be a `List<CivilianRoute>` exactly.** Any other collection - including the
   observable collection the game's own view models use - was refused with *"not a list of routes the
   mod can write to"*. Traffic is now added through `IList.Add`, which every collection type has, and
   the count is **read back** afterwards so a list that swallows the add is reported rather than
   believed.
2. **A route needed all forty of `CivilianRoute`'s private fields.** One renamed optional field threw
   the whole route away and the only clue was a "Problems: no field ..." tail. Now **four fields are
   essential** (waypoints, unit type, allowed units, concurrency cap) and everything else is
   best-effort; the fields this build lacks are named in `traffic-report.txt`.
3. **`Globals._neutralTaskforce` had to be non-null to begin.** In the editor it often is not. The
   taskforce is now looked for (including other neutral taskforces in `Globals`), and if none is
   found the route is still created and the report says so.
4. **Nothing verified the result.** The report now states the list's size after the add - *"The list
   now holds N route(s)"* - and a zero-route attempt reports the reason instead of a count of what was
   attempted.

### A zone without drawing one

**Map area** builds a zone from the bounding box of the mission's placed units plus 30%. Drawing a
polygon depends on the editor's interaction state, drawing layer and a clean mouse handover - three
things this mod does not own - so when any of them misbehaves the whole feature used to look dead
with no way past it. Now there is a second route that needs none of them.

`traffic-report.txt` also names the **type** of the route list (`[TrulyObservableCollection<CivilianRoute>]`),
because the old failure was a type mismatch that looked like an empty list.

## 0.13.1 — one scope error, and a check that will catch the next one

`OffensiveEcm.cs(407)`: `error CS0103: The name 'reach' does not exist in the current context`.
`reach` was declared in `BestJammerFor` and used in `LockOnContact` — the two are neighbours, the
edit moved one line and not the other. Fixed by computing the contact's reach where it is printed.

**The more useful part.** Nothing in the source checker could see that: the braces balanced, every
namespace resolved, every member existed. A scope error is invisible to each of those, and this
sandbox has no compiler to ask. So `tools/check-source.py` now collects the locals each method
declares — parameters, declarations, `foreach`/`catch`/pattern variables, lambda parameters — and
flags a camelCase name that a method uses but that is only a local of another method in the same
file. Fields are excluded, named arguments are excluded, and the three-pass tuning left **zero
false positives across all 58 files**.

Proved rather than assumed: re-introducing the exact line that failed makes `check-source.cmd` print

```
OffensiveEcm.cs: 'reach' used in LockOnContact() but it is a local of Lock() - CS0103
```

and exit 1, and removing it again returns clean. `build.cmd` step `[4/6]` reports findings without
blocking the build, as before — the compiler is still the judge — but a name out of scope now shows
up in `check-source.txt` before the two-minute build rather than after it.

## 0.13.0 — the ECM lock reaches aircraft

Invisible before, and it should not have been: a jammer could only ever be pointed at a ship.

**Two reasons, both fixed.**

1. **The reach was a ship's reach.** 40 nm is right for a hull or a sea-skimming missile, where both
   ends are metres above the water. An aircraft is not: the radio horizon from 15,000 ft is about
   65 nm by itself, so a masthead and an aircraft see each other at roughly seventy miles. There is
   now `Features/JamAirRangeNm`, default 90 nm, and the lock drops at the right horizon for what it
   is holding.
2. **The contact picture was one ship's.** Contacts were read only from the jammer's own
   `_threats` / `_spottedObjects` / `_dangerousMissiles`. An air plot is not always kept where the
   surface one is, and an aircraft a picket detected thirty miles away was invisible to every
   jammer in the fleet. The picture is now the **force's**, and the lists are **looked for** - any
   enumerable member on a unit whose name says it holds contacts, threats, tracks or detections -
   instead of three names being assumed.

**What the menu says now.** `Lock E-2C Hawkeye - aircraft (68 nm)`, and the submenu header counts
what is in the air: `4 contact(s) in range, 2 in the air`. The enemy aircraft's own menu entry reads
`Lock with ECM (<ship>, 68 nm, aircraft)`. A single `UnitFit.Kind` now names ship / submarine /
aircraft / helicopter everywhere it is shown.

**And what it admits.** Jamming an aircraft is not jamming a ship, and the mod says so:

- it has **one radar**, so radar off means blind - with the same limit as any other target, that its
  crew can switch it back on between assertions;
- its **receiver cannot be jammed off at all** - a passive set has nothing to switch down - so the
  report states that rather than claiming a clean sweep.

`offensive-ecm.txt` now records the target's kind and **which contact lists the picture was read
from**, so the next build can be exact if this build keeps its air picture somewhere else again.

Also: `EcmControl`'s aim note said "lock on the missile" whatever it was pointing at. It says
"aimed via <member>" now, and names the type it refused.

## 0.12.1 — builds again, and works with the multiplayer mod

### The build failure

`Civilian/RouteContainers.cs` used `MemberProbe` without importing `SeaPowerAdvanced.Interop`, which is
four `CS0103`s. Fixed, and the reason it was not caught is worth the note: the source checker verifies
member consistency between files but not whether a file imports the namespace its neighbour lives in —
`check-source.py` resolves library symbols, not this mod's own.

Also cleared, since they were in the same output: two obsolete `Object.FindObjectOfType` calls
(`CS0618`, now `FindFirstObjectByType`) and one `CS8604` on a dictionary key that Unity's own
`==` overload makes look null.

### Multiplayer

The mod now detects **SeapowerMultiplayer** by reflection — no reference, no build dependency, no
behaviour change for anyone without it — and adapts where its rules actually bite:

- **Radar blink** raises its half-cycle to 12 s in a session. The multiplayer mod drops a repeat radar
  toggle on the same ship and radar group inside 10 s, and a dropped toggle does not move the switch
  locally either, so a 6 s blink would have left the radar stuck.
- **Blink verifies its flips** in a session, four seconds after each one. If the switch did not take,
  the blink stops and says so instead of pretending.
- **Auto-ARM shutoff and defensive ECM** are not offered on a client: weapons are simulated by the
  host, so a local jam is overwritten and jams nothing. The menu entry says that, in one line.
- **Offensive ECM** on a client sends the game's own jam order, which is the route the multiplayer mod
  forwards to the host. The signature lock's switch-off assertion is not attempted there, because the
  target is simulated on the other machine.
- **`multiplayer.txt`** records the multiplayer mod's version, which side of the session this machine
  is on, and every rule that followed. Rewritten when a session starts or ends.

Stated, not hidden: a jam started from a client cannot be stopped from it — the multiplayer mod has no
stop-jamming order — and the effects of both jamming and missile defence are the host's to decide.

## 0.12.0 — blink asks which radar, and the traffic panel explains itself

### Radar blink: you choose the set

The entry is now a submenu - **air search radar**, **surface search radar** or **both** - because the
two do different jobs and only the air-search set is what an anti-radiation shot homes on. A hull
with no radar is not offered the entry at all, and stopping restores each set to the state it was
found in rather than assuming it was on.

### Civilian traffic: the actual fault, and a report that names it

- **The route list was assumed, not found.** The generator required
  `MissionManager._civilianRoutes`, which is very likely null in the mission editor - the manager is
  what *runs* a mission, and the editor is holding the one being *edited*. `Civilian/RouteContainers`
  now searches the mission manager, then the editor's view model, then the mission it is editing, and
  adds to whichever list it finds. The panel's message now says which container it used.
- **The panel no longer fails in silence.** It used to `return` with an empty catch when the editor
  view model was null or threw, so the feature could be broken with nothing in the log at all. Every
  hidden reason is now logged once, and written to the report.
- **`traffic-report.txt`** lists all seven prerequisites with an ok/-- mark - editor open, state
  manager, drawing layer, unit database, civilian classes, neutral taskforce, route list - plus the
  zone's point count and the outcome of the last attempt. Written on every spawn and by the panel's
  new **Check setup** button.
- **F8 is read from the GUI event as well as from `Input`**, so the panel is reachable on builds that
  run the new Input System only, where `Input.GetKeyDown` throws.
- A redundant null test on a value-type polygon point is gone (CS0472).

## 0.11.0 — the ECM cones, and a manual transmit order

- **The beams are on the map.** Every set that is aimed at something draws a long, narrow cone:
  cyan from an offensive jammer to the ship it is locked on (thin while locked, thick and labelled
  while transmitting), amber from a defensive set to the missile it is tracking, and a small cyan box
  on a selected ship that carries ECM but has nothing locked. `Features/ShowEcmCone`, default on.
- **Locking and transmitting are now two orders.** A lock reads the target, holds the track and aims
  the beam; it does not switch anything off. **`Jam now - transmit on the lock`** does that, and
  **`Jamming: on - click to stop transmitting`** stops it while keeping the lock. The same two
  buttons appear on the contact's own menu once that contact is locked, next to the order that acted
  on it.
- **The overlay never guesses a screen position.** `Interop/MapProjection.cs` looks for the game's own
  `GeoPosition`-to-world conversion first, then falls back to each unit's Unity transform through the
  active camera. If neither exists it draws nothing, says so on screen, and writes `ecm-cone.txt`
  with what it looked for. A cone in the wrong place would be worse than no cone.
- **Cost control, because this draws every frame**: only on repaint, the beam list rebuilds five times
  a second rather than every GUI event, and `GameMain` is now cached in `GameWorld` instead of being
  found by a scene walk on every query.
- Audit: `tools/check-source.py` clean across 55 files, brace balance clean, every build script's
  labels resolve, and the new Unity types are on the checker's allow-list so it cannot mistake
  `Color` for a game type.

## 0.10.1 — ECM runs silent, and only where it fits

Four corrections after playing it, all of them about the same thing: the mod was offering things to
units and in modes that did not suit them.

- **Offensive ECM no longer switches the radar on.** It runs on EMCON - the attacking ship radiates
  nothing while it jams. The air-search requirement was copied from the defensive feature, where it
  belongs: shooting at a missile needs a fire-control bearing, jamming a contact does not.
  `EcmControl.Engage` now takes that decision as a parameter instead of assuming it.
- **A contact's menu gets one entry, not twenty.** Right-clicking a hostile or neutral contact now
  adds **Lock with ECM (<ship>, N nm)** at the top level and nothing else; the game's own
  identification entries are untouched. The ship is whichever of yours carries ECM, is nearest, and
  is in range - so the player never has to work out which hull has the jammer. If nothing of yours is
  in range, the entry is not added.
- **Only units that can do a thing are offered it.** ECM entries appear on hulls the game says carry
  ECM (and on surface hulls where it does not say), and never on submarines. Active sonar and towed
  array follow the game's own `HasActiveSonar` / `HasTowedSonar`. Helicopter patterns need a flight
  deck or hangar. Replenishment is surface hulls only. Where the game does not answer, the mod shows
  the entry rather than hiding a feature it could not check.
- **"Partially blind" replaced with what it actually means:** *NOT all sensors off - <sensor> would
  not switch off on this build*. The old wording read like a mod state; it was a missing game member.

`Interop/UnitFit.cs` holds the per-unit answers, caches its lookups, and logs what it decided for the
first contact menu it augments, so a menu that looks wrong can be explained from the log.

## 0.10.0 — offensive ECM, in two modes

The ship's jammer, used deliberately. Standard stays the default and changes nothing; signature lock
is the focused mode.

- **Standard** is the game's own ECM behaviour. The mod does not touch the set and does not radiate
  for it. Switching back to it releases any lock.
- **Signature lock** reads one contact's sensor fit before jamming anything - which radar families
  it has and whether each is up, towed array, active sonar, ESM and its own jammer, and what its hull
  signature reads as. That read *is* the lock: nothing is jammed until the mod knows what the target
  radiates with.
- With the read in hand, every one of those families is switched off **on that contact only**, and
  re-asserted ten times a second for as long as the lock is held, because the target's crew can
  switch a set back on in between. Sustained suppression, not a one-off toggle.
- The jammer ship radiates air search for the whole engagement - the same honest cost as the
  defensive feature - and gets it back as it was found when the lock is released, the target is
  sunk, or it runs beyond `Features/JamRangeNm` (default 40 nm).
- **"Fully blind" is a claim about evidence, not intent.** It is only reported when every sensor
  family the mod could reach was switched off *and read back as off*. A family this build refuses to
  switch is reported as *partially blind*, named in the log and listed as `STILL` in
  `offensive-ecm.txt`. The mod does not edit anyone's picture to hide the gap.

### How it reads another ship

`Interop/SensorSet.cs` is the reading half: it enumerates a unit's sensor families from the state the
game exposes (`IsAirSearchRadarsOn`, `IsSurfaceSearchRadarsOn`, `HasTowedSonar`, `HasActiveSonar`),
finds ESM and jammer switches by name, and reads the hull signature out of the unit's parameter block.
The same code switches them off, so the read and the jam can never disagree about what a "sensor" is.

The contact list comes from the ship's own picture - `_threats`, `_spottedObjects`,
`_dangerousMissiles` - filtered to hostiles inside jam range, so the mod never invents a target the
force cannot see.

## 0.9.0 — automatic defensive ECM

Every ship that carries a jammer now defends itself and its formation with it, without being asked
twice. Armed per ship or for the whole task force, and automatic from there.

- **A vampire wakes the jammer.** Any weapon the game tracks against this ship - or against a
  task-force ship within 25 nm - triggers it. The missile *instance* is handed to the game's own ECM
  target member, so the set points at the seeker rather than at a bearing the mod computed, and the
  lock is re-pointed ten times a second for as long as the missile is tracked, so a lock the game
  clears is immediately re-established.
- **Air search radars go on, deliberately.** A jammer needs a fire-control quality bearing, and the
  price of that bearing is an emission the other side can see. The feature is loud on purpose; that
  is the trade, and it is why it is armed rather than always on.
- **Power to maximum** where this build exposes a power or mode member whose units the mod can
  trust. A bare number with unknown units is left alone rather than set to a guess.
- **Stands down on its own** six seconds after the last vampire, stops jamming, and restores the
  air-search radar to exactly the state it was found in.
- **Honest limit, stated in the menu and the docs:** the mod points the game's jammer at the game's
  missile and holds it there. Whether the seeker breaks lock is the simulation's answer. No mod that
  cannot see the jamming model should claim a missile "almost certainly" misses, and this one does
  not.

### How the interface was found

ECM is not in any dump this project has, and every game type named in code from memory has failed to
compile. So `Interop/EcmControl.cs` discovers the interface instead: it scans the game's unit type
for members whose names mention `ecm` or `jam`, sorts them by shape (switch, target holder, power or
mode), and uses whatever is there. `Interop/Inbound.cs` holds the single definition of "inbound",
now shared with the auto-ARM feature so the two can never disagree.

- **`ecm-report.txt`** is written at startup and on demand: every ECM-like member on the game's
  unit type, every type in `Seapower-Scripts.dll` whose name mentions ecm/jam/decoy, and what the
  mod does with each. If this build names its jammers something else, that file says what - it is
  read from the installed game, so it is ground truth rather than a guess.
- If nothing is found, the menu says so plainly instead of pretending, and the report is the file to
  send. `diagnose.cmd` now collects it alongside `capabilities.txt`.

## 0.8.2 — 0.8.1's compile errors

0.8.1 got past the `Capability` and `using` errors and stopped on twelve more, all of the same two
kinds: a game type named in code, and a member that no longer exists. Both are now fixed at the
cause rather than one at a time.

- **No game enum is named in code any more.** `WaypointHeightState` (threat axis, helo patterns),
  `UnitRoles` and `MissionType` (flight deck) are unreachable from this project's namespaces —
  naming them is CS0246 every time, exactly like `WeaponStatus`. Two new wrappers find the methods
  by name and build their arguments from their own signatures:
  `Interop/WaypointControl.cs` (a waypoint on any unit) and `Interop/FlightDeckControl.cs` (launch a
  helicopter, after asking the game whether one is on deck). If a build's call has a shape the mod
  cannot fill, the menu says so and the log prints the signature instead of guessing.
- **`Capabilities.ParametersOf` is back.** A rewrite had removed it while `Readiness`,
  `MerchantDisguise` kept calling it — CS0117 in three places. It is now a one-liner over
  `unit._obp`, and the checker below looks for member references so a rewrite cannot silently take
  it again.
- **`get_ThreateningMissile()` → the property, read by reflection.** Calling an accessor by name is
  CS0571. A value type there is treated as "no answer" rather than "inbound", because a ship whose
  radars can never come back on is worse than one that misses a warning.
- **`LaunchChaff(bool)` returns `void`,** so the breakaway sequence no longer assigns it to a bool;
  it goes through `SensorControl`, which reports whether the call was made.
- **Dead state removed.** Four `UnitRecord` fields nothing ever wrote, and three more that were
  written but never read. The two that had a use now have one: `Blink.IsOn` also consults the record,
  the disguise drop logs how long it was up, and `SalvoPlan.Release` frees anything the records say
  is waiting, not only what survived in a static list.

### The checker now catches all five

`tools/check-source.py` gained: a game type named in a type position that no build has proved this
project can see; a member called on a mod type that has no such member (`CS0117`); an accessor called
by name (`CS0571`); and `UnitRecord` fields that are never assigned or never read (`CS0649`). Each is
fault-injected and verified. `check-source.cmd` still runs it in a second and writes
`check-source.txt`.

## 0.8.1 — the build compiles again

0.8.0 did not compile, so nothing after it could run. This is a compile fix, not a behaviour change:
the menu visibility work from the previous commit is still the thing to look for in game, and it has
never been observed working yet.

- **`Capability` restored.** Rewriting `Interop/Capabilities.cs` had deleted the class that eleven of
  its members use, which reads as correct in the file itself and fails with eleven `CS0246`s. The
  type now lives in its own file, `Interop/Capability.cs`, so a future rewrite of the reporter cannot
  take it with it.
- **`using SeapowerUI;`, `using SeapowerUI.ViewModels;`, `using UniRx;`** added to `FleetMenu.cs`,
  `MenuRouting.cs` and `FleetMenuPatch.cs`. `ContextMenuItem`, `MapUnitViewModel` and
  `TrulyObservableCollection` do not resolve from `using SeaPower;` alone.
- **The weapons-status enum is never named in code.** `WeaponStatus` failed to compile even though it
  is the parameter type of `ObjectBase.SetWeaponStatus`, so it is now taken from that method's own
  signature at runtime (`Interop/WeaponStatusControl.cs`) and the salvo planner and readiness feature
  both go through it. If a future game build renames the enum, the feature reports that it cannot hold
  weapons instead of failing to build.
- **`UnitRecord.BlinkPhase`** added — `Blink.cs` read a member that no longer existed.

### The check that should have caught all of that

- **`tools/check-source.py`** finds, without compiling: a symbol used in a file whose namespace is not
  imported, a mod type referenced but declared nowhere, and a `UnitRecord` member read by a feature
  but not declared. It exits non-zero and can `--fix` the usings it is sure about.
- **`check-source.cmd`** runs it from a double-click: writes `check-source.txt`, pauses, uses no
  PowerShell. Add `--fix` to add the missing usings.
- **`build.cmd`** runs it as step `[4/6]`, between checking BepInEx and compiling. It is skipped when
  Python is absent, and a finding is a warning rather than a refusal: the compiler stays the judge.
  A four-second check cannot be wrong in a way that blocks a good build.

## Unreleased — diagnostics and plain-text docs

For an install problem where the console closes before its output can be copied.

- **`diagnose.cmd`**: one double-click collects system info, the game folder it finds, which mod
  and BepInEx files are present, `BepInEx\LogOutput.log`, the mod's own log, and a run of the
  analysis tools into `SeaPowerAdvanced-diagnose.txt`. Uses only `cmd.exe` — never PowerShell — and
  writes the file as it goes, so a window closing cannot lose the evidence.
- `survey.cmd` now tees every run into `run-log.txt` for the same reason, and `analyze.py` writes a
  full traceback to `analysis-error.txt` if it crashes.
- **`README.txt` / `INSTALL.txt`**: plain-text twins of the Markdown, generated by
  `tools/md2txt.py` (wrapped to 78 columns, ASCII only, CRLF, tables rendered as labelled blocks so
  they read in Notepad).
- **Install guide corrected.** It still described the 0.4-era `StreamingAssets\SeaPowerAdvanced`
  location in step 3, its expected output, the uninstall steps and the file tree, while step 4 said
  `BepInEx\plugins\SeaPowerAdvanced`. Anyone following it end to end could install to the wrong
  folder and see the mod never load. Also fixed the stale log paths in `docs/usage.md`.

## Unreleased — feature readiness probe

Planning tooling for the next batch of features (sensor/EMCON, tactics, formation, logistics).

- `tools/analysis features.py` + `analyze.py readiness`: probes each planned feature's decisive
  capabilities against the installed assembly and reports READY / PARTIAL / BLOCKED, with the
  matched member names as evidence. Exposed as `survey.cmd readiness`.
- Each feature records the guess made *before* the check, so a surprise stays visible.
- `docs/feature-plan.md`: the twelve requested features triaged by dependency, with the build order
  and an explicit list of what is probably not possible.
- Performance work found by testing on real assemblies: metadata table lookups are cached (they were
  being re-materialised inside loops, turning name scans into minutes), member labels are built
  lazily, and compiler-generated member names are excluded from capability matches.
- Three real bugs caught by running the probe on F# and Android assemblies rather than on the fixture:
  a `\b` missing from a "towed array" pattern matched `sonAr` inside "Compari**sonAr**b", a regex
  without word boundaries matched `ToArray`, and `types`/`member` failed on short type names.

## 0.6.0 — civilian traffic zones (mission editor)

New feature: random civilian traffic, spawned into an area you draw yourself.

- **Zone tool.** Press **F8** in the mission editor for the *Civilian traffic* panel, then
  **Draw zone**. That hands the mouse to the game's own polygon tool (the one the editor uses for
  map drawing): left-click adds a point, middle-click finishes, points snap to markers. The mod
  reads the finished polygon from that state instead of reimplementing map input.
- **Min/max for ships and planes.** Four number fields, per type: the mod rolls a count inside
  your range each time you press **Spawn traffic**.
- **Variety.** Each generate creates 1–6 *lanes* — one lane is one spawn point plus one course —
  and every lane gets its own random slice of the civilian unit classes in your install, read from
  `UnitMetadataProvider.UnitData` (every class flagged `_isCivilian`). So one lane can be
  container ships and the next fishing boats, and the class list follows whatever units you have.
- **Real civilian traffic.** Routes are created as the game's own `SeaPower.CivilianRoute` objects
  and appended to `MissionManager._civilianRoutes`, which is the list the mission file is written
  from, so **saving the mission keeps them** and playing it spawns them. No custom file format.
- **Remove generated** takes the routes this session created back out again.
- New `[CivilianTraffic]` config section: panel visibility, hotkey, default numbers, lane count
  and classes per lane.
- New reference: `Noesis.NoesisGUI` (only because the editor's polygon state is declared there;
  the mod never draws through Noesis), plus optional Unity module references for IMGUI input.

The document that went with it (`docs/civilian-traffic.md`) was deleted with the feature in 0.15.1,
so this line is history rather than a link.

## 0.5.1 — reference UnityEngine

Build fix for 0.5.0: `BaseUnityPlugin` derives from `MonoBehaviour`, which lives in the legacy
monolithic `UnityEngine.dll`, not in `UnityEngine.CoreModule.dll`. Only the latter was referenced, so
the BepInEx entry point failed with `CS0012: The type 'MonoBehaviour' is defined in an assembly that
is not referenced`.

- Added the `UnityEngine` reference (game's `Managed\UnityEngine.dll`), plus a build-time check with
  a readable message if it is missing.
- Verified against BepInEx 5.4.23.2's source that `BaseUnityPlugin : MonoBehaviour`, that it declares
  no `Awake` (so this plugin's `Awake` is not a hiding override), and that it requires `[BepInPlugin]`
  on the class, which is present.

## 0.5.0 — load as a BepInEx plugin

Confirmed from the user's game log: AnchorChain logged `Loaded assembly SeaPowerAdvanced,
Version=0.4.0.0` but never `Loaded plugin Sea Power Advanced`, no entry point output appeared and the
mod's own log file was never created. The same log shows **no** `Loaded plugin` line for any of the
other 28 AnchorChain mods either, so AnchorChain's trigger loop is not starting mods on that install.

Rather than depend on it:

- The DLL is now also a native BepInEx plugin (`[BepInPlugin]`, `BaseUnityPlugin.Awake`), and
  `-Install` puts it in `BepInEx\plugins\SeaPowerAdvanced\`, where BepInEx itself loads it. BepInEx's
  startup log then shows `Loading [Sea Power Advanced 0.5.0]`.
- Both entry points call one idempotent `Boot.Run`, so a double installation cannot patch anything
  twice, and the AnchorChain route still works where that loader functions.
- `Boot.Run` writes the log file before anything else, so the file's existence is proof the code
  ran; it prints the entry point, assembly path, log path, resolved settings and each patch target.
- The installer warns when a second copy is still present in `StreamingAssets`, which would be
  loaded twice.
- Docs updated: no mod-manager checkbox on the BepInEx route, and how to verify both ways.

## 0.4.0 — the map menu is a different code path

Reported: right-clicking a ship showed Orders with only "Identify". The cause was found in the
assembly: a unit's map right-click is built by `MapUnitViewModel.GetContextMenuItems()`, which
constructs its own list (it only calls `get_Unit`, `get_Identified`, `get_Value` and three `Add`s)
and never delegates to `ObjectBaseViewModel`. Patching only `ObjectBaseViewModel` therefore never
touched the menu a player actually uses.

- Patches **both** menu surfaces: `MapUnitViewModel.GetContextMenuItems` (map right-click, taking the
  unit from `MapUnitViewModel.Unit.BaseObject`) and `ObjectBaseViewModel.GetContextMenuItems`.
- `TryAdd` is idempotent (skips if a `Weapon hatches` entry is already present), so a shared
  collection cannot produce duplicates.
- Each surface logs `Context menu patch is live (map menu)` / `(unit menu)` the first time it runs,
  which distinguishes "patch never called" from "patch called and declined".
- Logs to its **own file** as well as BepInEx: `StreamingAssets\SeaPowerAdvanced\seapoweradvanced.log`.
  "Is the mod running at all?" is now answerable without reading another mod's log.
- The entry point announces itself: `=== Sea Power Advanced 0.4.0 entry point reached ===`.

## 0.3.1 — say why, never silently

Reported: hovering Orders showed only the vanilla "Identify", with no hatch entry. That is the
0.2.0 filter bug (fixed in 0.3.0), but a missing entry should never require guessing which build is
installed.

- The submenu tooltip now carries the mod version, so hovering proves which DLL is loaded.
- Logs "Context menu patch is live" the first time any context menu is built, which separates "the
  patch is not installed" from "the patch ran and declined".
- Every skipped unit is logged once with the reason: destroyed, not a player unit, no taskforce,
  or `General/Enabled` false in the config.
- A missing player taskforce no longer hides the feature; it is treated as "cannot compare, allow".

## 0.3.0 — the menu shows up

Reported: after installing and enabling, the Weapon hatches section never appeared. Cause: the
mount filter required hatch data, and hatches are only built for launchers whose unit data declares
them (in `WeaponSystemLauncher`'s constructor), so on many ships every mount was filtered out and the
whole submenu was skipped.

- Every launcher is now listed whether or not its data has hatches, and mounts without hatch data
  say so in the label and tooltip instead of disappearing.
- The submenu appears even on units with no mounts (`Menu/ShowWhenNoMounts`, default on), with a
  report action, so "mod is running but this ship has nothing to open" is distinguishable from "mod
  is not running".
- If the localised Orders submenu cannot be identified, the menu is added at the top level and the
  reason is logged, instead of failing silently.
- Logs what it found per unit, once per distinct result: number of weapon systems, launchers, mounts
  with hatch control and hatch models, plus a status line per mount.
- Logs the package folder and the active settings at startup, to make activation problems obvious.
- `hatch-report.txt` now records whether each cell has open/close animations, which is the usual
  reason a hatch cannot move.

## 0.2.0 — real API

The game's hatch code was located in `Seapower-Scripts.dll` (see `docs/hatch-api.md`), so the
feature is now implemented against it directly instead of probing for a handle at runtime.

- Per-cell open/close uses `WeaponContainer.openHatches()` / `closeHatches()` — the same methods the
  game's launch sequence calls — and reads live state from `_areHatchesOpen` for the check marks.
- Mount covers (rail launchers and box launchers without per-cell hatches) use
  `WeaponSystem.openSystem()` / `closeSystem(delay)`.
- A manual order clears the game's pending auto-close (`_triggerCloseAnimation`) so a hatch does not
  slam shut a moment after opening.
- Menu items use named constructor arguments: `ContextMenuItem` has two constructors with only
  optional parameters, which makes positional calls ambiguous.
- Cells are labelled from the game's own `_containerIndex`, with the hatch count when a cell holds
  several lids.
- "Log hatch report" replaces the previous discovery dumps and writes `hatch-report.txt`.
- Removed the reflective probing layer, the assembly-wide API dump and the animator heuristics they
  fed; roughly a third of the code is gone.

## 0.1.0 — first build

- **Orders → Weapon hatches** submenu: per mount, per cell open/close, plus whole-mount and
  whole-ship actions.
- Cells derived from the game's own `WeaponSystem._containers`.
- Layered strategy chain for the unknown hatch handle (container member, Unity Animator, blueprint
  code animation, mount member), each step logged.
- Startup assembly dump and per-ship dump to identify the members.
- Anchor Chain plugin, HarmonyX patch on `ObjectBaseViewModel.GetContextMenuItems`, honours the
  mod-manager checkbox, BepInEx config, Windows and POSIX build scripts, portable SDK route.
