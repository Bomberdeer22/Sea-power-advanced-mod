"""
The survey: one text file that answers "what can this game actually do?".

`analyze.py brief` writes it. The point is that the output is small enough to send back and read,
and repeatable, so an API question is answered by evidence from the shipped assembly instead of a
guess. Sections are driven by the topology below, not by hard-coded type names: each topic lists
type names matching a pattern and then dumps the members of the matches, so a renamed class still
shows up.

Adding a topic for a new feature means adding one entry to TOPICS.
"""

from __future__ import annotations

import re
import sys

from gameapi import Assembly, access, modifiers

# topic -> (type-name pattern, member-name pattern, notes)
TOPICS: dict[str, tuple[str, str, str]] = {
    "SENSORS & DETECTION": (
        r"Sensor|Radar|Sonar|Detect|Emission|RWR|ESM|Datalink|Signature|Track",
        r"Range|Detect|Active|Passive|Emission|Radiat|Scan|Track|Identify|Search|Sweep|Power|On$|Off$",
        "What each unit can see, how far, and whether it is radiating. Threat rings need the "
        "per-sensor range; EMCON needs the on/off switch.",
    ),
    "WEAPONS & FIRING": (
        r"WeaponSystem|WeaponContainer|WeaponParameter|Ammunition|Launcher|Missile|Salvo|Reload|Magazine",
        r"Fire|Launch|Salvo|Reload|Ammo|Magazine|Ready|Target|Range|Engage|Shoot|Order",
        "How a mount fires, whether one cell can fire alone (salvo planner), and where the ammo "
        "count lives (reload and magazine limits).",
    ),
    "ORDERS & TASKING": (
        r"ObjectBase$|Order|Taskforce|GroupOfUnits|MissionManager",
        r"Order|Weapon|Fire|Attack|Engage|ROE|Free|Tight|Hold|Target",
        "The order surface: how the game tells a unit to attack, and whether a weapon-status or "
        "rules-of-engagement switch exists.",
    ),
    "MAP DRAWING": (
        r"Drawing|MapControl|MapView|MapLayer|Graphic|Shape|Ring|Circle",
        r"Add|Draw|Graphics|Color|Preset|Circle|Ring|Radius|Layer|Update",
        "How to draw something on the map yourself — needed to render threat rings. The civilian "
        "zone tool already uses PolygonDrawing on MapDrawingLayer.Graphics.",
    ),
    "UNITS & IDENTIFICATION": (
        r"ObjectBase|Vehicle|Identification|Identif|Contact|UnitType",
        r"Identif|Known|Class|Type|Name|Side|Destroy|Damage|Health|Integrity",
        "Contact identity and health, for identification helpers and for the 'is this thing alive' "
        "checks a planner needs before it commits a salvo.",
    ),
    "MENUS & UI": (
        r"ViewModel|ContextMenu|ContextMenuItem|DelegateCommand",
        r"GetContextMenuItems|Command|Invoke|Label|IsChecked|Enabled",
        "The click surface. Already used by the hatch menu and the civilian panel.",
    ),
}

# Members whose call sites are worth listing: they show the real path the game itself takes.
CALL_SITE_NEEDLES = [
    "WeaponSystem::Fire",
    "WeaponContainer::Fire",
    "::Launch",
    "::setEmission",
    "::setActive",
    "_radarsActive",
    "ObjectBase::setOrder",
    "::openHatches",
]

ENUM_CANDIDATES = [
    "ObjectBase.ObjectType", "Target", "Side", "OrderType", "WeaponStatus",
    "SensorType", "RadarType", "SonarType", "DetectionType", "PatrolAreaType",
]

MAX_TYPES_PER_TOPIC = 45
MAX_MEMBERS_PER_TYPE = 90
MAX_CALL_SITE_ROWS = 30
MAX_TYPE_LIST = 260


def _matches(pattern: str, value: str) -> bool:
    return re.search(pattern, value, re.IGNORECASE) is not None


def _member_line(kind: str, name: str, type_text: str, flags, prefix: str) -> str:
    return f"    {kind} {access(flags, prefix):<18} {modifiers(flags, prefix):<20} {type_text} {name}"


def build_report(assembly: Assembly, verbose_types: bool = False) -> str:
    out: list[str] = []
    add = out.append

    info = assembly.summary()
    add("=" * 100)
    add("SEA POWER API SURVEY")
    add("=" * 100)
    add(f"file            {info['path']}")
    add(f"assembly        {info['assembly']}  {info['version']}")
    add(f"types           {info['types']}")
    add(f"methods         {info['methods']}")
    add(f"fields          {info['fields']}")
    add(f"references      {', '.join(assembly.assembly_references())}")
    add("")
    add("How to read this: each topic lists the types whose names match it, then the members of the")
    add("best matches. `call`/`callvirt` listings are real call sites found in method bodies, so a")
    add("line like 'X::Y -> Z::W' means X.Y calls Z.W: that is the path the game itself uses.")
    add("")

    for topic, (type_pattern, member_pattern, note) in TOPICS.items():
        _topic(assembly, out, topic, type_pattern, member_pattern, note)

    _call_sites(assembly, out)
    _enums(assembly, out)
    _type_inventory(assembly, out, verbose_types)

    add("=" * 100)
    add("END OF SURVEY")
    add("=" * 100)
    return "\n".join(out)


def _topic(assembly: Assembly, out: list[str], topic: str, type_pattern: str, member_pattern: str, note: str):
    names = sorted(name for name in assembly.types if _matches(type_pattern, name))

    out.append("")
    out.append("#" * 100)
    out.append(f"# {topic}")
    out.append(f"# {note}")
    out.append("#" * 100)
    out.append(f"matching types: {len(names)}")

    if len(names) > MAX_TYPES_PER_TOPIC:
        out.append(f"(showing the first {MAX_TYPES_PER_TOPIC}; the rest are in the type inventory)")
        names = names[:MAX_TYPES_PER_TOPIC]

    for full_name in names:
        typedef = assembly.types[full_name]
        flags = typedef.Flags
        kind = []
        if getattr(flags, "tdInterface", False):
            kind.append("interface")
        elif getattr(flags, "tdAbstract", False):
            kind.append("abstract class")
        else:
            kind.append("class")
        if getattr(flags, "tdNestedPublic", False):
            kind.append("nested-public")
        if getattr(flags, "tdPublic", False):
            kind.append("public")
        else:
            kind.append("non-public")

        out.append("")
        out.append(f"  {full_name}   ({', '.join(kind)})")

        # Public surface first, then the private fields: the private ones matter too (the
        # emission flags are private), but they should not bury the callable API.
        lines: list[tuple[int, str]] = []

        for name, type_text, field_flags in assembly.fields(typedef):
            if not _matches(member_pattern, name):
                continue
            rank = 0 if access(field_flags, "fd") == "public" else 1
            lines.append((rank, _member_line("field ", name, type_text, field_flags, "fd")))

        for method in assembly.methods(typedef):
            name = method["name"]
            if name.startswith("<") or not _matches(member_pattern, name):
                continue
            rank = 0 if access(method["flags"], "md") == "public" else 2
            parameters = ", ".join(method["parameters"])
            lines.append((rank, _member_line("METHOD", f"{name}({parameters})", method["returns"],
                                             method["flags"], "md")))

        lines.sort(key=lambda item: item[0])
        for _, line in lines[:MAX_MEMBERS_PER_TYPE]:
            out.append(line)

        shown = len(lines)

        if shown == 0:
            out.append("    (no members matched; members may use different names)")
        elif shown >= MAX_MEMBERS_PER_TYPE:
            out.append("    (truncated)")


def _call_sites(assembly: Assembly, out: list[str]):
    out.append("")
    out.append("#" * 100)
    out.append("# CALL SITES (who actually calls what)")
    out.append("#" * 100)

    # One pass over every method body, bucketed per needle: scanning once per needle would walk
    # tens of thousands of bodies each time.
    buckets: dict[str, list[tuple[str, str, str]]] = {needle: [] for needle in CALL_SITE_NEEDLES}
    totals: dict[str, int] = {needle: 0 for needle in CALL_SITE_NEEDLES}

    def progress(seen: int, total: int):
        print(f"  scanning method bodies: {seen}/{total}", file=sys.stderr)

    try:
        for caller_type, caller_method, target in assembly.iter_call_sites(
                progress=progress, needles=CALL_SITE_NEEDLES):
            for needle in CALL_SITE_NEEDLES:
                if needle in target:
                    totals[needle] += 1
                    if len(buckets[needle]) < MAX_CALL_SITE_ROWS:
                        buckets[needle].append((caller_type, caller_method, target))
    except Exception as error:
        out.append(f"  (scan failed: {error})")
        return

    for needle in CALL_SITE_NEEDLES:
        out.append("")
        out.append(f"  {needle}")
        if not totals[needle]:
            out.append("    no call sites found")
            continue
        for caller_type, caller_method, target in buckets[needle]:
            out.append(f"    {caller_type}.{caller_method}  ->  {target}")
        if totals[needle] > len(buckets[needle]):
            out.append(f"    ... {totals[needle] - len(buckets[needle])} more")


def _enums(assembly: Assembly, out: list[str]):
    out.append("")
    out.append("#" * 100)
    out.append("# ENUMS (numeric values matter for stored flags)")
    out.append("#" * 100)

    for name in ENUM_CANDIDATES:
        full = name if name in assembly.types else assembly.type_name_without_namespace.get(name, [None])[0] \
            if len(assembly.type_name_without_namespace.get(name, [])) == 1 else None
        if not full:
            continue
        values = assembly.enum_values(full)
        if not values:
            continue
        out.append("")
        out.append(f"  {full}")
        index = 0
        for member, value in values:
            out.append(f"    {member} = {value}")
            index += 1
            if index >= 40:
                out.append("    (truncated)")
                break


def _type_inventory(assembly: Assembly, out: list[str], verbose: bool):
    out.append("")
    out.append("#" * 100)
    out.append("# TYPE INVENTORY")
    out.append("#" * 100)
    names = sorted(assembly.types)
    out.append(f"  {len(names)} types")
    for name in names[:MAX_TYPE_LIST]:
        out.append(f"    {name}")
    if len(names) > MAX_TYPE_LIST:
        out.append(f"  ... {len(names) - MAX_TYPE_LIST} more (run analyze.py types <regex> for the rest)")
