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
/// Cross-connection vehicle registry. Maps hardware UID to <see cref="Vehicle"/>.
/// This is the single source of truth for known vehicles, independent of
/// which connection discovered them.
/// </summary>
public class VehicleRegistry
{
    private readonly Dictionary<ulong, Vehicle> _vehicles = new();
    private readonly object _lock = new();

    /// <summary>
    /// Returns the vehicle for the given UID. If the vehicle already exists,
    /// adds the VehicleState to it. Otherwise creates a new vehicle.
    /// </summary>
    public Vehicle GetOrAdd(ulong uid, VehicleState state)
    {
        lock (_lock)
        {
            if (_vehicles.TryGetValue(uid, out var existing))
            {
                existing.AddState(state);
                return existing;
            }

            var vehicle = new Vehicle(uid, state);
            _vehicles[uid] = vehicle;
            return vehicle;
        }
    }

    public Vehicle? TryGet(ulong uid)
    {
        lock (_lock)
        {
            return _vehicles.TryGetValue(uid, out var vehicle) ? vehicle : null;
        }
    }

    public IEnumerable<Vehicle> Vehicles
    {
        get
        {
            lock (_lock)
            {
                return _vehicles.Values.ToList();
            }
        }
    }
}
