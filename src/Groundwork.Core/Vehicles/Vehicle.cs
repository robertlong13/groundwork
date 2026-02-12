// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Vehicles;

/// <summary>
/// A vehicle identified by hardware UID from AUTOPILOT_VERSION.
/// First-class identity independent of any connection or sysid.
///
/// Owns a collection of <see cref="VehicleState"/>s (one per connection the
/// vehicle is reachable on). At M0, there is exactly one.
/// At M4+, the collection grows additively when multi-connection arrives.
/// UID is mocked from sysid at M0 via <see cref="MockUidFromSysid"/>.
/// </summary>
public class Vehicle
{
    private readonly List<VehicleState> _states = new();

    public Vehicle(ulong uid, VehicleState initialState)
    {
        Uid = uid;
        AddState(initialState);
    }

    public ulong Uid { get; }

    /// <summary>
    /// Canonical vehicle state. At M0 this is the only VehicleState.
    /// At M4+ this becomes the primary connection's state or a reconciled view.
    /// </summary>
    public VehicleState? State => _states.FirstOrDefault();

    public IReadOnlyList<VehicleState> States => _states;

    public void AddState(VehicleState state)
    {
        _states.Add(state);
        state.Vehicle = this;
    }

    /// <summary>
    /// Temporary M0 mock -- generates a deterministic UID from sysid until
    /// AUTOPILOT_VERSION request-response is implemented.
    /// </summary>
    public static ulong MockUidFromSysid(byte sysid) => sysid;
}
