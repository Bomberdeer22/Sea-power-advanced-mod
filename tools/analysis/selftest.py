"""
Self-test: read a known assembly and compare the disassembly with a listing made by ILDasm.

A metadata reader is easy to get subtly wrong (offsets, member tokens, string tokens) and the
mistakes only show up as confusing results much later. This pins the behaviour: if the decoder can
reproduce ILDasm's own output for a known method, it can be trusted on the game.

The fixture in testdata/ is a 3.5 KB compiled "hello world" from the dnfile project's test files
(https://github.com/malwarefrank/dnfile-testfiles, MIT), stored together with its ILDasm listing.

It is stored as **base64 text**, not as the .exe itself, and decoded to a temporary file here. A
compiled, unsigned executable committed to the repository made browsers and antivirus products refuse
to download the release archive at all - "couldn't download, virus detected" - which stops the mod
reaching the people it is for. Nothing else in this repository is a binary, and that is the rule:
`check-repo.py` enforces it.
"""

from __future__ import annotations

import base64
import hashlib
import os
import re
import sys
import tempfile

from gameapi import Assembly

# sha256 of the decoded fixture, as recorded in the header of hello.exe.b64. Checked before use, so a
# corrupted or swapped fixture fails loudly instead of quietly changing what the self-test asserts.
FIXTURE_SHA256 = "9d4490567a1339cdb30d9df1520621949774c55a4da996b7f06319065102800f"

EXPECTED_MAIN = [
    ("IL_0000", "nop"),
    ("IL_0001", "ldstr"),
    ("IL_0006", "call"),
    ("IL_000b", "nop"),
    ("IL_000c", "ret"),
]


def unpack(here: str) -> str | None:
    """The fixture as a path on disk, decoding the stored copy when there is no local .exe.

    A reader can also drop their own `testdata/hello.exe` in place; that is used as-is and not
    hash-checked, because they may be testing against a different assembly on purpose.
    """
    sample = os.path.join(here, "testdata", "hello.exe")
    if os.path.isfile(sample):
        return sample

    packed = sample + ".b64"
    if not os.path.isfile(packed):
        print(f"MISSING fixture: neither {sample} nor {packed}")
        return None

    with open(packed, encoding="ascii") as handle:
        text = "".join(line for line in handle if not line.startswith("#"))

    data = base64.b64decode(text)
    digest = hashlib.sha256(data).hexdigest()

    if digest != FIXTURE_SHA256:
        print(f"BAD fixture: {packed} decodes to sha256 {digest}, expected {FIXTURE_SHA256}")
        return None

    handle_to, path = tempfile.mkstemp(prefix="seapower-selftest-", suffix=".exe")
    with os.fdopen(handle_to, "wb") as out:
        out.write(data)

    return path


def run(here: str) -> int:
    sample = unpack(here)
    listing = os.path.join(here, "testdata", "hello.ildasm.txt")

    if sample is None:
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

    # The decoded copy is a temporary file; a reader who dropped in their own .exe keeps it.
    if sample.endswith(".exe") and "seapower-selftest-" in os.path.basename(sample):
        try:
            os.remove(sample)
        except OSError:
            pass

    return 0
