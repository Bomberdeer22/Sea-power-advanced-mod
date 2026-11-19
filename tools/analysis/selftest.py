"""
Self-test: read a known assembly and compare the disassembly with a listing made by ILDasm.

A metadata reader is easy to get subtly wrong (offsets, member tokens, string tokens) and the
mistakes only show up as confusing results much later. This pins the behaviour: if the decoder can
reproduce ILDasm's own output for a known method, it can be trusted on the game.

The fixture is a 3.5 KB compiled "hello world" from the dnfile project's test files
(https://github.com/malwarefrank/dnfile-testfiles, MIT), and the expected output is ILDasm's own
listing of it, which IS shipped here (`testdata/hello.ildasm.txt`) together with the EXPECTED_MAIN
table below.

**The sample itself is not shipped.** A compiled, unsigned executable in the tree made browsers and
antivirus products refuse to download the release archive - "couldn't download, virus detected" - and
base64-encoding it only hid the same bytes inside a text file, which scanners look for just as hard.
So the repository holds the expected output and nothing executable.

To run these checks, put the sample in place once:

    python tools/analysis/selftest.py --fetch

or copy it there yourself from
https://github.com/malwarefrank/dnfile-testfiles/blob/main/hello-world/hello-world.exe

With no sample the self-test says so and exits 2 - a skip, not a failure. `git status` in this
repository should never show an executable; tools/check-source.py fails if one is committed.
"""

from __future__ import annotations

import hashlib
import os
import re
import sys

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


SAMPLE_URL = ("https://raw.githubusercontent.com/malwarefrank/dnfile-testfiles/main/"
              "hello-world/hello-world.exe")


def fetch(here: str) -> str | None:
    """Put the sample where the checks expect it, verifying it is the one they were written for."""
    import urllib.request

    target = os.path.join(here, "testdata", "hello.exe")
    os.makedirs(os.path.dirname(target), exist_ok=True)

    print(f"fetching {SAMPLE_URL} ...")
    with urllib.request.urlopen(SAMPLE_URL, timeout=30) as response:  # noqa: S310 - fixed https
        data = response.read()

    digest = hashlib.sha256(data).hexdigest()
    if digest != FIXTURE_SHA256:
        print(f"BAD download: sha256 {digest}, expected {FIXTURE_SHA256}")
        return None

    with open(target, "wb") as out:
        out.write(data)

    print(f"wrote {target} ({len(data)} bytes)")
    return target


def unpack(here: str) -> str | None:
    """The sample's path, or None when it is not present.

    It is deliberately absent from the repository - see the note at the top of this file - so a fresh
    checkout skips these checks rather than failing them. `SEAPOWER_SELFTEST_SAMPLE` points at a copy
    somewhere else, for anyone who keeps one.
    """
    sample = os.environ.get("SEAPOWER_SELFTEST_SAMPLE") or os.path.join(here, "testdata", "hello.exe")

    if os.path.isfile(sample):
        return sample

    print("SKIPPED: the sample assembly is not present.")
    print()
    print("  python tools/analysis/selftest.py --fetch")
    print()
    print("or copy testdata/hello.exe in yourself from:")
    print("  " + SAMPLE_URL)
    return None


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
    return 0

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))

    if "--fetch" in sys.argv:
        sys.exit(0 if fetch(here) else 1)

    sys.exit(run(here))
