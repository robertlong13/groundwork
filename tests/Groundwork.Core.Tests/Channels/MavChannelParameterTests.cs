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

namespace Groundwork.Core.Tests.Channels;

public class MavChannelParameterTests : IDisposable
{
    private const byte VehicleSysId = 1;
    private const byte CompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

    private readonly FakeConnection _connection = new();
    private readonly MavChannel _channel;

    public MavChannelParameterTests()
    {
        var registry = new VehicleRegistry();
        _channel = new MavChannel(_connection, registry, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        // Complete the pipe writer first -- MavLinkParser.Dispose blocks until
        // ReadPacket returns, and ReadPacket does a blocking Stream.Read.
        _connection.Writer.Complete();
        _channel.Dispose();
    }

    // -- Fetch tests --

    [Fact]
    public async Task FetchParameterAsync_ReturnsValue_OnParamValue()
    {
        var fetchTask = _channel.FetchParameterAsync(
            VehicleSysId,
            "TEST_PARAM",
            perAttemptTimeout: TimeSpan.FromSeconds(5)
        );

        await InjectParamValueAsync("TEST_PARAM", 42f);

        var result = await fetchTask;
        Assert.Equal(42f, result);
    }

    [Fact]
    public async Task FetchParameterAsync_ThrowsTimeout_WhenNoResponse()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _channel.FetchParameterAsync(
                VehicleSysId,
                "MISSING_PARAM",
                perAttemptTimeout: TimeSpan.FromMilliseconds(50)
            )
        );
    }

    [Fact]
    public async Task FetchParameterAsync_ThrowsParameterException_OnParamError()
    {
        var fetchTask = _channel.FetchParameterAsync(
            VehicleSysId,
            "BAD_PARAM",
            perAttemptTimeout: TimeSpan.FromSeconds(5)
        );

        await InjectParamErrorAsync("BAD_PARAM", MAVLink.MAV_PARAM_ERROR.DOES_NOT_EXIST);

        var ex = await Assert.ThrowsAsync<ParameterException>(() => fetchTask);
        Assert.Equal("BAD_PARAM", ex.ParamName);
        Assert.Equal(MAVLink.MAV_PARAM_ERROR.DOES_NOT_EXIST, ex.Error);
    }

    // -- Set tests --

    [Fact]
    public async Task SetParameterAsync_ReturnsValue_WhenConfirmed()
    {
        var setTask = _channel.SetParameterAsync(
            VehicleSysId,
            "TEST_PARAM",
            42f,
            perAttemptTimeout: TimeSpan.FromSeconds(5)
        );

        await InjectParamValueAsync("TEST_PARAM", 42f);

        var result = await setTask;
        Assert.Equal(42f, result);
    }

    [Fact]
    public async Task SetParameterAsync_ThrowsParameterException_OnValueMismatch()
    {
        var setTask = _channel.SetParameterAsync(
            VehicleSysId,
            "CLAMPED_PARAM",
            100f,
            perAttemptTimeout: TimeSpan.FromMilliseconds(50)
        );

        // Autopilot clamps to 50 on every attempt.
        for (int i = 0; i < 3; i++)
            await InjectParamValueAsync("CLAMPED_PARAM", 50f);

        var ex = await Assert.ThrowsAsync<ParameterException>(() => setTask);
        Assert.Equal("CLAMPED_PARAM", ex.ParamName);
        Assert.Null(ex.Error);
    }

    [Fact]
    public async Task SetParameterAsync_ThrowsParameterException_OnParamError()
    {
        var setTask = _channel.SetParameterAsync(
            VehicleSysId,
            "READONLY_PARAM",
            1f,
            perAttemptTimeout: TimeSpan.FromSeconds(5)
        );

        await InjectParamErrorAsync("READONLY_PARAM", MAVLink.MAV_PARAM_ERROR.READ_ONLY);

        var ex = await Assert.ThrowsAsync<ParameterException>(() => setTask);
        Assert.Equal("READONLY_PARAM", ex.ParamName);
        Assert.Equal(MAVLink.MAV_PARAM_ERROR.READ_ONLY, ex.Error);
    }

    // -- Helpers --

    private async Task InjectParamValueAsync(string paramName, float value)
    {
        var pv = new MAVLink.mavlink_param_value_t
        {
            param_id = EncodeParamId(paramName),
            param_value = value,
            param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32,
        };

        await InjectPacketAsync(MAVLink.MAVLINK_MSG_ID.PARAM_VALUE, pv);
    }

    private async Task InjectParamErrorAsync(string paramName, MAVLink.MAV_PARAM_ERROR error)
    {
        var pe = new MAVLink.mavlink_param_error_t
        {
            param_id = EncodeParamId(paramName),
            error = (byte)error,
        };

        await InjectPacketAsync(MAVLink.MAVLINK_MSG_ID.PARAM_ERROR, pe);
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

    private static byte[] EncodeParamId(string name)
    {
        var id = new byte[16];
        Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, 16), id, 0);
        return id;
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
