// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Modes;

namespace Groundwork.Core.Tests.Modes;

public class ArduPilotModeMapTests
{
    // -- ModeToName --

    [Theory]
    [InlineData(MAVLink.MAV_TYPE.QUADROTOR, 0u, "STABILIZE")]
    [InlineData(MAVLink.MAV_TYPE.QUADROTOR, 4u, "GUIDED")]
    [InlineData(MAVLink.MAV_TYPE.QUADROTOR, 9u, "LAND")]
    [InlineData(MAVLink.MAV_TYPE.HELICOPTER, 6u, "RTL")]
    public void ModeToName_Copter_ReturnsDescription(
        MAVLink.MAV_TYPE type,
        uint mode,
        string expected
    )
    {
        Assert.Equal(expected, ArduPilotModeMap.ModeToName(mode, type));
    }

    [Theory]
    [InlineData(MAVLink.MAV_TYPE.FIXED_WING, 0u, "MANUAL")]
    [InlineData(MAVLink.MAV_TYPE.FIXED_WING, 5u, "FBWA")]
    [InlineData(MAVLink.MAV_TYPE.FIXED_WING, 6u, "FBWB")]
    [InlineData(MAVLink.MAV_TYPE.FIXED_WING, 15u, "GUIDED")]
    [InlineData(MAVLink.MAV_TYPE.FIXED_WING, 17u, "QSTABILIZE")]
    [InlineData(MAVLink.MAV_TYPE.VTOL_TILTROTOR, 10u, "AUTO")]
    public void ModeToName_Plane_ReturnsDescription(
        MAVLink.MAV_TYPE type,
        uint mode,
        string expected
    )
    {
        Assert.Equal(expected, ArduPilotModeMap.ModeToName(mode, type));
    }

    [Theory]
    [InlineData(MAVLink.MAV_TYPE.GROUND_ROVER, 0u, "MANUAL")]
    [InlineData(MAVLink.MAV_TYPE.GROUND_ROVER, 10u, "AUTO")]
    [InlineData(MAVLink.MAV_TYPE.SURFACE_BOAT, 15u, "GUIDED")]
    public void ModeToName_Rover_ReturnsDescription(
        MAVLink.MAV_TYPE type,
        uint mode,
        string expected
    )
    {
        Assert.Equal(expected, ArduPilotModeMap.ModeToName(mode, type));
    }

    [Fact]
    public void ModeToName_UnknownMode_ReturnsModeN()
    {
        Assert.Equal("Mode(255)", ArduPilotModeMap.ModeToName(255, MAVLink.MAV_TYPE.QUADROTOR));
    }

    [Fact]
    public void ModeToName_UnmappedType_ReturnsModeN()
    {
        Assert.Equal("Mode(0)", ArduPilotModeMap.ModeToName(0, MAVLink.MAV_TYPE.GCS));
    }

    // -- NameToMode --

    [Theory]
    [InlineData("GUIDED", MAVLink.MAV_TYPE.QUADROTOR, 4u)]
    [InlineData("LAND", MAVLink.MAV_TYPE.QUADROTOR, 9u)]
    [InlineData("FBWA", MAVLink.MAV_TYPE.FIXED_WING, 5u)]
    [InlineData("AUTO", MAVLink.MAV_TYPE.GROUND_ROVER, 10u)]
    public void NameToMode_KnownName_ReturnsNumber(
        string name,
        MAVLink.MAV_TYPE type,
        uint expected
    )
    {
        Assert.Equal(expected, ArduPilotModeMap.NameToMode(name, type));
    }

    [Theory]
    [InlineData("guided")]
    [InlineData("Guided")]
    [InlineData("GUIDED")]
    public void NameToMode_CaseInsensitive(string name)
    {
        Assert.Equal(4u, ArduPilotModeMap.NameToMode(name, MAVLink.MAV_TYPE.QUADROTOR));
    }

    [Fact]
    public void NameToMode_UnknownName_ReturnsNull()
    {
        Assert.Null(ArduPilotModeMap.NameToMode("BOGUS", MAVLink.MAV_TYPE.QUADROTOR));
    }

    [Fact]
    public void NameToMode_UnmappedType_ReturnsNull()
    {
        Assert.Null(ArduPilotModeMap.NameToMode("GUIDED", MAVLink.MAV_TYPE.GCS));
    }

    [Fact]
    public void NameToMode_SameNameDifferentType_ReturnsDifferentValues()
    {
        // LOITER is mode 5 for copter, mode 12 for plane
        var copter = ArduPilotModeMap.NameToMode("LOITER", MAVLink.MAV_TYPE.QUADROTOR);
        var plane = ArduPilotModeMap.NameToMode("LOITER", MAVLink.MAV_TYPE.FIXED_WING);

        Assert.Equal(5u, copter);
        Assert.Equal(12u, plane);
    }

    // -- Vehicle type -> firmware type mapping --

    [Theory]
    [InlineData(MAVLink.MAV_TYPE.HEXAROTOR)]
    [InlineData(MAVLink.MAV_TYPE.OCTOROTOR)]
    [InlineData(MAVLink.MAV_TYPE.TRICOPTER)]
    [InlineData(MAVLink.MAV_TYPE.COAXIAL)]
    [InlineData(MAVLink.MAV_TYPE.GENERIC_MULTIROTOR)]
    public void CopterTypes_UseCopterModes(MAVLink.MAV_TYPE type)
    {
        // STABILIZE=0 is copter-specific (plane has MANUAL=0)
        Assert.Equal("STABILIZE", ArduPilotModeMap.ModeToName(0, type));
    }

    [Theory]
    [InlineData(MAVLink.MAV_TYPE.VTOL_TAILSITTER_DUOROTOR)]
    [InlineData(MAVLink.MAV_TYPE.VTOL_TAILSITTER_QUADROTOR)]
    [InlineData(MAVLink.MAV_TYPE.VTOL_FIXEDROTOR)]
    [InlineData(MAVLink.MAV_TYPE.VTOL_GYRODYNE)]
    public void VtolTypes_UsePlaneModes(MAVLink.MAV_TYPE type)
    {
        // MANUAL=0 is plane-specific (copter has STABILIZE=0)
        Assert.Equal("MANUAL", ArduPilotModeMap.ModeToName(0, type));
    }
}
