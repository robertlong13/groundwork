// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reactive.Subjects;
using Groundwork.Core.Connections;
using Groundwork.Core.Protocol;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Channels;

/// <summary>
/// MAVLink protocol layer over a persistent <see cref="IConnection"/>.
/// Parses the connection's byte stream into <see cref="Messages"/>,
/// discovers vehicles, and owns per-vehicle <see cref="States"/>.
/// </summary>
public sealed class MavChannel : IDisposable
{
    private readonly IConnection _connection;
    private readonly VehicleRegistry _vehicleRegistry;
    private readonly ILogger<MavChannel> _logger;
    private readonly MavLinkParser _parser;
    private readonly IDisposable _parserSubscription;
    private readonly Subject<MAVLink.MAVLinkMessage> _messages = new();
    private readonly MAVLink.MavlinkParse _generator = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<byte, VehicleState> _states = new();
    private readonly Dictionary<byte, Vehicle> _vehicles = new();

    public MavChannel(
        IConnection connection,
        VehicleRegistry vehicleRegistry,
        ILoggerFactory loggerFactory
    )
    {
        _connection = connection;
        _vehicleRegistry = vehicleRegistry;
        _logger = loggerFactory.CreateLogger<MavChannel>();

        _parser = new MavLinkParser(
            connection.BaseStream,
            loggerFactory.CreateLogger<MavLinkParser>()
        );

        _parserSubscription = _parser.Messages.Subscribe(
            onNext: OnMessageReceived,
            onError: ex => _logger.LogWarning(ex, "{Name}: parser error", Name),
            onCompleted: () => _messages.OnCompleted()
        );
    }

    /// <summary>
    /// Human-readable name, derived from the connection.
    /// </summary>
    public string Name => _connection.Name;

    /// <summary>
    /// Hot observable of all MAVLink messages received on this channel.
    /// </summary>
    public IObservable<MAVLink.MAVLinkMessage> Messages => _messages;

    /// <summary>
    /// VehicleStates on this channel, keyed by sysid. Single source of truth
    /// for per-channel telemetry.
    /// </summary>
    public IReadOnlyDictionary<byte, VehicleState> States => _states;

    /// <summary>
    /// Vehicles discovered on this channel, keyed by sysid. Navigational
    /// references -- ownership is in VehicleRegistry.
    /// </summary>
    public IReadOnlyDictionary<byte, Vehicle> Vehicles => _vehicles;

    /// <summary>
    /// Sends bytes on the underlying connection.
    /// </summary>
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        _connection.SendAsync(data, ct);

    /// <summary>
    /// Returns the VehicleState for a given sysid, or null if not discovered.
    /// </summary>
    public VehicleState? GetState(byte sysId) =>
        _states.TryGetValue(sysId, out var state) ? state : null;

    public void Dispose()
    {
        _parserSubscription.Dispose();
        _messages.Dispose();
        _parser.Dispose();
    }

    private void OnMessageReceived(MAVLink.MAVLinkMessage message)
    {
        // Vehicle discovery: new sysid -> new VehicleState.
        if (!_states.TryGetValue(message.sysid, out var state))
        {
            state = new VehicleState();
            _states[message.sysid] = state;

            // Mock UID from sysid at M0.
            var uid = Vehicle.MockUidFromSysid(message.sysid);
            var vehicle = _vehicleRegistry.GetOrCreate(uid, message.sysid);
            _vehicles[message.sysid] = vehicle;
            vehicle.AddChannel(this);

            _logger.LogInformation(
                "{Name}: discovered vehicle sysid={Sysid} uid={Uid}",
                Name,
                message.sysid,
                uid
            );
        }

        // Update state before forwarding to external consumers, so
        // subscribers always see up-to-date VehicleState.
        state.Update(message);
        _messages.OnNext(message);
    }
}
