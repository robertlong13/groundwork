## Project

See [ROADMAP.md](ROADMAP.md) for milestones and design decisions.

**Solution file:** `Groundwork.slnx` (`.slnx`, not `.sln`).

**Python tooling:** [uv](https://docs.astral.sh/uv/) with `uv.lock`. Use
`uv run` to invoke Python scripts and tools -- it auto-syncs the venv. Don't
use `pip install`.

## Session Start

Run `python scripts/codemap.py` before touching any C# files. It prints a
structural map of every C# type in `src/` -- classes, interfaces, inheritance.
Takes ~100ms and orients the whole session.

## License

This project is **GPL-3.0-or-later**.

C# source files get the full header:

```csharp
// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
```

Python and shell scripts get the one-liner:

```python
# SPDX-License-Identifier: GPL-3.0-or-later
```

No license headers on docs, config, or data files (`.md`, `.toml`, `.json`,
`.yml`, `.xml`, etc.).

## Architecture Principles

These govern all implementation decisions. Violating them requires a decision
doc.

1. **MAVLink is a hard assumption** — The protocol layer is not abstracted.
   Groundwork speaks MAVLink, period. "Vehicle-agnostic" means it doesn't
   *require* ArduPilot specifically (could support PX4 or other MAVLink
   autopilots), but MAVLink itself is baked in. Sysid/compid routing, message
   parsing, heartbeat handling — all can assume MAVLink semantics.

2. **Separation of connection and vehicle** — A connection is a persistent
   transport (serial, UDP, TCP, log file). A vehicle is a logical entity
   identified by hardware UID (from AUTOPILOT_VERSION), not sysid. Four
   entities model this: **IConnection** (persistent transport, owns config and
   reconnection), **MavChannel** (MAVLink protocol layer -- parser, vehicle
   discovery, message routing), **VehicleState** (per-channel-per-vehicle
   telemetry and link health), **Vehicle** (UID-keyed identity, owns
   params/missions/config).

3. **UI is a projection of state** — MVVM, strictly. ViewModels observe vehicle
   state, they don't own it. Core must be fully usable without UI.

4. **Reactive streams for telemetry, async/await for request-response** —
   IObservable (System.Reactive) for the message bus and telemetry pipeline.
   async/await for request-response protocols (param fetch, command ACK, mission
   upload). The boundary mirrors MAVLink's own distinction between unsolicited
   messages and request-response sequences.

5. **Plugins via composition** — Define extension points with clear interfaces.
   Plugins register implementations, they can't reach into internals. Plugins
   are an app-layer concern (GUI + Commands), not Core's concern.

6. **Core owns protocol, Console owns UX** — New commands build in Console
   first. When protocol details leak into Console (magic numbers,
   firmware-version gates, multi-step sequences), promote to Core. Console
   handlers should be thin: parse args, call Core, format output.

## MAVProxy Parity

`scripts/parity.py` tracks which MAVProxy commands Groundwork has implemented.
It scrapes both codebases on every run -- no manifest to maintain.

```bash
python scripts/parity.py              # target modules, default verbosity
python scripts/parity.py arm mode     # zoom in: full todo list, no cap
python scripts/parity.py --all        # include non-target modules
python scripts/parity.py --directory  # show mapping table instead of coverage
```

Rules for implementing commands:

- **Match wire behavior, not just presence.** Read the full MAVProxy handler
  source before implementing. Match the exact message type, parameter values,
  and edge-case handling. The parity tracker can only check presence -- a
  command showing as "done" must mean "implemented correctly." Divergence from MAVProxy must only be done when it is **strictly better**.

- **Tracking granularity is the subcommand.** The tracker extracts subcommands
  from MAVProxy's `add_command` completion rules and handler dispatch patterns
  (e.g. `arm throttle`, `arm check`, `arm safetyon`). A Groundwork
  `Register("arm throttle", ...)` covers `arm throttle`. Registering just
  `Register("arm", ...)` does not cover `arm throttle`.

- **Implement all sub-subcommands when you implement a subcommand.** Most
  subcommands are leaf-level, but a few have deeper dispatch (e.g. `param
  bitmask set`). When you implement a subcommand, implement everything under it
  so the tracker shows full coverage. Run `python scripts/parity.py <module>` to
  see the full list.

- **IMPORTANT_MODULES** in the script defines which modules are target
  priorities for CLI parity. Edit it when the target set changes.

## SITL Testing

See [tests/integration/README.md](tests/integration/README.md) for launch
commands, port conventions, and flight sequences.

## Git

- AP/MP scope-tag style: `Scope: message` when there's an obvious scope, plain
  message for multi-scope commits. No conventional commits.

## Code Style

- **XML doc comments follow BCL conventions.** Classes use "Provides..." or
  "Represents...", interfaces use "Defines...", properties use "Gets...",
  methods start with a verb. One sentence max in `<summary>`; use optional
  `<remarks>` for behavioral details, but sparingly.

- **Python docstrings use Google style.** Imperative mood ("Return", not
  "Returns"). One-liner preferred; expand only when behavior is non-obvious.
  Skip Args/Returns sections that just restate type annotations. No docstrings
  on test functions unless the test does something surprising.

- **ASCII only in source files.** No em dashes, curly quotes, or other non-ASCII
  characters in `.cs`, `.py`, `.sh`, `.csproj`, `.xml`, `.yml`, `.yaml`, `.json`,
  `.axaml`, or `.resx` files -- not in strings, comments, or doc comments. Use
  plain hyphens (`-` or `--`), straight quotes, and XML/language escapes for
  special characters. Enforced by `scripts/check_ascii.py` (runs in CI and
  pre-push hook).
