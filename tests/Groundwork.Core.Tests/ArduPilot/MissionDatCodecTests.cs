// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.ArduPilot;
using Groundwork.Core.Vehicles;

namespace Groundwork.Core.Tests.ArduPilot;

public class MissionDatCodecTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "data",
        "test_mission.dat"
    );

    [Fact]
    public void Decode_SitlFixture_ReturnsExpectedItems()
    {
        var data = File.ReadAllBytes(FixturePath);
        var items = MissionDatCodec.Decode(data);

        // 8 items total: home + 7 mission items (matches test_mission.waypoints).
        Assert.Equal(8, items.Count);

        // Item 0 is home (NAV_WAYPOINT, frame GLOBAL).
        Assert.Equal(MAVLink.MAV_CMD.WAYPOINT, items[0].Command);
        Assert.Equal(MAVLink.MAV_FRAME.GLOBAL, items[0].Frame);

        // Item 1 is takeoff to 50m.
        Assert.Equal(MAVLink.MAV_CMD.TAKEOFF, items[1].Command);
        Assert.Equal(50.0f, items[1].Z);

        // Item 5 is DO_LAND_START.
        Assert.Equal(MAVLink.MAV_CMD.DO_LAND_START, items[5].Command);

        // Item 7 is LAND.
        Assert.Equal(MAVLink.MAV_CMD.LAND, items[7].Command);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new List<MissionItem>
        {
            new(
                MAVLink.MAV_FRAME.GLOBAL,
                MAVLink.MAV_CMD.WAYPOINT,
                0,
                0,
                0,
                0,
                -353632621,
                1491652374,
                584.0f,
                1,
                1
            ),
            new(
                MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
                MAVLink.MAV_CMD.TAKEOFF,
                0,
                0,
                0,
                0,
                0,
                0,
                50.0f,
                1
            ),
            new(
                MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
                MAVLink.MAV_CMD.WAYPOINT,
                0,
                0,
                0,
                0,
                -353610000,
                1491670000,
                80.0f,
                1
            ),
        };

        var encoded = MissionDatCodec.Encode(original, MAVLink.MAV_MISSION_TYPE.MISSION);
        var decoded = MissionDatCodec.Decode(encoded);

        Assert.Equal(original.Count, decoded.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Frame, decoded[i].Frame);
            Assert.Equal(original[i].Command, decoded[i].Command);
            Assert.Equal(original[i].Param1, decoded[i].Param1);
            Assert.Equal(original[i].Param2, decoded[i].Param2);
            Assert.Equal(original[i].Param3, decoded[i].Param3);
            Assert.Equal(original[i].Param4, decoded[i].Param4);
            Assert.Equal(original[i].X, decoded[i].X);
            Assert.Equal(original[i].Y, decoded[i].Y);
            Assert.Equal(original[i].Z, decoded[i].Z);
            Assert.Equal(original[i].Autocontinue, decoded[i].Autocontinue);
            Assert.Equal(original[i].Current, decoded[i].Current);
        }
    }

    [Fact]
    public void Decode_BadMagic_Throws()
    {
        var data = new byte[10];
        data[0] = 0xFF;
        data[1] = 0xFF;

        var ex = Assert.Throws<InvalidDataException>(() => MissionDatCodec.Decode(data));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_TruncatedHeader_Throws()
    {
        var data = new byte[5];

        Assert.Throws<InvalidDataException>(() => MissionDatCodec.Decode(data));
    }

    [Fact]
    public void Decode_TruncatedItems_Throws()
    {
        // Valid header claiming 1 item, but no item data.
        var data = new byte[10];
        data[0] = 0x3d;
        data[1] = 0x76; // magic
        data[8] = 1;
        data[9] = 0; // num_items = 1

        var ex = Assert.Throws<InvalidDataException>(() => MissionDatCodec.Decode(data));
        Assert.Contains("too short", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Encode_EmptyList_ProducesHeaderOnly()
    {
        var encoded = MissionDatCodec.Encode([], MAVLink.MAV_MISSION_TYPE.MISSION);

        Assert.Equal(10, encoded.Length);
        // Verify magic.
        Assert.Equal(0x3d, encoded[0]);
        Assert.Equal(0x76, encoded[1]);
        // Verify count = 0.
        Assert.Equal(0, encoded[8]);
        Assert.Equal(0, encoded[9]);
    }
}
