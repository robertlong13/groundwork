# Groundwork

Groundwork is a new ground-up GCS written in .NET and Avalonia. The intent is to
be a spiritual successor to Mission Planner, keeping what it does well while
overhauling the UI and building on a modern, cross-platform, maintainable
foundation.

## Status

Pre-pre-alpha. Currently a console-only application with ~20% of MAVProxy's
commands.

See [ROADMAP.md](ROADMAP.md) for milestones and design decisions.

## Principles

- .NET, not web stack: native performance, low resource footprint (testing on
  Pi4 and low-end Android devices; "runs on a potato")
- ArduPilot: Groundwork targets ArduPilot specifically. PX4 support may come
  in the future if someone wants to take that on.
- Customizable: plugins for developers, configurable UI for everyone.

## License

GPL-3.0-or-later - See [LICENSE](LICENSE)
