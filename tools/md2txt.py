#!/usr/bin/env python3
"""
Convert a Markdown file to plain text that reads well in Notepad.

The repository's documentation is written in Markdown because GitHub renders it, but the people
reading it on a game PC double-click a file in Notepad. This produces the .txt twin so the same
content is readable there: wrapped lines, no syntax characters, tables turned into aligned columns.

Usage:  python tools/md2txt.py README.md README.txt
"""

from __future__ import annotations

import re
import sys
import textwrap

WIDTH = 78

REPLACEMENTS = {
    "\u2014": " - ", "\u2013": "-", "\u2019": "'", "\u2018": "'",
    "\u201c": '"', "\u201d": '"', "\u2026": "...", "\u2192": "->",
    "\u2265": ">=", "\u2264": "<=", "\u00b2": "2", "\u00b7": "-",
    "\u2713": "x", "\u25b8": ">", "\u2500": "-",
    # Box drawing, used by the file trees: Notepad mangles these in a UTF-8 file without a BOM.
    "\u251c": "|", "\u2514": "+", "\u2502": "|", "\u252c": "+",
    "\u2534": "+", "\u253c": "+", "\u2510": "+", "\u250c": "+",
}


def clean(text: str) -> str:
    for source, target in REPLACEMENTS.items():
        text = text.replace(source, target)
    # inline code and emphasis markers
    text = re.sub(r"`([^`]*)`", r"\1", text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"\1", text)
    text = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"\1", text)
    # links -> "label (url)", but keep it short when the label is the url
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", lambda m: m.group(1) if m.group(1) == m.group(2)
                  else f"{m.group(1)} ({m.group(2)})", text)
    return text.rstrip()


def split_row(line: str) -> list[str]:
    cells = line.strip().strip("|").split("|")
    return [clean(cell).strip() for cell in cells]


def is_separator(cells: list[str]) -> bool:
    return all(re.fullmatch(r":?-{2,}:?", cell.replace(" ", "")) for cell in cells if cell != "")


def render_table(rows: list[str]) -> list[str]:
    """Narrow tables keep aligned columns; wide ones become labelled blocks.

    A four-column table of settings does not survive a 78-column page as columns: the cells wrap
    into an unreadable pile. Turning each row into a small block, with the column headings as
    labels, stays readable in Notepad whatever the width.
    """
    parsed = [split_row(row) for row in rows]
    parsed = [row for row in parsed if not is_separator(row)]
    if not parsed:
        return []

    width = max(len(row) for row in parsed)
    for row in parsed:
        row.extend([""] * (width - len(row)))

    header, body = parsed[0], parsed[1:]

    sizes = []
    for index in range(width):
        longest = max((len(row[index]) for row in parsed), default=0)
        sizes.append(min(longest, 30 if index else 24))

    widest = max(
        (len("  " + "  ".join(row[index].ljust(sizes[index]) for index in range(width)).rstrip())
         for row in parsed), default=0)

    if width <= 1 or widest <= WIDTH:
        return ["  " + "  ".join(row[index].ljust(sizes[index])
                                 for index in range(width)).rstrip() for row in parsed]

    lines = []
    for position, row in enumerate(body):
        if position:
            lines.append("")
        # "Section / Key" as the block title reads better than repeating the section each row.
        first = 1
        title = row[0]
        if width >= 3 and (header[1] or "").strip().lower() == "key" and row[1]:
            title = f"{row[0]} / {row[1]}"
            first = 2
        lines.append("  " + title if title else "  -")
        for index in range(first, width):
            cell = row[index]
            if not cell:
                continue
            label = (header[index] or "").strip()
            text = f"{label}: {cell}" if label else cell
            wrapped = textwrap.wrap(text, WIDTH - 8) or [""]
            lines.append("      " + wrapped[0])
            for extra in wrapped[1:]:
                lines.append("        " + extra)
    return lines


def convert(source: str) -> str:
    lines = source.splitlines()
    out: list[str] = []
    index = 0
    in_code = False
    paragraph: list[str] = []

    def flush_paragraph():
        if not paragraph:
            return
        joined = clean(" ".join(segment.strip() for segment in paragraph))
        for wrapped in textwrap.wrap(joined, WIDTH):
            out.append(wrapped)
        out.append("")
        paragraph.clear()

    while index < len(lines):
        line = lines[index]
        stripped = line.strip()

        if stripped.startswith("```"):
            flush_paragraph()
            in_code = not in_code
            if in_code:
                out.append("")
            index += 1
            continue

        if in_code:
            out.append("    " + clean(line))
            index += 1
            continue

        if not stripped:
            flush_paragraph()
            if out and out[-1] != "":
                out.append("")
            index += 1
            continue

        heading = re.match(r"^(#{1,6})\s+(.*)$", stripped)
        if heading:
            flush_paragraph()
            level = len(heading.group(1))
            title = clean(heading.group(2)).strip()
            if level == 1:
                out.append("=" * WIDTH)
                out.append(title.upper())
                out.append("=" * WIDTH)
            elif level == 2:
                out.append("")
                out.append(title.upper())
                out.append("-" * min(len(title), WIDTH))
            else:
                out.append("")
                out.append(title)
            out.append("")
            index += 1
            continue

        if stripped.startswith("|"):
            flush_paragraph()
            block = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                block.append(lines[index])
                index += 1
            out.extend(render_table(block))
            out.append("")
            continue

        if re.match(r"^[-*]\s+", stripped):
            flush_paragraph()
            item = re.sub(r"^[-*]\s+", "", stripped)
            wrapped = textwrap.wrap(clean(item), WIDTH - 4) or [""]
            out.append("  * " + wrapped[0])
            for extra in wrapped[1:]:
                out.append("    " + extra)
            index += 1
            continue

        if re.match(r"^\d+\.\s+", stripped):
            flush_paragraph()
            number, _, rest = stripped.partition(".")
            wrapped = textwrap.wrap(clean(rest.strip()), WIDTH - 5) or [""]
            out.append(f"  {number}. " + wrapped[0])
            for extra in wrapped[1:]:
                out.append("     " + extra)
            index += 1
            continue

        if stripped.startswith(">"):
            flush_paragraph()
            for wrapped in textwrap.wrap(clean(stripped.lstrip("> ")), WIDTH - 4):
                out.append("  " + wrapped)
            index += 1
            continue

        if stripped in ("---", "***", "___"):
            flush_paragraph()
            out.append("-" * WIDTH)
            index += 1
            continue

        paragraph.append(line)
        index += 1

    flush_paragraph()

    # collapse runs of blank lines
    result: list[str] = []
    for line in out:
        if line == "" and result and result[-1] == "":
            continue
        result.append(line)

    return "\n".join(result).strip() + "\n"


BANNER = (
    "Plain-text copy of {source}, kept in step with it by tools/md2txt.py.\n"
    "The Markdown version is the one that renders on GitHub; this one is for Notepad.\n"
)


def main() -> None:
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    with open(sys.argv[1], encoding="utf-8") as handle:
        text = handle.read()
    banner = BANNER.format(source=sys.argv[1].replace("\\", "/"))
    with open(sys.argv[2], "w", encoding="utf-8", newline="\r\n") as handle:
        handle.write(banner + "\n" + convert(text))
    print(f"Wrote {sys.argv[2]}")


if __name__ == "__main__":
    main()
