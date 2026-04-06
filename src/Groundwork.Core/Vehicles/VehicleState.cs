// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Reactive;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Represents per-channel-per-vehicle telemetry.
/// </summary>
/// <remarks>
/// Driven by <see cref="Channels.MavChannel"/>, which calls
/// <see cref="Update"/> for each received message. Properties grouped
/// by MAVLink message are exposed as <see cref="ObservableValue{T}"/>
/// so GUI widgets can subscribe and command handlers can read the
/// current value synchronously via <c>.Value</c>.
/// </remarks>
public sealed class VehicleState
{
    /// <summary>
    /// Gets the heartbeat-derived status (type, mode, armed, etc.).
    /// </summary>
    public ObservableValue<HeartbeatState> Heartbeat { get; } = new(default);

    /// <summary>
    /// Gets the position and heading.
    /// </summary>
    public ObservableValue<PositionState> Position { get; } = new(default);

    /// <summary>
    /// Gets the battery telemetry.
    /// </summary>
    public ObservableValue<BatteryState> Battery { get; } = new(default);

    /// <summary>
    /// Gets the protocol capabilities bitmap from AUTOPILOT_VERSION.
    /// </summary>
    public MAVLink.MAV_PROTOCOL_CAPABILITY Capabilities { get; private set; }

    /// <summary>
    /// Gets the firmware version number from AUTOPILOT_VERSION.
    /// </summary>
    public uint FirmwareVersion { get; private set; }

    /// <summary>
    /// Updates state from a HEARTBEAT message. Casts from the pymavlink
    /// byte fields to proper enum types are localized here.
    /// </summary>
    public void UpdateFromHeartbeat(MAVLink.mavlink_heartbeat_t heartbeat)
    {
        Heartbeat.Set(
            new HeartbeatState(
                Type: (MAVLink.MAV_TYPE)heartbeat.type,
                Autopilot: (MAVLink.MAV_AUTOPILOT)heartbeat.autopilot,
                CustomMode: heartbeat.custom_mode,
                SystemStatus: (MAVLink.MAV_STATE)heartbeat.system_status,
                Armed: ((MAVLink.MAV_MODE_FLAG)heartbeat.base_mode).HasFlag(
                    MAVLink.MAV_MODE_FLAG.SAFETY_ARMED
                )
            )
        );
    }

    /// <summary>
    /// Updates position and heading from a GLOBAL_POSITION_INT message.
    /// Converts from MAVLink wire units (degE7, mm, cdeg) to standard
    /// units (degrees, meters, degrees).
    /// </summary>
    public void UpdateFromGlobalPositionInt(MAVLink.mavlink_global_position_int_t msg)
    {
        Position.Set(
            new PositionState(
                Latitude: msg.lat / 1e7,
                Longitude: msg.lon / 1e7,
                AltitudeRel: msg.relative_alt / 1000f,
                AltitudeMsl: msg.alt / 1000f,
                Heading: msg.hdg / 100f
            )
        );
    }

    /// <summary>
    /// Updates battery state from a SYS_STATUS message. Converts from
    /// MAVLink wire units (mV, cA) to standard units (V, A).
    /// </summary>
    public void UpdateFromSysStatus(MAVLink.mavlink_sys_status_t msg)
    {
        Battery.Set(
            new BatteryState(
                Voltage: msg.voltage_battery / 1000f,
                Current: msg.current_battery / 100f,
                Remaining: msg.battery_remaining
            )
        );
    }

    /// <summary>
    /// Updates capabilities and firmware version from an AUTOPILOT_VERSION message.
    /// </summary>
    public void UpdateFromAutopilotVersion(MAVLink.mavlink_autopilot_version_t msg)
    {
        Capabilities = (MAVLink.MAV_PROTOCOL_CAPABILITY)msg.capabilities;
        FirmwareVersion = msg.flight_sw_version;
    }

    /// <summary>
    /// Dispatches a raw MAVLink message to the appropriate update method.
    /// </summary>
    public void Update(MAVLink.MAVLinkMessage message)
    {
        switch ((MAVLink.MAVLINK_MSG_ID)message.msgid)
        {
            case MAVLink.MAVLINK_MSG_ID.HEARTBEAT:
                UpdateFromHeartbeat(message.ToStructure<MAVLink.mavlink_heartbeat_t>());
                break;
            case MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT:
                UpdateFromGlobalPositionInt(
                    message.ToStructure<MAVLink.mavlink_global_position_int_t>()
                );
                break;
            case MAVLink.MAVLINK_MSG_ID.SYS_STATUS:
                UpdateFromSysStatus(message.ToStructure<MAVLink.mavlink_sys_status_t>());
                break;
            case MAVLink.MAVLINK_MSG_ID.AUTOPILOT_VERSION:
                UpdateFromAutopilotVersion(
                    message.ToStructure<MAVLink.mavlink_autopilot_version_t>()
                );
                break;
        }
    }
}
