// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Represents heartbeat-derived vehicle status fields.
/// </summary>
public readonly record struct HeartbeatState(
    MAVLink.MAV_TYPE Type,
    MAVLink.MAV_AUTOPILOT Autopilot,
    uint CustomMode,
    MAVLink.MAV_STATE SystemStatus,
    bool Armed
);

/// <summary>
/// Represents position and heading.
/// </summary>
public readonly record struct PositionState(
    double Latitude,
    double Longitude,
    float AltitudeRel,
    float AltitudeMsl,
    float Heading
);

/// <summary>
/// Represents battery telemetry.
/// </summary>
public readonly record struct BatteryState(float Voltage, float Current, sbyte Remaining);
