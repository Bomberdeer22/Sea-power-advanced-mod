#!/usr/bin/env python3
"""Catch the batch-file faults that cmd.exe reports as something else entirely.

There is no Windows machine in the place these scripts are written, and cmd.exe has no compiler:
a mistyped label produces "The system cannot find the batch label specified" *after* the file has
already done half its work, and an unclosed parenthesis swallows the rest of the file without a
word. This is the checklist that catches those before the script reaches a player:

1. A `goto` or `call :label` whose label does not exist - the commonest batch bug there is.
2. The same label declared twice, so one of the two branches silently goes somewhere else.
3. `for /f ... in ("C:\\some\\file")` without `usebackq`: cmd.exe parses the quoted text as a
   *string* to split, not as a file to read, and the loop simply never runs.
4. Unbalanced parentheses outside quoted text - one missing `)` and the rest of the script is
   read as part of the block.
5. A `%VAR%` read inside a parenthesised block that the same block has just assigned. The whole
   block is expanded before it runs, so the read sees the *old* value; this is why `!VAR!` with
   `enabledelayedexpansion` exists.
6. `!VAR!` used with delayed expansion switched off, where it is a literal string.

The script never runs cmd.exe. It is a spelling and structure check, not an interpreter.

    python tools/check-batch.py            # report
    python tools/check-batch.py --quiet    # only the findings
    python tools/check-batch.py --verbose  # also note anything that only looks odd

Exit code 0 when clean, 1 when something was found.
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

LABEL_DEF = re.compile(r"^\s*:([A-Za-z0-9_.\-]+)\s*$")
GOTO = re.compile(r"\bgoto\s+:?([A-Za-z0-9_.\-]+)", re.IGNORECASE)
CALL_LABEL = re.compile(r"\bcall\s+:([A-Za-z0-9_.\-]+)", re.IGNORECASE)
FOR_IN_QUOTED = re.compile(
    r"\bfor\s+/f\b((?:\"[^\"]*\"|[^\"])*?)\bin\s*\(\s*\"([^\"()]*)\"", re.IGNORECASE
)

# Non-fatal observations, printed only with --verbose (see check_file).
NOTES: list[str] = []


def strip_comment(line: str) -> str:
    """Drop `rem ...` and `:: ...` lines: their text is not code."""
    stripped = line.strip()
    if stripped.lower().startswith("rem ") or stripped.lower() == "rem":
        return ""
    if stripped.startswith("::"):
        return ""
    return line


def split_quoted(line: str) -> tuple[str, str]:
    """Return (code outside quotes, code inside quotes) for one line."""
    outside, inside = [], []
    in_quote = False
    for char in line:
        if char == '"':
            in_quote = not in_quote
            continue
        (inside if in_quote else outside).append(char)
    return "".join(outside), "".join(inside)


def check_file(path: pathlib.Path) -> list[str]:
    raw = path.read_bytes()
    text = raw.decode("utf-8", errors="replace")
    lines = text.replace("\r\n", "\n").split("\n")
    problems: list[str] = []
    name = path.name

    delayed = "enabledelayedexpansion" in text.lower()
    labels: dict[str, int] = {}
    gotos: list[tuple[int, str]] = []
    calls: list[tuple[int, str]] = []

    depth = 0
    # Variable names assigned inside the block we are currently in, with the depth they were
    # assigned at. Cleared whenever the block closes, because after that `%VAR%` is correct again.
    assigned_at: dict[int, set[str]] = {}

    for number, line in enumerate(lines, start=1):
        code = strip_comment(line)
        if not code.strip():
            continue

        outside, _inside = split_quoted(code)

        match = LABEL_DEF.match(code)
        if match and not code.strip().startswith("::"):
            label = match.group(1).lower()
            if label in labels:
                problems.append(
                    f"{name}:{number}: label :{label} is declared twice (first at line "
                    f"{labels[label]}) - one of the two branches will go somewhere else"
                )
            else:
                labels[label] = number
            continue

        for goto in GOTO.finditer(outside):
            gotos.append((number, goto.group(1).lower()))
        for call in CALL_LABEL.finditer(outside):
            calls.append((number, call.group(1).lower()))

        # for /f without usebackq reading what looks like a path
        for match in FOR_IN_QUOTED.finditer(code):
            options, target = match.group(1), match.group(2)
            looks_like_file = ("\\" in target or "/" in target
                               or target.lower().endswith((".txt", ".log", ".cfg", ".ini")))
            if looks_like_file and "usebackq" not in options.lower():
                problems.append(
                    f"{name}:{number}: for /f reads \"{target}\" without usebackq - cmd.exe will "
                    f"split the quoted text as a string, not read the file, and the loop will not run"
                )

        # Parenthesis depth, outside quotes.
        opened = outside.count("(")
        closed = outside.count(")")
        before = depth
        depth += opened - closed
        if depth < 0:
            problems.append(f"{name}:{number}: more ')' than '(' - this closes a block that was "
                            f"never opened")
            depth = 0
        if before == 0 and opened:
            assigned_at.setdefault(1, set())
        if opened and before >= 0:
            assigned_at.setdefault(depth, set())

        # %VAR% read inside a block that assigned VAR earlier in the same block
        if "(" in outside or depth > 0:
            for read in re.finditer(r"%([A-Za-z_][A-Za-z0-9_]*)%", outside):
                var = read.group(1).lower()
                for level, names in assigned_at.items():
                    if depth >= level > 0 and var in names:
                        problems.append(
                            f"{name}:{number}: %{read.group(1)}% is read inside the same ( ) "
                            f"block that sets it - it expands to the old value; use "
                            f"!{read.group(1)}! with enabledelayedexpansion"
                        )
                        break

        for assign in re.finditer(r"\bset\s+\"?([A-Za-z_][A-Za-z0-9_]*)=", code, re.IGNORECASE):
            if depth > 0:
                assigned_at.setdefault(depth, set()).add(assign.group(1).lower())

        # !VAR! with delayed expansion off
        if not delayed:
            for bang in re.finditer(r"!([A-Za-z_][A-Za-z0-9_]*)!", outside):
                problems.append(
                    f"{name}:{number}: !{bang.group(1)}! needs enabledelayedexpansion, which this "
                    f"file does not set - it will be used as a literal string"
                )
                break

        if closed:
            for level in [level for level in assigned_at if level > max(depth, 0)]:
                assigned_at.pop(level, None)

    if depth != 0:
        problems.append(f"{name}: a ( ) block is opened and never closed - the rest of the file "
                        f"is read as part of it ({depth} still open at the end)")

    for number, target in gotos:
        if target in ("eof", "end"):
            continue
        if target not in labels:
            problems.append(f"{name}:{number}: goto :{target} - no such label in this file")
    for number, target in calls:
        if target in ("eof", "end"):
            continue
        if target not in labels:
            problems.append(f"{name}:{number}: call :{target} - no such label in this file")

    if "pause" not in text.lower():
        problems.append(f"{name}: no pause - a double-clicked tool must not close its own window")

    if "setlocal" not in text.lower() and "set " in text.lower():
        problems.append(f"{name}: sets variables with no setlocal - it will change the caller's "
                        f"environment")

    if b"\r\n" not in raw:
        # Not fatal: the repository normalises *.cmd to CRLF on checkout. Only interesting when a
        # script arrived some other way, so it is printed with --verbose rather than every run.
        NOTES.append(f"{name} has LF line endings in this copy; git restores CRLF on checkout")

    return problems


def main(argv: list[str]) -> int:
    quiet = "--quiet" in argv
    verbose = "--verbose" in argv
    files = sorted(ROOT.glob("*.cmd")) + sorted(ROOT.glob("*.bat"))
    if not files:
        print("no .cmd files found")
        return 0

    problems: list[str] = []
    for path in files:
        problems.extend(check_file(path))

    if verbose:
        for note in NOTES:
            print("  note: " + note)

    if not problems:
        print(f"clean: {len(files)} batch file(s), every label resolves, no unclosed blocks, "
              f"no stale %expansion% inside a block, no silent for /f reads")
        return 0

    if not quiet:
        print()
    for problem in problems:
        print("  " + problem)
    print()
    print(f"{len(problems)} problem(s) across {len(files)} batch file(s)")
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
