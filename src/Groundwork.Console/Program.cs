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

var registry = new VehicleRegistry(
    new Dictionary<MAVLink.MAV_DATA_STREAM, int>
    {
        [MAVLink.MAV_DATA_STREAM.EXTRA1] = 10,
        [MAVLink.MAV_DATA_STREAM.POSITION] = 5,
        [MAVLink.MAV_DATA_STREAM.EXTENDED_STATUS] = 2,
        [MAVLink.MAV_DATA_STREAM.RC_CHANNELS] = 2,
        [MAVLink.MAV_DATA_STREAM.EXTRA2] = 2,
        [MAVLink.MAV_DATA_STREAM.EXTRA3] = 2,
    }
);

// Select connection from arguments: tlog path [speed] or UDP listen (default).
IConnection connection;
if (args.Length > 0 && args[0].EndsWith(".tlog", StringComparison.OrdinalIgnoreCase))
{
    var speed = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 1.0;
    connection = new TlogConnection(args[0], loggerFactory.CreateLogger<TlogConnection>(), speed);
}
else
{
    connection = new UdpListenConnection(14550, loggerFactory.CreateLogger<UdpListenConnection>());
}

await using var _ = connection;
await connection.OpenAsync(cts.Token);

using var channel = new MavChannel(connection, registry, loggerFactory);

using var subscription = channel
    .Messages.Where(m => m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
    .Subscribe(
        onNext: m =>
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
        },
        onCompleted: () => cts.Cancel()
    );

if (connection is TlogConnection)
{
    logger.LogInformation("Replaying tlog... (Ctrl+C to exit)");
}
else
{
    channel.StartHeartbeat();
    logger.LogInformation("Waiting for heartbeats on UDP port 14550... (Ctrl+C to exit)");
}

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Clean shutdown.
}

logger.LogInformation("Shutting down");
