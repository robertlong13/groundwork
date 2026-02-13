// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reactive.Linq;
using Groundwork.Core.Channels;
using Groundwork.Core.Connections;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groundwork.Core.Tests.Integration;

public class TlogPipelineTests
{
    private const string TlogFixture = "sitl_quadplane_mission.tlog";

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Groundwork.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not find solution root");
    }

    [Fact]
    public async Task Tlog_DiscoversVehicleAndParsesHeartbeat()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var registry = new VehicleRegistry();
        var tlogPath = Path.Combine(FindSolutionRoot(), "data", TlogFixture);

        var connection = new TlogConnection(
            tlogPath,
            loggerFactory.CreateLogger<TlogConnection>(),
            speed: double.MaxValue
        );
        await connection.OpenAsync();

        using var channel = new MavChannel(connection, registry, loggerFactory);

        // Wait for a vehicle heartbeat (skip GCS heartbeats from Mission Planner).
        var vehicleHeartbeat = await channel
            .Messages.Where(m =>
            {
                if (m.msgid != (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
                    return false;
                var hb = m.ToStructure<MAVLink.mavlink_heartbeat_t>();
                return (MAVLink.MAV_TYPE)hb.type != MAVLink.MAV_TYPE.GCS;
            })
            .Timeout(TimeSpan.FromSeconds(10))
            .FirstAsync();

        // Vehicle should be discovered and registered.
        Assert.NotEmpty(channel.States);

        var state = channel.States[vehicleHeartbeat.sysid];
        var vehicle = channel.Vehicles[vehicleHeartbeat.sysid];
        Assert.Same(vehicle, registry.TryGet(vehicle.Uid));
        Assert.Same(state, vehicle.CanonicalState);

        // Vehicle heartbeat fields should be populated from the SITL quadplane.
        Assert.NotEqual(MAVLink.MAV_TYPE.GCS, state.Type);
        Assert.Equal(MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, state.Autopilot);

        await connection.DisposeAsync();
    }
}
