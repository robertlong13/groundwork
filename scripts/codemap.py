#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

"""Print a structural map of the codebase for quick orientation.

Walk the source tree, extract C# type declarations (class, interface,
struct, enum, record) with their inheritance, and print a tree grouped
by project and directory.

Usage:
    python scripts/codemap.py              # print to stdout
    python scripts/codemap.py -o .codemap  # write to file
"""

import argparse
import re
import sys
from pathlib import Path

REPO_ROOT = Path.cwd()
if not list(REPO_ROOT.glob("*.slnx")):
    print("Error: run from the repo root (no .slnx found)", file=sys.stderr)
    sys.exit(1)

# Directories to skip entirely.
SKIP_DIRS = {"obj", "bin", ".vs", ".idea", ".vscode", ".claude", "__pycache__"}

# Generated files -- show the file but not its (hundreds of) types.
GENERATED_FILES = {"mavlink.cs"}

# Regex for C# type declarations.  Captures:
#   1. modifiers (public sealed, static, etc.) -- unused but keeps regex simple
#   2. kind (class, interface, struct, enum, record)
#   3. name
#   4. inheritance clause (: Base, IFoo) if present
TYPE_RE = re.compile(
    r"^\s*"
    r"(?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|ref)\s+)*"
    r"(class|interface|struct|enum|record)\s+"
    r"(\w+)"
    r"(?:<[^>]+>)?"  # generic parameters
    r"(?:\s*:\s*([^\n{]+))?",  # inheritance
    re.MULTILINE,
)


def extract_types(path: Path) -> list[tuple[str, str, str]]:
    """Return [(kind, name, bases), ...] from a .cs file."""
    try:
        text = path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeDecodeError):
        return []

    results = []
    for m in TYPE_RE.finditer(text):
        kind = m.group(1)
        name = m.group(2)
        bases = m.group(3)
        if bases:
            bases = bases.strip().rstrip("{").strip()
        results.append((kind, name, bases or ""))
    return results


def scan_directory(root: Path) -> dict[Path, list[tuple[str, str, str]]]:
    """Walk root for .cs files, returning {relative_path: types}."""
    entries: dict[Path, list[tuple[str, str, str]]] = {}

    for cs_file in sorted(root.rglob("*.cs")):
        # Skip generated/build directories.
        if any(part in SKIP_DIRS for part in cs_file.parts):
            continue
        if cs_file.parent.name == "obj" or "/obj/" in str(cs_file):
            continue

        if cs_file.name in GENERATED_FILES:
            types = []  # Show file, suppress generated types.
        else:
            types = extract_types(cs_file)
        rel = cs_file.relative_to(REPO_ROOT)
        entries[rel] = types

    return entries


def format_tree(entries: dict[Path, list[tuple[str, str, str]]]) -> str:
    """Format entries as a readable tree."""
    lines: list[str] = []
    current_dir: Path | None = None

    for path, types in entries.items():
        # Group header when directory changes.
        parent = path.parent
        if parent != current_dir:
            if current_dir is not None:
                lines.append("")
            lines.append(f"{parent}/")
            current_dir = parent

        # File line with type summary.
        name = path.name
        generated = path.name in GENERATED_FILES
        if generated:
            lines.append(f"  {name:<40} (generated)")
        elif not types:
            lines.append(f"  {name}")
        elif len(types) == 1:
            kind, tname, bases = types[0]
            suffix = f" : {bases}" if bases else ""
            lines.append(f"  {name:<40} {kind} {tname}{suffix}")
        else:
            # Multiple types in one file -- list each.
            lines.append(f"  {name}")
            for kind, tname, bases in types:
                suffix = f" : {bases}" if bases else ""
                lines.append(f"    {kind} {tname}{suffix}")

    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate codebase structure map.")
    parser.add_argument(
        "-o",
        "--output",
        type=Path,
        help="Write output to file instead of stdout",
    )
    parser.add_argument(
        "roots",
        nargs="*",
        type=Path,
        default=[REPO_ROOT / "src"],
        help="Directories to scan (default: src/)",
    )
    args = parser.parse_args()

    all_entries: dict[Path, list[tuple[str, str, str]]] = {}
    for root in args.roots:
        if not root.is_dir():
            print(f"Warning: {root} is not a directory, skipping", file=sys.stderr)
            continue
        all_entries.update(scan_directory(root))

    output = format_tree(all_entries)

    if args.output:
        args.output.write_text(output, encoding="utf-8")
        print(f"Wrote {args.output}", file=sys.stderr)
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
