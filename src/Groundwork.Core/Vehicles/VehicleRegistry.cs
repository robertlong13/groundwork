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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Provides hardware-UID-keyed lookup of known <see cref="Vehicle"/>s across all channels.
/// </summary>
public class VehicleRegistry
{
    private readonly OrderedDictionary<ulong, Vehicle> _vehicles = new();
    private readonly Lock _lock = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly IReadOnlyDictionary<MAVLink.MAV_DATA_STREAM, int>? _defaultStreamRates;
    private readonly ArduPilot.ParamMetadataFetcher? _metadataFetcher;

    public VehicleRegistry(
        ILoggerFactory? loggerFactory = null,
        IReadOnlyDictionary<MAVLink.MAV_DATA_STREAM, int>? defaultStreamRates = null,
        ArduPilot.ParamMetadataFetcher? metadataFetcher = null
    )
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _defaultStreamRates = defaultStreamRates;
        _metadataFetcher = metadataFetcher;
    }

    /// <summary>
    /// Returns the vehicle for the given UID, creating one if it doesn't exist.
    /// </summary>
    public Vehicle GetOrCreate(ulong uid, byte sysId, MavChannel channel)
    {
        lock (_lock)
        {
            if (_vehicles.TryGetValue(uid, out var existing))
                return existing;

            var vehicle = new Vehicle(
                uid,
                sysId,
                channel,
                _loggerFactory,
                _defaultStreamRates,
                _metadataFetcher
            );
            _vehicles[uid] = vehicle;
            return vehicle;
        }
    }

    /// <summary>
    /// Removes a vehicle only if it has no remaining channels.
    /// </summary>
    /// <returns><see langword="true"/> if the vehicle was removed.</returns>
    public bool RemoveIfOrphaned(ulong uid)
    {
        lock (_lock)
        {
            if (!_vehicles.TryGetValue(uid, out var vehicle))
                return false;
            if (vehicle.Channels.Count > 0)
                return false;
            _vehicles.Remove(uid);
            return true;
        }
    }

    public Vehicle? TryGet(ulong uid)
    {
        lock (_lock)
        {
            return _vehicles.TryGetValue(uid, out var vehicle) ? vehicle : null;
        }
    }

    /// <summary>
    /// Gets all vehicles in discovery order.
    /// </summary>
    public IReadOnlyList<Vehicle> Vehicles
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
