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

namespace Groundwork.Core.Tests.Channels;

public class MavChannelCommandTests : IDisposable
{
    private const byte VehicleSysId = 1;
    private const byte CompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    private readonly FakeConnection _connection = new();
    private readonly MavChannel _channel;

    public MavChannelCommandTests()
    {
        var registry = new VehicleRegistry();
        _channel = new MavChannel(
            _connection,
            registry,
            NullLoggerFactory.Instance,
            commandRetryInterval: RetryInterval
        );
    }

    public void Dispose()
    {
        _connection.Writer.Complete();
        _channel.Dispose();
    }

    [Fact]
    public async Task SendCommandAsync_ReturnsResult_OnAck()
    {
        var commandTask = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(5)
        );

        await InjectCommandAckAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            MAVLink.MAV_RESULT.ACCEPTED
        );

        var result = await commandTask;
        Assert.Equal(MAVLink.MAV_RESULT.ACCEPTED, result);
    }

    [Fact]
    public async Task SendCommandAsync_Retries_WhenFirstAttemptTimesOut()
    {
        var commandTask = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(5)
        );

        // Let the first attempt time out before injecting the ACK.
        await Task.Delay(RetryInterval * 3);

        await InjectCommandAckAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            MAVLink.MAV_RESULT.ACCEPTED
        );

        var result = await commandTask;
        Assert.Equal(MAVLink.MAV_RESULT.ACCEPTED, result);
    }

    [Fact]
    public async Task SendCommandAsync_ThrowsTimeout_WhenNoAck()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _channel.SendCommandAsync(
                VehicleSysId,
                CompId,
                MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
                timeout: TimeSpan.FromMilliseconds(120)
            )
        );
    }

    [Fact]
    public async Task SendCommandAsync_Serializes_SameCommand()
    {
        // Start two concurrent calls for the same command.
        var task1 = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(5)
        );
        var task2 = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(5)
        );

        // task1 holds the semaphore; task2 is blocked waiting for it.
        await Task.Delay(50);
        Assert.False(task2.IsCompleted);

        // One ACK releases task1 and frees the semaphore for task2.
        await InjectCommandAckAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            MAVLink.MAV_RESULT.ACCEPTED
        );
        await task1;

        // Give task2 time to acquire the semaphore and subscribe before injecting.
        await Task.Delay(20);
        Assert.False(task2.IsCompleted);

        await InjectCommandAckAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            MAVLink.MAV_RESULT.ACCEPTED
        );
        Assert.Equal(MAVLink.MAV_RESULT.ACCEPTED, await task2);
    }

    [Fact]
    public async Task SendCommandAsync_DoesNotBlock_DifferentCommands()
    {
        var task1 = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(5)
        );
        var task2 = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.DO_SET_MODE,
            timeout: TimeSpan.FromSeconds(5)
        );

        // Both should be in-flight simultaneously.
        await Task.Delay(50);
        Assert.False(task1.IsCompleted);
        Assert.False(task2.IsCompleted);

        await InjectCommandAckAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            MAVLink.MAV_RESULT.ACCEPTED
        );
        await task1;

        await InjectCommandAckAsync(MAVLink.MAV_CMD.DO_SET_MODE, MAVLink.MAV_RESULT.ACCEPTED);
        await task2;
    }

    [Fact]
    public async Task SendCommandAsync_ThrowsTimeout_WhenBlockedOnSemaphore()
    {
        // Hold the semaphore with a long-running command.
        using var blockerCts = new CancellationTokenSource();
        var blocker = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(30),
            ct: blockerCts.Token
        );

        await Task.Delay(50); // Let blocker acquire the semaphore.

        // Short overall timeout: should expire while waiting for the semaphore.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _channel.SendCommandAsync(
                VehicleSysId,
                CompId,
                MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
                timeout: TimeSpan.FromMilliseconds(100)
            )
        );

        blockerCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocker);
    }

    [Fact]
    public async Task SendCommandAsync_ThrowsOperationCanceled_OnCancellation()
    {
        using var cts = new CancellationTokenSource();
        var commandTask = _channel.SendCommandAsync(
            VehicleSysId,
            CompId,
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            timeout: TimeSpan.FromSeconds(5),
            ct: cts.Token
        );

        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => commandTask);
    }

    // -- Helpers --

    private async Task InjectCommandAckAsync(MAVLink.MAV_CMD command, MAVLink.MAV_RESULT result)
    {
        var ack = new MAVLink.mavlink_command_ack_t
        {
            command = (ushort)command,
            result = (byte)result,
        };

        await InjectPacketAsync(MAVLink.MAVLINK_MSG_ID.COMMAND_ACK, ack);
    }

    private async Task InjectPacketAsync(MAVLink.MAVLINK_MSG_ID msgId, object data)
    {
        var generator = new MAVLink.MavlinkParse();
        var packet = generator.GenerateMAVLinkPacket20(
            msgId,
            data,
            sysid: VehicleSysId,
            compid: CompId
        );

        await _connection.Writer.WriteAsync(packet);
        await _connection.Writer.FlushAsync();

        // Give the parser background task time to read and dispatch.
        await Task.Delay(50);
    }

    private sealed class FakeConnection : IConnection
    {
        private readonly Pipe _pipe = new();

        public string Name => "Fake";
        public Stream BaseStream => _pipe.Reader.AsStream();
        public PipeWriter Writer => _pipe.Writer;

        public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken ct = default)
        {
            _pipe.Writer.Complete();
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            return default;
        }
    }
}
