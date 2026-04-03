# Integration Testing with SITL

## Prerequisites

```bash
python scripts/fetch_sitl.py              # ArduPlane stable (default)
python scripts/fetch_sitl.py --force      # re-download
python scripts/fetch_sitl.py --wipe       # clean runtime dir (logs, eeprom)
```

This downloads ArduPlane.exe, Cygwin DLLs (Windows) or native ELF (Linux),
and default parameters into `tests/integration/sitl/`. Version-tracked via
`git.txt` -- skips download if already current.

## Port Conventions

| Port | Purpose |
| - | - |
| 14580+ | SITL serial0 -> Groundwork Console (`--link udpin:14580`) |
| 4242 | Groundwork remote REPL (`--repl-remote 4242`) |
| 5570 | SITL `--base-port` |

Port 14580 and base-port 5570 avoid colliding with a developer's own SITL
on the default ports (14550, 5760).

SITL exposes three default tcp servers per instance relative to base-port:
base+0, base+2, base+3 (offset 1 is skipped). `--instance N` shifts the
entire set by 10*N, so instance 1 at base-port 5570 uses 5580, 5582, 5583.
Chain mode connects serial2 to the next instance's serial1 server (base+2).

## Launching

All commands run from the repo root:

```bash
scripts/start_sitl.sh                                      # SITL (QuadPlane)
dotnet run --project src/Groundwork.Console -- \
  --link udpin:14580 --repl-remote 4242                     # Groundwork
python scripts/repl_client.py overview                      # one-shot command
python scripts/repl_client.py                               # interactive REPL
```

`start_sitl.sh` handles the working-directory change internally and bakes
in port defaults (14580, base-port 5570). Accepts `--port` and
`--base-port` overrides.

### Multi-vehicle

```bash
scripts/start_sitl.sh --count 3                            # multi-link (separate ports)
scripts/start_sitl.sh --count 3 --same-sysid               # same sysid, UID-based identity
scripts/start_sitl.sh --count 3 --chain                    # single-link daisy chain
```

Multi-link launches each instance on its own UDP port (14580, 14581, ...),
connect with `--link udpin:14580 --link udpin:14581 ...`. Chain mode puts
all vehicles on one link via serial port chaining -- connect with a single
`--link udpin:14580`.

The `--same-sysid` flag gives all vehicles sysid 1, exercising UID-based
identity resolution. Only meaningful for multi-link (chain requires unique
sysids for routing).

When `--repl-remote` is set, Console stays alive after stdin EOF.
Terminate via `exit` through the TCP socket or Ctrl+C.

For integration tests needing a pymavlink observer, add to the SITL launch:

```bash
--serial1=udpclient:127.0.0.1:14590
```

## Lossy Link Simulation

Debug builds include `LossyConnection`, an IConnection decorator that wraps
live links with configurable impairment. All controls are runtime-adjustable
via the `lossy` command family:

| Command | Description |
| - | - |
| `lossy status` | Show all links with current impairment settings |
| `lossy uplink [index]` | Toggle outbound kill switch |
| `lossy downlink [index]` | Toggle inbound kill switch |
| `lossy rxcorrupt [index] <percent>` | Set inbound byte corruption rate (%) |
| `lossy rxpacketloss [index] <percent>` | Set corruption rate from target packet loss % |
| `lossy txdrop [index] <percent>` | Set outbound per-packet drop rate (%) |
| `lossy latency [index] <ms>` | Set symmetric one-way latency in ms |

Link index defaults to 0 when omitted.

### LTE profile

Simulates a medium-quality cellular link:

```plaintext
lossy latency 500
lossy rxpacketloss 3
lossy txdrop 5
```

## QuadPlane Flight Sequences

QuadPlane reports as `FIXED_WING`. Uses `PLANE_MODE` enum for mode names.

### RC Takeoff (QLOITER)

```
mode qloiter
arm throttle
rc 3 1700          # throttle up -> climb
rc 3 0             # release -> QLOITER holds altitude
```

### Command Takeoff (GUIDED)

```
mode guided
arm throttle
takeoff 30         # MAV_CMD_TAKEOFF -- only works in GUIDED
```

Order matters: mode GUIDED first, then arm, then takeoff.

### Cruise Flight

```
mode cruise        # transitions to fixed-wing flight
rc 1 1700          # aileron right (turn right)
rc 1 1300          # aileron left (turn left)
rc 1 0             # release
rc 2 1700          # elevator back = nose up = climb
rc 2 1300          # elevator forward = nose down = descend
rc 2 0             # release
```

RC channel conventions (standard RC):

- Channel 1: aileron (roll). 1700 = right, 1300 = left.
- Channel 2: elevator (pitch). 1700 = back/climb, 1300 = forward/descend.
- Channel 3: throttle. 1700 = up, 1300 = down.

### Landing

The `land` command sends `DO_LAND_START`, which requires a landing
sequence in the mission. It fails if no mission is loaded.

For landing without a mission:

- `mode qrtl` -- flies home as fixed-wing, transitions to VTOL, descends,
  auto-disarms.
- `mode qland` -- immediate vertical landing at current position.
