# SPDX-License-Identifier: GPL-3.0-or-later

"""Provide pytest fixtures for Groundwork integration tests.

Manage SITL and Groundwork.Console processes, plus pymavlink and
remote REPL connections.
"""

import os
import socket
import subprocess
import sys
import time

import pytest
from helpers import ReplClient
from helpers import drain
from helpers import wait_heartbeat
from helpers import wait_ready_to_arm
from pymavlink import mavutil

# ---------------------------------------------------------------------------
# Paths -- derived from test directory layout, overridable via env vars
# ---------------------------------------------------------------------------

_TEST_DIR = os.path.dirname(os.path.abspath(__file__))
_SITL_BASE = os.path.join(_TEST_DIR, "sitl")

SITL_DIR = os.environ.get("GW_SITL_DIR", os.path.join(_SITL_BASE, "quadplane"))
_EXE_NAME = "ArduPlane.exe" if sys.platform == "win32" else "ArduPlane"
SITL_EXE = os.environ.get("GW_SITL_EXE", os.path.join(_SITL_BASE, _EXE_NAME))
SITL_DEFAULTS = os.environ.get(
    "GW_SITL_DEFAULTS",
    os.path.join(_SITL_BASE, "defaults", "quadplane.parm"),
)

# Groundwork.Console project path (relative to repo root).
CONSOLE_PROJECT = os.environ.get(
    "GW_CONSOLE_PROJECT",
    "src/Groundwork.Console",
)

# Ports
GW_PORT = 14580  # SITL --serial0 -> Groundwork
PYMAV_PORT = 14590  # SITL --serial1 -> pymavlink observer
REMOTE_PORT = 4242  # Groundwork remote REPL socket

# ---------------------------------------------------------------------------
# CLI options
# ---------------------------------------------------------------------------


def pytest_addoption(parser):
    parser.addoption(
        "--skip-sitl",
        action="store_true",
        default=False,
        help="Skip launching SITL (assume it is already running).",
    )


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(scope="session")
def repo_root():
    """Locate the repository root (directory containing Groundwork.slnx)."""
    d = os.path.dirname(__file__)
    while d != os.path.dirname(d):
        if os.path.exists(os.path.join(d, "Groundwork.slnx")):
            return d
        d = os.path.dirname(d)
    raise RuntimeError("Could not find Groundwork.slnx above test directory")


@pytest.fixture(scope="session")
def sitl(request):
    """Launch SITL (session-scoped). Skipped with --skip-sitl."""
    if request.config.getoption("--skip-sitl"):
        yield None
        return

    if not os.path.isfile(SITL_EXE):
        pytest.skip(f"SITL executable not found: {SITL_EXE}")

    # SITL_DIR is the runtime working directory (logs, eeprom, terrain).
    # It may not exist on a fresh checkout or in CI.
    os.makedirs(SITL_DIR, exist_ok=True)

    # --defaults is relative to cwd (SITL_DIR). When the parm lives in
    # SITL_DIR itself we can just pass the filename.
    defaults = os.path.relpath(SITL_DEFAULTS, SITL_DIR)

    cmd = [
        SITL_EXE,
        "-M",
        "quadplane",
        "--defaults",
        defaults,
        "--speedup",
        "5",
        f"--serial0=udpclient:127.0.0.1:{GW_PORT}",
        f"--serial1=udpclient:127.0.0.1:{PYMAV_PORT}",
    ]

    proc = subprocess.Popen(
        cmd,
        cwd=SITL_DIR,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

    yield proc

    proc.terminate()
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.kill()


@pytest.fixture(scope="session")
def mav(sitl):
    """Session-scoped pymavlink connection for independent state verification.

    Doubles as the SITL readiness gate by blocking until a heartbeat
    arrives on the observer port.
    """
    conn = mavutil.mavlink_connection(f"udpin:127.0.0.1:{PYMAV_PORT}")
    wait_heartbeat(conn, timeout=30)
    yield conn
    conn.close()  # type: ignore


@pytest.fixture(scope="session")
def console(repo_root, mav):
    """Launch Groundwork.Console with --link and --repl-remote (session-scoped).

    Depends on mav (not sitl directly) so SITL is confirmed ready before
    Console starts.
    """
    cmd = [
        "dotnet",
        "run",
        "--project",
        os.path.join(repo_root, CONSOLE_PROJECT),
        "--",
        "--link",
        f"udpin:{GW_PORT}",
        "--repl-remote",
        str(REMOTE_PORT),
    ]

    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

    # Wait for the remote REPL port to accept connections.
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        try:
            s = socket.create_connection(("127.0.0.1", REMOTE_PORT), timeout=1)
            s.close()
            break
        except OSError:
            time.sleep(0.5)
    else:
        proc.kill()
        raise RuntimeError(f"Groundwork.Console did not open remote port {REMOTE_PORT} within 30s")

    yield proc

    # Graceful shutdown: close stdin so the REPL exits.
    proc.stdin.close()  # type: ignore
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        proc.kill()


@pytest.fixture(scope="session")
def vehicle_ready(mav):
    """Wait for SITL vehicle to pass pre-arm checks (GPS lock, EKF, etc.)."""
    wait_ready_to_arm(mav, timeout=60)


@pytest.fixture(scope="session")
def gw_ready(console, vehicle_ready):
    """Wait for Groundwork.Console to discover the vehicle.

    SITL readiness (via pymavlink) does not guarantee Groundwork has
    processed heartbeats on its own link yet. Poll the REPL until
    ``overview`` reports a vehicle.
    """
    client = ReplClient(port=REMOTE_PORT)
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        lines = client.send_multi("overview")
        text = "\n".join(lines)
        if "No vehicle connected" not in text:
            client.close()
            return
        time.sleep(0.5)
    client.close()
    raise RuntimeError("Groundwork.Console did not discover a vehicle within 30s")


@pytest.fixture(autouse=True)
def _drain_mav(request):
    """Drain buffered pymavlink messages before each test.

    Prevents large stale backlogs from previous tests from causing false
    timeouts in wait_* helpers. Only runs for tests that use the mav fixture.
    """
    if "mav" in request.fixturenames:
        mav = request.getfixturevalue("mav")
        drain(mav)


@pytest.fixture()
def repl(console):
    """Per-test remote REPL client."""
    client = ReplClient(port=REMOTE_PORT)
    yield client
    client.close()
