#!/usr/bin/env python3
"""Catch the compiler errors that have actually broken this project's builds.

Six kinds, every one of which has cost a round trip through a real machine:

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
6. A method is called by name that nothing in the project declares - a helper that was referenced
   from one place and never written. CS0103, and it reads exactly like the calls around it.
7. A member is called on a GAME type that the build has already proved absent, or a type is used
   from a namespace whose assembly this project does not reference. CS1061 and CS0234. Both are
   invisible to every other check here, because the game's own API is not in this repository.
8. A possibly-null expression passed as an argument (CS8604) - a warning, which is why it survived
   being "fixed": the first pass guarded the two calls it could see and left the one in the catch
   block, and the next build reported the same warning from a different line.
9. A compiled executable is committed. Not a compile error at all - a distribution one. A single
   unsigned .exe in the tree made browsers and antivirus products refuse the whole release archive
   ("couldn't download, virus detected"), so the mod never reached the people it is for.

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
    # The BCL. These were missing from this table until 0.24.1, and the cost was a failed build:
    # "List<> could not be found" in Multiplayer.cs, because the file had grown a List and a
    # Dictionary and only imported System, Reflection and Text. Every type here lives in exactly one
    # namespace, so there is no ambiguity to get wrong - unlike Object or Debug, which are excluded
    # on purpose because System and UnityEngine both declare them.
    "List": ["System.Collections.Generic"],
    "Dictionary": ["System.Collections.Generic"],
    "HashSet": ["System.Collections.Generic"],
    "SortedDictionary": ["System.Collections.Generic"],
    "Queue": ["System.Collections.Generic"],
    "Stack": ["System.Collections.Generic"],
    "KeyValuePair": ["System.Collections.Generic"],
    "IEnumerable": ["System.Collections.Generic"],
    "IEnumerator": ["System.Collections.Generic"],
    "ICollection": ["System.Collections.Generic"],
    "IList": ["System.Collections.Generic"],
    "IDictionary": ["System.Collections.Generic"],
    "IReadOnlyList": ["System.Collections.Generic"],
    "IReadOnlyCollection": ["System.Collections.Generic"],
    "StringBuilder": ["System.Text"],
    "Regex": ["System.Text.RegularExpressions"],
    "StringComparison": ["System"],
    "StringSplitOptions": ["System"],
    "DateTime": ["System"],
    "TimeSpan": ["System"],
    "Math": ["System"],
    "Convert": ["System"],
    "Guid": ["System"],
    "Lazy": ["System"],
    "Version": ["System"],
    "Type": ["System"],
    "Exception": ["System"],
    "Action": ["System"],
    "Func": ["System"],
    "BindingFlags": ["System.Reflection"],
    "Assembly": ["System.Reflection"],
    "MemberInfo": ["System.Reflection"],
    "MethodInfo": ["System.Reflection"],
    "PropertyInfo": ["System.Reflection"],
    "FieldInfo": ["System.Reflection"],
    "ParameterInfo": ["System.Reflection"],
    "ConstructorInfo": ["System.Reflection"],
    "Path": ["System.IO"],
    "File": ["System.IO"],
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
    "WaypointControl", "FlightDeckControl", "Inbound", "EcmControl", "DefensiveEcm", "OffensiveEcm", "SensorSet", "UnitFit", "MapProjection", "Selection",
    "EcmConeOverlay", "EcmBeam",
    "Multiplayer",
    "HatchMenu", "FleetMenu", "FleetMenuPatch", "MenuRouting", "MenuReport",
    "HatchBay", "HatchControl", "HatchGroup", "ShipReport",
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
    # Globals is the game's static world state: 0.24.x read Globals._playerTaskforce and shipped,
    # and 0.25.0 reads the enemy and neutral task forces off it the same way. Proved by a build.
    "Globals",
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
    "GameObject", "GUILayout", "GUIStyle", "GUI", "GUIUtility", "Input", "KeyCode", "Mathf",
    "MonoBehaviour", "Matrix4x4", "Object", "Rect", "Screen", "Texture2D", "TextureFormat",
    "Time", "Vector2", "Vector3", "Color", "Camera", "Transform", "Quaternion", "Resources",
    "Application", "Debug", "Font", "GUIContent", "GUISkin", "Event", "EventType",
    "FontStyle", "TextAnchor",
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


# ---------------------------------------------------------------------------------------------
# Locals used outside the method that declares them.
#
# This exists because of a build failure that nothing else here could see:
#
#     OffensiveEcm.cs(407,67): error CS0103: The name 'reach' does not exist in the current context
#
# The identifier was declared in BestJammerFor and used in LockOnContact, which is a scope error the
# brace counter, the namespace check and the member check all pass without noticing - and the sandbox
# this mod is written in has no compiler to catch it. So: collect the locals each method declares,
# and flag a camelCase name used in one method that is only declared as a local in another. Fields
# are excluded (they are not locals), which is what keeps the false-positive rate at zero.
# ---------------------------------------------------------------------------------------------

KEYWORDS = {
    "abstract", "add", "alias", "and", "as", "ascending", "async", "await", "base", "bool", "break",
    "by", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal",
    "default", "delegate", "descending", "do", "double", "dynamic", "else", "enum", "equals",
    "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "from",
    "get", "global", "goto", "group", "if", "implicit", "in", "init", "int", "interface",
    "internal", "into", "is", "join", "let", "lock", "long", "nameof", "namespace", "new", "not",
    "null", "object", "on", "operator", "or", "orderby", "out", "override", "params", "partial",
    "private", "protected", "public", "readonly", "record", "ref", "remove", "return", "sbyte",
    "sealed", "select", "set", "short", "sizeof", "stackalloc", "static", "string", "struct",
    "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
    "ushort", "using", "value", "var", "virtual", "void", "volatile", "when", "where", "while",
    "with", "yield",
}

METHOD_HEAD = re.compile(
    r"^\s*(?:(?:public|private|internal|protected|static|async|override|virtual|sealed|new|unsafe|extern|partial)\s+)+"
    r"[\w<>,\[\]\?\.\s]*?(\w+)\s*\([^;{}]*\)\s*$")

DECLARED = re.compile(
    r"(?<![\w.])([A-Za-z_][\w<>,\[\]\?\.]*)\s+([a-z_]\w*)\s*"
    r"(?==|;|,|\)|\]|\}|&&|\|\||\?|:|\sin\b|\bwhen\b)")
MULTI_DECL = re.compile(r"(?<![\w.])[A-Za-z_][\w<>,\[\]\?\.]*\s+[a-z_]\w*\s*=[^;]*;")
MORE_DECLS = re.compile(r",\s*([a-z_]\w*)\s*=(?!=)")
LAMBDA_ONE = re.compile(r"\(?\b([a-z_]\w*)\b\s*=>")
LAMBDA_MANY = re.compile(r"\(([^()]*)\)\s*=>")
USED = re.compile(r"(?<![\w.])[a-z_]\w*(?![\w])")

FIELD = re.compile(
    r"(?:private|internal|public|protected)\s+(?:static\s+)?(?:readonly\s+)?"
    r"[\w<>,\[\]\?\. ]+\s+(\w+)\s*(?:[;=]|=>)")


def method_bodies(code: str):
    """Yield (name, parameter list, body) for every method with a block body in this file."""
    lines = code.splitlines(keepends=True)
    offsets, running = [], 0
    for line in lines:
        offsets.append(running)
        running += len(line)

    for index, line in enumerate(lines):
        match = METHOD_HEAD.match(line)
        if not match:
            continue

        # The opening brace is on a following line, ignoring blank lines.
        start = None
        for probe in range(index + 1, min(index + 4, len(lines))):
            if lines[probe].strip().startswith("{"):
                start = offsets[probe] + lines[probe].index("{")
                break
        if start is None:
            continue

        parameters = line[line.index("(") + 1:line.rindex(")")]

        depth, position = 0, start
        while position < len(code):
            char = code[position]
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    yield match.group(1), parameters, code[start:position + 1]
                    break
            position += 1


def parameters_of(listing: str) -> set[str]:
    """The names a parameter list declares, however many commas are inside its generics."""
    names: set[str] = set()
    depth, part = 0, ""

    for char in listing + ",":
        if char in "<([":
            depth += 1
        elif char in ">)]":
            depth -= 1
        if char == "," and depth == 0:
            word = part.split("=")[0].strip()
            token = re.findall(r"[A-Za-z_]\w*", word)
            if token:
                names.add(token[-1])
            part = ""
            continue
        part += char

    return names


def locals_of(body: str) -> set[str]:
    names = {name for _, name in DECLARED.findall(body)}
    for statement in MULTI_DECL.findall(body):
        names.update(MORE_DECLS.findall(statement))
    names.update(LAMBDA_ONE.findall(body))
    for group in LAMBDA_MANY.findall(body):
        for part in group.split(","):
            word = part.strip()
            if word.isidentifier():
                names.add(word)
    return names


def used_names(body: str) -> set[str]:
    """Names the body reads, minus named arguments - "label:" in `new(label: x)` is a parameter
    name from another method, not a variable."""
    names: set[str] = set()

    for match in USED.finditer(body):
        start, end = match.span()

        # Far enough back to clear a newline and indentation: "new(\n    label: x)".
        before = body[max(0, start - 80):start].rstrip()
        after = body[end:end + 2].lstrip()

        # `(label: x)` and `, label: x` are argument names; a ternary's `a : b` never follows a
        # bracket or a comma directly, so this does not swallow real uses.
        if after.startswith(":") and after[1:2] != ":" and before[-1:] in {"(", ","}:
            continue

        names.add(match.group(0))

    return names


def scope_problems(files) -> list[str]:
    problems: list[str] = []

    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))
        short = path.relative_to(ROOT)

        fields = set(FIELD.findall(code))
        methods = list(method_bodies(code))
        if not methods:
            continue

        declared: dict[str, set[str]] = {}
        for name, parameters, body in methods:
            declared.setdefault(name, set()).update(locals_of(body))
            declared[name].update(parameters_of(parameters))

        for name, parameters, body in methods:
            own = locals_of(body) | parameters_of(parameters)
            for used in used_names(body):
                if used in KEYWORDS or used in own or used in fields:
                    continue
                for other, locals_ in declared.items():
                    if other == name or used not in locals_:
                        continue
                    problems.append(
                        f"{short}: '{used}' used in {name}() but it is a local of {other}() - CS0103")
                    break

    return problems


# ---------------------------------------------------------------------------------------------
# A method called by name that nothing in the project declares.
#
# This exists because of a build failure nothing else here could see:
#
#     FleetMenu.cs(107,40): error CS0103: The name 'AnyJammer' does not exist in the current context
#
# The helper was referenced from InstallContact and never written - an edit script had aborted
# before the step that added it. Every other check passed: the name is camelCased like a method,
# takes no arguments, and reads exactly like the calls around it. The tell is that no file declares
# it, which is what this asks.
#
# Only BARE calls are checked (`Foo(`), and only where the name starts with a capital and is not
# preceded by a type word, so declarations, constructors and dotted calls are all left alone. A
# small allowlist covers the members a Unity component inherits, which by definition are declared
# in an assembly this script cannot read.
# ---------------------------------------------------------------------------------------------

BARE_CALL = re.compile(r"(?<![\w.])([A-Z]\w*)\s*(?:<[^<>();]*>)?\s*\(")

# Members every MonoBehaviour/Component has, plus the ones BepInEx adds. Declared outside this
# project, so "nothing declares it" is true and harmless.
INHERITED = {
    "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy",
    "OnGUI", "OnApplicationQuit", "StartCoroutine", "StopCoroutine", "StopAllCoroutines",
    "Invoke", "InvokeRepeating", "CancelInvoke", "IsInvoking", "GetComponent", "GetComponents",
    "GetComponentInChildren", "GetComponentsInChildren", "GetComponentInParent", "TryGetComponent",
    "AddComponent", "Instantiate", "Destroy", "DestroyImmediate", "DontDestroyOnLoad",
    "FindFirstObjectByType", "FindAnyObjectByType", "FindObjectsByType", "FindObjectsOfTypeAll",
    "CompareTag", "SendMessage", "BroadcastMessage", "Equals", "GetHashCode", "ToString",
    "ReferenceEquals", "MemberwiseClone",
    "GetInstanceID", "Print", "LogInfo", "LogWarning", "LogError", "LogDebug", "LogMessage",
}


def undeclared_call_problems(files) -> list[str]:
    problems: list[str] = []

    declared_methods: set[str] = set()
    codes: dict[pathlib.Path, str] = {}

    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))
        codes[path] = code

        # Declarations: a name immediately before a parameter list, with a type word in front of
        # it. The type word is what tells `bool AnyJammer()` from a call to it.
        for match in BARE_CALL.finditer(code):
            before = code[max(0, match.start() - 40):match.start()]
            if re.search(r"[\w>\]?]\s+$", before):
                declared_methods.add(match.group(1))

    declared_methods.update(DECL.findall("\n".join(codes.values())))

    for path, code in codes.items():
        for match in BARE_CALL.finditer(code):
            name = match.group(1)

            before = code[max(0, match.start() - 40):match.start()]
            # `bool AnyJammer(` - a declaration, not a use.
            if re.search(r"[\w>\]?]\s+$", before):
                continue
            # `new Something(` - a constructor.
            if before.rstrip().endswith("new"):
                continue
            # `[ACPlugin(...)]` - an attribute, which is a type from another assembly by design.
            if before.rstrip().endswith("["):
                continue

            if name in declared_methods or name in INHERITED or name in KEYWORDS:
                continue

            line = code.count("\n", 0, match.start()) + 1
            problems.append(
                f"{path.relative_to(ROOT)}:{line}: {name}() is called but nothing in the project "
                f"declares it (CS0103)")

    return problems


# ---------------------------------------------------------------------------------------------
# Game members the compiler has already told us about.
#
# The mod calls dozens of methods on the game's own types, and nothing here knows what that build
# declares - try to list them and the list is wrong within a release. So this is not a list of what
# exists: it is a list of names the game's types were PROVEN not to have, by a build error, and the
# name that replaced them. A name invented by pattern ("everything has getX") lands here the moment
# it costs a build, and never again.
#
#   MapProjection.cs(207): error CS1061: 'ObjectBase' does not contain a definition for 'getUnitName'
#   MapProjection.cs(256): error CS1061: 'ObjectBase' does not contain a definition for 'getUnitName'
#
# Both of those were written by the same hand, in the same file, minutes apart: the first cost nothing
# to recognise in hindsight and everything to catch at the time.
# ---------------------------------------------------------------------------------------------

ABSENT_GAME_MEMBERS = {
    "getUnitName": "ObjectBase has getName() and DisplayNameShort; there is no getUnitName",
}


# ---------------------------------------------------------------------------------------------
# Namespaces whose assemblies this project does not reference.
#
# A project compiles against the assemblies it is given, and a Unity mod is given very few: the game's
# own DLLs plus what BepInEx needs. UnityEngine.UI is a PACKAGE, not part of UnityEngine.dll, so a type
# in it is not available just because it exists in the game's install. Reaching for one is CS0234 at
# the reference level and, worse, adding the reference to satisfy it would produce a DLL that fails to
# load on an install that does not carry that package.
#
#   MapProjection.cs(262): error CS0234: The type or namespace name 'Text' does not exist in the
#                          namespace 'UnityEngine.UI'
# ---------------------------------------------------------------------------------------------

NOT_REFERENCED = {
    "UnityEngine.UI": "the UI package is not referenced; draw with IMGUI (GUI.*) or use scene objects",
    "UnityEngine.EventSystems": "the event-system package is not referenced",
    "UnityEngine.Video": "the video module is not referenced",
    "UnityEngine.Animations": "the animation module is not referenced",
    "UnityEngine.AI": "the AI module is not referenced",
    "UnityEngine.Android": "an Android-only module",
}


def nullable_argument_problems(files) -> list[str]:
    """
    A possibly-null expression passed where the call cannot take null. CS8604, which is a warning -
    and a warning is how it survived being "fixed": the first pass guarded the two calls it could see
    in the body and left the one in the catch block, so the next build reported the same warning from
    a different line.

    The shapes that have actually happened are `Remove(entry.Key)` on a dictionary being walked and
    the same thing inside a catch cleaning up after a failure - the second matters more than the
    warning suggests, because the whole reason that line runs is that something already threw.
    """
    problems: list[str] = []

    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))
        lines = code.split("\n")

        for match in re.finditer(r"\b(\w+)\.Remove\(\s*([A-Za-z_]\w*)\.[A-Za-z_]\w*\s*\)", code):
            owner, keyed = match.group(1), match.group(2)

            # Only the dictionary being iterated: `foreach (var x in Y)` with `Y.Remove(x.Key)`.
            if not re.search(rf"foreach\s*\([^)]*\bin\s+{re.escape(owner)}\b", code):
                continue

            line = code.count("\n", 0, match.start()) + 1
            problems.append(
                f"{path.relative_to(ROOT)}:{line}: {owner}.Remove({keyed}.<key>) - a possible-null "
                f"argument (CS8604); hold the key in a local and test it first")

    return problems


def unreferenced_type_problems(files) -> list[str]:
    """A type used from a namespace whose assembly is not referenced, and game members proven absent."""
    problems: list[str] = []

    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))

        for absent, advice in ABSENT_GAME_MEMBERS.items():
            for match in re.finditer(rf"\.{absent}\b", code):
                line = code.count("\n", 0, match.start()) + 1
                problems.append(
                    f"{path.relative_to(ROOT)}:{line}: .{absent} - {advice} (CS1061)")

        for space, why in NOT_REFERENCED.items():
            for match in re.finditer(rf"\b{re.escape(space)}\.(\w+)", code):
                line = code.count("\n", 0, match.start()) + 1
                problems.append(
                    f"{path.relative_to(ROOT)}:{line}: {space}.{match.group(1)} - {why} (CS0234)")

    return problems


# ---------------------------------------------------------------------------------------------
# No executables in the tree.
#
# Not a compiler rule - a distribution one. 0.24.0's archive contained
# tools/analysis/testdata/hello.exe: a 3.5 KB compiled "hello world", committed as the fixture for
# the assembly reader's self-test. Every browser and antivirus product that saw it refused the
# download, and the report was "couldn't download - virus detected", which reads as though the mod
# were malicious. It was not; it was a test fixture in a folder nobody runs.
#
# The fixture is now base64 text, decoded to a temporary file at run time, so the repository carries
# no binary at all. This keeps it that way - and the allowlist is deliberate, so adding a script is a
# decision rather than an accident.
# ---------------------------------------------------------------------------------------------

EXECUTABLE_SUFFIXES = {
    ".exe", ".dll", ".msi", ".scr", ".com", ".pif", ".cpl", ".ocx", ".sys", ".drv",
    ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
    ".reg", ".lnk", ".msc", ".jar", ".apk", ".app", ".dmg",
    ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
}

# The batch and PowerShell files this repository is made of. They are text, they are readable in
# Notepad, and they are the point of the tooling - but only at these paths, and only these.
ALLOWED_SCRIPTS = {
    "build.cmd", "update.cmd", "uninstall.cmd", "check-source.cmd", "check-features.cmd",
    "diagnose.cmd", "build.ps1", "build.sh",
    "tools/build-portable.ps1", "tools/build-portable.cmd", "tools/build-portable.sh",
    "tools/dump-game-api.ps1", "tools/setup-venv.ps1", "tools/setup-venv.cmd",
    "tools/analysis/setup.ps1", "tools/analysis/setup.sh", "tools/analysis/survey.ps1",
    "tools/analysis/survey.cmd",
}

# Not scanned: not what a player downloads.
# Deliberately NOT tools/analysis: that is exactly where the last executable hid, and a skip list
# that excludes the folder the mistake was made in is a skip list that cannot learn.
SKIP_DIRS = {".git", ".github", "node_modules", "__pycache__", ".venv", "build", "obj", "bin",
             "dist", "out", ".vs", ".idea"}

# The needle the encoded-executable rule looks for, spelled in pieces: kept out of this file as a
# contiguous string so that the gate never flags its own source.
ENCODED_PE = b"TVqQ" + b"AAMAA" + b"AAE"

SCRIPT_SUFFIXES = {".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta"}


# A handful of names that mean something to the C# compiler even though this project never declares
# them. A member of a mod type with one of these names hides the real thing for the whole file, and
# the compiler says so with an error that is hard to connect to the cause: CS0119 ("'X.Screen(...)'
# is a method, which is not valid in the given context") arrived sixteen times from one method called
# Screen in an overlay that also draws to Screen.width.
SHADOWED_TYPES = {
    "Screen": "UnityEngine.Screen - every Screen.width and Screen.height in the file fails (CS0119)",
    "Application": "UnityEngine.Application",
    "Time": "UnityEngine.Time",
    "Debug": "UnityEngine.Debug",
    "Object": "System.Object",
    "Random": "UnityEngine.Random",
    "Camera": "UnityEngine.Camera",
    "Input": "UnityEngine.Input",
    "Mathf": "UnityEngine.Mathf",
    "Color": "UnityEngine.Color",
}


# A member declaration: one or more modifiers, then a type, then the name. Modifiers are
# REQUIRED - that is what stops a ternary's `? new Type(...)` from being read as a declaration.
DECLARATION = re.compile(
    r"^[ \t]*(?:(?:internal|private|public|protected|static|sealed|override|virtual|async|new|"
    r"readonly|extern|unsafe|partial|volatile)[ \t]+)+"
    r"(?:[\w.]+(?:<[^>()]*>)?(?:\[\])?\??)[ \t]+(?:([A-Za-z_]\w*)[ \t]*)?[({;=]",
    re.M)


def shadowing_problems(files) -> list[str]:
    """A method or property named after a type the file also uses."""
    problems: list[str] = []

    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))

        # Declarations only: `private static Vector2 Screen(GeoPosition ...)`, `internal bool Input`.
        #
        # The line has to START with a modifier. An earlier version allowed the modifiers to be
        # optional, and then `\s*[\w<>?\[\],\s]+?\s+(\w+)\s*[({;]` matched the second half of
        #     ? new Color(0.35f, 1f, 0.5f, ...)
        # in a conditional - the `?` was eaten by the type part and `Color` was reported as a member
        # hiding UnityEngine.Color. A false positive on a ternary is worse than no check, because a
        # gate that cries wolf gets switched off.
        for match in DECLARATION.finditer(code):
            name = match.group(match.lastindex or 1)
            if name not in SHADOWED_TYPES:
                continue

            line = code.count("\n", 0, match.start()) + 1
            problems.append(
                f"{path.relative_to(ROOT)}:{line}: a member called {name} hides "
                f"{SHADOWED_TYPES[name]} (CS0119 the moment the file uses it)")

    return problems


def _type_bodies(code: str):
    """Every type declaration in a file, with its body span. Brace scan, no regex nesting games."""
    found = []

    for match in re.finditer(r"\b(?:class|struct)\s+(\w+)", code):
        # The body opens at the next '{' after the header.
        brace = code.find("{", match.end())
        if brace < 0:
            continue

        depth = 0
        end = -1
        for i in range(brace, len(code)):
            if code[i] == "{":
                depth += 1
            elif code[i] == "}":
                depth -= 1
                if depth == 0:
                    end = i
                    break

        if end > 0:
            found.append((match.group(1), brace, end))

    return found


def _arities(body: str, name: str) -> set[int]:
    """How many parameters each declaration of `name` in this body takes."""
    found = set()

    for match in re.finditer(rf"\b{name}\s*\(", body):
        head = body[max(0, match.start() - 80):match.start()].rstrip()

        # `=> ` ends with '>', which the type-word test below would take for a declaration. An
        # expression body is the commonest shape in this project, so it is handled first.
        if head.endswith("=>") or head.endswith("="):
            continue
        if not re.search(r"[\w>\]?]\s+$", head + " "):
            continue

        close = _matching(body, match.end() - 1)
        if close < 0:
            continue

        inner = body[match.end():close].strip()
        found.add(0 if not inner else inner.count(",") + 1)

    return found


def _call_arities(body: str, name: str) -> set[int]:
    """How many arguments each *call* to `name` in this body passes."""
    found = set()

    for match in re.finditer(rf"\b{name}\s*\(", body):
        head = body[max(0, match.start() - 80):match.start()].rstrip()
        if head.endswith("=>") or head.endswith("="):
            pass                           # an expression body: still a declaration, skipped above
        elif re.search(r"[\w>\]?]\s+$", head + " "):
            continue                       # a declaration, handled above
        if head.endswith("new"):
            continue

        close = _matching(body, match.end() - 1)
        if close < 0:
            continue

        inner = body[match.end():close].strip()
        found.add(0 if not inner else inner.count(",") + 1)

    return found


def _matching(body: str, open_paren: int) -> int:
    depth = 0
    for i in range(open_paren, len(body)):
        if body[i] == "(":
            depth += 1
        elif body[i] == ")":
            depth -= 1
            if depth == 0:
                return i
    return -1


METHOD_DECL = re.compile(
    r"^\s*(?:(?:internal|private|public|protected|static|sealed|override|virtual|async|"
    r"readonly|extern|unsafe|new|partial)\s+)+[\w<>?\[\],.\s]+?\s(\w+)\s*\(", re.M)


def nested_shadowing_problems(files) -> list[str]:
    """A nested type declaring a method the enclosing type also declares.

    Not a style point - CS1501. Calls inside the nested type resolve to its own member first, so a
    nested class whose `Add(unit)` shares a name with the enclosing type's `Add(list, item)` makes
    every call to the enclosing one a "no overload takes 2 arguments" error. That cost a build on
    0.27.0; this checks it with a brace scan instead of another compile.
    """
    problems: list[str] = []

    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))
        types = _type_bodies(code)

        for outer_name, outer_open, outer_close in types:
            for inner_name, inner_open, inner_close in types:
                # Directly nested only: inside the outer body, and not inside a third type.
                if not (outer_open < inner_open < inner_close < outer_close):
                    continue

                inside_another = False
                for _, other_open, other_close in types:
                    if other_open == outer_open:
                        continue
                    if outer_open < other_open < inner_open and inner_close < other_close < outer_close:
                        inside_another = True
                        break
                if inside_another:
                    continue

                outer_body = code[outer_open:inner_open] + code[inner_close:outer_close]
                inner_body = code[inner_open:inner_close]

                outer_methods = set(METHOD_DECL.findall(outer_body))
                inner_methods = set(METHOD_DECL.findall(inner_body))

                # A constructor is named after its type: not a shadowing member.
                inner_methods.discard(inner_name)
                inner_methods.discard(outer_name)

                for shared in sorted(outer_methods & inner_methods):
                    # Only a real error when the nested type calls the name with a shape its own
                    # overloads cannot take: member lookup inside a nested type stops at that type,
                    # so a 2-argument call to a name it declares with 1 parameter is CS1501.
                    outer_arity = _arities(outer_body, shared)
                    inner_arity = _arities(inner_body, shared)

                    for called in _call_arities(inner_body, shared):
                        if called in inner_arity:
                            continue
                        if called not in outer_arity:
                            continue

                        line = code.count("\n", 0, inner_open + inner_body.find(shared)) + 1
                        problems.append(
                            f"{path.relative_to(ROOT)}:{line}: '{shared}' is called with {called} "
                            f"argument(s) inside the nested {inner_name}, which declares it with "
                            f"{'/'.join(str(a) for a in sorted(inner_arity))} - the enclosing "
                            f"{outer_name} version is not in scope there (CS1501). Rename one")

    return problems


def binary_problems() -> list[str]:
    problems: list[str] = []

    for path in sorted(ROOT.rglob("*")):
        if not path.is_file():
            continue

        relative = path.relative_to(ROOT)
        if any(part in SKIP_DIRS for part in relative.parts):
            continue

        suffix = path.suffix.lower()
        posix = relative.as_posix()

        if suffix in SCRIPT_SUFFIXES:
            if posix not in ALLOWED_SCRIPTS:
                problems.append(f"{relative}: is a script a scanner will flag and it is not one of "
                                f"the repository's own tools - add it to ALLOWED_SCRIPTS "
                                f"deliberately, or do not commit it")
            continue

        if suffix and suffix in EXECUTABLE_SUFFIXES:
            problems.append(f"{relative}: is a compiled or packaged binary ({suffix}) - one of these "
                            f"in the tree makes browsers and antivirus products refuse the release "
                            f"archive")
            continue

        # A PE with no suffix at all is still a PE: trust the header, not the name.
        try:
            head = path.read_bytes()[:4096]
        except OSError:
            continue

        if len(head) > 8 and head[:2] == b"MZ":
            problems.append(f"{relative}: is a Windows executable (MZ header) - do not commit it")
            continue

        # An encoded one is the same problem wearing a hat: base64's spelling of an "MZ" header plus
        # the DOS stub after it. A text file carrying that is a packed executable however harmless
        # it looks - exactly the pattern a scanner is trained to find, and exactly what this
        # repository tried as its first fix. The needle is assembled from pieces below so that this
        # file does not itself contain the contiguous header it is looking for.
        if ENCODED_PE in head:
            problems.append(f"{relative}: contains a base64-encoded Windows executable - scanners "
                            f"flag that as readily as the binary itself; do not commit it")

    return problems


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

        # Names this file declares itself: a type of that name needs no import, and neither does a
        # member - ModInfo.cs has `const string Version`, so `Version` is a field here, not
        # System.Version. Skipping the symbol when the file owns the name is the cheap, safe answer;
        # the cost is a missed import in the rare file that both declares and needs the type.
        own_names = set(MEMBER.findall(code)) | set(DECL.findall(code))

        missing: dict[str, list[str]] = {}
        for symbol, namespaces in SYMBOLS.items():
            if symbol in own_names:
                continue

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
        record_members = set(re.findall(r"internal\s+[\w<>?\[\]]+\s+(\w+)\s*(?:=\s*[^;]+)?;",
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

    # A ConfigEntry declared, read and never *bound* is CS0649 - and it is a feature whose setting
    # silently falls back to its default, which is worse than the warning. This is how
    # ModSettings.lidarSeconds was caught only by a real compile.
    settings = SRC / "SeaPowerAdvanced" / "ModSettings.cs"
    if settings.exists():
        text = settings.read_text(encoding="utf-8")
        declared = set(re.findall(r"private static ConfigEntry<[^>]+>\?\s+(\w+);", text))
        bound = set(re.findall(r"\b(\w+)\s*=\s*file\.Bind\(", text))

        for field in sorted(declared - bound):
            problems.append(
                f"ModSettings.cs: ConfigEntry '{field}' is never assigned - CS0649, and the setting "
                f"silently falls back to its default. Add a file.Bind for it")

    problems.extend(scope_problems(files))
    problems.extend(undeclared_call_problems(files))
    problems.extend(unreferenced_type_problems(files))
    problems.extend(nullable_argument_problems(files))
    # Reflection calls that exist on FieldInfo/PropertyInfo/MethodInfo but NOT on MemberInfo, the
    # abstract base. Holding a GetField() result in a MemberInfo and calling GetValue on it is
    # CS1061 - the error that stopped the 0.27.0 build in DatalinkControl.
    reflection_only = ("GetValue", "SetValue", "GetGetMethod", "GetSetMethod", "Invoke",
                       "GetIndexParameters", "IsInitOnly", "IsLiteral", "CanWrite")
    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))

        # A variable typed MemberInfo? (or MemberInfo) ...
        for match in re.finditer(r"\bMemberInfo\??\s+(\w+)\s*=", code):
            name = match.group(1)
            after = code[match.end():match.end() + 600]

            for call in reflection_only:
                if re.search(rf"\b{name}\.{call}\b", after):
                    line = code.count("\n", 0, match.start()) + 1
                    problems.append(
                        f"{path.relative_to(ROOT)}:{line}: MemberInfo '{name}' used with .{call} - "
                        f"that member is on FieldInfo/PropertyInfo/MethodInfo, not on MemberInfo "
                        f"(CS1061). Hold it as the concrete type, or go through MemberProbe")

    # A keyboard shortcut that only exists on the old Input Manager. A build running the new Input
    # System only throws from Input.GetKeyDown, so a hotkey wired that way alone does nothing at all,
    # with the throw swallowed by the same try/catch that keeps the overlay alive. The mod already
    # learned this on the civilian-traffic panel (0.11.0) and then repeated it in the LIDAR overlay,
    # which is exactly why it is a rule now.
    for path in files:
        code = strip_noise(path.read_text(encoding="utf-8"))
        if "Input.GetKeyDown" not in code and "Input.GetKeyUp" not in code:
            continue

        if "EventType.KeyDown" in code:
            continue

        line = code.count("\n", 0, code.index("Input.GetKey")) + 1
        problems.append(
            f"{path.relative_to(ROOT)}:{line}: the shortcut is read with Input.GetKey* and nowhere "
            f"else - on a build running the new Input System only, that throws and the key does "
            f"nothing; also check Event.current.keyCode in OnGUI")

    problems.extend(shadowing_problems(files))
    problems.extend(nested_shadowing_problems(files))
    problems.extend(binary_problems())

    if not problems:
        print(f"clean: {len(files)} file(s), no missing namespaces, UnitRecord consistent, "
              f"no out-of-scope locals, no undeclared calls, no absent game members, "
              f"no unreferenced assemblies, no possible-null arguments, no binaries committed")
        return 0

    print()
    for problem in problems:
        print("  " + problem)
    print()
    print(f"{len(problems)} problem(s) across {len(files)} file(s)")
    return 0 if args.fix else 1


if __name__ == "__main__":
    sys.exit(main())
