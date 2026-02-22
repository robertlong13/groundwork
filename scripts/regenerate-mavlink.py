#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

"""Regenerate MAVLink C# bindings from pymavlink.

Usage:
    python scripts/regenerate-mavlink.py                          # regen from pinned SHAs
    python scripts/regenerate-mavlink.py --mavlink HEAD           # test latest mavlink XML
    python scripts/regenerate-mavlink.py --pymavlink abc123       # specific pymavlink ref
    python scripts/regenerate-mavlink.py --verify                 # CI: check committed = generated
    python scripts/regenerate-mavlink.py \\
        --mavlink-repo https://github.com/someone/mavlink.git \\
        --mavlink feature-branch                                  # test a fork/PR

Clones pymavlink and mavlink into .mavlink-gen/ (gitignored) on first run.
Subsequent runs fetch and checkout. No pip dependencies; adds the pymavlink
clone to sys.path to import the generator directly.

Prerequisites: Python 3, git
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent
OUTPUT_DIR = REPO_ROOT / "src" / "Groundwork.Core" / "MAVLink"
CACHE_DIR = REPO_ROOT / ".mavlink-gen"
VERSION_FILE = OUTPUT_DIR / "MAVLINK_VERSION"
DIALECT = "all"
WIRE_PROTOCOL = "2.0"

PYMAVLINK_REPO = "https://github.com/ArduPilot/pymavlink.git"
MAVLINK_REPO = "https://github.com/mavlink/mavlink.git"

GENERATED_FILES = [
    "mavlink.cs",
    "MavlinkCRC.cs",
    "MAVLinkMessage.cs",
    "MavlinkParse.cs",
    "MavlinkUtil.cs",
]


def git(*args: str, cwd: Path | None = None) -> str:
    """Run a git command and return stdout."""
    result = subprocess.run(
        ["git", *args],
        cwd=cwd,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed:\n{result.stderr.strip()}")
    return result.stdout.strip()


def read_version_file() -> dict[str, str]:
    """Read key=value pairs from MAVLINK_VERSION."""
    result: dict[str, str] = {}
    if not VERSION_FILE.exists():
        return result
    for line in VERSION_FILE.read_bytes().decode().splitlines():
        line = line.strip()
        if "=" in line and not line.startswith("#"):
            key, _, value = line.partition("=")
            result[key.strip()] = value.strip()
    return result


def build_version_content(
    pymavlink_url: str,
    pymavlink_sha: str,
    pymavlink_date: str,
    mavlink_url: str,
    mavlink_sha: str,
    mavlink_date: str,
) -> str:
    """Build deterministic MAVLINK_VERSION content."""
    return (
        f"dialect={DIALECT}\n"
        f"wire_protocol={WIRE_PROTOCOL}\n"
        f"pymavlink_url={pymavlink_url}\n"
        f"pymavlink_ref={pymavlink_sha}\n"
        f"pymavlink_date={pymavlink_date}\n"
        f"mavlink_url={mavlink_url}\n"
        f"mavlink_ref={mavlink_sha}\n"
        f"mavlink_date={mavlink_date}\n"
    )


def remote_name_from_url(url: str) -> str:
    """Derive a remote name from a repo URL: 'https://github.com/foo/bar.git' -> 'foo-bar'."""
    # Strip trailing .git and slashes
    clean = url.rstrip("/")
    if clean.endswith(".git"):
        clean = clean[:-4]
    parts = clean.rsplit("/", 2)
    if len(parts) >= 2:
        return f"{parts[-2]}-{parts[-1]}"
    return parts[-1]


def ensure_repo(name: str, default_url: str, custom_url: str | None = None) -> tuple[Path, str]:
    """Clone or fetch a git repo. Returns (repo_dir, remote) where remote is
    the name to use for ref resolution."""
    repo_dir = CACHE_DIR / name
    if not (repo_dir / ".git").is_dir():
        print(f"Cloning {name}...")
        CACHE_DIR.mkdir(parents=True, exist_ok=True)
        git("clone", "--quiet", default_url, str(repo_dir))
    else:
        print(f"Fetching {name}...")
        git("fetch", "--quiet", "origin", cwd=repo_dir)

    if custom_url and custom_url != default_url:
        remote = remote_name_from_url(custom_url)
        # Add or update the custom remote
        try:
            existing = git("remote", "get-url", remote, cwd=repo_dir)
            if existing != custom_url:
                git("remote", "set-url", remote, custom_url, cwd=repo_dir)
        except RuntimeError:
            git("remote", "add", remote, custom_url, cwd=repo_dir)
        print(f"Fetching {name} from {remote}...")
        git("fetch", "--quiet", remote, cwd=repo_dir)
        return repo_dir, remote

    return repo_dir, "origin"


def checkout_ref(name: str, ref: str, repo_dir: Path, remote: str = "origin") -> str:
    """Checkout a ref (branch, tag, or SHA) and return the resolved SHA."""
    sha = None
    for candidate in [f"{remote}/{ref}", ref]:
        try:
            sha = git("rev-parse", "--verify", candidate, cwd=repo_dir)
            break
        except RuntimeError:
            continue

    if sha is None:
        print(f"Error: can't resolve {name} ref '{ref}'")
        sys.exit(1)

    git("checkout", "--quiet", sha, cwd=repo_dir)
    date = git("log", "-1", "--format=%cs", cwd=repo_dir)
    print(f"  {name}: {sha} ({date})")
    return sha


def run_generator(pymavlink_dir: Path, xml_path: Path, output_dir: Path) -> None:
    """Run the pymavlink C# generator."""
    # Add pymavlink's parent to sys.path so we can import the generator
    parent = str(pymavlink_dir.parent)
    if parent not in sys.path:
        sys.path.insert(0, parent)

    # Force reimport in case a pip-installed version was loaded earlier
    for mod in list(sys.modules):
        if mod.startswith("pymavlink"):
            del sys.modules[mod]

    from pymavlink.generator import mavgen

    class Opts:
        language = "CS"
        wire_protocol = WIRE_PROTOCOL
        output = str(output_dir)
        validate = False
        error_limit = 200
        strict_units = False

    if not mavgen.mavgen(Opts(), [str(xml_path)]):
        print("Error: mavgen failed")
        sys.exit(1)

    # Remove the csproj the generator copies in (we use our own)
    csproj = output_dir / "MAVLink.csproj"
    if csproj.exists():
        csproj.unlink()


def normalize_line_endings(directory: Path) -> None:
    """Normalize CRLF to LF in generated files.

    The pymavlink generator writes platform-native line endings. Since we
    enforce LF in the repo via .gitattributes, generated output must be
    normalized before comparison or commit.
    """
    for f in GENERATED_FILES:
        path = directory / f
        if path.exists():
            content = path.read_bytes()
            normalized = content.replace(b"\r\n", b"\n")
            if content != normalized:
                path.write_bytes(normalized)


def is_only_build_date(diff_output: str) -> bool:
    """Check whether a git diff contains only a MAVLINK_BUILD_DATE change."""
    changed_lines = [
        line
        for line in diff_output.splitlines()
        if line.startswith(("+", "-")) and not line.startswith(("+++", "---"))
    ]
    return all(re.search(r"MAVLINK_BUILD_DATE", line) for line in changed_lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Check committed output matches generated (for CI)",
    )
    parser.add_argument("--pymavlink", metavar="REF", help="pymavlink git ref")
    parser.add_argument("--mavlink", metavar="REF", help="mavlink git ref")
    parser.add_argument(
        "--pymavlink-repo", metavar="URL", help="pymavlink repo URL (for forks/PRs)"
    )
    parser.add_argument("--mavlink-repo", metavar="URL", help="mavlink repo URL (for forks/PRs)")
    args = parser.parse_args()

    # Resolve refs: CLI args > pinned versions > error
    version = read_version_file()
    pymavlink_ref = args.pymavlink or version.get("pymavlink_ref")
    mavlink_ref = args.mavlink or version.get("mavlink_ref")

    if not pymavlink_ref:
        print(f"Error: no --pymavlink ref and no pinned version in {VERSION_FILE}")
        sys.exit(1)
    if not mavlink_ref:
        print(f"Error: no --mavlink ref and no pinned version in {VERSION_FILE}")
        sys.exit(1)

    # Effective repo URLs: CLI override > hardcoded default
    pymavlink_url = args.pymavlink_repo or PYMAVLINK_REPO
    mavlink_url = args.mavlink_repo or MAVLINK_REPO

    # Clone/fetch and checkout
    pymavlink_dir, pymavlink_remote = ensure_repo("pymavlink", PYMAVLINK_REPO, args.pymavlink_repo)
    mavlink_dir, mavlink_remote = ensure_repo("mavlink", MAVLINK_REPO, args.mavlink_repo)

    print("\nChecking out...")
    pymavlink_sha = checkout_ref("pymavlink", pymavlink_ref, pymavlink_dir, pymavlink_remote)
    mavlink_sha = checkout_ref("mavlink", mavlink_ref, mavlink_dir, mavlink_remote)

    xml_path = mavlink_dir / "message_definitions" / "v1.0" / f"{DIALECT}.xml"
    if not xml_path.exists():
        print(f"Error: {xml_path} not found")
        sys.exit(1)

    # Verify requires a clean working tree in the output directory
    if args.verify:
        dirty = git("diff", "--name-only", "--", str(OUTPUT_DIR), cwd=REPO_ROOT)
        if dirty:
            print(f"Error: uncommitted changes in {OUTPUT_DIR}")
            print("Commit or stash them before running --verify.")
            sys.exit(1)

    # Generate in-place
    print("\nGenerating MAVLink C# bindings...")
    print(f"  dialect:  {DIALECT}")
    print(f"  protocol: {WIRE_PROTOCOL}")

    run_generator(pymavlink_dir, xml_path, OUTPUT_DIR)
    normalize_line_endings(OUTPUT_DIR)

    # Check expected files
    for f in GENERATED_FILES:
        if not (OUTPUT_DIR / f).exists():
            print(f"Error: expected file {f} not found in generator output")
            sys.exit(1)

    # Write version file
    pymavlink_date = git("log", "-1", "--format=%cs", cwd=pymavlink_dir)
    mavlink_date = git("log", "-1", "--format=%cs", cwd=mavlink_dir)
    new_version = build_version_content(
        pymavlink_url,
        pymavlink_sha,
        pymavlink_date,
        mavlink_url,
        mavlink_sha,
        mavlink_date,
    )
    VERSION_FILE.write_bytes(new_version.encode())

    if args.verify:
        diff = git("diff", "--", str(OUTPUT_DIR), cwd=REPO_ROOT)
        git("checkout", "--", str(OUTPUT_DIR), cwd=REPO_ROOT)
        if not diff:
            print("\nOK: committed output matches generated output.")
            sys.exit(0)
        elif is_only_build_date(diff):
            print("\nOK: only MAVLINK_BUILD_DATE differs (non-deterministic).")
            sys.exit(0)
        else:
            print("\nFAIL: committed MAVLink files don't match generator output.")
            print("Run: python scripts/regenerate-mavlink.py")
            sys.exit(1)

    print(f"\nDone. Files written to {OUTPUT_DIR}")
    print("Review the diff and commit when ready.")


if __name__ == "__main__":
    main()
