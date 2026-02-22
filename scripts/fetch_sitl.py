#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

"""Fetch ArduPilot SITL binaries for integration testing.

Download the SITL executable, Cygwin DLLs (Windows), and default parameter
files from firmware.ardupilot.org. Version-tracked via git.txt so repeated
runs are fast.

Usage:
    python scripts/fetch_sitl.py                            # ArduPlane stable
    python scripts/fetch_sitl.py ArduCopter                 # ArduCopter stable
    python scripts/fetch_sitl.py --force                    # re-download
    python scripts/fetch_sitl.py --wipe                     # clean runtime dir
"""

import argparse
import gzip
import json
import os
import platform
import stat
import sys
import urllib.error
import urllib.request
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent
SITL_DIR = REPO_ROOT / "tests" / "integration" / "sitl"

# ---------------------------------------------------------------------------
# URLs
# ---------------------------------------------------------------------------

CYGWIN_STABLE_URL = "https://firmware.ardupilot.org/Tools/MissionPlanner/sitl/"
MANIFEST_URL = "https://firmware.ardupilot.org/manifest.json.gz"
GITHUB_RAW = "https://raw.githubusercontent.com/ArduPilot/ardupilot"

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

VEHICLES = ["ArduPlane", "ArduCopter", "ArduRover", "ArduHeli", "ArduSub"]

CYGWIN_DLLS = [
    "cygatomic-1.dll",
    "cyggcc_s-1.dll",
    "cyggcc_s-seh-1.dll",
    "cyggomp-1.dll",
    "cygiconv-2.dll",
    "cygintl-8.dll",
    "cygquadmath-0.dll",
    "cygssp-0.dll",
    "cygstdc++-6.dll",
    "cygwin1.dll",
]

# Vehicle -> stable subdirectory on firmware.ardupilot.org.
CYGWIN_VEHICLE_DIR = {
    "ArduPlane": "PlaneStable",
    "ArduCopter": "CopterStable",
    "ArduRover": "RoverStable",
    "ArduHeli": "CopterStable",
    "ArduSub": "CopterStable",
}

# SITL model -> default params path in the ArduPilot repo (Tools/autotest/).
MODEL_DEFAULTS = {
    "quadplane": "default_params/quadplane.parm",
    "+": "default_params/copter.parm",
    "plane": "default_params/plane.parm",
    "rover": "default_params/rover.parm",
    "heli": "default_params/heli.parm",
}

# Manifest mav-type strings keyed by vehicle name.
MANIFEST_MAV_TYPE = {
    "ArduPlane": "FIXED_WING",
    "ArduCopter": "Copter",
    "ArduRover": "GROUND_ROVER",
    "ArduHeli": "HELICOPTER",
    "ArduSub": "SUBMARINE",
}

# Manifest platform strings keyed by machine architecture.
MANIFEST_PLATFORM = {
    "x86_64": "SITL_x86_64_linux_gnu",
    "aarch64": "SITL_arm_linux_gnueabihf",
}

MANIFEST_RELEASE_TYPE = "OFFICIAL"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def is_windows() -> bool:
    return sys.platform == "win32"


def parse_commit_hash(git_txt: str) -> str:
    """Extract the full commit hash from git-log-formatted git.txt content."""
    # Format: "commit <40-hex-chars>\nAuthor: ..."
    first_line = git_txt.strip().splitlines()[0]
    parts = first_line.split()
    if len(parts) >= 2 and len(parts[1]) == 40:
        return parts[1]
    raise ValueError(f"Cannot parse commit hash from git.txt: {first_line!r}")


def fetch_text(url: str) -> str:
    """Download a small text resource and return its content."""
    with urllib.request.urlopen(url, timeout=30) as resp:
        return resp.read().decode("utf-8")


def download_file(url: str, dest: Path) -> None:
    """Download a file with Content-Length validation and atomic write."""
    with urllib.request.urlopen(url, timeout=30) as resp:
        expected = resp.headers.get("Content-Length")
        data = resp.read()

    if expected is not None:
        expected_int = int(expected)
        if len(data) != expected_int:
            raise RuntimeError(
                f"Size mismatch for {url}: expected {expected_int}, got {len(data)}"
            )

    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".tmp")
    tmp.write_bytes(data)
    tmp.replace(dest)


def check_version(remote_text: str, local_path: Path, force: bool) -> bool:
    """Return True if a download is needed."""
    if force:
        return True
    if not local_path.exists():
        return True
    local_text = local_path.read_bytes().decode("utf-8").strip()
    return local_text != remote_text.strip()


def cygwin_base_url(vehicle: str) -> str:
    """Return the base URL for Windows Cygwin stable SITL builds."""
    return CYGWIN_STABLE_URL + CYGWIN_VEHICLE_DIR[vehicle] + "/"


def fetch_defaults(commit_hash: str, model: str, output_dir: Path) -> None:
    """Fetch default parameter file from ArduPilot at the build's commit."""
    rel_path = MODEL_DEFAULTS.get(model)
    if rel_path is None:
        print(f"  No known defaults for model {model!r}, skipping", file=sys.stderr)
        return

    url = f"{GITHUB_RAW}/{commit_hash}/Tools/autotest/{rel_path}"
    dest = output_dir / "defaults" / Path(rel_path).name

    print(f"  {Path(rel_path).name} (from {commit_hash[:10]})")
    try:
        download_file(url, dest)
    except urllib.error.HTTPError as e:
        print(f"  Warning: could not fetch defaults: HTTP {e.code}", file=sys.stderr)


# ---------------------------------------------------------------------------
# Platform-specific fetch
# ---------------------------------------------------------------------------


def fetch_windows(vehicle: str, model: str, output_dir: Path, force: bool) -> int:
    base_url = cygwin_base_url(vehicle)
    git_txt_path = output_dir / "git.txt"

    # Version check
    print(f"Checking version at {base_url}git.txt ...")
    remote_text = fetch_text(base_url + "git.txt")
    commit_hash = parse_commit_hash(remote_text)
    needs_download = check_version(remote_text, git_txt_path, force)

    # Check for missing DLLs even if version matches
    missing_dlls = [d for d in CYGWIN_DLLS if not (output_dir / d).exists()]
    exe_name = vehicle + ".exe"
    exe_missing = not (output_dir / exe_name).exists()

    if not needs_download and not missing_dlls and not exe_missing:
        print(f"Already up to date ({commit_hash[:10]}).")
        return 0

    output_dir.mkdir(parents=True, exist_ok=True)

    # Download vehicle binary (.elf -> .exe)
    if needs_download or exe_missing:
        elf_url = base_url + vehicle + ".elf"
        print(f"Downloading {vehicle}:")
        print(f"  {exe_name}")
        download_file(elf_url, output_dir / exe_name)

    # Download Cygwin DLLs
    dlls_to_fetch = CYGWIN_DLLS if needs_download else missing_dlls
    if dlls_to_fetch:
        print(f"  Cygwin DLLs ({len(dlls_to_fetch)}):")
        for dll in dlls_to_fetch:
            dll_url = base_url + dll
            try:
                download_file(dll_url, output_dir / dll)
                print(f"    {dll}")
            except urllib.error.HTTPError as e:
                if e.code == 404:
                    # Stable builds have fewer DLLs than dev
                    pass
                else:
                    raise

    # Fetch default params at the build's commit
    print("  Defaults:")
    fetch_defaults(commit_hash, model, output_dir)

    # Write version marker last (crash-safe)
    git_txt_path.write_bytes(remote_text.encode("utf-8"))
    print(f"Done ({commit_hash[:10]}).")
    return 0


def fetch_linux(vehicle: str, model: str, output_dir: Path, force: bool) -> int:
    arch = platform.machine()
    plat = MANIFEST_PLATFORM.get(arch)
    if plat is None:
        print(f"Unsupported architecture: {arch}", file=sys.stderr)
        return 1

    mav_type = MANIFEST_MAV_TYPE[vehicle]

    # Fetch and parse manifest
    print("Fetching firmware manifest ...")
    with urllib.request.urlopen(MANIFEST_URL, timeout=30) as resp:
        raw = gzip.decompress(resp.read())
    manifest = json.loads(raw)

    # Filter to stable releases
    entries = [
        fw
        for fw in manifest["firmware"]
        if fw.get("platform") == plat
        and fw.get("mav-type") == mav_type
        and fw.get("mav-firmware-version-type") == MANIFEST_RELEASE_TYPE
    ]

    if not entries:
        print(
            f"No manifest entry for {vehicle} stable on {plat}",
            file=sys.stderr,
        )
        return 1

    # Pick newest by firmware version
    entries.sort(key=lambda fw: fw.get("mav-firmware-version", ""), reverse=True)
    entry = entries[0]
    git_sha = entry["git-sha"]
    fw_url = entry["url"]

    # Version check
    git_txt_path = output_dir / "git.txt"
    if not check_version(git_sha, git_txt_path, force):
        print(f"Already up to date ({git_sha[:10]}).")
        return 0

    output_dir.mkdir(parents=True, exist_ok=True)

    # Download binary
    print(f"Downloading {vehicle} ({git_sha[:10]}):")
    dest = output_dir / vehicle
    print(f"  {vehicle}")
    download_file(fw_url, dest)
    os.chmod(dest, dest.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    # Fetch default params at the build's commit
    print("  Defaults:")
    fetch_defaults(git_sha, model, output_dir)

    # Write version marker last
    git_txt_path.write_bytes(git_sha.encode("utf-8"))
    print(f"Done ({git_sha[:10]}).")
    return 0


# ---------------------------------------------------------------------------
# Wipe
# ---------------------------------------------------------------------------


def wipe_runtime(model: str, output_dir: Path) -> int:
    """Remove the SITL runtime directory (logs, eeprom, terrain)."""
    runtime_dir = output_dir / model
    if not runtime_dir.exists():
        print(f"Nothing to wipe ({runtime_dir}).")
        return 0

    import shutil

    shutil.rmtree(runtime_dir)
    print(f"Wiped {runtime_dir}.")
    return 0


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "vehicle",
        nargs="?",
        default="ArduPlane",
        choices=VEHICLES,
        help="vehicle type (default: ArduPlane)",
    )
    parser.add_argument(
        "--model",
        default="quadplane",
        choices=list(MODEL_DEFAULTS),
        help="SITL model for default params (default: quadplane)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="force download even if version matches",
    )
    parser.add_argument(
        "--wipe",
        action="store_true",
        help="remove runtime directory (logs, eeprom, terrain)",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=SITL_DIR,
        help="output directory (default: tests/integration/sitl)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    output_dir: Path = args.output_dir

    if args.wipe:
        return wipe_runtime(args.model, output_dir)

    try:
        if is_windows():
            return fetch_windows(args.vehicle, args.model, output_dir, args.force)
        else:
            return fetch_linux(args.vehicle, args.model, output_dir, args.force)
    except urllib.error.HTTPError as e:
        print(f"HTTP error {e.code}: {e.url}", file=sys.stderr)
        return 1
    except urllib.error.URLError as e:
        print(f"Network error: {e.reason}", file=sys.stderr)
        return 1
    except (OSError, RuntimeError) as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
