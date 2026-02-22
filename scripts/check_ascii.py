#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

"""Check source files for non-ASCII characters.

Usage:
    python scripts/check_ascii.py          # check and report
    python scripts/check_ascii.py --fix    # replace non-ASCII with \\xNN escapes
"""

import argparse
import subprocess
import sys
import unicodedata
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent

SOURCE_EXTENSIONS = frozenset(
    {
        ".cs",
        ".py",
        ".sh",
        ".csproj",
        ".xml",
        ".yml",
        ".yaml",
        ".json",
        ".axaml",
        ".resx",
    }
)
EXCLUDE_PATHS = frozenset({"MAVLink"})  # tracked but generated


def find_source_files(root: Path) -> list[Path]:
    """Find all source files under root, respecting .gitignore."""
    result = subprocess.run(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
        cwd=root,
        capture_output=True,
        text=True,
        check=True,
    )
    paths = []
    for entry in result.stdout.split("\0"):
        if not entry:
            continue
        p = Path(entry)
        if p.suffix in SOURCE_EXTENSIONS and not (set(p.parts) & EXCLUDE_PATHS):
            paths.append(root / p)
    paths.sort()
    return paths


def escape_char(char: str) -> str:
    """Return \\xNN escape sequence for a character's UTF-8 bytes."""
    return "".join(f"\\x{b:02x}" for b in char.encode("utf-8"))


def char_description(char: str) -> str:
    """Return U+NNNN with Unicode name if available."""
    code_point = f"U+{ord(char):04X}"
    name = unicodedata.name(char, "")
    if name:
        return f"{code_point} {name}"
    return code_point


def check_file(path: Path, root: Path) -> list[str]:
    """Check a file for non-ASCII characters. Return diagnostic messages."""
    rel = path.relative_to(root)
    diagnostics = []
    text = path.read_bytes().decode("utf-8")
    for line_num, line in enumerate(text.splitlines(), 1):
        for col, char in enumerate(line, 1):
            if ord(char) > 127:
                escaped = escape_char(char)
                desc = char_description(char)
                diagnostics.append(f"{rel}:{line_num}:{col}: {escaped} ({desc})")
    return diagnostics


def fix_file(path: Path, root: Path) -> int:
    """Replace non-ASCII characters with \\xNN escapes in-place. Return count."""
    content = path.read_bytes().decode("utf-8")
    count = 0
    parts = []
    for char in content:
        if ord(char) > 127:
            parts.append(escape_char(char))
            count += 1
        else:
            parts.append(char)
    if count > 0:
        path.write_bytes("".join(parts).encode("utf-8"))
        rel = path.relative_to(root)
        print(f"{rel}: replaced {count} non-ASCII character(s)")
    return count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--fix",
        action="store_true",
        help="replace non-ASCII characters with \\xNN escapes in-place",
    )
    args = parser.parse_args()

    files = find_source_files(REPO_ROOT)
    total = 0

    for path in files:
        if args.fix:
            total += fix_file(path, REPO_ROOT)
        else:
            diagnostics = check_file(path, REPO_ROOT)
            for msg in diagnostics:
                print(msg)
            total += len(diagnostics)

    if total == 0:
        print("OK: no non-ASCII characters found")
        return 0

    action = "replaced" if args.fix else "found"
    print(f"\n{total} non-ASCII character(s) {action}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
