# Groundwork

Mission Planner is showing its age, but it remains a valuable tool without a
1-to-1 replacement. It's still the most user-friendly (for some definitions of
user-friendly), feature-complete desktop application targeted at ArduPilot. Many
tools do specific things better than Mission Planner, but none do everything it
does (other than MAVProxy, which is why I say Planner is relatively
"user-friendly").

Groundwork is a ground-up rearchitecture using .NET 10 and Avalonia, keeping
what Mission Planner does well while building on a modern, cross-platform,
maintainable foundation. Development is console-first: a MAVProxy-like CLI that
proves the core is solid before any GUI work begins.

## Principles

- **Console-first development** — GUI comes after core is solid
- **MAVProxy UX compatibility** — Muscle memory should transfer
- **Multi-vehicle and multi-link as first-class concepts** — Not bolted on later
- **Plugin architecture via composition** — Clear extension points, no reaching into internals
- **ArduPilot-first, interfaces vehicle-agnostic** — Only implement ArduPilot, but don't close doors

## Status

Early development. Not yet functional. See [ROADMAP.md](ROADMAP.md) for
milestones and design decisions.

## License

GPL-3.0-or-later - See [LICENSE](LICENSE)
