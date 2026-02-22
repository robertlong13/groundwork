#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

"""Client for the Groundwork remote REPL socket.

Usage:
    python scripts/repl_client.py                          # interactive
    python scripts/repl_client.py overview                  # single command
    python scripts/repl_client.py "mode qloiter"            # quoted command
    python scripts/repl_client.py -p 5000 overview          # custom port
"""

import argparse
import re
import socket
import sys

# Matches a prompt at the start of a line: bare "> " or "MODE> ".
_PROMPT_RE = re.compile(r"^\w*> $")


def read_until_prompt(rfile):
    """Read lines until a prompt appears at the start of a line."""
    lines = []
    buf = ""
    while True:
        ch = rfile.read(1)
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


def send_command(wfile, rfile, command):
    """Send a command and return response lines."""
    wfile.write(command + "\n")
    wfile.flush()
    return read_until_prompt(rfile)


def run_interactive(host, port, timeout):
    """Run an interactive session, reading commands from stdin."""
    sock = socket.create_connection((host, port), timeout=timeout)
    sock.settimeout(timeout)
    rfile = sock.makefile("r")
    wfile = sock.makefile("w")

    read_until_prompt(rfile)  # consume connect prompt

    try:
        while True:
            try:
                line = input("> ")
            except (EOFError, KeyboardInterrupt):
                break

            line = line.strip()
            if not line:
                continue
            if line.lower() in ("exit", "quit"):
                break

            try:
                for resp in send_command(wfile, rfile, line):
                    print(resp)
            except ConnectionError:
                print("Connection closed by server", file=sys.stderr)
                break
    finally:
        try:
            wfile.write("exit\n")
            wfile.flush()
        except OSError:
            pass
        rfile.close()
        wfile.close()
        sock.close()


def run_oneshot(host, port, timeout, command):
    """Send a single command, print response, disconnect."""
    sock = socket.create_connection((host, port), timeout=timeout)
    sock.settimeout(timeout)
    rfile = sock.makefile("r")
    wfile = sock.makefile("w")

    read_until_prompt(rfile)  # consume connect prompt

    try:
        for line in send_command(wfile, rfile, command):
            print(line)
    finally:
        try:
            wfile.write("exit\n")
            wfile.flush()
        except OSError:
            pass
        rfile.close()
        wfile.close()
        sock.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("-p", "--port", type=int, default=4242, help="REPL port (default: 4242)")
    parser.add_argument("--host", default="127.0.0.1", help="REPL host (default: 127.0.0.1)")
    parser.add_argument(
        "-t", "--timeout", type=float, default=10, help="socket timeout (default: 10)"
    )
    parser.add_argument("command", nargs="*", help="command to send (omit for interactive)")
    args = parser.parse_args()

    command = " ".join(args.command) if args.command else None

    try:
        if command:
            run_oneshot(args.host, args.port, args.timeout, command)
        else:
            run_interactive(args.host, args.port, args.timeout)
    except ConnectionRefusedError:
        print(
            f"Could not connect to {args.host}:{args.port} -- is Groundwork running with --repl-remote?",
            file=sys.stderr,
        )
        sys.exit(1)


if __name__ == "__main__":
    main()
