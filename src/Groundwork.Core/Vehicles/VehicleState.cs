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
/// Per-channel-per-vehicle telemetry and link health. Pure data with
/// no backreferences; context comes from the owning <see cref="Channels.MavChannel"/>.
/// </summary>
public class VehicleState
{
    // -- HEARTBEAT fields --

    public MAVLink.MAV_TYPE Type { get; set; }
    public MAVLink.MAV_AUTOPILOT Autopilot { get; set; }
    public uint CustomMode { get; set; }
    public MAVLink.MAV_STATE SystemStatus { get; set; }
    public bool Armed { get; set; }

    /// <summary>
    /// Updates state from a HEARTBEAT message. Casts from the pymavlink
    /// byte fields to proper enum types are localized here.
    /// </summary>
    public void UpdateFromHeartbeat(MAVLink.mavlink_heartbeat_t heartbeat)
    {
        Type = (MAVLink.MAV_TYPE)heartbeat.type;
        Autopilot = (MAVLink.MAV_AUTOPILOT)heartbeat.autopilot;
        CustomMode = heartbeat.custom_mode;
        SystemStatus = (MAVLink.MAV_STATE)heartbeat.system_status;
        Armed = ((MAVLink.MAV_MODE_FLAG)heartbeat.base_mode).HasFlag(
            MAVLink.MAV_MODE_FLAG.SAFETY_ARMED
        );
    }
}
