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
    /// <summary>Normalized name to mode number, for flexible input matching.</summary>
    private static readonly Dictionary<FirmwareFamily, Dictionary<string, uint>> NameToNum;

    /// <summary>Mode number to canonical display name.</summary>
    private static readonly Dictionary<FirmwareFamily, Dictionary<uint, string>> NumToName;

    static ModeMap()
    {
        NumToName = new Dictionary<FirmwareFamily, Dictionary<uint, string>>
        {
            [FirmwareFamily.Copter] = BuildNumToName<MAVLink.COPTER_MODE>(),
            [FirmwareFamily.Plane] = BuildNumToName<MAVLink.PLANE_MODE>(),
            [FirmwareFamily.Rover] = BuildNumToName<MAVLink.ROVER_MODE>(),
            [FirmwareFamily.Tracker] = BuildNumToName<MAVLink.TRACKER_MODE>(),
            [FirmwareFamily.Sub] = BuildNumToName<MAVLink.SUB_MODE>(),
        };

        NameToNum = new Dictionary<FirmwareFamily, Dictionary<string, uint>>();

        foreach (var (family, modes) in NumToName)
        {
            var nameToNum = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mode, name) in modes)
                nameToNum.TryAdd(Normalize(name), mode);
            NameToNum[family] = nameToNum;
        }
    }

    /// <summary>
    /// Returns the custom_mode number for a mode name.
    /// </summary>
    /// <remarks>
    /// Accepts canonical names ("ALT HOLD"), underscored ("ALT_HOLD"), and
    /// collapsed ("AltHold", "althold") forms via case-insensitive normalized lookup.
    /// </remarks>
    public static uint? NameToMode(string name, MAVLink.MAV_TYPE vehicleType)
    {
        var family = FirmwareFamilyMap.FromMavType(vehicleType);
        if (family is null)
            return null;

        return NameToNum[family.Value].TryGetValue(Normalize(name), out var mode) ? mode : null;
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
    /// Gets all known modes for a vehicle type, keyed by mode number with canonical display names.
    /// </summary>
    public static IReadOnlyDictionary<uint, string> GetModes(MAVLink.MAV_TYPE vehicleType)
    {
        var family = FirmwareFamilyMap.FromMavType(vehicleType);
        if (family is null)
            return new Dictionary<uint, string>();

        return NumToName[family.Value];
    }

    private static string Normalize(string name) =>
        name.Replace(" ", "").Replace("_", "").Replace("-", "");

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
