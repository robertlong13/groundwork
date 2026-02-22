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

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Represents a vehicle identified by hardware UID, holding references to the
/// <see cref="MavChannel"/>s that can reach it.
/// </summary>
public class Vehicle
{
    private readonly HashSet<MavChannel> _channels = new();
    private readonly Lock _lock = new();

    public Vehicle(
        ulong uid,
        byte sysId,
        IReadOnlyDictionary<MAVLink.MAV_DATA_STREAM, int>? defaultStreamRates = null
    )
    {
        Uid = uid;
        SysId = sysId;
        RateController = new StreamRateController(sysId, defaultStreamRates);
    }

    public ulong Uid { get; }

    /// <summary>
    /// Gets the MAVLink system ID, constant for a given vehicle across all links.
    /// </summary>
    public byte SysId { get; }

    /// <summary>
    /// Gets the controller for outbound telemetry rate requests on this vehicle.
    /// </summary>
    public StreamRateController RateController { get; }

    /// <summary>
    /// Gets the primary channel for this vehicle, through which outbound commands are routed.
    /// </summary>
    public MavChannel? PrimaryChannel
    {
        get
        {
            lock (_lock)
            {
                // TODO: channel selection strategy (manual/auto).
                foreach (var ch in _channels)
                    return ch;
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the canonical vehicle state, delegated from the primary channel.
    /// </summary>
    public VehicleState? CanonicalState => PrimaryChannel?.GetState(SysId);

    /// <summary>
    /// Gets the channels that can reach this vehicle.
    /// </summary>
    public IReadOnlyCollection<MavChannel> Channels
    {
        get
        {
            lock (_lock)
            {
                return _channels.ToArray();
            }
        }
    }

    /// <summary>
    /// Returns the custom_mode number for a mode name.
    /// </summary>
    /// <returns>The custom_mode number, or <see langword="null"/> if unrecognized or no heartbeat received.</returns>
    public uint? NameToMode(string name) =>
        CanonicalState is { } state ? Modes.ArduPilotModeMap.NameToMode(name, state.Type) : null;

    /// <summary>
    /// Returns the display name for a custom_mode number.
    /// </summary>
    /// <returns>The mode display name, or "Mode(N)" for unrecognized modes.</returns>
    public string ModeToName(uint customMode) =>
        CanonicalState is { } state
            ? Modes.ArduPilotModeMap.ModeToName(customMode, state.Type)
            : $"Mode({customMode})";

    /// <summary>
    /// Gets the available mode names and their custom_mode numbers for this vehicle type.
    /// </summary>
    public IReadOnlyDictionary<string, uint> AvailableModes =>
        CanonicalState is { } state
            ? Modes.ArduPilotModeMap.GetModes(state.Type)
            : new Dictionary<string, uint>();

    /// <summary>
    /// Arms the vehicle.
    /// </summary>
    /// <param name="force">Bypass pre-arm checks (ArduPilot-specific).</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> ArmAsync(bool force = false, CancellationToken ct = default) =>
        SendCommandAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            param1: 1f,
            // ArduPilot force-arm constant (not MAVLink spec).
            param2: force ? 2989f : 0f,
            ct: ct
        );

    /// <summary>
    /// Disarms the vehicle.
    /// </summary>
    /// <param name="force">Force disarm (ArduPilot-specific).</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> DisarmAsync(
        bool force = false,
        CancellationToken ct = default
    ) =>
        SendCommandAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            param1: 0f,
            // ArduPilot force-disarm constant (not MAVLink spec).
            param2: force ? 21196f : 0f,
            ct: ct
        );

    /// <summary>
    /// Sets the flight mode via COMMAND_LONG DO_SET_MODE.
    /// </summary>
    /// <param name="customMode">The autopilot-specific custom_mode number.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> SetModeAsync(uint customMode, CancellationToken ct = default) =>
        SendCommandAsync(
            MAVLink.MAV_CMD.DO_SET_MODE,
            param1: (float)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED,
            param2: customMode,
            ct: ct
        );

    /// <summary>
    /// Enables or disables the hardware safety switch via SET_MODE.
    /// </summary>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public Task SetSafetyAsync(bool on, CancellationToken ct = default)
    {
        var msg = new MAVLink.mavlink_set_mode_t
        {
            target_system = SysId,
            base_mode = (byte)MAVLink.MAV_MODE_FLAG_DECODE_POSITION.SAFETY,
            custom_mode = on ? 1u : 0u,
        };

        return SendAsync(MAVLink.MAVLINK_MSG_ID.SET_MODE, msg, ct);
    }

    /// <summary>
    /// Sends a COMMAND_LONG to the autopilot via the primary channel and awaits the matching COMMAND_ACK.
    /// </summary>
    /// <param name="command">The MAVLink command to send.</param>
    /// <param name="timeout">ACK timeout. Defaults to 5 seconds.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> SendCommandAsync(
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
        var channel =
            PrimaryChannel ?? throw new InvalidOperationException("No channel available.");

        return channel.SendCommandAsync(
            SysId,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
            command,
            param1,
            param2,
            param3,
            param4,
            param5,
            param6,
            param7,
            timeout,
            ct
        );
    }

    /// <summary>
    /// Sends a typed MAVLink message via the primary channel.
    /// </summary>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public Task SendAsync(
        MAVLink.MAVLINK_MSG_ID messageType,
        object data,
        CancellationToken ct = default
    )
    {
        var channel =
            PrimaryChannel ?? throw new InvalidOperationException("No channel available.");

        return channel.SendAsync(messageType, data, ct);
    }

    internal void AddChannel(MavChannel channel)
    {
        lock (_lock)
        {
            _channels.Add(channel);
        }

        var vehicleMessages = channel.Messages.Where(m => m.sysid == SysId);
        RateController.AddChannel(channel.SendAsync, vehicleMessages);
    }

    internal void RemoveChannel(MavChannel channel)
    {
        lock (_lock)
        {
            _channels.Remove(channel);
        }

        RateController.RemoveChannel(channel.SendAsync);
    }

    /// <summary>
    /// Generates a deterministic UID from sysid. Placeholder until
    /// AUTOPILOT_VERSION provides the real hardware UID.
    /// </summary>
    public static ulong MockUidFromSysid(byte sysid) => sysid;
}
