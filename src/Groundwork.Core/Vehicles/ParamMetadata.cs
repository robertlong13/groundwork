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
/// Represents static metadata for a parameter (description, range, enum values, etc.).
/// Firmware-agnostic; populated by autopilot-specific fetchers.
/// </summary>
public sealed record ParamMetadata
{
    /// <summary>
    /// Gets the short user-facing name (e.g. "Arm Checks to Perform").
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the full description of the parameter.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the minimum valid value.
    /// </summary>
    public float? Min { get; init; }

    /// <summary>
    /// Gets the maximum valid value.
    /// </summary>
    public float? Max { get; init; }

    /// <summary>
    /// Gets the recommended UI step size.
    /// </summary>
    public float? Increment { get; init; }

    /// <summary>
    /// Gets the unit of measure (e.g. "m", "m/s", "Hz").
    /// </summary>
    public string? Units { get; init; }

    /// <summary>
    /// Gets the enumerated value options, keyed by numeric code.
    /// </summary>
    public IReadOnlyDictionary<int, string>? Values { get; init; }

    /// <summary>
    /// Gets the bitmask field definitions, keyed by bit index.
    /// </summary>
    public IReadOnlyDictionary<int, string>? Bitmask { get; init; }

    /// <summary>
    /// Gets whether changing this parameter requires a reboot.
    /// </summary>
    public bool RebootRequired { get; init; }

    /// <summary>
    /// Gets whether this parameter is read-only.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets whether this parameter's value is volatile (may change without being set).
    /// </summary>
    public bool Volatile { get; init; }

    /// <summary>
    /// Gets the parameter group (e.g. "BATT_", "ARMING_").
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Gets the user-facing category (e.g. "Standard", "Advanced").
    /// </summary>
    public string? Category { get; init; }
}
