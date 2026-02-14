// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

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
    private readonly object _lock = new();

    public Vehicle(ulong uid, byte sysId)
    {
        Uid = uid;
        SysId = sysId;
    }

    public ulong Uid { get; }

    /// <summary>
    /// MAVLink system ID. Constant for a given vehicle across all links
    /// (ArduPilot guarantee, PX4 behaves the same).
    /// </summary>
    public byte SysId { get; }

    /// <summary>
    /// The primary channel for this vehicle. Outbound commands and heartbeats
    /// go through this channel. At M0 with one channel, this is trivially the
    /// only channel.
    /// </summary>
    public MavChannel? PrimaryChannel
    {
        get
        {
            lock (_lock)
            {
                // M0: return the only channel. M4+: manual/auto selection.
                foreach (var ch in _channels)
                    return ch;
                return null;
            }
        }
    }

    /// <summary>
    /// Canonical vehicle state, delegated through the primary channel.
    /// Vehicle does not store or cache state.
    /// </summary>
    public VehicleState? CanonicalState => PrimaryChannel?.GetState(SysId);

    /// <summary>
    /// Channels that can reach this vehicle. Navigational references --
    /// ownership is in MavChannelRegistry.
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
    }

    internal void RemoveChannel(MavChannel channel)
    {
        lock (_lock)
        {
            _channels.Remove(channel);
        }
    }

    /// <summary>
    /// Temporary M0 mock -- generates a deterministic UID from sysid until
    /// AUTOPILOT_VERSION request-response is implemented.
    /// </summary>
    public static ulong MockUidFromSysid(byte sysid) => sysid;
}
