#!/usr/bin/env python3
"""Catch the compiler errors that have actually broken this project's builds.

Five kinds, every one of which has cost a round trip through a real machine:

1. A symbol is used but the namespace that declares it is not imported. CS0246, in a file that
   looks completely fine.
2. A mod type is referenced that no longer exists - e.g. a class deleted by a rewrite of a
   neighbouring file. CS0246, different cause.
3. A game type is *named* in code (typeof, a cast, default, new) that this project cannot see.
   CS0246 again. UnitRoles, MissionType, WaypointHeightState and WeaponStatus were all this, and
   each one cost a build; the answer now is to reach them by reflection instead.
4. A member is called on a mod type and that member no longer exists -
   "Capabilities does not contain a definition for ParametersOf". CS0117.
5. An accessor is called by name, unit.get_ThreatningMissile(). CS0571, every time.

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
    "WaypointControl", "FlightDeckControl",
    "HatchMenu", "FleetMenu", "FleetMenuPatch", "MenuRouting", "MenuReport",
    "HatchBay", "HatchControl", "HatchGroup", "ShipReport", "TrafficPanel",
    "FleetTicker", "Blink", "AutoArm", "SalvoPlan", "Breakaway", "Readiness",
    "ThreatAxis", "HeloPatterns", "SearchPattern", "Replenishment", "MerchantDisguise",
    "LocalControl", "SprintDrift",
]

# Game and library types this project is *proven* to be able to name, because a build that named
# them succeeded. A game type outside this list is flagged: naming it is a guess, and every guess so
# far has failed to compile. Reach it by reflection (see Interop/FlightDeckControl.cs) or add it here
# only after a build proves it.
PROVEN_TYPES = {
    # SeaPower, proven by a build
    "ObjectBase", "ObjectType", "GeoPosition", "WeaponSystem", "WeaponContainer",
    "CivilianRoute", "GameMain", "ObjectBaseParameters",
    # the game's UI assembly
    "ContextMenuItem", "DelegateCommand", "MapUnitViewModel", "ObjectBaseViewModel",
    "TrulyObservableCollection", "ReactiveProperty",
    # harmony
    "Harmony", "HarmonyMethod", "AccessTools",
    # BepInEx
    "ConfigFile", "ConfigEntry", "BaseUnityPlugin",
}

# Harmless, well-known BCL/Unity names. Nothing here is game-specific; they exist so the scan can
# tell "new Dictionary<...>" from "new UnitRoles(...)".
DOTNET_TYPES = {
    "Action", "Activator", "ArgumentException", "ArgumentNullException", "Array", "ArrayList",
    "Boolean", "Byte", "CancellationToken", "Char", "Comparison", "ConditionalWeakTable",
    "Convert", "DateTime", "Decimal", "Delegate", "Dictionary", "Double", "Enum", "EventArgs",
    "Exception", "FieldInfo", "File", "FileInfo", "Func", "GC", "Guid", "HashSet", "ICollection",
    "IComparable", "IDictionary", "IEnumerable", "IEnumerator", "IList", "IReadOnlyList",
    "IReadOnlyCollection", "Int16", "Int32", "Int64", "InvalidOperationException", "KeyValuePair",
    "Lazy", "List", "Math", "MemberInfo", "MethodInfo", "Nullable", "Object", "ParameterInfo",
    "Path", "PropertyInfo", "Queue", "Random", "Regex", "Single", "SortedDictionary",
    "StackTrace", "Stack", "String", "StringBuilder", "StringComparison", "StringSplitOptions",
    "TimeSpan", "Tuple", "Type", "UInt32", "ValueTuple", "Version", "Void",
    "BindingFlags", "ConstructorInfo", "Assembly", "Module", "TargetInvocationException",
    # Unity
    "GameObject", "GUILayout", "GUIStyle", "Input", "KeyCode", "Mathf", "MonoBehaviour",
    "Object", "Rect", "Texture2D", "Time", "Vector2", "Vector3",
}

# A name in a type position that is none of these is reported. Type positions are deliberately a
# narrow set of constructs: a method call `Foo(...)` is not one, so this cannot fire on a variable.
TYPE_POSITION = re.compile(r"\b(?:typeof|default)\s*\(\s*([A-Z][\w.]*)|\bnew\s+([A-Z][\w.]*)\s*[\\({<]")

# A declared member of a mod type: a method, property or field.
MEMBER = re.compile(
    r"\b(?:internal|public|private|protected)\s+"
    r"(?:(?:static|readonly|sealed|override|abstract|virtual|async|const|new|partial|unsafe)\s+)*"
    r"[\w<>?,\[\]\.]+\s+(\w+)\s*(?:[\(=\{;]|=>)")

ENUM_BODY = re.compile(r"\benum\s+(\w+)\s*(?::\s*[\w.]+\s*)?\{([^}]*)\}", re.S)

# owner.member, where the owner looks like a type: used to notice a deleted member.
MEMBER_USE = re.compile(r"(?<![\w.])([A-Z]\w*)\s*\.\s*(\w+)")

# Explicit accessor calls - unit.get_Foo() is CS0571 every single time.
ACCESSOR_CALL = re.compile(r"(?<![\w])(?:get|set)_[A-Z]\w*\s*\(")

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

    # Members of each mod type, so "Capabilities does not contain a definition for ParametersOf"
    # (CS0117) is caught here instead of by a two-minute compile.
    mod_members: dict[str, set[str]] = {name: set() for name in MOD_TYPES}
    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))
        members = set(MEMBER.findall(code))
        for body in ENUM_BODY.findall(code):
            for entry in body[1].split(","):
                word = entry.strip().split("=")[0].strip()
                if word.isidentifier():
                    members.add(word)
        for name in set(DECL.findall(code)):
            if name in mod_members:
                mod_members[name] |= members

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

        # A game type named in code: typeof(UnitRoles), (MissionType)x, default(WaypointHeightState).
        # Every one of those failed to compile, because the enum's namespace is not importable.
        named = {a or b for a, b in TYPE_POSITION.findall(code)}
        for name in sorted(named):
            if "." in name:
                continue          # fully qualified: the namespace is right there in the name
            root = name.split(".")[0]
            if root in declared or root in PROVEN_TYPES or root in DOTNET_TYPES:
                continue
            problems.append(
                f"{short}: names the type {name}, which no build has proved this project can see "
                f"- reach it through reflection instead (CS0246 if it is a guess)")

        # Accessor called by name: unit.get_ThreateningMissile() is CS0571.
        for match in ACCESSOR_CALL.finditer(code):
            problems.append(f"{short}: calls the accessor {match.group(0).strip('(')}() directly "
                            f"(CS0571) - read the property, or use MemberProbe")

        # A member called on a mod type that has no such member: CS0117.
        for owner, member in sorted(set(MEMBER_USE.findall(code))):
            if owner not in mod_members:
                continue
            if member in mod_members[owner] or member in declared:
                continue
            if owner in DECL.findall(code):
                continue
            problems.append(
                f"{short}: {owner}.{member} - {owner} has no member called {member} (CS0117)")

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

        # A field nothing ever writes is either a wiring mistake or dead weight; the compiler warns
        # about it (CS0649) and four of them turned out to be leftovers from removed features.
        everything = "\n".join(strip_noise(p.read_text(encoding="utf-8")) for p in files)
        for member in sorted(record_members):
            if not re.search(rf"\.\s*{member}\s*(?:[-+*/]?=(?!=))", everything):
                problems.append(
                    f"UnitState.cs: UnitRecord.{member} is never assigned - CS0649, and probably "
                    f"a field left behind by a feature that no longer uses it")
            if not re.search(rf"\.\s*{member}\b(?!\s*[-+*/]?=(?!=))", everything):
                problems.append(
                    f"UnitState.cs: UnitRecord.{member} is never read - it is written and forgotten")

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
