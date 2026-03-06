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

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Provides bidirectional mapping between ArduPilot mode names and custom_mode numbers.
/// </summary>
public static class ModeMap
{
    private static readonly Dictionary<FirmwareFamily, Dictionary<string, uint>> NameToNum;
    private static readonly Dictionary<FirmwareFamily, Dictionary<uint, string>> NumToName;

    static ModeMap()
    {
        NameToNum = new Dictionary<FirmwareFamily, Dictionary<string, uint>>
        {
            [FirmwareFamily.Copter] = BuildNameToNum<MAVLink.COPTER_MODE>(),
            [FirmwareFamily.Plane] = BuildNameToNum<MAVLink.PLANE_MODE>(),
            [FirmwareFamily.Rover] = BuildNameToNum<MAVLink.ROVER_MODE>(),
            [FirmwareFamily.Tracker] = BuildNameToNum<MAVLink.TRACKER_MODE>(),
            [FirmwareFamily.Sub] = BuildNameToNum<MAVLink.SUB_MODE>(),
        };

        NumToName = new Dictionary<FirmwareFamily, Dictionary<uint, string>>
        {
            [FirmwareFamily.Copter] = BuildNumToName<MAVLink.COPTER_MODE>(),
            [FirmwareFamily.Plane] = BuildNumToName<MAVLink.PLANE_MODE>(),
            [FirmwareFamily.Rover] = BuildNumToName<MAVLink.ROVER_MODE>(),
            [FirmwareFamily.Tracker] = BuildNumToName<MAVLink.TRACKER_MODE>(),
            [FirmwareFamily.Sub] = BuildNumToName<MAVLink.SUB_MODE>(),
        };
    }

    /// <summary>
    /// Returns the custom_mode number for a mode name.
    /// </summary>
    public static uint? NameToMode(string name, MAVLink.MAV_TYPE vehicleType)
    {
        var family = FirmwareFamilyMap.FromMavType(vehicleType);
        if (family is null)
            return null;

        return NameToNum[family.Value].TryGetValue(name, out var mode) ? mode : null;
    }

    /// <summary>
    /// Returns the display name for a custom_mode number.
    /// </summary>
    public static string ModeToName(uint customMode, MAVLink.MAV_TYPE vehicleType)
    {
        var family = FirmwareFamilyMap.FromMavType(vehicleType);
        if (family is null)
            return $"Mode({customMode})";

        return NumToName[family.Value].TryGetValue(customMode, out var name)
            ? name
            : $"Mode({customMode})";
    }

    /// <summary>
    /// Gets all known modes for a vehicle type.
    /// </summary>
    public static IReadOnlyDictionary<string, uint> GetModes(MAVLink.MAV_TYPE vehicleType)
    {
        var family = FirmwareFamilyMap.FromMavType(vehicleType);
        if (family is null)
            return new Dictionary<string, uint>();

        return NameToNum[family.Value];
    }

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
