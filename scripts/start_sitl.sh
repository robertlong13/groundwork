#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later

# Launch QuadPlane SITL from the repo root.
# Usage: scripts/start_sitl.sh [--port PORT] [--base-port PORT]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SITL_DIR="$REPO_ROOT/tests/integration/sitl"
WORK_DIR="$SITL_DIR/quadplane"

MAVLINK_PORT=14580
BASE_PORT=5570

while [[ $# -gt 0 ]]; do
    case "$1" in
        --port)    MAVLINK_PORT="$2"; shift 2 ;;
        --base-port) BASE_PORT="$2"; shift 2 ;;
        *)         echo "Unknown arg: $1" >&2; exit 1 ;;
    esac
done

# Resolve binary name: .exe on Windows (Cygwin build), bare on Linux.
if [[ -f "$SITL_DIR/ArduPlane.exe" ]]; then
    EXE="ArduPlane.exe"
elif [[ -f "$SITL_DIR/ArduPlane" ]]; then
    EXE="ArduPlane"
else
    echo "ArduPlane not found in $SITL_DIR" >&2
    echo "Run: python scripts/fetch_sitl.py" >&2
    exit 1
fi

mkdir -p "$WORK_DIR"
cd "$WORK_DIR"
exec "../$EXE" -M quadplane \
    --defaults ../defaults/quadplane.parm \
    --serial0 "udpclient:127.0.0.1:$MAVLINK_PORT" \
    --base-port "$BASE_PORT"
