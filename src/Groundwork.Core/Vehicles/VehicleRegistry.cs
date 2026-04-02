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
using Groundwork.Core.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Provides hardware-UID-keyed lookup of known <see cref="Vehicle"/>s across all channels.
/// </summary>
public class VehicleRegistry
{
    private readonly ConcurrentDictionary<ulong, Vehicle> _vehicles = new();
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
        return _vehicles.GetOrAdd(
            uid,
            _ => new Vehicle(
                uid,
                sysId,
                channel,
                _loggerFactory,
                _defaultStreamRates,
                _metadataFetcher
            )
        );
    }

    public Vehicle? TryGet(ulong uid)
    {
        return _vehicles.TryGetValue(uid, out var vehicle) ? vehicle : null;
    }

    public IEnumerable<Vehicle> Vehicles => _vehicles.Values;
}
