// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Channels;

/// <summary>
/// Represents an error reported by the autopilot via PARAM_ERROR (AP 4.7+).
/// </summary>
public sealed class ParameterException : Exception
{
    public ParameterException(string paramName, MAVLink.MAV_PARAM_ERROR error)
        : base($"Parameter '{paramName}': {error}")
    {
        ParamName = paramName;
        Error = error;
    }

    public ParameterException(string paramName, float requested, float confirmed)
        : base($"Parameter '{paramName}': set to {requested} but confirmed {confirmed}")
    {
        ParamName = paramName;
    }

    /// <summary>
    /// Gets the parameter name that caused the error.
    /// </summary>
    public string ParamName { get; }

    /// <summary>
    /// Gets the error code from the autopilot, or <see langword="null"/> for value-mismatch errors.
    /// </summary>
    public MAVLink.MAV_PARAM_ERROR? Error { get; }
}
