# SPDX-License-Identifier: GPL-3.0-or-later

"""Integration tests for Groundwork.Console commands via remote REPL.

Requires SITL (QuadPlane) and Groundwork.Console to be running.
See conftest.py for fixture details.
"""

import os

from conftest import SITL_DIR
from helpers import ReplClient
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


def test_ftp_burst_exact_multiple(console, gw_ready, tmp_path):
    """Download a file whose size is an exact multiple of burst payload (239).

    717 = 3 x 239 triggers an ArduPilot bug where burst_complete is never
    set and the EOF NAK offset is one payload short. The confirmation
    burst strategy works around this.
    """
    # Plant a 717-byte file in SITL's working directory (its FTP root).
    file_data = bytes(range(256)) * 2 + bytes(range(717 - 512))
    remote_name = "gw_test_717.bin"
    remote_path = os.path.join(SITL_DIR, remote_name)
    with open(remote_path, "wb") as f:
        f.write(file_data)

    local_path = tmp_path / "downloaded.bin"

    try:
        # Use a longer timeout -- FTP burst + confirmation can take a few seconds.
        repl = ReplClient(timeout=30)
        try:
            response = repl.send(f"ftp get {remote_name} {local_path}")
            assert "Downloaded" in response, f"Unexpected response: {response}"
            assert local_path.exists(), "Local file was not created"
            assert local_path.read_bytes() == file_data
        finally:
            repl.close()
    finally:
        # Clean up the planted file.
        os.unlink(remote_path)
