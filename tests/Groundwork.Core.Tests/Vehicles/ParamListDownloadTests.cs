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

public class ParamListDownloadTests : IDisposable
{
    private const byte VehicleSysId = 1;
    private const byte CompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

    private readonly FakeConnection _connection = new();
    private readonly MavChannel _channel;

    public ParamListDownloadTests()
    {
        var registry = new VehicleRegistry();
        _channel = new MavChannel(_connection, registry, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _connection.Writer.Complete();
        _channel.Dispose();
    }

    [Fact]
    public async Task CompletesWhenAllParamsReceived()
    {
        const int totalParams = 10;

        var downloadTask = ParamListDownload.DownloadAsync(
            _channel,
            VehicleSysId,
            NullLogger.Instance
        );

        for (ushort i = 0; i < totalParams; i++)
            await InjectParamValueAsync($"PARAM_{i}", i * 1.0f, i, totalParams);

        var count = await downloadTask;
        Assert.Equal(totalParams, count);
    }

    [Fact]
    public async Task GapFill_CompletesAfterMissingParamsArrive()
    {
        const int totalParams = 10;
        // Indices to skip in the initial flood -- they'll arrive after gap fill.
        var skipped = new HashSet<ushort> { 3, 7 };

        var downloadTask = ParamListDownload.DownloadAsync(
            _channel,
            VehicleSysId,
            NullLogger.Instance
        );

        // Send initial flood minus the skipped indices.
        for (ushort i = 0; i < totalParams; i++)
        {
            if (!skipped.Contains(i))
                await InjectParamValueAsync($"PARAM_{i}", i * 1.0f, i, totalParams);
        }

        // Wait for the stall timeout to trigger gap fill requests.
        await Task.Delay(3000);

        // Now send the missing params (simulating autopilot responding to gap fill).
        foreach (var idx in skipped)
            await InjectParamValueAsync($"PARAM_{idx}", idx * 1.0f, idx, totalParams);

        var count = await downloadTask;
        Assert.Equal(totalParams, count);
    }

    [Fact]
    public async Task ReportsProgress()
    {
        const int totalParams = 5;
        var reports = new List<ParamDownloadProgress>();
        var progress = new Progress<ParamDownloadProgress>(p => reports.Add(p));

        var downloadTask = ParamListDownload.DownloadAsync(
            _channel,
            VehicleSysId,
            NullLogger.Instance,
            progress
        );

        for (ushort i = 0; i < totalParams; i++)
            await InjectParamValueAsync($"PARAM_{i}", i * 1.0f, i, totalParams);

        await downloadTask;

        // Progress may arrive asynchronously via SynchronizationContext, give it a moment.
        await Task.Delay(100);

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.False(r.ViaFtp));

        var last = reports[^1];
        Assert.Equal(totalParams, last.Received);
        Assert.Equal(totalParams, last.Total);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();

        var downloadTask = ParamListDownload.DownloadAsync(
            _channel,
            VehicleSysId,
            NullLogger.Instance,
            ct: cts.Token
        );

        // Send a few params so it's in progress.
        for (ushort i = 0; i < 3; i++)
            await InjectParamValueAsync($"PARAM_{i}", i * 1.0f, i, 20);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask);
    }

    // -- Helpers --

    private async Task InjectParamValueAsync(string paramName, float value, ushort index, int total)
    {
        var pv = new MAVLink.mavlink_param_value_t
        {
            param_id = EncodeParamId(paramName),
            param_value = value,
            param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32,
            param_count = (ushort)total,
            param_index = index,
        };

        var generator = new MAVLink.MavlinkParse();
        var packet = generator.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.PARAM_VALUE,
            pv,
            sysid: VehicleSysId,
            compid: CompId
        );

        await _connection.Writer.WriteAsync(packet);
        await _connection.Writer.FlushAsync();
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
