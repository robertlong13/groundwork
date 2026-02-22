# SPDX-License-Identifier: GPL-3.0-or-later

"""Integration tests for Groundwork.Console commands via remote REPL.

Requires SITL (QuadPlane) and Groundwork.Console to be running.
See conftest.py for fixture details.
"""

from helpers import wait_altitude
from helpers import wait_armed
from helpers import wait_disarmed
from helpers import wait_mode


def test_vehicle_discovery(repl, vehicle_ready):
    lines = repl.send_multi("overview")
    text = "\n".join(lines)
    assert "No vehicle connected" not in text, "Vehicle not discovered"
    assert "Type:" in text


def test_overview_fields(repl, vehicle_ready):
    lines = repl.send_multi("overview")
    text = "\n".join(lines)
    for field in ("Type:", "Mode:", "Armed:", "Position:", "Alt:", "Heading:", "Battery:"):
        assert field in text, f"Missing field: {field}"


def test_unknown_command(repl):
    response = repl.send("notacommand")
    assert "Unknown command" in response


def test_mode_change(repl, mav, vehicle_ready):
    response = repl.send("mode GUIDED")
    assert response == "Mode: GUIDED"

    wait_mode(mav, "GUIDED", timeout=5)


def test_arm_disarm(repl, mav, vehicle_ready):
    # Ensure GUIDED mode (required for arming without RC).
    repl.send("mode GUIDED")
    wait_mode(mav, "GUIDED", timeout=5)

    response = repl.send("arm throttle")
    assert "Armed" in response
    wait_armed(mav, timeout=5)

    response = repl.send("disarm")
    assert "Disarmed" in response
    wait_disarmed(mav, timeout=5)


def test_takeoff_land(repl, mav, vehicle_ready):
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
