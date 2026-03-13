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
using System.Text;
using Groundwork.Core.Channels;
using Groundwork.Core.Connections;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public async Task AddChannel_PopulatesParameters_FromBackgroundParamValue()
    {
        var pipe = new Pipe();
        var connection = new FakeConnection(pipe);
        var registry = new VehicleRegistry();
        using var channel = new MavChannel(connection, registry, NullLoggerFactory.Instance);

        var vehicle = new Vehicle(
            uid: 1,
            sysId: SysId,
            channel: channel,
            loggerFactory: NullLoggerFactory.Instance
        );

        // Inject a PARAM_VALUE packet as if the autopilot sent it unprompted.
        var pv = new MAVLink.mavlink_param_value_t
        {
            param_id = EncodeParamId("STAT_RUNTIME"),
            param_value = 12345f,
            param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32,
        };

        var generator = new MAVLink.MavlinkParse();
        var packet = generator.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.PARAM_VALUE,
            pv,
            sysid: SysId,
            compid: (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1
        );

        await pipe.Writer.WriteAsync(packet);
        await pipe.Writer.FlushAsync();
        await Task.Delay(50);

        Assert.True(vehicle.Parameters.ContainsKey("STAT_RUNTIME"));
        Assert.Equal(12345f, vehicle.Parameters["STAT_RUNTIME"].Value);

        // Complete writer before channel dispose -- MavLinkParser.Dispose blocks
        // until ReadPacket returns, which needs EOF on the stream.
        pipe.Writer.Complete();
    }

    [Theory]
    [InlineData(null, (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1)]
    [InlineData((byte)0, (byte)0)]
    [InlineData((byte)1, (byte)1)]
    public async Task SendCommandAsync_TargetComponent_FlowsToWire(
        byte? targetComponent,
        byte expectedComponent
    )
    {
        var pipe = new Pipe();
        var connection = new CapturingConnection(pipe);
        var registry = new VehicleRegistry();
        using var channel = new MavChannel(connection, registry, NullLoggerFactory.Instance);

        var vehicle = new Vehicle(
            uid: 1,
            sysId: SysId,
            channel: channel,
            loggerFactory: NullLoggerFactory.Instance
        );

        // Start the command -- it will timeout waiting for an ACK, but we
        // only care about the outgoing packet content.
        var cmdTask = vehicle.SendCommandAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            param1: 0f,
            targetComponent: targetComponent,
            timeout: TimeSpan.FromMilliseconds(200)
        );

        await Assert.ThrowsAsync<TimeoutException>(() => cmdTask);

        // Parse the captured outgoing COMMAND_LONG.
        Assert.Single(connection.SentPackets);
        using var ms = new MemoryStream(connection.SentPackets[0]);
        var parsed = new MAVLink.MavlinkParse().ReadPacket(ms);
        Assert.NotNull(parsed);

        var cmd = parsed.ToStructure<MAVLink.mavlink_command_long_t>();
        Assert.Equal(expectedComponent, cmd.target_component);
        Assert.Equal((ushort)MAVLink.MAV_CMD.COMPONENT_ARM_DISARM, cmd.command);

        pipe.Writer.Complete();
    }

    private static byte[] EncodeParamId(string name)
    {
        var id = new byte[16];
        Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, 16), id, 0);
        return id;
    }

    private sealed class FakeConnection(Pipe pipe) : IConnection
    {
        public string Name => "Fake";
        public Stream BaseStream => pipe.Reader.AsStream();

        public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken ct = default)
        {
            pipe.Writer.Complete();
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            pipe.Writer.Complete();
            pipe.Reader.Complete();
            return default;
        }
    }

    private sealed class CapturingConnection(Pipe pipe) : IConnection
    {
        public string Name => "Capturing";
        public Stream BaseStream => pipe.Reader.AsStream();
        public List<byte[]> SentPackets { get; } = [];

        public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken ct = default)
        {
            pipe.Writer.Complete();
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SentPackets.Add(data.ToArray());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            pipe.Writer.Complete();
            pipe.Reader.Complete();
            return default;
        }
    }
}
