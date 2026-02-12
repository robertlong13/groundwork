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
using Groundwork.Core.Connections;
using Groundwork.Core.Links;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("Groundwork");
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

const int port = 14550;
var registry = new VehicleRegistry();

await using var connection = new UdpListenConnection(
    port,
    loggerFactory.CreateLogger<UdpListenConnection>()
);

await connection.OpenAsync(cts.Token);

using var vehicleLink = new VehicleLink(connection, registry, loggerFactory);

using var subscription = vehicleLink
    .Messages.Where(m => m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
    .Subscribe(m =>
    {
        var hb = m.ToStructure<MAVLink.mavlink_heartbeat_t>();
        logger.LogInformation(
            "HEARTBEAT sysid={Sysid} compid={Compid} type={Type} autopilot={Autopilot} mode={BaseMode}",
            m.sysid,
            m.compid,
            (MAVLink.MAV_TYPE)hb.type,
            (MAVLink.MAV_AUTOPILOT)hb.autopilot,
            (MAVLink.MAV_MODE_FLAG)hb.base_mode
        );
    });

logger.LogInformation("Waiting for heartbeats on UDP port {Port}... (Ctrl+C to exit)", port);

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Clean shutdown.
}

logger.LogInformation("Shutting down");
