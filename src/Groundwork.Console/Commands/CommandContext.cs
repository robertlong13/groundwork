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
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Console.Commands;

/// <summary>
/// Represents the shared state passed to every command invocation.
/// </summary>
public sealed class CommandContext(
    VehicleRegistry vehicleRegistry,
    MavChannelRegistry channelRegistry,
    LinkManager links,
    ILoggerFactory loggerFactory,
    TextWriter output,
    CancellationToken shutdownToken
)
{
    public VehicleRegistry VehicleRegistry { get; } = vehicleRegistry;

    /// <summary>
    /// Gets the registry of all active channels.
    /// </summary>
    public MavChannelRegistry ChannelRegistry { get; } = channelRegistry;

    public LinkManager Links { get; } = links;

    /// <summary>
    /// Gets the logger factory for creating loggers in command handlers.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;

    /// <summary>
    /// Gets the output destination for command responses.
    /// </summary>
    public TextWriter Output { get; } = output;

    public CancellationToken ShutdownToken { get; } = shutdownToken;

    /// <summary>
    /// Gets or sets the currently active vehicle.
    /// </summary>
    /// <remarks>
    /// Falls back to the first discovered vehicle when no explicit selection
    /// has been made. Clears a stale selection if the vehicle is no longer
    /// in the registry.
    /// </remarks>
    public Vehicle? CurrentVehicle
    {
        get
        {
            if (
                _selectedVehicle is not null
                && VehicleRegistry.TryGet(_selectedVehicle.Uid) is null
            )
                _selectedVehicle = null;

            return _selectedVehicle ?? VehicleRegistry.Vehicles.FirstOrDefault();
        }
        set => _selectedVehicle = value;
    }

    private Vehicle? _selectedVehicle;
}
