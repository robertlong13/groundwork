// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Vehicles;

namespace Groundwork.Core.Tests.Vehicles;

public class VehicleStateTests
{
    [Fact]
    public void UpdateFromHeartbeat_SetsTypeAndAutopilot()
    {
        var state = new VehicleState();
        var heartbeat = new MAVLink.mavlink_heartbeat_t
        {
            type = (byte)MAVLink.MAV_TYPE.QUADROTOR,
            autopilot = (byte)MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA,
            custom_mode = 5,
            system_status = (byte)MAVLink.MAV_STATE.ACTIVE,
            base_mode = (byte)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED,
        };

        state.UpdateFromHeartbeat(heartbeat);

        Assert.Equal(MAVLink.MAV_TYPE.QUADROTOR, state.Type);
        Assert.Equal(MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, state.Autopilot);
        Assert.Equal(5u, state.CustomMode);
        Assert.Equal(MAVLink.MAV_STATE.ACTIVE, state.SystemStatus);
    }

    [Fact]
    public void UpdateFromHeartbeat_ArmedFlag_Set()
    {
        var state = new VehicleState();
        var heartbeat = new MAVLink.mavlink_heartbeat_t
        {
            base_mode = (byte)(
                MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED | MAVLink.MAV_MODE_FLAG.SAFETY_ARMED
            ),
        };

        state.UpdateFromHeartbeat(heartbeat);

        Assert.True(state.Armed);
    }

    [Fact]
    public void UpdateFromHeartbeat_ArmedFlag_NotSet()
    {
        var state = new VehicleState();
        // Pre-set armed to verify it flips back.
        state.Armed = true;
        var heartbeat = new MAVLink.mavlink_heartbeat_t
        {
            base_mode = (byte)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED,
        };

        state.UpdateFromHeartbeat(heartbeat);

        Assert.False(state.Armed);
    }

    [Fact]
    public void UpdateFromGlobalPositionInt_SetsPosition()
    {
        var state = new VehicleState();
        var msg = new MAVLink.mavlink_global_position_int_t
        {
            lat = -353632620, // -35.3632620 deg
            lon = 1491652370, // 149.1652370 deg
            relative_alt = 50000, // 50 m AGL
            alt = 630000, // 630 m MSL
            hdg = 18045, // 180.45 deg
        };

        state.UpdateFromGlobalPositionInt(msg);

        Assert.Equal(-35.363262, state.Latitude, 6);
        Assert.Equal(149.165237, state.Longitude, 6);
        Assert.Equal(50f, state.Altitude, 2);
        Assert.Equal(630f, state.AltitudeMsl, 2);
        Assert.Equal(180.45f, state.Heading, 2);
    }

    [Fact]
    public void UpdateFromGlobalPositionInt_HeadingUnknown()
    {
        var state = new VehicleState();
        var msg = new MAVLink.mavlink_global_position_int_t
        {
            hdg = ushort.MaxValue, // 65535 = unknown
        };

        state.UpdateFromGlobalPositionInt(msg);

        Assert.True(state.Heading >= 360f);
    }

    [Fact]
    public void UpdateFromSysStatus_SetsBatteryFields()
    {
        var state = new VehicleState();
        var msg = new MAVLink.mavlink_sys_status_t
        {
            voltage_battery = 12600, // 12.6 V
            current_battery = 1550, // 15.5 A
            battery_remaining = 75,
        };

        state.UpdateFromSysStatus(msg);

        Assert.Equal(12.6f, state.BatteryVoltage, 2);
        Assert.Equal(15.5f, state.BatteryCurrent, 2);
        Assert.Equal(75, state.BatteryRemaining);
    }

    [Fact]
    public void UpdateFromSysStatus_BatteryRemainingNotReported()
    {
        var state = new VehicleState();
        var msg = new MAVLink.mavlink_sys_status_t { battery_remaining = -1 };

        state.UpdateFromSysStatus(msg);

        Assert.Equal(-1, state.BatteryRemaining);
    }
}
