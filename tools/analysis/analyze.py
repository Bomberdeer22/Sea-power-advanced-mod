#!/usr/bin/env python3
"""
Ask questions of a .NET assembly from the command line.

    python analyze.py brief                     # the full survey -> analysis-report.txt
    python analyze.py readiness                 # which planned features are buildable
    python analyze.py types   Sensor
    python analyze.py member  SeaPower.ObjectBase
    python analyze.py il      SeaPower.ObjectBase setOrder
    python analyze.py find    "::fire"          # call sites of anything matching
    python analyze.py enums   Target
    python analyze.py selftest                  # prove the reader still works

The assembly defaults to the Steam install path, or pass --dll. Nothing is loaded or executed:
only the metadata and the IL bytes are read.
"""

from __future__ import annotations

import argparse
import os
import sys

from gameapi import Assembly, access, modifiers
from features import Probe, format_report
from survey import build_report

DEFAULT_DLL = r"C:\Program Files (x86)\Steam\steamapps\common\Sea Power\Sea Power_Data\Managed\Seapower-Scripts.dll"
HERE = os.path.dirname(os.path.abspath(__file__))


def resolve_type(assembly: Assembly, name: str):
    """Exact name first, then a unique short name (e.g. "CivilianRoute")."""
    typedef = assembly.types.get(name)
    if typedef is not None:
        return typedef
    candidates = assembly.type_name_without_namespace.get(name, [])
    if len(candidates) == 1:
        return assembly.types.get(candidates[0])
    return None


def load(path: str) -> Assembly:
    if not os.path.isfile(path):
        sys.exit(f"Assembly not found: {path}\nPass --dll <path to Seapower-Scripts.dll>.")
    return Assembly(path)


def cmd_brief(assembly: Assembly, args) -> None:
    report = build_report(assembly, verbose_types=args.verbose)
    # Written next to the tool rather than into the current directory, so the path in the docs is
    # the path you get whichever folder the command was run from.
    output = args.output or os.path.join(HERE, "analysis-report.txt")
    with open(output, "w", encoding="utf-8") as handle:
        handle.write(report)
    print(f"Wrote {output} ({len(report.splitlines())} lines, {len(report) // 1024} KB)")
    print("Send that file back; it contains everything needed to check the API.")


def cmd_readiness(assembly: Assembly, args) -> None:
    probe = Probe(assembly, callsites=not args.fast, log=lambda message: print(message, file=sys.stderr))
    results = probe.run()
    report = format_report(assembly, results)

    output = args.output or os.path.join(HERE, "feature-readiness.txt")
    with open(output, "w", encoding="utf-8") as handle:
        handle.write(report)

    for result in results:
        print(f"  [{result['verdict']:<7}] {result['name']}")

    print()
    print(f"Wrote {output}")
    print("Send that file back to have the buildable ones implemented.")


def cmd_types(assembly: Assembly, args) -> None:
    matches = sorted(name for name in assembly.types if args.pattern.lower() in name.lower())
    print(f"{len(matches)} type(s) matching {args.pattern!r}")
    for name in matches:
        flags = assembly.types[name].Flags
        scope = "public" if getattr(flags, "tdPublic", False) else "non-public"
        print(f"  {name}   ({scope})")


def cmd_member(assembly: Assembly, args) -> None:
    typedef = resolve_type(assembly, args.type)
    if typedef is None:
        sys.exit(f"No such type: {args.type}")

    flags = typedef.Flags
    print(f"{args.type}  public={getattr(flags, 'tdPublic', False)} "
          f"interface={getattr(flags, 'tdInterface', False)} "
          f"abstract={getattr(flags, 'tdAbstract', False)}")
    print("\nfields:")
    for name, type_text, field_flags in assembly.fields(typedef):
        print(f"  {access(field_flags, 'fd'):<18} {modifiers(field_flags, 'fd'):<18} {type_text} {name}")
    print("\nmethods:")
    for method in assembly.methods(typedef):
        if method["name"].startswith("<"):
            continue
        parameters = ", ".join(method["parameters"])
        body = "IL" if method["rva"] else "no body"
        print(f"  {access(method['flags'], 'md'):<18} {modifiers(method['flags'], 'md'):<18} "
              f"{method['returns']} {method['name']}({parameters})   [{body}]")


def cmd_il(assembly: Assembly, args) -> None:
    method = assembly.method(args.type, args.method, args.parameters)
    if method is None:
        sys.exit(f"No method {args.type}.{args.method}")
    print(f"{args.type}.{method['name']}({', '.join(method['parameters'])})  -> {method['returns']}")
    print(assembly.disassembly(method))


def cmd_find(assembly: Assembly, args) -> None:
    if args.callsites:
        hits = assembly.find_calls(args.pattern)
        print(f"{len(hits)} call site(s) referencing {args.pattern!r}")
        for caller_type, caller_method, target in hits[: args.limit]:
            print(f"  {caller_type}.{caller_method}  ->  {target}")
        if len(hits) > args.limit:
            print(f"  ... {len(hits) - args.limit} more")
        return

    print(f"members matching {args.pattern!r}")
    for full_name, typedef in sorted(assembly.types.items()):
        for name, type_text, _ in assembly.fields(typedef):
            if args.pattern.lower() in name.lower():
                print(f"  field  {full_name}::{name}  ({type_text})")
        for method in assembly.methods(typedef):
            if method["name"].startswith("<"):
                continue
            if args.pattern.lower() in method["name"].lower():
                parameters = ", ".join(method["parameters"])
                print(f"  method {full_name}::{method['name']}({parameters})  -> {method['returns']}")


def cmd_enums(assembly: Assembly, args) -> None:
    values = assembly.enum_values(args.type)
    if not values:
        sys.exit(f"{args.type} has no constant values (wrong name?)")
    for name, value in values:
        print(f"  {name} = {value}")


def cmd_selftest(_assembly, _args) -> None:
    sys.path.insert(0, HERE)
    from selftest import run
    sys.exit(run(HERE))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dll", default=os.environ.get("SEAPOWER_DLL", DEFAULT_DLL),
                        help="path to Seapower-Scripts.dll (or set SEAPOWER_DLL)")
    sub = parser.add_subparsers(dest="command", required=True)

    brief = sub.add_parser("brief", help="write the full API survey")
    brief.add_argument("-o", "--output")
    brief.add_argument("--verbose", action="store_true", help="longer type lists")
    brief.set_defaults(func=cmd_brief)

    readiness = sub.add_parser("readiness", help="which planned features this build can support")
    readiness.add_argument("-o", "--output")
    readiness.add_argument("--fast", action="store_true", help="skip call-site scans (much faster)")
    readiness.set_defaults(func=cmd_readiness)

    types = sub.add_parser("types", help="list type names containing a string")
    types.add_argument("pattern")
    types.set_defaults(func=cmd_types)

    member = sub.add_parser("member", help="dump one type's fields and methods")
    member.add_argument("type")
    member.set_defaults(func=cmd_member)

    il = sub.add_parser("il", help="disassemble one method")
    il.add_argument("type")
    il.add_argument("method")
    il.add_argument("--parameters", type=int, default=None, help="match on parameter count")
    il.set_defaults(func=cmd_il)

    find = sub.add_parser("find", help="find members, or call sites with --callsites")
    find.add_argument("pattern")
    find.add_argument("--callsites", action="store_true")
    find.add_argument("--limit", type=int, default=60)
    find.set_defaults(func=cmd_find)

    enums = sub.add_parser("enums", help="dump an enum's values")
    enums.add_argument("type")
    enums.set_defaults(func=cmd_enums)

    selftest = sub.add_parser("selftest", help="check the reader against a known assembly")
    selftest.set_defaults(func=cmd_selftest)

    args = parser.parse_args()

    if args.command == "selftest":
        args.func(None, args)
        return

    assembly = load(args.dll)
    args.func(assembly, args)


if __name__ == "__main__":
    main()
