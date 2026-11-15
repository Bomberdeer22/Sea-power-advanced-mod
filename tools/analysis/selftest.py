"""
Self-test: read a known assembly and compare the disassembly with a listing made by ILDasm.

A metadata reader is easy to get subtly wrong (offsets, member tokens, string tokens) and the
mistakes only show up as confusing results much later. This pins the behaviour: if the decoder can
reproduce ILDasm's own output for a known method, it can be trusted on the game.

The fixture in testdata/ is a 3.5 KB compiled "hello world" from the dnfile project's test files
(https://github.com/malwarefrank/dnfile-testfiles, MIT), committed together with its ILDasm listing.
"""

from __future__ import annotations

import os
import re
import sys

from gameapi import Assembly

EXPECTED_MAIN = [
    ("IL_0000", "nop"),
    ("IL_0001", "ldstr"),
    ("IL_0006", "call"),
    ("IL_000b", "nop"),
    ("IL_000c", "ret"),
]


def run(here: str) -> int:
    sample = os.path.join(here, "testdata", "hello.exe")
    listing = os.path.join(here, "testdata", "hello.ildasm.txt")

    if not os.path.isfile(sample):
        print(f"MISSING fixture: {sample}")
        return 2

    assembly = Assembly(sample)
    checks: list[tuple[str, bool, str]] = []

    checks.append(("assembly name", assembly.name == "1-hello-world", assembly.name))
    checks.append(("type count", len(assembly.types) == 2, str(len(assembly.types))))
    checks.append(("types listed", "HelloWorld" in assembly.types, ", ".join(assembly.types)))

    main = assembly.method("HelloWorld", "Main")
    checks.append(("Main found", main is not None, str(main is not None)))

    if main:
        checks.append(("Main signature",
                       main["returns"] == "void" and main["parameters"] == ["string[]"],
                       f"{main['returns']} ({', '.join(main['parameters'])})"))

        instructions = assembly.instructions(main)
        actual = [(f"IL_{offset:04x}", opcode) for offset, opcode, _ in instructions]
        checks.append(("Main IL matches ILDasm", actual == EXPECTED_MAIN,
                       " | ".join(f"{o} {c}" for o, c in actual)))

        operands = [operand for _, _, operand in instructions]
        checks.append(("ldstr decoded", operands[1] == "'Hello World!'", str(operands[1])))
        checks.append(("call target decoded",
                       operands[2] == "System.Console::WriteLine", str(operands[2])))

        # If ILDasm's own listing is present, every instruction should appear in it.
        if os.path.isfile(listing):
            with open(listing, encoding="utf-8", errors="replace") as handle:
                reference = handle.read()
            found = sum(1 for offset, opcode in actual
                        if re.search(rf"{offset}:\s+{re.escape(opcode)}", reference))
            checks.append(("offset/opcode pairs present in ILDasm listing",
                           found == len(actual), f"{found}/{len(actual)}"))

    width = max(len(name) for name, _, _ in checks)
    failures = 0
    for name, passed, detail in checks:
        mark = "ok  " if passed else "FAIL"
        if not passed:
            failures += 1
        print(f"{mark} {name:<{width}}  {detail}")

    print()
    if failures:
        print(f"{failures} check(s) failed.")
        return 1

    print("All checks passed: the reader reproduces ILDasm's output.")
    return 0
