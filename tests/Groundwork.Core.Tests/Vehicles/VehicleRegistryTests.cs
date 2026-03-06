// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO.Pipelines;
using Groundwork.Core.Channels;
using Groundwork.Core.Connections;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groundwork.Core.Tests.Vehicles;

public class VehicleRegistryTests : IDisposable
{
    private readonly List<MavChannel> _channels = new();
    private readonly List<Pipe> _pipes = new();

    private MavChannel MakeChannel(VehicleRegistry registry)
    {
        var pipe = new Pipe();
        _pipes.Add(pipe);
        var connection = new FakeConnection(pipe);
        var channel = new MavChannel(connection, registry, NullLoggerFactory.Instance);
        _channels.Add(channel);
        return channel;
    }

    public void Dispose()
    {
        foreach (var pipe in _pipes)
            pipe.Writer.Complete();
        foreach (var channel in _channels)
            channel.Dispose();
    }

    [Fact]
    public void GetOrCreate_CreatesNewVehicle()
    {
        var registry = new VehicleRegistry();
        var channel = MakeChannel(registry);

        var vehicle = registry.GetOrCreate(42, 1, channel);

        Assert.Equal(42ul, vehicle.Uid);
        Assert.Equal(1, vehicle.SysId);
    }

    [Fact]
    public void GetOrCreate_ReturnsSameVehicle()
    {
        var registry = new VehicleRegistry();
        var channel = MakeChannel(registry);

        var vehicle1 = registry.GetOrCreate(42, 1, channel);
        var vehicle2 = registry.GetOrCreate(42, 1, channel);

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
        var channel = MakeChannel(registry);
        var added = registry.GetOrCreate(42, 1, channel);

        var found = registry.TryGet(42);

        Assert.Same(added, found);
    }

    [Fact]
    public void Vehicles_ReturnsAllRegistered()
    {
        var registry = new VehicleRegistry();
        var channel = MakeChannel(registry);
        registry.GetOrCreate(1, 1, channel);
        registry.GetOrCreate(2, 2, channel);

        var vehicles = registry.Vehicles.ToList();

        Assert.Equal(2, vehicles.Count);
    }

    private sealed class FakeConnection(Pipe pipe) : IConnection
    {
        public string Name => "Fake";
        public Stream BaseStream => pipe.Reader.AsStream();

        public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
