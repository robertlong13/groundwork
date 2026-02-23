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

        // Wait for AUTOPILOT_VERSION from a vehicle (triggers registration).
        var versionMsg = await channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.AUTOPILOT_VERSION && m.sysid != 255
            )
            .Timeout(TimeSpan.FromSeconds(10))
            .FirstAsync();

        // Vehicle should be discovered and registered via uid2 hash.
        Assert.NotEmpty(channel.States);

        var state = channel.States[versionMsg.sysid];
        var vehicle = channel.Vehicles[versionMsg.sysid];
        Assert.Same(vehicle, registry.TryGet(vehicle.Uid));
        Assert.Same(state, vehicle.CanonicalState);

        // Vehicle heartbeat fields should be populated from the SITL quadplane.
        Assert.NotEqual(MAVLink.MAV_TYPE.GCS, state.Type);
        Assert.Equal(MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, state.Autopilot);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Tlog_PopulatesPosition()
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

        // Wait for a GLOBAL_POSITION_INT so the vehicle state has position.
        await channel
            .Messages.Where(m => m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT)
            .Timeout(TimeSpan.FromSeconds(10))
            .FirstAsync();

        // Find the vehicle state (skip GCS sysids).
        var state = channel.States.Values.First(s => s.Type != MAVLink.MAV_TYPE.GCS);

        Assert.NotEqual(0.0, state.Latitude);
        Assert.NotEqual(0.0, state.Longitude);
        Assert.True(state.Altitude != 0f || state.AltitudeMsl != 0f);

        await connection.DisposeAsync();
    }
}
