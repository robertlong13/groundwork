// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Groundwork.Core.Connections;
using Groundwork.Core.Protocol;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Channels;

/// <summary>
/// Provides the MAVLink protocol layer over a persistent <see cref="IConnection"/>,
/// parsing the byte stream into messages, discovering vehicles, and owning per-vehicle state.
/// </summary>
public sealed class MavChannel : IDisposable
{
    private const byte GcsSysId = 255;

    private const byte GcsCompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER;

    private readonly IConnection _connection;
    private readonly VehicleRegistry _vehicleRegistry;
    private readonly ILogger<MavChannel> _logger;
    private readonly MavLinkParser _parser;
    private readonly IDisposable _parserSubscription;
    private readonly Subject<MAVLink.MAVLinkMessage> _messages = new();
    private readonly MAVLink.MavlinkParse _generator = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<byte, VehicleState> _states = new();
    private readonly ConcurrentDictionary<byte, Vehicle> _vehicles = new();
    private readonly ConcurrentDictionary<uint, long> _messageCounts = new();
    private Task? _heartbeatTask;

    public MavChannel(
        IConnection connection,
        VehicleRegistry vehicleRegistry,
        ILoggerFactory loggerFactory
    )
    {
        _connection = connection;
        _vehicleRegistry = vehicleRegistry;
        _logger = loggerFactory.CreateLogger<MavChannel>();

        _parser = new MavLinkParser(connection.BaseStream, loggerFactory);

        _parserSubscription = _parser.Messages.Subscribe(
            onNext: OnMessageReceived,
            onError: ex => _logger.LogWarning(ex, "{Name}: parser error", Name),
            onCompleted: () => _messages.OnCompleted()
        );
    }

    /// <summary>
    /// Gets the human-readable name, derived from the connection.
    /// </summary>
    public string Name => _connection.Name;

    /// <summary>
    /// Gets the hot observable of all MAVLink messages received on this channel.
    /// </summary>
    public IObservable<MAVLink.MAVLinkMessage> Messages => _messages;

    /// <summary>
    /// Gets the vehicle states on this channel, keyed by sysid.
    /// </summary>
    public IReadOnlyDictionary<byte, VehicleState> States => _states;

    /// <summary>
    /// Gets the vehicles discovered on this channel, keyed by sysid.
    /// </summary>
    public IReadOnlyDictionary<byte, Vehicle> Vehicles => _vehicles;

    /// <summary>
    /// Gets the per-message-type receive counts, keyed by msgid.
    /// </summary>
    public IReadOnlyDictionary<uint, long> MessageCounts => _messageCounts;

    /// <summary>
    /// Gets the parser instance with CRC and framing statistics.
    /// </summary>
    public MavLinkParser Parser => _parser;

    /// <summary>
    /// Sends raw bytes on the underlying connection.
    /// </summary>
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        _connection.SendAsync(data, ct);

    /// <summary>
    /// Encodes a MAVLink struct as a v2 packet and sends it on the
    /// underlying connection, using the GCS sysid/compid.
    /// </summary>
    public Task SendAsync(
        MAVLink.MAVLINK_MSG_ID messageType,
        object data,
        CancellationToken ct = default
    )
    {
        var packet = _generator.GenerateMAVLinkPacket20(
            messageType,
            data,
            sysid: GcsSysId,
            compid: GcsCompId
        );
        return _connection.SendAsync(packet, ct);
    }

    /// <summary>
    /// Sends a COMMAND_LONG and awaits the matching COMMAND_ACK.
    /// </summary>
    /// <param name="command">The MAVLink command to send.</param>
    /// <param name="timeout">ACK timeout. Defaults to 5 seconds.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="TimeoutException">No ACK received within the timeout period.</exception>
    public async Task<MAVLink.MAV_RESULT> SendCommandAsync(
        byte targetSysId,
        byte targetCompId,
        MAVLink.MAV_CMD command,
        float param1 = 0,
        float param2 = 0,
        float param3 = 0,
        float param4 = 0,
        float param5 = 0,
        float param6 = 0,
        float param7 = 0,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);

        // Subscribe BEFORE sending to avoid race with fast ACK.
        var ackTask = Messages
            .Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_ACK && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_command_ack_t>())
            .Where(ack => ack.command == (ushort)command)
            .Take(1)
            .Timeout(effectiveTimeout)
            .ToTask(ct);

        var cmd = new MAVLink.mavlink_command_long_t
        {
            target_system = targetSysId,
            target_component = targetCompId,
            command = (ushort)command,
            param1 = param1,
            param2 = param2,
            param3 = param3,
            param4 = param4,
            param5 = param5,
            param6 = param6,
            param7 = param7,
        };

        await SendAsync(MAVLink.MAVLINK_MSG_ID.COMMAND_LONG, cmd, ct).ConfigureAwait(false);

        var ack = await ackTask.ConfigureAwait(false);
        return (MAVLink.MAV_RESULT)ack.result;
    }

    /// <summary>
    /// Starts sending GCS heartbeats at 1 Hz. Must be called at most once.
    /// Heartbeats stop when the channel is disposed.
    /// </summary>
    public void StartHeartbeat()
    {
        if (_heartbeatTask is not null)
            throw new InvalidOperationException("Heartbeat already started.");

        _heartbeatTask = RunHeartbeatLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Returns the VehicleState for a given sysid, or null if not discovered.
    /// </summary>
    public VehicleState? GetState(byte sysId) =>
        _states.TryGetValue(sysId, out var state) ? state : null;

    public void Dispose()
    {
        _cts.Cancel();

        if (_heartbeatTask is not null)
        {
            try
            {
                _heartbeatTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _parserSubscription.Dispose();
        _messages.Dispose();
        _parser.Dispose();
        _cts.Dispose();
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        var heartbeat = new MAVLink.mavlink_heartbeat_t
        {
            type = (byte)MAVLink.MAV_TYPE.GCS,
            autopilot = (byte)MAVLink.MAV_AUTOPILOT.INVALID,
            mavlink_version = 3,
        };

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await SendAsync(MAVLink.MAVLINK_MSG_ID.HEARTBEAT, heartbeat, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Name}: heartbeat send failed", Name);
            }
        }
    }

    private void OnMessageReceived(MAVLink.MAVLinkMessage message)
    {
        // Vehicle discovery: new sysid -> new VehicleState.
        if (!_states.TryGetValue(message.sysid, out var state))
        {
            state = new VehicleState();
            _states[message.sysid] = state;

            // GCS sysids get state tracking but not vehicle registration.
            if (message.sysid != GcsSysId)
            {
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
            else
            {
                _logger.LogDebug("{Name}: tracking GCS sysid={Sysid}", Name, message.sysid);
            }
        }

        _messageCounts.AddOrUpdate(message.msgid, 1, static (_, count) => count + 1);

        // Update state before forwarding to external consumers, so
        // subscribers always see up-to-date VehicleState.
        state.Update(message);
        _messages.OnNext(message);
    }
}
