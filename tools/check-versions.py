#!/usr/bin/env python3
"""Check that the version number is the same everywhere it is written down.

It is written in seven places, and a release that says one number in the DLL and another in the
README is the kind of mistake that only shows up after somebody has installed it:

    src/SeaPowerAdvanced/ModInfo.cs        the assembly's own answer, and what version.txt says
    src/SeaPowerAdvanced/*.csproj          <Version> and <AssemblyVersion>
    CHANGELOG.md                           the newest heading is the release being cut
    README.md                              the line a visitor reads first
    INSTALL.md                             the two log lines a player compares against
    docs/testing.md                        "version under test" - what the checks belong to

    python tools/check-versions.py          # report, exit 1 on a mismatch
    python tools/check-versions.py --fix    # rewrite the documentation copies, not the source

The source of truth is ModInfo.cs: build.cmd reads it to write version.txt, so the DLL and the
installed version can never disagree with it. Everything else follows.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def source_version() -> str:
    match = re.search(r'const\s+string\s+Version\s*=\s*"([^"]+)"', read("src/SeaPowerAdvanced/ModInfo.cs"))
    if not match:
        raise SystemExit("ModInfo.cs has no Version constant")
    return match.group(1)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fix", action="store_true", help="rewrite the documentation copies")
    args = parser.parse_args()

    version = source_version()
    problems: list[tuple[str, str]] = []

    def check(where: str, found: str | None, expected: str) -> None:
        if found != expected:
            problems.append((where, f"found {found!r}, expected {expected!r}"))

    # --- the build -------------------------------------------------------------------------------
    csproj = read("src/SeaPowerAdvanced/SeaPowerAdvanced.csproj")
    check("csproj <Version>", re.search(r"<Version>([^<]+)</Version>", csproj).group(1), version)
    check("csproj <AssemblyVersion>",
          re.search(r"<AssemblyVersion>([^<]+)</AssemblyVersion>", csproj).group(1), version + ".0")

    # --- the changelog: the newest heading, not any mention of a version --------------------------
    heading = re.search(r"^##\s+([0-9]+\.[0-9]+\.[0-9]+)", read("CHANGELOG.md"), re.MULTILINE)
    check("CHANGELOG.md newest heading", heading.group(1) if heading else None, version)

    # --- README: the line a visitor reads first ---------------------------------------------------
    readme = read("README.md")
    match = re.search(r"\*\*Version ([0-9]+\.[0-9]+\.[0-9]+)\*\*", readme)
    check("README.md version line", match.group(1) if match else None, version)

    # --- INSTALL.md: the two log lines a player compares against ----------------------------------
    install = read("INSTALL.md")
    loading = re.search(r"Loading \[Sea Power Advanced ([0-9]+\.[0-9]+\.[0-9]+)\]", install)
    check("INSTALL.md BepInEx log line", loading.group(1) if loading else None, version)

    banner = re.search(r"=+ Sea Power Advanced ([0-9]+\.[0-9]+\.[0-9]+) =+", install)
    check("INSTALL.md mod banner", banner.group(1) if banner else None, version)

    # --- docs/testing.md: which build the checks belong to ---------------------------------------
    testing = read("docs/testing.md")
    under_test = re.search(r"Version under test:\s*\*\*([0-9]+\.[0-9]+\.[0-9]+)\*\*", testing)
    check("docs/testing.md version under test", under_test.group(1) if under_test else None, version)

    if not problems:
        print(f"clean: {version} in the source, the build, the changelog, README, INSTALL and testing")
        return 0

    for where, detail in problems:
        print(f"  {where}: {detail}")

    if not args.fix:
        print(f"\n{len(problems)} place(s) disagree with ModInfo.cs ({version}).")
        print("Run with --fix to rewrite the documentation copies (the source is never guessed at).")
        return 1

    readme = re.sub(r"\*\*Version [0-9]+\.[0-9]+\.[0-9]+\*\*", f"**Version {version}**", readme)
    (ROOT / "README.md").write_text(readme, encoding="utf-8")

    install = re.sub(r"Loading \[Sea Power Advanced [0-9]+\.[0-9]+\.[0-9]+\]",
                     f"Loading [Sea Power Advanced {version}]", install)
    install = re.sub(r"=+ Sea Power Advanced [0-9]+\.[0-9]+\.[0-9]+ =+",
                     f"================ Sea Power Advanced {version} ================", install)
    (ROOT / "INSTALL.md").write_text(install, encoding="utf-8")

    under_test = re.sub(r"Version under test:\s*\*\*[0-9]+\.[0-9]+\.[0-9]+\*\*",
                        f"Version under test: **{version}**", testing)
    (ROOT / "docs/testing.md").write_text(under_test, encoding="utf-8")

    print(f"\nrewrote README.md, INSTALL.md and docs/testing.md to {version}")
    print("csproj and CHANGELOG are not rewritten: a version change is a release decision.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
