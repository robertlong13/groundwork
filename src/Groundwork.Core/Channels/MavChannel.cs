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
using System.Text;
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

    private static readonly TimeSpan NegotiationRetryInterval = TimeSpan.FromSeconds(2);

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
    private readonly ConcurrentDictionary<byte, Task> _negotiations = new();
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
    /// Fetches a single parameter by name from the autopilot, retrying up to 3 times.
    /// </summary>
    /// <returns>The parameter value from the autopilot.</returns>
    /// <exception cref="ParameterException">The autopilot reported a PARAM_ERROR (AP 4.7+).</exception>
    /// <exception cref="TimeoutException">No response after all retry attempts.</exception>
    public async Task<float> FetchParameterAsync(
        byte targetSysId,
        string name,
        TimeSpan? perAttemptTimeout = null,
        CancellationToken ct = default
    )
    {
        var timeout = perAttemptTimeout ?? TimeSpan.FromSeconds(1);
        var upperName = name.ToUpperInvariant();

        // Subscribe once before the retry loop to avoid missing fast responses.
        var successStream = Messages
            .Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_param_value_t>())
            .Where(pv => ParamIdEquals(pv.param_id, upperName));

        var errorStream = Messages
            .Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_ERROR && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_param_error_t>())
            .Where(pe => ParamIdEquals(pe.param_id, upperName))
            .Select<MAVLink.mavlink_param_error_t, MAVLink.mavlink_param_value_t>(pe =>
                throw new ParameterException(upperName, (MAVLink.MAV_PARAM_ERROR)pe.error)
            );

        var merged = successStream.Merge(errorStream).Take(1);

        var msg = new MAVLink.mavlink_param_request_read_t
        {
            target_system = targetSysId,
            target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
            param_index = -1,
            param_id = EncodeParamId(upperName),
        };

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var responseTask = merged.Timeout(timeout).ToTask(ct);

            await SendAsync(MAVLink.MAVLINK_MSG_ID.PARAM_REQUEST_READ, msg, ct)
                .ConfigureAwait(false);

            try
            {
                var pv = await responseTask.ConfigureAwait(false);
                return pv.param_value;
            }
            catch (TimeoutException)
            {
                // Retry; ParameterException propagates immediately.
            }
        }

        throw new TimeoutException($"No PARAM_VALUE for '{upperName}' after 3 attempts");
    }

    /// <summary>
    /// Sets a parameter by name and awaits the confirmed value, retrying up to 3 times.
    /// </summary>
    /// <returns>The confirmed parameter value from the autopilot's PARAM_VALUE ACK.</returns>
    /// <exception cref="ParameterException">The autopilot rejected the value.</exception>
    /// <exception cref="TimeoutException">No response after all retry attempts.</exception>
    public async Task<float> SetParameterAsync(
        byte targetSysId,
        string name,
        float value,
        TimeSpan? perAttemptTimeout = null,
        CancellationToken ct = default
    )
    {
        var timeout = perAttemptTimeout ?? TimeSpan.FromSeconds(1);
        var upperName = name.ToUpperInvariant();

        var successStream = Messages
            .Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_param_value_t>())
            .Where(pv => ParamIdEquals(pv.param_id, upperName));

        var errorStream = Messages
            .Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_ERROR && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_param_error_t>())
            .Where(pe => ParamIdEquals(pe.param_id, upperName))
            .Select<MAVLink.mavlink_param_error_t, MAVLink.mavlink_param_value_t>(pe =>
                throw new ParameterException(upperName, (MAVLink.MAV_PARAM_ERROR)pe.error)
            );

        var merged = successStream.Merge(errorStream).Take(1);

        var msg = new MAVLink.mavlink_param_set_t
        {
            target_system = targetSysId,
            target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
            param_id = EncodeParamId(upperName),
            param_value = value,
            param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32,
        };

        float? lastConfirmed = null;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var responseTask = merged.Timeout(timeout).ToTask(ct);

            await SendAsync(MAVLink.MAVLINK_MSG_ID.PARAM_SET, msg, ct).ConfigureAwait(false);

            try
            {
                var pv = await responseTask.ConfigureAwait(false);

                if (Math.Abs(pv.param_value - value) <= 0.00001f)
                    return pv.param_value;

                // Autopilot confirmed a different value (clamped or rejected).
                lastConfirmed = pv.param_value;
            }
            catch (TimeoutException)
            {
                // Retry; ParameterException propagates immediately.
            }
        }

        if (lastConfirmed.HasValue)
            throw new ParameterException(upperName, value, lastConfirmed.Value);

        throw new TimeoutException($"No PARAM_VALUE for '{upperName}' after 3 attempts");
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

        // Wait for background tasks to complete.
        var tasks = _negotiations.Values.ToList();
        if (_heartbeatTask is not null)
            tasks.Add(_heartbeatTask);

        try
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
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
        _messageCounts.AddOrUpdate(message.msgid, 1, static (_, count) => count + 1);

        if (message.sysid == GcsSysId)
        {
            _messages.OnNext(message);
            return;
        }

        if (!_states.TryGetValue(message.sysid, out var state))
        {
            state = new VehicleState();
            _states[message.sysid] = state;

            _logger.LogInformation(
                "{Name}: new sysid={Sysid}, requesting AUTOPILOT_VERSION",
                Name,
                message.sysid
            );
            _negotiations[message.sysid] = NegotiateVersionAsync(message.sysid);
        }

        // Update state before forwarding to external consumers, so
        // subscribers always see up-to-date VehicleState.
        state.Update(message);

        // Vehicle registration: AUTOPILOT_VERSION provides the real
        // hardware UID. Handled here (synchronous) rather than in the
        // negotiation task to avoid races with tlog replay where the
        // response may arrive before the async task subscribes.
        if (
            message.msgid == (uint)MAVLink.MAVLINK_MSG_ID.AUTOPILOT_VERSION
            && !_vehicles.ContainsKey(message.sysid)
        )
        {
            var version = message.ToStructure<MAVLink.mavlink_autopilot_version_t>();
            var uid = Vehicle.ComputeUid(version.uid, version.uid2, message.sysid);

            if (uid is null)
            {
                _logger.LogWarning(
                    "{Name}: sysid={Sysid} AUTOPILOT_VERSION has no hardware UID",
                    Name,
                    message.sysid
                );
            }
            else
            {
                var vehicle = _vehicleRegistry.GetOrCreate(uid.Value, message.sysid);
                _vehicles[message.sysid] = vehicle;
                vehicle.AddChannel(this);

                _logger.LogInformation(
                    "{Name}: vehicle registered sysid={Sysid} uid={Uid:X16}",
                    Name,
                    message.sysid,
                    uid.Value
                );
            }
        }

        _messages.OnNext(message);
    }

    internal static string DecodeParamId(byte[] paramId) =>
        Encoding.ASCII.GetString(paramId).TrimEnd('\0');

    private static bool ParamIdEquals(byte[] paramId, string name) =>
        string.Equals(DecodeParamId(paramId), name, StringComparison.OrdinalIgnoreCase);

    private static byte[] EncodeParamId(string name)
    {
        var id = new byte[16];
        Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, 16), id, 0);
        return id;
    }

    /// <summary>
    /// Sends MAV_CMD_REQUEST_MESSAGE for AUTOPILOT_VERSION until the vehicle
    /// is registered or the channel is disposed.
    /// Vehicle registration happens in <see cref="OnMessageReceived"/>
    /// when the response arrives.
    /// </summary>
    private async Task NegotiateVersionAsync(byte sysId)
    {
        try
        {
            var cmd = new MAVLink.mavlink_command_long_t
            {
                target_system = sysId,
                target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
                command = (ushort)MAVLink.MAV_CMD.REQUEST_MESSAGE,
                param1 = (float)MAVLink.MAVLINK_MSG_ID.AUTOPILOT_VERSION,
            };

            while (!_vehicles.ContainsKey(sysId))
            {
                await SendAsync(MAVLink.MAVLINK_MSG_ID.COMMAND_LONG, cmd, _cts.Token)
                    .ConfigureAwait(false);

                await Task.Delay(NegotiationRetryInterval, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Channel disposing.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Name}: version negotiation failed for sysid={Sysid}",
                Name,
                sysId
            );
        }
        finally
        {
            _negotiations.TryRemove(sysId, out _);
        }
    }
}
