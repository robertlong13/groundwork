// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Provides ArduPilot parameters that should be excluded when loading from
/// a .parm file because they sneakily cause problems.
/// </summary>
/// <remarks>
/// These parameters should be marked in some way in ArduPilot metadata (@ReadOnly, even if they
/// technically can be written to) but are not. If AP ever fixes their metadata, this class becomes
/// empty.
/// </remarks>
public static class ParamLoadExclusions
{
    /// <summary>
    /// Gets parameter names that should be skipped during file loads.
    /// </summary>
    public static IReadOnlySet<string> DefaultWildcards { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FENCE_TOTAL", "FORMAT_VERSION" };
}
