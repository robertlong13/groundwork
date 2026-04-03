# SPDX-License-Identifier: GPL-3.0-or-later

"""Integration tests for multi-vehicle discovery and command targeting.

Launch two SITL instances on separate UDP ports and verify Groundwork
discovers both vehicles and routes commands independently.
"""

import os
import subprocess
import sys
import time

import pytest
from helpers import ReplClient

_TEST_DIR = os.path.dirname(os.path.abspath(__file__))
_SITL_BASE = os.path.join(_TEST_DIR, "sitl")
_EXE_NAME = "ArduPlane.exe" if sys.platform == "win32" else "ArduPlane"
_SITL_EXE = os.path.join(_SITL_BASE, _EXE_NAME)
_SITL_DEFAULTS = os.path.join(_SITL_BASE, "defaults", "quadplane.parm")

# Ports offset from the single-vehicle suite to avoid conflicts.
_GW_PORT_0 = 14595
_GW_PORT_1 = 14596
_REMOTE_PORT = 4243
_BASE_PORT = 5670


@pytest.fixture(scope="module")
def multi_sitl():
    """Launch two SITL instances on separate UDP ports."""
    if not os.path.isfile(_SITL_EXE):
        pytest.skip(f"SITL executable not found: {_SITL_EXE}")

    procs = []
    for i in range(2):
        work_dir = os.path.join(_SITL_BASE, f"multi_{i}")
        os.makedirs(work_dir, exist_ok=True)
        defaults = os.path.relpath(_SITL_DEFAULTS, work_dir)
        port = _GW_PORT_0 + i
        base = _BASE_PORT + 10 * i

        cmd = [
            _SITL_EXE,
            "-M",
            "quadplane",
            "--defaults",
            defaults,
            "--wipe",
            "--speedup",
            "5",
            f"--serial0=udpclient:127.0.0.1:{port}",
            f"--base-port={base}",
            f"--instance={i}",
            f"--sysid={i + 1}",
        ]

        proc = subprocess.Popen(
            cmd,
            cwd=work_dir,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        procs.append(proc)

    yield procs

    for proc in procs:
        proc.terminate()
    for proc in procs:
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()


@pytest.fixture(scope="module")
def multi_console(multi_sitl):
    """Launch Console with two links and wait for the REPL port."""
    repo = os.path.dirname(os.path.dirname(_TEST_DIR))
    cmd = [
        "dotnet",
        "run",
        "--project",
        os.path.join(repo, "src", "Groundwork.Console"),
        "--",
        "--link",
        f"udpin:{_GW_PORT_0}",
        "--link",
        f"udpin:{_GW_PORT_1}",
        "--repl-remote",
        str(_REMOTE_PORT),
    ]

    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

    yield proc

    proc.stdin.close()
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        proc.kill()


@pytest.fixture(scope="module")
def multi_ready(multi_console):
    """Wait for both vehicles to be discovered."""
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            client = ReplClient(port=_REMOTE_PORT)
        except (ConnectionError, OSError):
            time.sleep(1)
            continue
        try:
            lines = client.send_multi("vehicle list")
            vehicle_lines = [l for l in lines if "sysid" in l]
            if len(vehicle_lines) >= 2:
                client.close()
                return
        except (ConnectionError, OSError):
            pass
        client.close()
        time.sleep(1)
    raise RuntimeError("Did not discover 2 vehicles within 60s")


@pytest.fixture()
def repl(multi_console):
    """Per-test REPL client on the multi-vehicle port."""
    client = ReplClient(port=_REMOTE_PORT)
    yield client
    client.close()


def test_multi_vehicle_discovery(repl, multi_ready):
    """Both vehicles appear in vehicle list with distinct sysids."""
    lines = repl.send_multi("vehicle list")
    vehicle_lines = [l for l in lines if "sysid" in l]
    assert len(vehicle_lines) == 2, f"Expected 2 vehicles, got: {lines}"

    # Extract sysids.
    sysids = set()
    for line in vehicle_lines:
        # "  * 0: sysid 1  FIXED_WING ..."
        parts = line.split()
        idx = parts.index("sysid")
        sysids.add(int(parts[idx + 1]))
    assert len(sysids) == 2, f"Expected 2 distinct sysids, got: {sysids}"


def test_multi_vehicle_independent_modes(repl, multi_ready):
    """Mode changes target the correct vehicle."""
    # Set vehicle 0 to RTL.
    repl.send("vehicle select 0")
    response = repl.send("mode rtl")
    assert "RTL" in response

    # Set vehicle 1 to GUIDED.
    repl.send("vehicle select 1")
    response = repl.send("mode guided")
    assert "GUIDED" in response

    # Wait for heartbeats to propagate.
    time.sleep(2)

    # Verify vehicle 0 is still RTL.
    repl.send("vehicle select 0")
    lines = repl.send_multi("overview")
    text = "\n".join(lines)
    assert "RTL" in text, f"Vehicle 0 should be RTL: {text}"

    # Verify vehicle 1 is still GUIDED.
    repl.send("vehicle select 1")
    lines = repl.send_multi("overview")
    text = "\n".join(lines)
    assert "GUIDED" in text, f"Vehicle 1 should be GUIDED: {text}"
