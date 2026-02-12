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
using Groundwork.Core.Protocol;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Links;

/// <summary>
/// MAVLink protocol layer over a persistent <see cref="IConnection"/>. Manages
/// parser lifecycle, vehicle discovery, and message routing. The connection
/// handles transport-level concerns (reconnection, port config); VehicleLink
/// handles protocol-level concerns (parsing, sysid discovery, message dispatch).
///
/// The <see cref="Messages"/> observable produces all parsed MAVLink messages.
/// The connection's stream survives reconnection via its internal pipe, so
/// the parser sees a continuous byte stream with possible gaps.
/// </summary>
public sealed class VehicleLink : IDisposable
{
    private readonly IConnection _connection;
    private readonly VehicleRegistry _vehicleRegistry;
    private readonly ILogger<VehicleLink> _logger;
    private readonly MavLinkParser _parser;
    private readonly IDisposable _parserSubscription;
    private readonly Dictionary<byte, VehicleState> _vehicleStates = new();

    public VehicleLink(
        IConnection connection,
        VehicleRegistry vehicleRegistry,
        ILoggerFactory loggerFactory
    )
    {
        _connection = connection;
        _vehicleRegistry = vehicleRegistry;
        _logger = loggerFactory.CreateLogger<VehicleLink>();

        _parser = new MavLinkParser(
            connection.BaseStream,
            loggerFactory.CreateLogger<MavLinkParser>()
        );

        _parserSubscription = _parser.Messages.Subscribe(
            onNext: OnMessageReceived,
            onError: ex => _logger.LogWarning(ex, "{Name}: parser error", Name)
        );
    }

    /// <summary>
    /// Human-readable name, derived from the connection.
    /// </summary>
    public string Name => _connection.Name;

    /// <summary>
    /// Hot observable of all MAVLink messages received on this link.
    /// </summary>
    public IObservable<MAVLink.MAVLinkMessage> Messages => _parser.Messages;

    /// <summary>
    /// VehicleStates discovered on this link, keyed by sysid.
    /// </summary>
    public IReadOnlyDictionary<byte, VehicleState> VehicleStates => _vehicleStates;

    /// <summary>
    /// Sends bytes on the underlying connection.
    /// </summary>
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        _connection.SendAsync(data, ct);

    public void Dispose()
    {
        _parserSubscription.Dispose();
        _parser.Dispose();
    }

    private void OnMessageReceived(MAVLink.MAVLinkMessage message)
    {
        // Vehicle discovery: new sysid -> new VehicleState.
        if (!_vehicleStates.ContainsKey(message.sysid))
        {
            var state = new VehicleState(message.sysid, message.compid);
            _vehicleStates[message.sysid] = state;

            // Mock UID from sysid at M0.
            var uid = Vehicle.MockUidFromSysid(message.sysid);
            var vehicle = _vehicleRegistry.GetOrAdd(uid, state);

            _logger.LogInformation(
                "{Name}: discovered vehicle sysid={Sysid} compid={Compid} uid={Uid}",
                Name,
                message.sysid,
                message.compid,
                uid
            );
        }

        // Update VehicleState from HEARTBEAT.
        if (message.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
        {
            var state = _vehicleStates[message.sysid];
            var heartbeat = message.ToStructure<MAVLink.mavlink_heartbeat_t>();
            state.UpdateFromHeartbeat(heartbeat);
        }
    }
}
