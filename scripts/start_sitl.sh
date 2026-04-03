#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later

# Launch QuadPlane SITL from the repo root.
#
# Single vehicle (default):
#   scripts/start_sitl.sh
#
# Single vehicle, three links (serial1 + serial2 as extra UDP):
#   scripts/start_sitl.sh --extra-links 2
#
# Multi-link swarm (each vehicle on its own UDP port):
#   scripts/start_sitl.sh --count 3
#
# Multi-link, same sysid (exercises UID-based identity):
#   scripts/start_sitl.sh --count 3 --same-sysid
#
# Single-link chain (all vehicles on one link):
#   scripts/start_sitl.sh --count 3 --chain

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SITL_DIR="$REPO_ROOT/tests/integration/sitl"

MAVLINK_PORT=14580
BASE_PORT=5570
COUNT=1
CHAIN=false
SAME_SYSID=false
EXTRA_LINKS=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --port)         MAVLINK_PORT="$2"; shift 2 ;;
        --base-port)    BASE_PORT="$2"; shift 2 ;;
        --count)        COUNT="$2"; shift 2 ;;
        --chain)        CHAIN=true; shift ;;
        --same-sysid)   SAME_SYSID=true; shift ;;
        --extra-links)  EXTRA_LINKS="$2"; shift 2 ;;
        *)              echo "Unknown arg: $1" >&2; exit 1 ;;
    esac
done

if [[ "$CHAIN" == true && "$EXTRA_LINKS" -gt 0 ]]; then
    echo "--chain and --extra-links are incompatible (chain uses serial1/serial2)" >&2
    exit 1
fi

if [[ "$EXTRA_LINKS" -gt 2 ]]; then
    echo "--extra-links max is 2 (serial1 + serial2)" >&2
    exit 1
fi

# Resolve binary name: .exe on Windows (Cygwin build), bare on Linux.
if [[ -f "$SITL_DIR/ArduPlane.exe" ]]; then
    EXE="$SITL_DIR/ArduPlane.exe"
elif [[ -f "$SITL_DIR/ArduPlane" ]]; then
    EXE="$SITL_DIR/ArduPlane"
else
    echo "ArduPlane not found in $SITL_DIR" >&2
    echo "Run: python scripts/fetch_sitl.py" >&2
    exit 1
fi

# Ports per instance: serial0 + extra links.
PORTS_PER_INSTANCE=$((1 + EXTRA_LINKS))

PIDS=()

cleanup() {
    for pid in "${PIDS[@]}"; do
        kill "$pid" 2>/dev/null || true
    done
    wait 2>/dev/null || true
}

trap cleanup EXIT

for ((i = 0; i < COUNT; i++)); do
    WORK_DIR="$SITL_DIR/quadplane/$i"
    mkdir -p "$WORK_DIR"

    if [[ "$SAME_SYSID" == true ]]; then
        SYSID=1
    else
        SYSID=$((i + 1))
    fi

    INSTANCE_BASE_PORT=$((MAVLINK_PORT + i * PORTS_PER_INSTANCE))

    INSTANCE_BASE=$((BASE_PORT + 10 * i))

    ARGS=(
        -M quadplane
        --defaults "$SITL_DIR/defaults/quadplane.parm"
        --base-port "$INSTANCE_BASE"
        --instance "$i"
        --sysid "$SYSID"
    )

    if [[ "$CHAIN" == true ]]; then
        # Chain mode: only instance 0 talks to the GCS. Each instance's
        # serial1 defaults to a tcp server (base-port + 10*instance + 2).
        # serial2 connects as tcpclient to the next instance's serial1.
        # All instances need serial0 overridden: SITL blocks on the
        # default tcp listener waiting for a client before initializing
        # other serial ports.
        if [[ $i -eq 0 ]]; then
            ARGS+=(--serial0 "udpclient:127.0.0.1:$MAVLINK_PORT")
        else
            ARGS+=(--serial0 "udpclient:127.0.0.1:1")
        fi
        if [[ $i -lt $((COUNT - 1)) ]]; then
            NEXT_SERIAL1_PORT=$((BASE_PORT + 10 * (i + 1) + 2))
            ARGS+=(--serial2 "tcpclient:127.0.0.1:$NEXT_SERIAL1_PORT")
        fi
    else
        # Separate mode: each instance sends to its own UDP port(s).
        ARGS+=(--serial0 "udpclient:127.0.0.1:$INSTANCE_BASE_PORT")
        if [[ "$EXTRA_LINKS" -ge 1 ]]; then
            ARGS+=(--serial1 "udpclient:127.0.0.1:$((INSTANCE_BASE_PORT + 1))")
        fi
        if [[ "$EXTRA_LINKS" -ge 2 ]]; then
            ARGS+=(--serial2 "udpclient:127.0.0.1:$((INSTANCE_BASE_PORT + 2))")
        fi
    fi

    cd "$WORK_DIR"
    "$EXE" "${ARGS[@]}" &
    PIDS+=($!)
    cd - > /dev/null
done

echo "Launched $COUNT instance(s)"
if [[ "$CHAIN" == true ]]; then
    echo "Chain mode: connect to udpin:$MAVLINK_PORT"
else
    for ((i = 0; i < COUNT; i++)); do
        BASE=$((MAVLINK_PORT + i * PORTS_PER_INSTANCE))
        PORTS="udpin:$BASE"
        for ((j = 1; j <= EXTRA_LINKS; j++)); do
            PORTS="$PORTS, udpin:$((BASE + j))"
        done
        echo "  Instance $i: $PORTS"
    done
fi

wait
