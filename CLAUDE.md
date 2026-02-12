## Project

See [ROADMAP.md](ROADMAP.md) for milestones and design decisions.

**Solution file:** `Groundwork.slnx` (`.slnx`, not `.sln`).

## License

This project is **GPL-3.0-or-later**. When generating source files, use this header:

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

For non-code files that support comments, use the short form:

```plaintext
SPDX-License-Identifier: GPL-3.0-or-later
```

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
   identified by hardware UID (from AUTOPILOT_VERSION), not sysid. Three
   entities model this: **IConnection** (persistent transport, owns config and
   reconnection), **VehicleLink** (MAVLink protocol layer -- parser, vehicle
   discovery, message routing), **VehicleState** (per-connection-per-vehicle
   junction -- sysid/compid identity, telemetry, link health), **Vehicle**
   (UID-keyed identity, owns params/missions/config).

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

## Code Style

- **ASCII only in source files.** No em dashes, curly quotes, or other non-ASCII
  characters in `.cs`, `.py`, `.sh`, `.csproj`, `.xml`, `.yml`, `.yaml`, `.json`,
  `.axaml`, or `.resx` files -- not in strings, comments, or doc comments. Use
  plain hyphens (`-` or `--`), straight quotes, and XML/language escapes for
  special characters.
