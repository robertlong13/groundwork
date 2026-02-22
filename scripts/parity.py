#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

"""Track MAVProxy command parity.

Scrape MAVProxy's command registrations and Groundwork's command
registrations, then show a per-module coverage report. Auto-fetch
a shallow clone of MAVProxy (pinned to MAVPROXY_PIN) into .parity/ on
first run.

Usage:
    python scripts/parity.py
    python scripts/parity.py --mavproxy /path/to/mavproxy
"""

import argparse
import ast
import re
import subprocess
import sys
import warnings
from dataclasses import dataclass
from dataclasses import field
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from rich.console import Console
    from rich.table import Table
    from rich.text import Text

try:
    from rich.console import Console
    from rich.table import Table
    from rich.text import Text

    HAS_RICH = True
except ImportError:
    HAS_RICH = False

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent

MAVPROXY_PIN = "v1.8.74"
MAVPROXY_REPO_URL = "https://github.com/ArduPilot/MAVProxy.git"
PARITY_CACHE = REPO_ROOT / ".parity"


def ensure_mavproxy(pin: str = MAVPROXY_PIN) -> Path:
    """Clone or reuse a shallow MAVProxy checkout at the pinned version."""
    cache = PARITY_CACHE / "mavproxy"
    marker = cache / ".parity-pin"

    if marker.is_file() and marker.read_text().strip() == pin:
        return cache

    if cache.exists():
        # Pin changed -- wipe and re-clone.
        import shutil

        shutil.rmtree(cache)

    print(f"Fetching MAVProxy {pin} ...", file=sys.stderr)
    subprocess.run(
        [
            "git",
            "clone",
            "--depth",
            "1",
            "--branch",
            pin,
            MAVPROXY_REPO_URL,
            str(cache),
        ],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.PIPE,
    )
    marker.write_text(pin)
    return cache


# Commands hardcoded in mavproxy.py's command_map and input loop, not registered
# via add_command.  Maps command name to a pseudo-module name for display.
BUILTIN_COMMANDS = {
    "help": "_builtin",
    "status": "_builtin",
    "set": "_builtin",
    "watch": "_builtin",
    "script": "_builtin",
    "alias": "_builtin",
    "module": "_builtin",
    "setup": "_builtin",
    "reset": "_builtin",
    "click": "_builtin",
}

# Modules we intend to implement for CLI parity (no GUI required).
# Everything else is shown under "Other" in the grouped report.
IMPORTANT_MODULES = {
    # Built-in commands (status, set, watch, etc.)
    "_builtin",
    # Core flight
    "arm",
    "mode",
    "rc",
    "cmdlong",
    "relay",
    # Params & config
    "param",
    "calibration",
    "rcsetup",
    "auxopt",
    "tuneopt",
    # Navigation & missions
    "oldwp",
    "fenceitem_protocol",
    "rallypoint_protocol",
    "terrain",
    # Logging & data
    "log",
    "ftp",
    "dataflash_logger",
    "message",
    "messagerate",
    "sensors",
    # Links & system
    "link",
    "output",
    "serial",
    "signing",
    "battery",
    "firmware",
    # Misc (reboot, version, land, gethome, etc.)
    "misc",
}


# ---------------------------------------------------------------------------
# Data types
# ---------------------------------------------------------------------------


@dataclass
class MavCommand:
    """A single add_command registration in MAVProxy."""

    name: str
    handler: str
    description: str
    subcommands: set[str] = field(default_factory=set)
    has_completion_subs: bool = False  # True if completions declared subcommands


@dataclass
class MavModule:
    """One MAVProxy module file."""

    name: str
    filepath: Path
    commands: list[MavCommand] = field(default_factory=list)

    def trackable_items(self) -> list[str]:
        """Return leaf-level items for parity tracking.

        If a command has completion-declared subcommands, each
        (command, subcommand) pair is an item.  If subcommands only
        came from dispatch scraping (optional args like 'force'),
        include both the bare command and the subcommand items.
        Commands with no subcommands are tracked as bare items.
        """
        items = []
        for cmd in self.commands:
            if cmd.subcommands and cmd.has_completion_subs:
                # Completions are authoritative -- only track subcommands
                for sub in sorted(cmd.subcommands):
                    items.append(f"{cmd.name} {sub}")
            elif cmd.subcommands:
                # Dispatch-only subs (likely optional args) -- track both
                items.append(cmd.name)
                for sub in sorted(cmd.subcommands):
                    items.append(f"{cmd.name} {sub}")
            else:
                items.append(cmd.name)
        return sorted(items)


# ---------------------------------------------------------------------------
# MAVProxy parsing (AST-based)
# ---------------------------------------------------------------------------


def parse_mavproxy(mavproxy_root: Path) -> list[MavModule]:
    """Discover and parse all MAVProxy module files."""
    modules_dir = mavproxy_root / "MAVProxy" / "modules"
    if not modules_dir.is_dir():
        print(f"Error: MAVProxy modules dir not found: {modules_dir}", file=sys.stderr)
        sys.exit(1)

    results = []

    # Single-file modules
    for path in sorted(modules_dir.glob("mavproxy_*.py")):
        mod = _parse_module_file(path)
        if mod:
            results.append(mod)

    # Directory-based modules
    for path in sorted(modules_dir.glob("mavproxy_*/__init__.py")):
        mod = _parse_module_file(path)
        if mod:
            results.append(mod)

    # Add synthetic entries for commands hardcoded in mavproxy.py.
    # Group by pseudo-module name so they appear as one module.
    builtin_groups: dict[str, list[MavCommand]] = {}
    for cmd_name, module_name in BUILTIN_COMMANDS.items():
        builtin_groups.setdefault(module_name, []).append(
            MavCommand(name=cmd_name, handler="", description="built-in")
        )
    for module_name, cmds in builtin_groups.items():
        results.append(
            MavModule(
                name=module_name,
                filepath=mavproxy_root / "MAVProxy" / "mavproxy.py",
                commands=cmds,
            )
        )

    return results


def _module_name_from_path(path: Path) -> str:
    """Derive module name from file path."""
    if path.name == "__init__.py":
        # Directory module: mavproxy_SIYI/__init__.py -> SIYI
        return path.parent.name.removeprefix("mavproxy_")
    # File module: mavproxy_arm.py -> arm
    return path.stem.removeprefix("mavproxy_")


def _parse_module_file(path: Path) -> MavModule | None:
    """Parse one module file for add_command calls and dispatch patterns."""
    try:
        source = path.read_text(encoding="utf-8", errors="replace")
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", SyntaxWarning)
            tree = ast.parse(source, filename=str(path))
    except SyntaxError:
        return None

    name = _module_name_from_path(path)
    commands = _extract_add_commands(tree)

    if not commands:
        return None

    # Enrich with dispatch-extracted subcommands
    for cmd in commands:
        if cmd.handler:
            dispatch_subs = _extract_dispatch_subcommands(source, cmd.handler)
            cmd.subcommands |= dispatch_subs

    return MavModule(name=name, filepath=path, commands=commands)


def _extract_add_commands(tree: ast.Module) -> list[MavCommand]:
    """Walk AST for self.add_command(...) calls."""
    commands = []

    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        # Match self.add_command(...)
        func = node.func
        if not (
            isinstance(func, ast.Attribute)
            and func.attr == "add_command"
            and isinstance(func.value, ast.Name)
            and func.value.id == "self"
        ):
            continue

        args = node.args

        # First arg: command name (string)
        if len(args) < 1 or not isinstance(args[0], ast.Constant):
            continue
        cmd_name = str(args[0].value)

        # Second arg: handler (self.method_name)
        handler = ""
        if len(args) >= 2 and isinstance(args[1], ast.Attribute):
            handler = args[1].attr

        # Third arg: description
        description = ""
        if len(args) >= 3 and isinstance(args[2], ast.Constant):
            description = str(args[2].value)

        # Fourth arg: completions (optional)
        subcommands = set()
        completions_node = None
        if len(args) >= 4:
            completions_node = args[3]
        else:
            # Check keyword args
            for kw in node.keywords:
                if kw.arg == "completions":
                    completions_node = kw.value
                    break

        has_completion_subs = False
        if completions_node is not None:
            subcommands = _parse_completions_node(completions_node)
            has_completion_subs = len(subcommands) > 0

        commands.append(
            MavCommand(
                name=cmd_name,
                handler=handler,
                description=description,
                subcommands=subcommands,
                has_completion_subs=has_completion_subs,
            )
        )

    return commands


def _parse_completions_node(node: ast.expr) -> set[str]:
    """Extract subcommand names from a completions AST node."""
    if isinstance(node, ast.List):
        return _parse_completions_list(node)
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return _parse_completion_entry(node.value)
    # Dynamic (variable, function call, f-string, etc.) -- skip
    return set()


def _parse_completions_list(node: ast.List) -> set[str]:
    """Extract subcommand names from a list of completion entries."""
    subs = set()
    for elt in node.elts:
        if isinstance(elt, ast.Constant) and isinstance(elt.value, str):
            subs |= _parse_completion_entry(elt.value)
        elif isinstance(elt, ast.BinOp) and isinstance(elt.op, ast.Add):
            # String concatenation like 'check ' + self.checkables()
            # Extract the left side if it's a string constant
            text = _extract_left_string(elt)
            if text:
                subs |= _parse_completion_entry(text)
    return subs


def _extract_left_string(node: ast.BinOp) -> str | None:
    """Extract the leftmost string constant from a concatenation chain."""
    if isinstance(node.left, ast.Constant) and isinstance(node.left.value, str):
        return node.left.value.strip()
    if isinstance(node.left, ast.BinOp):
        return _extract_left_string(node.left)
    return None


def _parse_completion_entry(entry: str) -> set[str]:
    """Extract subcommand names from a single completion string.

    Examples:
        '<download|status|erase>' -> {'download', 'status', 'erase'}
        'throttle'                -> {'throttle'}
        'check <checkable>'       -> {'check'}
        '<set|show> (PARAMETER)'  -> {'set', 'show'}
        '(PARAMETER)'             -> set()  (argument placeholder)
    """
    entry = entry.strip()
    if not entry:
        return set()

    # Pure argument placeholder like (PARAMETER)
    if re.match(r"^\([A-Z_]+\)$", entry):
        return set()

    # Starts with <a|b|c> -- alternatives
    m = re.match(r"^<([^>]+)>", entry)
    if m:
        alternatives = m.group(1).split("|")
        # Filter out things that look like format specifiers or numbers-only
        return {
            a.strip() for a in alternatives if a.strip() and not re.match(r"^[%\d]+$", a.strip())
        }

    # Bare word(s) -- first word is the subcommand
    m = re.match(r"^([a-zA-Z_]\w*)", entry)
    if m:
        return {m.group(1)}

    return set()


def _extract_dispatch_subcommands(source: str, handler_name: str) -> set[str]:
    """Regex-scrape handler method body for args[0] dispatch patterns."""
    # Find the method definition
    pattern = re.compile(rf"def\s+{re.escape(handler_name)}\s*\(self.*?\):", re.DOTALL)
    match = pattern.search(source)
    if not match:
        return set()

    # Walk back to the start of the line to get the real indent
    line_start = source.rfind("\n", 0, match.start()) + 1
    def_line = source[line_start : source.find("\n", match.start())]
    def_indent = len(def_line) - len(def_line.lstrip())

    # Extract body lines from after the def line to next def at same indent
    rest_from_def = source[source.find("\n", match.start()) + 1 :]
    body_lines = []
    for line in rest_from_def.split("\n"):
        stripped = line.lstrip()
        if not stripped or stripped.startswith("#"):
            body_lines.append(line)
            continue
        indent = len(line) - len(stripped)
        if indent <= def_indent and stripped.startswith("def "):
            break
        if (
            indent <= def_indent
            and stripped
            and not stripped.startswith(("if ", "elif ", "else", "return", "print("))
        ):
            break
        body_lines.append(line)

    body = "\n".join(body_lines)
    return _scrape_dispatch_patterns(body)


def _scrape_dispatch_patterns(body: str) -> set[str]:
    """Find args[0] == 'x' and args[0] in [...] patterns."""
    subs = set()

    # Pattern: args[0] == 'value' or args[0] == "value"
    for m in re.finditer(r"""args\[0\]\s*==\s*['"](\w+)['"]""", body):
        subs.add(m.group(1))

    # Pattern: args[0] in ["a", "b"] or args[0] in ("a", "b")
    for m in re.finditer(r"""args\[0\]\s+in\s+[\[\(]([^\]\)]+)[\]\)]""", body):
        for sm in re.finditer(r"""['"](\w+)['"]""", m.group(1)):
            subs.add(sm.group(1))

    return subs


# ---------------------------------------------------------------------------
# Groundwork parsing
# ---------------------------------------------------------------------------

_REGISTER_RE = re.compile(r"""\.Register\(\s*"([^"]+)"\s*,""")

# Match "new SomeCommand(" to extract the class name
_NEW_CMD_RE = re.compile(r"""new\s+(\w+)\s*\(""")


def parse_groundwork(groundwork_root: Path) -> dict[str, tuple[str, str]]:
    """Scan .cs files for commands.Register("...", ...) calls.

    Returns dict mapping command string to (relative_file, class_name).
    """
    src_dir = groundwork_root / "src"
    if not src_dir.is_dir():
        print(f"Error: Groundwork src dir not found: {src_dir}", file=sys.stderr)
        sys.exit(1)

    commands: dict[str, tuple[str, str]] = {}
    for cs_file in src_dir.rglob("*.cs"):
        try:
            text = cs_file.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeDecodeError):
            continue
        rel_path = cs_file.relative_to(groundwork_root).as_posix()
        for m in _REGISTER_RE.finditer(text):
            cmd_name = m.group(1)
            # Try to extract class name from the rest of the line
            rest = text[m.end() : m.end() + 100]
            cls_match = _NEW_CMD_RE.search(rest)
            cls_name = cls_match.group(1) if cls_match else ""
            commands[cmd_name] = (rel_path, cls_name)

    return commands


# ---------------------------------------------------------------------------
# Matching
# ---------------------------------------------------------------------------


def find_covering_command(trackable: str, gw_commands: dict[str, tuple[str, str]]) -> str | None:
    """Find the Groundwork command that covers a MAVProxy trackable item.

    Returns the matching GW command string, or None.
    """
    t = trackable.lower()
    for g in gw_commands:
        gl = g.lower()
        if gl == t or gl.startswith(t + " "):
            return g
    return None


@dataclass
class TrackableItem:
    """One leaf-level MAVProxy command for parity tracking."""

    name: str
    module_name: str
    mavproxy_file: str
    gw_command: str  # matching GW command, or ""
    gw_file: str  # GW source file, or ""
    gw_class: str  # GW command class, or ""


@dataclass
class ModuleCoverage:
    """Coverage data for one MAVProxy module."""

    name: str
    description: str
    covered: list[str]
    uncovered: list[str]

    @property
    def total(self) -> int:
        return len(self.covered) + len(self.uncovered)


def compute_coverage(
    modules: list[MavModule], gw_commands: dict[str, tuple[str, str]]
) -> list[ModuleCoverage]:
    """Build per-module coverage data."""
    results = []
    for mod in modules:
        items = mod.trackable_items()
        if not items:
            continue

        desc = mod.commands[0].description if mod.commands else ""

        covered = [i for i in items if find_covering_command(i, gw_commands)]
        uncovered = [i for i in items if not find_covering_command(i, gw_commands)]

        results.append(
            ModuleCoverage(
                name=mod.name,
                description=desc,
                covered=covered,
                uncovered=uncovered,
            )
        )

    results.sort(key=lambda m: m.name.lower())
    return results


@dataclass
class GroundworkOnly:
    """A Groundwork command with no MAVProxy equivalent."""

    name: str
    gw_class: str


def build_directory(
    modules: list[MavModule], gw_commands: dict[str, tuple[str, str]]
) -> tuple[list[TrackableItem], list[GroundworkOnly]]:
    """Build the command directory for the --directory view.

    Returns (covered_items, gw_only_items).
    """
    used_gw: set[str] = set()
    covered = []
    for mod in modules:
        mavproxy_file = f"mavproxy_{mod.name}.py"
        for trackable in mod.trackable_items():
            gw_cmd = find_covering_command(trackable, gw_commands)
            if not gw_cmd:
                continue
            gw_file = ""
            gw_class = ""
            if gw_cmd in gw_commands:
                gw_file, gw_class = gw_commands[gw_cmd]
            # Mark all GW commands that are this trackable or deeper as used
            t = trackable.lower()
            for g in gw_commands:
                gl = g.lower()
                if gl == t or gl.startswith(t + " "):
                    used_gw.add(g)

            covered.append(
                TrackableItem(
                    name=trackable,
                    module_name=mod.name,
                    mavproxy_file=mavproxy_file,
                    gw_command=gw_cmd,
                    gw_file=gw_file,
                    gw_class=gw_class,
                )
            )
    covered.sort(key=lambda i: i.name.lower())

    gw_only = []
    for cmd_name, (_, cls_name) in sorted(gw_commands.items()):
        if cmd_name not in used_gw:
            gw_only.append(GroundworkOnly(name=cmd_name, gw_class=cls_name))

    return covered, gw_only


# ---------------------------------------------------------------------------
# Output formatting
# ---------------------------------------------------------------------------


def _bar(covered: int, total: int, width: int = 20) -> str:
    """Render a text progress bar."""
    if total == 0:
        return "-" * width
    filled = round(covered / total * width)
    return "#" * filled + "-" * (width - filled)


def _split_by_importance(
    coverage: list[ModuleCoverage],
) -> tuple[list[ModuleCoverage], list[ModuleCoverage]]:
    """Split coverage into important and other modules."""
    important = [m for m in coverage if m.name in IMPORTANT_MODULES]
    other = [m for m in coverage if m.name not in IMPORTANT_MODULES]
    return important, other


def _cap_list(items: list[str], max_items: int) -> str:
    """Join items with comma, capping at max_items (0 = unlimited)."""
    if max_items and len(items) > max_items:
        shown = ", ".join(items[:max_items])
        return f"{shown}, ({len(items) - max_items} more)"
    return ", ".join(items)


def format_report_plain(coverage: list[ModuleCoverage], *, todo_max: int = 6) -> str:
    """Render the report as plain text."""
    important, other = _split_by_importance(coverage)
    lines = ["MAVProxy Parity Tracker", "=" * 40]

    for label, group in [("Target Modules", important), ("Other", other)]:
        if not group:
            continue
        lines.append("")
        lines.append(f"  {label}")
        lines.append(f"  {'-' * len(label)}")

        for mod in group:
            bar = _bar(len(mod.covered), mod.total)
            header = f"  {mod.name:<20} [{bar}] {len(mod.covered):>3}/{mod.total:<3}"
            lines.append(header)
            if mod.covered:
                lines.append(f"    Done: {', '.join(mod.covered)}")
            if mod.uncovered:
                lines.append(f"    Todo: {_cap_list(mod.uncovered, todo_max)}")

    total_covered = sum(len(m.covered) for m in coverage)
    total_items = sum(m.total for m in coverage)
    imp_covered = sum(len(m.covered) for m in important)
    imp_total = sum(m.total for m in important)

    lines.append("")
    pct = round(100 * imp_covered / imp_total) if imp_total else 0
    lines.append(f"  Target:  {imp_covered}/{imp_total} ({pct}%)")
    pct = round(100 * total_covered / total_items) if total_items else 0
    lines.append(f"  Overall: {total_covered}/{total_items} ({pct}%)")
    lines.append("")

    return "\n".join(lines)


def _add_coverage_rows(
    table: Table, modules: list[ModuleCoverage], *, bar_width: int = 20, todo_max: int = 6
) -> None:
    """Add module rows to a rich table."""
    for mod in modules:
        n_covered = len(mod.covered)
        n_total = mod.total

        filled = round(n_covered / n_total * bar_width) if n_total else 0
        if n_covered == n_total:
            color = "green"
        elif n_covered > 0:
            color = "yellow"
        else:
            color = "red"

        bar = Text()
        bar.append("[")
        bar.append("#" * filled, style=f"bold {color}")
        bar.append("-" * (bar_width - filled), style="dim")
        bar.append("]")

        fraction = f"{n_covered}/{n_total}"
        done = ", ".join(mod.covered) if mod.covered else ""
        todo = _cap_list(mod.uncovered, todo_max) if mod.uncovered else ""

        table.add_row(mod.name, bar, fraction, done, todo)


def format_report_rich(coverage: list[ModuleCoverage], *, todo_max: int = 6) -> None:
    """Render the report using rich."""
    console = Console()
    important, other = _split_by_importance(coverage)

    total_covered = sum(len(m.covered) for m in coverage)
    total_items = sum(m.total for m in coverage)
    imp_covered = sum(len(m.covered) for m in important)
    imp_total = sum(m.total for m in important)

    console.print()
    console.print("[bold]MAVProxy Parity Tracker[/bold]")

    for label, group in [("Target Modules", important), ("Other", other)]:
        if not group:
            continue

        console.print()
        console.print(f"  [bold]{label}[/bold]")

        table = Table(show_header=True, header_style="bold", box=None, pad_edge=False)
        table.add_column("Module", style="cyan", min_width=16)
        table.add_column("Progress", min_width=24)
        table.add_column("Coverage", justify="right", min_width=7)
        table.add_column("Done", style="green", ratio=1)
        table.add_column("Todo", style="dim", ratio=1)

        _add_coverage_rows(table, group, todo_max=todo_max)
        console.print(table)

    console.print()
    imp_pct = round(100 * imp_covered / imp_total) if imp_total else 0
    all_pct = round(100 * total_covered / total_items) if total_items else 0
    console.print(f"  Target:  [bold]{imp_covered}/{imp_total}[/bold] ({imp_pct}%)")
    console.print(f"  Overall: [bold]{total_covered}/{total_items}[/bold] ({all_pct}%)")
    console.print()


def format_directory_rich(covered: list[TrackableItem], gw_only: list[GroundworkOnly]) -> None:
    """Render the command directory as a rich table."""
    console = Console()
    console.print()
    console.print("[bold]Groundwork Command Directory[/bold]")

    if covered:
        console.print()
        console.print("  [bold]MAVProxy Parity[/bold]")

        table = Table(show_header=True, header_style="bold", box=None, pad_edge=False)
        table.add_column("Command", style="cyan")
        table.add_column("MAVProxy Module")
        table.add_column("Groundwork Class", style="green")

        for item in covered:
            table.add_row(item.name, item.module_name, item.gw_class)

        console.print(table)

    if gw_only:
        console.print()
        console.print("  [bold]Groundwork Only[/bold]")

        table = Table(show_header=True, header_style="bold", box=None, pad_edge=False)
        table.add_column("Command", style="cyan")
        table.add_column("Groundwork Class", style="green")

        for item in gw_only:
            table.add_row(item.name, item.gw_class)

        console.print(table)

    console.print()


def format_directory_plain(covered: list[TrackableItem], gw_only: list[GroundworkOnly]) -> str:
    """Render the command directory as plain text."""
    lines = [
        "Groundwork Command Directory",
        "=" * 50,
    ]

    if covered:
        lines.append("")
        lines.append("  MAVProxy Parity")
        lines.append(f"  {'-' * 16}")
        lines.append(f"  {'Command':<30} {'Module':<16} {'GW Class'}")
        lines.append(f"  {'-' * 30} {'-' * 16} {'-' * 20}")
        for item in covered:
            lines.append(f"  {item.name:<30} {item.module_name:<16} {item.gw_class}")

    if gw_only:
        lines.append("")
        lines.append("  Groundwork Only")
        lines.append(f"  {'-' * 16}")
        lines.append(f"  {'Command':<30} {'GW Class'}")
        lines.append(f"  {'-' * 30} {'-' * 20}")
        for item in gw_only:
            lines.append(f"  {item.name:<30} {item.gw_class}")

    lines.append("")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Compare MAVProxy commands with Groundwork registrations."
    )
    parser.add_argument(
        "--mavproxy",
        type=Path,
        default=None,
        help="MAVProxy repo root (default: auto-fetch into .parity/)",
    )
    parser.add_argument(
        "--groundwork",
        type=Path,
        default=REPO_ROOT,
        help=f"Groundwork repo root (default: {REPO_ROOT})",
    )
    parser.add_argument(
        "--plain",
        action="store_true",
        help="Force plain text output (no rich)",
    )
    parser.add_argument(
        "--directory",
        action="store_true",
        help="Show full command directory (mapping table)",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Show all modules (default: target modules only)",
    )
    parser.add_argument(
        "--todo-max",
        type=int,
        default=6,
        metavar="N",
        help="Max todo items to list per module (0 = unlimited, default: 6)",
    )
    parser.add_argument(
        "modules",
        nargs="*",
        metavar="MODULE",
        help="Show only these modules (e.g. arm mode param)",
    )
    args = parser.parse_args()

    use_rich = HAS_RICH and not args.plain
    if not HAS_RICH and not args.plain:
        print("hint: install rich for table output: uv pip install rich", file=sys.stderr)
        print(file=sys.stderr)
    mavproxy_root = args.mavproxy or ensure_mavproxy()
    all_modules = parse_mavproxy(mavproxy_root)
    gw_commands = parse_groundwork(args.groundwork)

    # Filter modules; specifying modules implies unlimited todos
    if args.modules:
        selected = {m.lower() for m in args.modules}
        all_modules = [m for m in all_modules if m.name.lower() in selected]
        if args.todo_max == 6:  # still at default
            args.todo_max = 0
    elif not getattr(args, "all"):
        all_modules = [m for m in all_modules if m.name in IMPORTANT_MODULES]

    if args.directory:
        covered, gw_only = build_directory(all_modules, gw_commands)
        # GW-only is misleading when modules are filtered (items matching
        # excluded modules would appear as GW-only).
        if args.modules:
            gw_only = []
        if use_rich:
            format_directory_rich(covered, gw_only)
        else:
            print(format_directory_plain(covered, gw_only))
    else:
        coverage = compute_coverage(all_modules, gw_commands)
        if use_rich:
            format_report_rich(coverage, todo_max=args.todo_max)
        else:
            print(format_report_plain(coverage, todo_max=args.todo_max))


if __name__ == "__main__":
    main()
