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
/// Represents an ArduPilot firmware family. Values match the ParameterRepository
/// folder names (e.g. "Copter-4.6").
/// </summary>
public enum FirmwareFamily
{
    Copter,
    Plane,
    Rover,
    Tracker,
    Sub,
    Blimp,
}

/// <summary>
/// Provides mapping from MAVLink vehicle type to ArduPilot firmware family.
/// </summary>
public static class FirmwareFamilyMap
{
    /// <summary>
    /// Gets the ArduPilot firmware family for a MAVLink vehicle type.
    /// </summary>
    /// <returns>The firmware family, or <see langword="null"/> for unmapped types.</returns>
    public static FirmwareFamily? FromMavType(MAVLink.MAV_TYPE vehicleType) =>
        vehicleType switch
        {
            MAVLink.MAV_TYPE.QUADROTOR
            or MAVLink.MAV_TYPE.COAXIAL
            or MAVLink.MAV_TYPE.HELICOPTER
            or MAVLink.MAV_TYPE.ROCKET
            or MAVLink.MAV_TYPE.HEXAROTOR
            or MAVLink.MAV_TYPE.OCTOROTOR
            or MAVLink.MAV_TYPE.TRICOPTER
            or MAVLink.MAV_TYPE.DODECAROTOR
            or MAVLink.MAV_TYPE.DECAROTOR
            or MAVLink.MAV_TYPE.GENERIC_MULTIROTOR => FirmwareFamily.Copter,

            MAVLink.MAV_TYPE.FIXED_WING
            or MAVLink.MAV_TYPE.FLAPPING_WING
            or MAVLink.MAV_TYPE.KITE
            or MAVLink.MAV_TYPE.VTOL_TAILSITTER_DUOROTOR
            or MAVLink.MAV_TYPE.VTOL_TAILSITTER_QUADROTOR
            or MAVLink.MAV_TYPE.VTOL_TILTROTOR
            or MAVLink.MAV_TYPE.VTOL_FIXEDROTOR
            or MAVLink.MAV_TYPE.VTOL_TAILSITTER
            or MAVLink.MAV_TYPE.VTOL_TILTWING
            or MAVLink.MAV_TYPE.VTOL_RESERVED5
            or MAVLink.MAV_TYPE.PARAFOIL
            or MAVLink.MAV_TYPE.PARACHUTE
            or MAVLink.MAV_TYPE.VTOL_GYRODYNE => FirmwareFamily.Plane,

            MAVLink.MAV_TYPE.GROUND_ROVER
            or MAVLink.MAV_TYPE.SURFACE_BOAT
            or MAVLink.MAV_TYPE.GROUND_QUADRUPED => FirmwareFamily.Rover,

            MAVLink.MAV_TYPE.ANTENNA_TRACKER => FirmwareFamily.Tracker,

            MAVLink.MAV_TYPE.SUBMARINE => FirmwareFamily.Sub,

            MAVLink.MAV_TYPE.AIRSHIP or MAVLink.MAV_TYPE.FREE_BALLOON => FirmwareFamily.Blimp,

            _ => null,
        };
}
