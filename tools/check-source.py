#!/usr/bin/env python3
"""Catch the compiler errors that have actually broken this project's builds.

Two kinds, both of which have cost a round trip through a real machine:

1. A symbol is used but the namespace that declares it is not imported. The build stops with a
   screen of CS0246 errors in a file that looks completely fine.
2. A type is referenced that no longer exists - e.g. a class deleted by a rewrite of a neighbouring
   file. Same error, different cause.

The script never needs the game's DLLs, which is the point: it runs anywhere, so it can be checked
before handing a build to somebody else.

    python tools/check-source.py            # report
    python tools/check-source.py --fix      # also add the missing using directives

Exit code 0 when clean, 1 when something was found.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src"

# symbol -> the namespaces that would make it resolve. More than one is accepted when the answer is
# not certain (TrulyObservableCollection is shipped in UniRx by some builds, in the UI assembly by
# others), because a false alarm here would be as expensive as a missed one.
SYMBOLS: dict[str, list[str]] = {
    # the game's UI layer
    "ContextMenuItem": ["SeapowerUI.ViewModels", "SeapowerUI"],
    "MapUnitViewModel": ["SeapowerUI.ViewModels"],
    "ObjectBaseViewModel": ["SeapowerUI.ViewModels"],
    # Proven by ModActivation.cs, which uses it with only "using SeaPower;" and has always built.
    "TrulyObservableCollection": ["SeaPower", "UniRx", "SeapowerUI.ViewModels"],
    "DelegateCommand": ["SeapowerUI.ViewModels", "SeapowerUI", "UniRx", "System.Windows.Input"],
    # UniRx
    "ReactiveProperty": ["UniRx"],
    "CompositeDisposable": ["UniRx"],
    # Harmony
    "Harmony": ["HarmonyLib"],
    "HarmonyMethod": ["HarmonyLib"],
    "AccessTools": ["HarmonyLib"],
    # BepInEx
    "BaseUnityPlugin": ["BepInEx"],
    "ConfigFile": ["BepInEx.Configuration"],
    "ConfigEntry": ["BepInEx.Configuration"],
    "Paths": ["BepInEx"],
    "ManualLogSource": ["BepInEx.Logging"],
    # Unity
    "MonoBehaviour": ["UnityEngine"],
    "GameObject": ["UnityEngine"],
    "GUILayout": ["UnityEngine"],
    "KeyCode": ["UnityEngine"],
    "Mathf": ["UnityEngine"],
    "Time": ["UnityEngine"],
    "Input": ["UnityEngine"],
    # the game
    "ObjectBase": ["SeaPower"],
    "WeaponSystem": ["SeaPower"],
    "WeaponContainer": ["SeaPower"],
    "CivilianRoute": ["SeaPower"],
    "GeoPosition": ["SeaPower"],
    "Taskforce": ["SeaPower"],
    "Globals": ["SeaPower"],
    "SensorData": ["SeaPower"],
    "MissionManager": ["SeaPower"],
}

# Every type this project defines and other files depend on. Kept explicit rather than derived, so
# that deleting the declaration is noticed instead of silently shrinking the list.
MOD_TYPES = [
    "Boot", "ModInfo", "ModLog", "ModSettings", "ModFolders", "ModActivation",
    "Capabilities", "Capability", "CapabilityReport", "MemberProbe", "SensorControl",
    "GameWorld", "UnitState", "UnitRecord", "WeaponStatusControl",
    "HatchMenu", "FleetMenu", "FleetMenuPatch", "MenuRouting", "MenuReport",
    "HatchBay", "HatchControl", "HatchGroup", "ShipReport", "TrafficPanel",
    "FleetTicker", "Blink", "AutoArm", "SalvoPlan", "Breakaway", "Readiness",
    "ThreatAxis", "HeloPatterns", "SearchPattern", "Replenishment", "MerchantDisguise",
    "LocalControl", "SprintDrift",
]

USING = re.compile(r"^\s*using\s+([\w.]+)\s*;", re.M)
DECL = re.compile(r"\b(?:class|struct|enum|interface|record)\s+([A-Za-z_]\w*)")


def strip_noise(text: str) -> str:
    """Remove comments and string literals so symbol scans see code only."""
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    text = re.sub(r"^\s*//.*$", " ", text, flags=re.M)
    text = re.sub(r"//.*$", " ", text, flags=re.M)
    text = re.sub(r'"(?:[^"\\]|\\.)*"', '""', text)
    text = re.sub(r"'(?:[^'\\]|\\.)*'", "''", text)
    return text


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fix", action="store_true", help="add missing using directives")
    args = parser.parse_args()

    files = sorted(SRC.rglob("*.cs"))
    if not files:
        print("no C# files found under src/")
        return 1

    declared: set[str] = set()
    for path in files:
        declared.update(DECL.findall(strip_noise(path.read_text(encoding="utf-8"))))

    problems: list[str] = []

    for path in files:
        raw = path.read_text(encoding="utf-8")
        code = strip_noise(raw)
        usings = set(USING.findall(raw))
        short = path.relative_to(ROOT)

        missing: dict[str, list[str]] = {}
        for symbol, namespaces in SYMBOLS.items():
            # Not preceded by a dot: "UnityEngine.Time" is fully qualified and needs no import, and
            # counting it would be a false alarm on correct code.
            if symbol not in declared and not re.search(rf"(?<![\w.]){symbol}\b", code):
                continue
            # A type this file declares itself needs no import.
            if symbol in DECL.findall(code):
                continue
            if any(ns in usings for ns in namespaces):
                continue
            missing[symbol] = namespaces

        if missing:
            for symbol, namespaces in sorted(missing.items()):
                problems.append(
                    f"{short}: uses {symbol} but imports none of {', '.join(namespaces)}")

            if args.fix:
                lines = raw.split("\n")
                last = max((i for i, line in enumerate(lines) if line.startswith("using ")), default=-1)
                existing = {line.strip() for line in lines if line.startswith("using ")}
                additions: list[str] = []
                for symbol, namespaces in sorted(missing.items()):
                    for ns in namespaces:
                        directive = f"using {ns};"
                        if directive in existing or directive in additions:
                            continue
                        # Only the most likely namespace is added; the rest stay in the report.
                        additions.append(directive)
                        break
                if additions:
                    lines[last + 1:last + 1] = additions
                    path.write_text("\n".join(lines), encoding="utf-8")
                    print(f"{short}: added {', '.join(a[6:-1] for a in additions)}")

        # The mod's own types must still exist. This is the check that would have caught a rewrite
        # of the capability registry deleting the Capability class and leaving a dozen CS0246 errors.
        for name in MOD_TYPES:
            if name in declared and name not in DECL.findall(code):
                continue
            if name in DECL.findall(code):
                continue
            if not re.search(rf"(?<![\w.]){name}\b", code):
                continue
            problems.append(f"{short}: references mod type {name}, which is not declared anywhere")

    # UnitRecord is the mod's shared state bag and every feature reads it, so a member renamed in
    # one file and used in another is a compile error waiting to happen. Checked explicitly because
    # it cannot be seen without a compiler.
    unit_state = SRC / "SeaPowerAdvanced" / "Interop" / "UnitState.cs"
    if unit_state.exists():
        record_members = set(re.findall(r"internal\s+[\w<>?\[\]]+\s+(\w+)\s*;",
                                       unit_state.read_text(encoding="utf-8")))
        for path in files:
            code = strip_noise(path.read_text(encoding="utf-8"))
            for member in set(re.findall(r"\brecord\.(\w+)", code)):
                if member not in record_members:
                    problems.append(
                        f"{path.relative_to(ROOT)}: reads UnitRecord.{member}, which is not a member")

    if not problems:
        print(f"clean: {len(files)} file(s), no missing namespaces, UnitRecord consistent")
        return 0

    print()
    for problem in problems:
        print("  " + problem)
    print()
    print(f"{len(problems)} problem(s) across {len(files)} file(s)")
    return 0 if args.fix else 1


if __name__ == "__main__":
    sys.exit(main())
