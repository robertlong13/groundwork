// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Vehicles;

namespace Groundwork.Core.Tests.Vehicles;

public class VehicleTests
{
    private const byte SysId = 1;

    private static byte[] MakeUid2(byte fill)
    {
        var uid2 = new byte[18];
        uid2[0] = fill;
        return uid2;
    }

    [Fact]
    public void ComputeUid_PrefersUid2_OverUid()
    {
        var uid2 = MakeUid2(2);

        var fromBoth = Vehicle.ComputeUid(uid: 1, uid2, SysId);
        var fromUid2Only = Vehicle.ComputeUid(uid: 0, uid2, SysId);

        Assert.Equal(fromUid2Only, fromBoth);
    }

    [Fact]
    public void ComputeUid_FallsBackToUid_WhenUid2AllZero()
    {
        var zeroUid2 = new byte[18];

        var result = Vehicle.ComputeUid(uid: 1, zeroUid2, SysId);

        Assert.NotNull(result);
    }

    [Fact]
    public void ComputeUid_UidAndUid2_ProduceDifferentHashes()
    {
        var uid2 = MakeUid2(2);
        var zeroUid2 = new byte[18];

        var fromUid2 = Vehicle.ComputeUid(uid: 0, uid2, SysId);
        var fromUid = Vehicle.ComputeUid(uid: 1, zeroUid2, SysId);

        Assert.NotEqual(fromUid2, fromUid);
    }

    [Fact]
    public void ComputeUid_ReturnsNull_WhenBothZero()
    {
        var zeroUid2 = new byte[18];

        var result = Vehicle.ComputeUid(uid: 0, zeroUid2, SysId);

        Assert.Null(result);
    }
}
