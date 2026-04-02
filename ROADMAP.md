# Roadmap

See [README.md](README.md) for project overview and principles.

---

## Project Decisions

| Decision | Choice |
| -------- | ------ |
| Framework | .NET 10 + Avalonia (GUI later) |
| CI Matrix | Windows + Linux (macOS deferred until hardware available) |
| Test Framework | xUnit |
| Assertions | xUnit built-in |
| Formatting | CSharpier (opinionated auto-format) |
| Reactive Streams | System.Reactive for streams, async/await for request-response |
| MAVLink | pymavlink C# output committed in-tree. Modernized generator deferred |
| Console/GUI IPC | None — Core is a library, both apps consume it directly. Embedded console-in-GUI is a project-organization question, not an IPC question |
| Logging | Microsoft.Extensions.Logging abstraction. Provider chosen at app level |
| Configuration | Microsoft.Extensions.Configuration, JSON in AppData |
| DI | Pure DI (manual composition root). Container deferred until pain is felt (likely M5+) |
| Vehicle Identity | Vehicle-first model. Four entities: IConnection, MavChannel, VehicleState, Vehicle (UID from AUTOPILOT_VERSION). UID is the identity anchor, not sysid |

---

## Console Application

### Design Goals

- MAVProxy-inspired, not 100% compatible (see M1 for details)
- Interactive REPL-style interface
- "Fly SITL entirely from console" is the M1 exit criteria
- GUI work doesn't start until console can complete a flight

### Command Audit

Tracked by `scripts/parity.py`. See CLAUDE.md for usage.

---

## Milestones

### M0: Foundation

**Goal:** Project compiles, CI runs, MAVLink messages can be parsed, connections work.

**Exit Criteria:**

- [x] Solution builds on Windows and Linux
- [x] CI runs tests on both platforms
- [x] Can open UDP and tlog connections and receive MAVLink messages
- [x] Can parse HEARTBEAT and print vehicle sysid/compid/type
- [x] At least one unit test and one integration test pass

**Deliverables:**

- Connection abstraction with `UdpListenConnection` and `TlogConnection` implementations
- MAVLink bindings committed in-tree
- GitHub Actions workflow (build + test)
- Basic `Program.cs` that connects to SITL and prints heartbeats

---

### M1: Single Vehicle CLI

**Goal:** Fly a complete SITL mission from the console.

**Exit Criteria:**

- [x] REPL-style console with MAVProxy-inspired commands
- [x] Connect to SITL, see telemetry
- [x] Arm, takeoff, change mode, land, disarm - all from console
- [x] RC override commands work
- [x] Vehicle state displayed (mode, armed, position, altitude, battery)
- [x] Integration test: scripted arm -> takeoff -> land -> disarm

---

### M2: Parameters & Missions

**Goal:** Full parameter and mission management via traditional MAVLink protocols.

**Exit Criteria:**

- [x] Fetch all parameters from vehicle
- [x] Set individual parameters
- [x] Read/write `.param` files
- [x] Parameter metadata integration (descriptions, ranges, units)
- [x] Diff parameters against file or defaults
- [x] Upload mission to vehicle
- [x] Download mission from vehicle
- [x] Read/write `.waypoints` files
- [x] Monitor mission progress during flight
- [x] Console commands: `param show`, `param set`, `param load`, `param save`, `param diff`
- [x] Console commands: `wp load <file>`, `wp save <file>`, `wp list`

---

### M3: tlog Playback

**Goal:** Full user-facing log replay with playback controls.

**Exit Criteria:**

- [ ] Same vehicle state model works for replay
- [ ] Playback controls: play, pause, speed, seek
- [ ] Console commands: `log open <file>`, `log play`, `log pause`, `log speed <multiplier>`, `log seek <time>`

M0's `TlogConnection` provides the basic "read messages from file" capability.
This milestone adds the user-facing playback experience on top.

---

### M4: Multi-Connection

**Exit Criteria:**

- [ ] Multiple simultaneous connections
- [ ] Configurable routing (sysid-based and explicit)
- [ ] Redundant link handling (same vehicle, multiple connections)
- [ ] Multi-vehicle support (different sysids)
- [ ] Console commands: `link add <uri>`, `link remove`, `link list`, `vehicle list`, `vehicle select`

---

### M5: GUI Foundation

**Exit Criteria:**

- [ ] Avalonia project created and building
- [ ] Basic vehicle status view (mode, armed, position, battery)
- [ ] Map integration with vehicle position
- [ ] Connects to Groundwork.Core as a library (no IPC — Core is in-process)
- [ ] Can arm/disarm from GUI

---

### M6: GUI Core Features

**Exit Criteria:**

- [ ] Mission planning UI (map-based waypoint editing)
- [ ] Full mission item support (not just waypoints)
- [ ] Parameter editor with search, grouping, validation
- [ ] Multiple vehicle views
- [ ] HUD / artificial horizon

---

### M7: Plugin Architecture

**Exit Criteria:**

- [ ] Define extension point interfaces
- [ ] Plugin discovery and loading mechanism
- [ ] At least one example plugin demonstrating each extension point

**Extension Points:**

- Vehicle type extensions (custom telemetry, config panels)
- Mission item types
- Map layers
- Widgets/gauges
- Connection types
- Console commands

