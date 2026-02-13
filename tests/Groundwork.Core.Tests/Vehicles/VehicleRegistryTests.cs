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

public class VehicleRegistryTests
{
    [Fact]
    public void GetOrCreate_CreatesNewVehicle()
    {
        var registry = new VehicleRegistry();

        var vehicle = registry.GetOrCreate(42, 1);

        Assert.Equal(42ul, vehicle.Uid);
        Assert.Equal(1, vehicle.SysId);
    }

    [Fact]
    public void GetOrCreate_ReturnsSameVehicle()
    {
        var registry = new VehicleRegistry();

        var vehicle1 = registry.GetOrCreate(42, 1);
        var vehicle2 = registry.GetOrCreate(42, 1);

        Assert.Same(vehicle1, vehicle2);
    }

    [Fact]
    public void TryGet_ReturnsNullForUnknownUid()
    {
        var registry = new VehicleRegistry();

        Assert.Null(registry.TryGet(999));
    }

    [Fact]
    public void TryGet_ReturnsVehicleAfterCreate()
    {
        var registry = new VehicleRegistry();
        var added = registry.GetOrCreate(42, 1);

        var found = registry.TryGet(42);

        Assert.Same(added, found);
    }

    [Fact]
    public void Vehicles_ReturnsAllRegistered()
    {
        var registry = new VehicleRegistry();
        registry.GetOrCreate(1, 1);
        registry.GetOrCreate(2, 2);

        var vehicles = registry.Vehicles.ToList();

        Assert.Equal(2, vehicles.Count);
    }
}
