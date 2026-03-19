# SPDX-License-Identifier: GPL-3.0-or-later

"""Integration tests for Groundwork.Console commands via remote REPL.

Requires SITL (QuadPlane) and Groundwork.Console to be running.
See conftest.py for fixture details.
"""

from helpers import wait_altitude
from helpers import wait_armed
from helpers import wait_disarmed
from helpers import wait_mode
from helpers import wait_ready_to_arm


def test_vehicle_discovery(repl, gw_ready):
    lines = repl.send_multi("overview")
    text = "\n".join(lines)
    assert "No vehicle connected" not in text, "Vehicle not discovered"
    assert "Type:" in text


def test_overview_fields(repl, gw_ready):
    lines = repl.send_multi("overview")
    text = "\n".join(lines)
    for field in ("Type:", "Mode:", "Armed:", "Position:", "Alt:", "Heading:", "Battery:"):
        assert field in text, f"Missing field: {field}"


def test_unknown_command(repl):
    response = repl.send("notacommand")
    assert "Unknown command" in response


def test_mode_change(repl, mav, gw_ready):
    response = repl.send("mode GUIDED")
    assert response == "Mode: GUIDED"

    wait_mode(mav, "GUIDED", timeout=5)


def test_arm_disarm(repl, mav, gw_ready):
    # Ensure GUIDED mode (required for arming without RC).
    repl.send("mode GUIDED")
    wait_mode(mav, "GUIDED", timeout=5)

    response = repl.send("arm throttle")
    assert "Armed" in response
    wait_armed(mav, timeout=5)

    response = repl.send("disarm")
    assert "Disarmed" in response
    wait_disarmed(mav, timeout=5)


def test_takeoff_land(repl, mav, gw_ready):
    # Previous test may have just disarmed; wait for pre-arm checks to settle.
    wait_ready_to_arm(mav, timeout=10)

    # GUIDED mode for takeoff.
    repl.send("mode GUIDED")
    wait_mode(mav, "GUIDED", timeout=5)

    response = repl.send("arm throttle")
    assert "Armed" in response
    wait_armed(mav, timeout=5)

    response = repl.send("takeoff 20")
    assert "Takeoff" in response

    # Wait for the vehicle to reach target altitude.
    wait_altitude(mav, 20, tolerance=3, timeout=30)

    # Land via QLAND mode.
    response = repl.send("mode QLAND")
    assert "QLAND" in response
    wait_mode(mav, "QLAND", timeout=5)

    # Wait for touchdown and disarm (can take a while).
    wait_disarmed(mav, timeout=60)


# ---------------------------------------------------------------------------
# Mission round-trip
# ---------------------------------------------------------------------------

# Generous per-item ceiling: 0.5s each covers slow CI runners but catches
# regressions where every waypoint takes a full round-trip timeout.
_MAX_SECONDS_PER_ITEM = 0.5
_MISSION_SIZE = 200


def _generate_waypoints(path: str, count: int):
    """Write a QGC WPL 110 file with a home + count waypoints in a circle."""
    import math

    home_lat, home_lon, home_alt = -35.36326, 149.16524, 584.0
    radius = 0.002  # ~200m

    with open(path, "w", newline="\n") as f:
        f.write("QGC WPL 110\n")
        f.write(f"0\t1\t0\t16\t0\t0\t0\t0\t{home_lat:.8f}\t{home_lon:.8f}\t{home_alt:.6f}\t1\n")
        for i in range(1, count + 1):
            angle = 2 * math.pi * i / count
            lat = home_lat + radius * math.cos(angle)
            lon = home_lon + radius * math.sin(angle)
            f.write(f"{i}\t0\t3\t16\t0\t0\t0\t0\t{lat:.8f}\t{lon:.8f}\t80.000000\t1\n")


def _extract_elapsed(lines: list[str], pattern: str) -> float:
    """Find the first line matching pattern and return the elapsed seconds."""
    import re

    for line in lines:
        m = re.search(pattern, line)
        if m:
            return float(m.group(1))
    raise AssertionError(f"No line matched {pattern!r} in: {lines}")


def test_mission_round_trip(repl, gw_ready):
    """Upload a large mission, download via protocol and FTP, verify counts and timing."""
    import os
    import tempfile

    budget = _MISSION_SIZE * _MAX_SECONDS_PER_ITEM
    timeout = budget + 30  # socket headroom

    with tempfile.NamedTemporaryFile(suffix=".waypoints", delete=False, mode="w") as tmp:
        tmp_path = tmp.name
    _generate_waypoints(tmp_path, _MISSION_SIZE)

    try:
        # Upload via standard protocol.
        lines = repl.send_multi(f'wp load "{tmp_path}"', timeout=timeout)
        text = "\n".join(lines)
        assert "Upload accepted" in text, f"Upload failed: {text}"
        assert f"{_MISSION_SIZE} items" in text

        upload_secs = _extract_elapsed(lines, r"Upload accepted.*?(\d+\.?\d*)\s*s")
        assert upload_secs < budget, f"Upload took {upload_secs:.1f}s, budget {budget:.1f}s"

        # Download via standard protocol.
        lines = repl.send_multi("wp list", timeout=timeout)
        text = "\n".join(lines)
        assert f"Downloaded {_MISSION_SIZE} items" in text

        dl_secs = _extract_elapsed(lines, r"Downloaded.*?in\s+(\d+\.?\d*)\s*s")
        assert dl_secs < budget, f"Protocol download took {dl_secs:.1f}s, budget {budget:.1f}s"

        # Download via FTP.
        lines = repl.send_multi("wp ftp", timeout=timeout)
        text = "\n".join(lines)
        assert f"Downloaded {_MISSION_SIZE} items" in text

        ftp_secs = _extract_elapsed(lines, r"in\s+(\d+\.?\d*)\s*s")
        assert ftp_secs < budget, f"FTP download took {ftp_secs:.1f}s, budget {budget:.1f}s"
    finally:
        os.unlink(tmp_path)
