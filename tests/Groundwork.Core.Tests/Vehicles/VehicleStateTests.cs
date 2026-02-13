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
}
