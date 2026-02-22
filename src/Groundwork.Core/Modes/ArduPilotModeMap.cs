// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reflection;

namespace Groundwork.Core.Modes;

/// <summary>
/// Provides bidirectional mapping between ArduPilot mode names and custom_mode numbers.
/// </summary>
public static class ArduPilotModeMap
{
    private enum FirmwareType
    {
        Copter,
        Plane,
        Rover,
        Tracker,
        Sub,
    }

    private static readonly Dictionary<FirmwareType, Dictionary<string, uint>> NameToNum;
    private static readonly Dictionary<FirmwareType, Dictionary<uint, string>> NumToName;

    static ArduPilotModeMap()
    {
        NameToNum = new Dictionary<FirmwareType, Dictionary<string, uint>>
        {
            [FirmwareType.Copter] = BuildNameToNum<MAVLink.COPTER_MODE>(),
            [FirmwareType.Plane] = BuildNameToNum<MAVLink.PLANE_MODE>(),
            [FirmwareType.Rover] = BuildNameToNum<MAVLink.ROVER_MODE>(),
            [FirmwareType.Tracker] = BuildNameToNum<MAVLink.TRACKER_MODE>(),
            [FirmwareType.Sub] = BuildNameToNum<MAVLink.SUB_MODE>(),
        };

        NumToName = new Dictionary<FirmwareType, Dictionary<uint, string>>
        {
            [FirmwareType.Copter] = BuildNumToName<MAVLink.COPTER_MODE>(),
            [FirmwareType.Plane] = BuildNumToName<MAVLink.PLANE_MODE>(),
            [FirmwareType.Rover] = BuildNumToName<MAVLink.ROVER_MODE>(),
            [FirmwareType.Tracker] = BuildNumToName<MAVLink.TRACKER_MODE>(),
            [FirmwareType.Sub] = BuildNumToName<MAVLink.SUB_MODE>(),
        };
    }

    public static uint? NameToMode(string name, MAVLink.MAV_TYPE vehicleType)
    {
        var family = GetFamily(vehicleType);
        if (family is null)
            return null;

        return NameToNum[family.Value].TryGetValue(name, out var mode) ? mode : null;
    }

    public static string ModeToName(uint customMode, MAVLink.MAV_TYPE vehicleType)
    {
        var family = GetFamily(vehicleType);
        if (family is null)
            return $"Mode({customMode})";

        return NumToName[family.Value].TryGetValue(customMode, out var name)
            ? name
            : $"Mode({customMode})";
    }

    public static IReadOnlyDictionary<string, uint> GetModes(MAVLink.MAV_TYPE vehicleType)
    {
        var family = GetFamily(vehicleType);
        if (family is null)
            return new Dictionary<string, uint>();

        return NameToNum[family.Value];
    }

    private static FirmwareType? GetFamily(MAVLink.MAV_TYPE vehicleType) =>
        vehicleType switch
        {
            // Copter
            MAVLink.MAV_TYPE.QUADROTOR
            or MAVLink.MAV_TYPE.COAXIAL
            or MAVLink.MAV_TYPE.HELICOPTER
            or MAVLink.MAV_TYPE.AIRSHIP
            or MAVLink.MAV_TYPE.FREE_BALLOON
            or MAVLink.MAV_TYPE.ROCKET
            or MAVLink.MAV_TYPE.HEXAROTOR
            or MAVLink.MAV_TYPE.OCTOROTOR
            or MAVLink.MAV_TYPE.TRICOPTER
            or MAVLink.MAV_TYPE.DODECAROTOR
            or MAVLink.MAV_TYPE.DECAROTOR
            or MAVLink.MAV_TYPE.GENERIC_MULTIROTOR => FirmwareType.Copter,

            // Plane
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
            or MAVLink.MAV_TYPE.VTOL_GYRODYNE => FirmwareType.Plane,

            // Rover
            MAVLink.MAV_TYPE.GROUND_ROVER
            or MAVLink.MAV_TYPE.SURFACE_BOAT
            or MAVLink.MAV_TYPE.GROUND_QUADRUPED => FirmwareType.Rover,

            // Tracker
            MAVLink.MAV_TYPE.ANTENNA_TRACKER => FirmwareType.Tracker,

            // Sub
            MAVLink.MAV_TYPE.SUBMARINE => FirmwareType.Sub,

            _ => null,
        };

    private static Dictionary<string, uint> BuildNameToNum<TEnum>()
        where TEnum : struct, Enum
    {
        var dict = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var desc = field.GetCustomAttribute<MAVLink.Description>();
            var name = desc?.Text ?? field.Name;
            var value = (uint)(int)field.GetRawConstantValue()!;
            dict[name] = value;
        }

        return dict;
    }

    private static Dictionary<uint, string> BuildNumToName<TEnum>()
        where TEnum : struct, Enum
    {
        var dict = new Dictionary<uint, string>();

        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var desc = field.GetCustomAttribute<MAVLink.Description>();
            var name = desc?.Text ?? field.Name;
            var value = (uint)(int)field.GetRawConstantValue()!;
            dict[value] = name;
        }

        return dict;
    }
}
