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
/// Represents a single mission item (waypoint, command, or action).
/// Sequence number is not stored -- it is a wire/file-level concept
/// determined by position in the list.
/// </summary>
public readonly record struct MissionItem(
    MAVLink.MAV_FRAME Frame,
    MAVLink.MAV_CMD Command,
    float Param1,
    float Param2,
    float Param3,
    float Param4,
    int X,
    int Y,
    float Z,
    byte Autocontinue,
    byte Current = 0
)
{
    /// <summary>
    /// Gets the latitude in degrees (global frames only).
    /// </summary>
    public double Latitude => X * 1e-7;

    /// <summary>
    /// Gets the longitude in degrees (global frames only).
    /// </summary>
    public double Longitude => Y * 1e-7;

    /// <summary>
    /// Gets the altitude in meters.
    /// </summary>
    public float Altitude => Z;

    /// <summary>
    /// Creates a <see cref="MissionItem"/> from a MAVLink MISSION_ITEM_INT struct.
    /// </summary>
    public static MissionItem FromMavLink(MAVLink.mavlink_mission_item_int_t m) =>
        new(
            (MAVLink.MAV_FRAME)m.frame,
            (MAVLink.MAV_CMD)m.command,
            m.param1,
            m.param2,
            m.param3,
            m.param4,
            m.x,
            m.y,
            m.z,
            m.autocontinue,
            m.current
        );

    /// <summary>
    /// Converts to a MAVLink MISSION_ITEM_INT struct for transmission.
    /// Seq is set to 0 -- the caller must assign the correct wire index.
    /// </summary>
    public MAVLink.mavlink_mission_item_int_t ToMavLink(
        byte targetSysId,
        byte targetCompId,
        MAVLink.MAV_MISSION_TYPE missionType = MAVLink.MAV_MISSION_TYPE.MISSION
    ) =>
        new(
            Param1,
            Param2,
            Param3,
            Param4,
            X,
            Y,
            Z,
            0,
            (ushort)Command,
            targetSysId,
            targetCompId,
            (byte)Frame,
            Current,
            Autocontinue,
            (byte)missionType
        );
}
