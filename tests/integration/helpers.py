# SPDX-License-Identifier: GPL-3.0-or-later

"""Provide remote REPL client and pymavlink wait utilities for tests."""

import re
import socket
import time

from pymavlink import mavutil

# Matches a prompt at the start of a partial line: bare "> " or "MODE> ".
_PROMPT_RE = re.compile(r"^\w*> $")

# Elapsed-time prefix for progress lines, matching ArduPilot's AT- style.
_t0 = time.monotonic()


def _progress(text: str):
    """Print timestamped progress. Captured by pytest; shown on failure."""
    elapsed = time.monotonic() - _t0
    print(f"GW-{elapsed:06.1f}: {text}")


class ReplClient:
    """Line-oriented TCP client for the Groundwork remote REPL socket.

    The server sends "> " after connecting and after each command completes.
    This prompt acts as a sentinel marking end-of-response.
    """

    def __init__(self, host: str = "127.0.0.1", port: int = 4242, timeout: float = 5):
        self._sock = socket.create_connection((host, port), timeout=timeout)
        self._sock.settimeout(timeout)
        self._rfile = self._sock.makefile("r")
        self._wfile = self._sock.makefile("w")
        # Consume the initial connect prompt.
        self._read_until_prompt()

    def send(self, command: str, timeout: float | None = None) -> str:
        """Send a command and return the single response line.

        For commands that produce exactly one line of output.
        """
        lines = self.send_multi(command, timeout=timeout)
        return lines[0] if lines else ""

    def send_multi(self, command: str, timeout: float | None = None) -> list[str]:
        """Send a command and collect all response lines until the prompt.

        Returns lines between the command and the next "> " prompt.
        If timeout is given, temporarily override the socket timeout.
        """
        _progress(f"REPL> {command}")
        prev = self._sock.gettimeout()
        if timeout is not None:
            self._sock.settimeout(timeout)
        try:
            self._wfile.write(command + "\n")
            self._wfile.flush()
            lines = self._read_until_prompt()
        finally:
            self._sock.settimeout(prev)
        for line in lines:
            _progress(f"REPL< {line}")
        return lines

    def _read_until_prompt(self) -> list[str]:
        """Read lines until a prompt ("> " or "MODE> ") appears."""
        lines = []
        buf = ""
        while True:
            ch = self._rfile.read(1)
            if not ch:
                raise ConnectionError("Remote server closed the connection")
            buf += ch
            if ch == " " and _PROMPT_RE.match(buf):
                before = buf[: buf.index(">")].strip()
                if before:
                    lines.append(before)
                break
            if ch == "\n":
                lines.append(buf.rstrip("\r\n"))
                buf = ""
        return lines

    def close(self):
        try:
            self._wfile.write("exit\n")
            self._wfile.flush()
        except OSError:
            pass
        self._rfile.close()
        self._wfile.close()
        self._sock.close()


def drain(mav):
    """Drain all buffered messages so subsequent waits see fresh data."""
    while mav.recv_match(blocking=False) is not None:
        pass


def wait_ready_to_arm(mav, timeout: float = 60):
    """Wait until SYS_STATUS PREARM_CHECK bit is healthy.

    Only checks the PREARM_CHECK meta-bit (0x10000000), not all enabled
    sensors. Some sensors (e.g. TERRAIN) may stay unhealthy in SITL
    without affecting armability.
    """
    _progress(f"Waiting for ready-to-arm (timeout={timeout}s)")
    prearm_bit = 0x10000000  # MAV_SYS_STATUS_PREARM_CHECK
    deadline = time.monotonic() + timeout
    last_print = 0.0
    while time.monotonic() < deadline:
        msg = mav.recv_match(type="SYS_STATUS", blocking=True, timeout=2)
        if msg is not None and msg.onboard_control_sensors_health & prearm_bit:
            _progress("Vehicle ready to arm")
            return msg
        now = time.monotonic()
        if now - last_print >= 5:
            _progress("Still waiting for pre-arm checks...")
            last_print = now
    raise TimeoutError(f"Vehicle not ready to arm within {timeout}s")


def wait_heartbeat(mav, timeout: float = 10):
    """Wait for the first heartbeat from the vehicle."""
    _progress(f"Waiting for heartbeat (timeout={timeout}s)")
    msg = mav.recv_match(type="HEARTBEAT", blocking=True, timeout=timeout)
    if msg is None:
        raise TimeoutError(f"No heartbeat within {timeout}s")
    _progress("Heartbeat received")
    return msg


def wait_armed(mav, timeout: float = 10):
    """Wait until the vehicle reports armed in HEARTBEAT."""
    _progress(f"Waiting for armed (timeout={timeout}s)")
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        msg = mav.recv_match(type="HEARTBEAT", blocking=True, timeout=1)
        if msg is not None and msg.base_mode & mavutil.mavlink.MAV_MODE_FLAG_SAFETY_ARMED:
            _progress("Vehicle armed")
            return msg
    raise TimeoutError(f"Vehicle not armed within {timeout}s")


def wait_disarmed(mav, timeout: float = 30):
    """Wait until the vehicle reports disarmed in HEARTBEAT."""
    _progress(f"Waiting for disarmed (timeout={timeout}s)")
    deadline = time.monotonic() + timeout
    last_print = 0.0
    while time.monotonic() < deadline:
        msg = mav.recv_match(type="HEARTBEAT", blocking=True, timeout=1)
        if msg is not None and not (msg.base_mode & mavutil.mavlink.MAV_MODE_FLAG_SAFETY_ARMED):
            _progress("Vehicle disarmed")
            return msg
        now = time.monotonic()
        if now - last_print >= 5:
            _progress("Still armed, waiting for disarm...")
            last_print = now
    raise TimeoutError(f"Vehicle not disarmed within {timeout}s")


def wait_mode(mav, mode_name: str, timeout: float = 10):
    """Wait until HEARTBEAT reports the expected mode (by name)."""
    _progress(f"Waiting for mode {mode_name} (timeout={timeout}s)")
    mode_map = mav.mode_mapping()
    if mode_map is None:
        raise RuntimeError("No mode mapping available -- no heartbeat received?")
    target = mode_map.get(mode_name)
    if target is None:
        raise ValueError(f"Unknown mode {mode_name!r} for this vehicle type")

    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        msg = mav.recv_match(type="HEARTBEAT", blocking=True, timeout=1)
        if msg is not None and msg.custom_mode == target:
            _progress(f"Mode is {mode_name}")
            return msg
    raise TimeoutError(f"Mode did not change to {mode_name} within {timeout}s")


def wait_altitude(mav, target_alt: float, tolerance: float = 2, timeout: float = 30):
    """Wait until GLOBAL_POSITION_INT reports relative_alt near target.

    relative_alt is in mm, target_alt is in meters.
    """
    _progress(f"Waiting for altitude {target_alt}m +/-{tolerance}m (timeout={timeout}s)")
    deadline = time.monotonic() + timeout
    last_print = 0.0
    while time.monotonic() < deadline:
        msg = mav.recv_match(type="GLOBAL_POSITION_INT", blocking=True, timeout=1)
        if msg is not None:
            alt_m = msg.relative_alt / 1000.0
            if abs(alt_m - target_alt) <= tolerance:
                _progress(f"Altitude reached: {alt_m:.1f}m")
                return msg
            now = time.monotonic()
            if now - last_print >= 3:
                _progress(f"Altitude: {alt_m:.1f}m (want {target_alt}m)")
                last_print = now
    raise TimeoutError(f"Altitude did not reach {target_alt}m within {timeout}s")
