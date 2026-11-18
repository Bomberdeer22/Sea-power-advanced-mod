# API analysis toolchain

Reads Sea Power's `Seapower-Scripts.dll` and answers questions about it: what types exist, what a
member's signature and accessibility are, and — most usefully — **which method calls what**.

```bat
tools\analysis\survey.cmd                 :: full survey -> tools\analysis\analysis-report.txt
tools\analysis\survey.cmd readiness       :: which planned features are buildable
tools\analysis\survey.cmd selftest        :: prove the reader still works
```

```bash
tools/analysis/survey.sh                  # same, on Linux / Steam Proton
```

## Why this exists

The mod is written against an undocumented, moving target. Every feature so far hinges on details
that are not in any guide — whether `MapUnitViewModel.GetContextMenuItems` delegates to
`ObjectBaseViewModel`'s (it does not), whether `WeaponContainer.openHatches()` returns the new state
(it does not), that `_maxUnits` on a civilian route is a concurrency limit rather than a total.

Those answers come from the assembly, not from guessing. This directory makes that repeatable:
the same questions can be asked again after a game patch, and a "does the game support this?"
question gets an answer with evidence attached instead of a promise.

## What it does and does not do

| | |
|---|---|
| Reads | Type definitions, fields, methods, signatures, access flags, enum values, constant tables, nested-type relationships, assembly references, and method bodies (IL). |
| Does not | Load the assembly, resolve its references, run any game code, or need the game open. Nothing is written outside this folder except the report. |

That matters for a Unity game: reflection would have to resolve `UnityEngine`, `Noesis`, `UniRx`
and every other dependency before it could list a single type. Metadata plus IL needs only the file.

## Quick start

**Windows** — nothing to install by hand. `survey.cmd` creates a private Python environment on
first run (it needs Python 3 on the machine; `survey.cmd` says so if it cannot find it).

```bat
cd "C:\Users\you\Downloads\Sea-power-advanced-mod"
tools\analysis\survey.cmd
```

It writes `tools\analysis\analysis-report.txt`. **Send that file back** — it is the evidence used to
write the next feature.

The DLL is looked for at the usual Steam path. If the game is installed somewhere else:

```bat
tools\analysis\survey.cmd "D:\Steam\steamapps\common\Sea Power\Sea Power_Data\Managed\Seapower-Scripts.dll"
```

**Linux / Proton**:

```bash
./tools/analysis/survey.sh /path/to/Sea Power_Data/Managed/Seapower-Scripts.dll
```

PowerShell execution policy blocking `.ps1`? Use `survey.cmd` (batch files are unaffected), or
`powershell -ExecutionPolicy Bypass -File tools\analysis\survey.ps1`.

## Commands

Run them with `tools\analysis\.venv\Scripts\python.exe tools\analysis\analyze.py <command>` (or
`tools/analysis/.venv/bin/python tools/analysis/analyze.py`).

| Command | Answers |
|---|---|
| `brief` | The whole survey: topics, member dumps, call sites, enums, type inventory. |
| `types <text>` | Which types have `<text>` in their name. |
| `member <Type>` | Every field and method of one type, with access, modifiers, signature and whether it has a body. |
| `il <Type> <Method>` | Disassemble one method, ILDasm style, with member names resolved. |
| `find <text>` | Members whose name contains `<text>`. |
| `find <text> --callsites` | **Every method that calls something matching `<text>`** — this is how "who does the game actually invoke?" gets answered. |
| `enums <Type>` | An enum's members and their numeric values. |
| `readiness` | **Which of the planned features this build can support**, capability by capability. Add `--fast` to skip the call-site scans. |
| `selftest` | Verifies the reader against a known assembly (see below). |

Examples:

```bat
python analyze.py member SeaPower.CivilianRoute
python analyze.py il SeaPower.CivilianRoute SpawnUnit
python analyze.py find "_radarsActive" --callsites
python analyze.py --dll "D:\...\Seapower-Scripts.dll" types WeaponSystem
```

## What the report contains

`brief` writes one text file with these sections:

* **header** — file, assembly name and version, type/method/field counts, referenced assemblies.
  The version pins down which game build the answers belong to.
* **topics** — `SENSORS & DETECTION`, `WEAPONS & FIRING`, `ORDERS & TASKING`, `MAP DRAWING`,
  `UNITS & IDENTIFICATION`, `MENUS & UI`. Each lists the types whose names match the topic, with
  their matching members: access, modifiers, full signature, and whether the method has an IL body
  (a method with no body is external or abstract — important to know before planning to call it).
* **call sites** — real call sites for members like `WeaponSystem::Fire`, `::setEmission`,
  `_radarsActive`, `ObjectBase::setOrder`. `A.B -> C.D` means `A.B` calls `C.D`: that is the path the
  game itself takes, which is the safest thing to copy.
* **enums** — numeric values, because flags stored in fields need the numbers.
* **type inventory** — names, so a type can be found even when the topic patterns miss it.

Adding a topic is one entry in `TOPICS` in `survey.py`: a type-name pattern, a member-name pattern
and a sentence saying what the feature needs to learn.

## The readiness probe

`readiness` answers "can this game support feature X?" before any code is written for it. Each
planned feature is broken into the capabilities it needs, and each capability is one question with a
concrete answer in the assembly:

```
  [READY  ] Radar blink cycling
            prior guess: likely
              ok   an emitter can be switched on and off
                   SeaPower.SensorSystem::set_Active
  [BLOCKED] Battle stations fatigue
            prior guess: unlikely
             MISS  crew state exists as a modelled thing
```

`READY` = every required capability found, `PARTIAL` = some, `BLOCKED` = at least one missing. The
matched names are printed so a match can be judged: the probe looks for *names*, which answers "does
this concept exist?" and not "does it work the way we need?". A `READY` verdict means *worth
starting*.

The feature list and the reasoning behind each verdict live in
[docs/feature-plan.md](../../docs/feature-plan.md); the checks themselves are in `features.py`, one
entry per feature, so a new feature is a new entry there.

## Is the reader trustworthy?

`analyze.py selftest` reads `testdata/hello.exe` and compares its disassembly against
`testdata/hello.ildasm.txt`, the listing ILDasm produced for the same file:

```
ok   Main IL matches ILDasm         IL_0000 nop | IL_0001 ldstr | IL_0006 call | IL_000b nop | IL_000c ret
ok   ldstr decoded                  'Hello World!'
ok   call target decoded            System.Console::WriteLine
ok   offset/opcode pairs present in ILDasm listing   5/5
```

Beyond that it was exercised on two real assemblies:

| Assembly | Size | Types | Methods | Result |
|---|---|---|---|---|
| `FSharp.Core.dll` | 1.6 MB | 733 | 9,492 | generics, nested types, enum constants and fields all decode; survey runs in ~4 s |
| `Mono.Android.dll` | 24 MB | 5,000+ | 133,620 | full survey in ~100 s; no unhandled case |

Two bugs found by those runs are worth recording, because they are the kind that produce
plausible-looking nonsense rather than an error:

* A **generic method signature** carries its own generic-parameter count before the parameter
  count (ECMA-335 II.23.2.1). Skipping it shifts every field by one, so a return type reads as
  `void`. Fixed.
* The **internal** flag is spelled `fdAssembly` on fields but `mdAssem` on methods in dnfile's
  flags; matching only the first showed `?` for internal methods. Fixed.

## Limits

* **Static only.** IL says what the game *calls*, not what it does at runtime with your exact save,
  mission or mod load order. Anything that depends on live state still needs a log from a real run.
* **Obfuscation or trimming** would make names meaningless; Sea Power ships readable metadata, but
  a future patch could change that.
* **Bodies that fail to decode are skipped, not fatal** — a survey that loses one method still
  answers the rest. Call-site counts can therefore be a hair lower than reality on exotic IL.
* **Loading dominates on a large assembly.** On a 24 MB assembly the first load takes ~100 s, almost
  all of it inside dnfile parsing the PE and metadata tables — not this code. A game-sized assembly
  is much quicker, and nothing else in the toolchain is close to that cost.
* Python 3 (3.9+) is required; the Windows wrapper creates `.venv` inside this folder and installs
  `dnfile` and `dncil` from PyPI. No admin rights, nothing system-wide; delete `.venv` to undo it.

## Files

| File | Purpose |
|---|---|
| `gameapi.py` | Metadata and IL reader (`Assembly`): types, members, signatures, enum values, call sites. |
| `features.py` | The readiness probe: the planned features and the capabilities each needs. |
| `survey.py` | The report: topics, call-site buckets, enum dumps, inventory. |
| `analyze.py` | Command line. |
| `selftest.py` | The check described above. |
| `setup.ps1` / `setup.sh` | Create `.venv` and install dependencies. |
| `survey.ps1` / `survey.cmd` / `survey.sh` | Setup if needed, then run the survey. |
| `testdata/hello.exe` | 3.5 KB compiled "hello world" from the dnfile project's test files (MIT), used by the self-test. |
| `testdata/hello.ildasm.txt` | ILDasm's listing of the same file — the expected output. |
