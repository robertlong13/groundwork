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
/// A vehicle identified by hardware UID. Holds references to the
/// <see cref="MavChannel"/>s that can reach it. State lives on the
/// channels; <see cref="CanonicalState"/> delegates to the primary one.
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
    /// MAVLink system ID. Constant for a given vehicle across all links.
    /// </summary>
    public byte SysId { get; }

    /// <summary>
    /// Manages outbound telemetry rate requests for this vehicle.
    /// Rates are sent on all channels.
    /// </summary>
    public StreamRateController RateController { get; }

    /// <summary>
    /// The primary channel for this vehicle. Outbound commands and
    /// heartbeats are routed through this channel.
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
    /// Canonical vehicle state, delegated from the primary channel.
    /// </summary>
    public VehicleState? CanonicalState => PrimaryChannel?.GetState(SysId);

    /// <summary>
    /// Channels that can reach this vehicle.
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
