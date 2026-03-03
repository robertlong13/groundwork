// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Buffers.Binary;
using System.IO.Pipelines;
using Groundwork.Core.Channels;
using Groundwork.Core.Channels.Ftp;
using Groundwork.Core.Connections;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groundwork.Core.Tests.Channels.Ftp;

public class FtpClientTests : IDisposable
{
    private const byte VehicleSysId = 1;
    private const byte CompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

    private readonly FakeConnection _connection = new();
    private readonly MavChannel _channel;

    public FtpClientTests()
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
    public async Task DownloadFileAsync_DownloadsSmallFile()
    {
        var fileData = new byte[100];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);

        var downloadTask = client.DownloadFileAsync("@PARAM/param.pck");

        // Respond to ResetSessions.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // Respond to OpenFileRO with file size.
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fileData.Length);
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 1,
            offset: 0,
            data: sizeBytes
        );

        // Respond to BurstReadFile with the entire file in one chunk.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: fileData,
            burstComplete: true
        );

        // Respond to TerminateSession.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.TERMINATESESSION,
            session: 1,
            offset: 0
        );

        var result = await downloadTask;
        Assert.Equal(fileData, result);
    }

    [Fact]
    public async Task DownloadFileAsync_HandlesMultipleChunks()
    {
        var fileData = new byte[400];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);

        var downloadTask = client.DownloadFileAsync("test.bin");

        // ResetSessions ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // OpenFileRO ACK with file size.
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fileData.Length);
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 1,
            offset: 0,
            data: sizeBytes
        );

        // Send two burst chunks (239 + 161 bytes).
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: fileData[..239]
        );

        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 239,
            data: fileData[239..],
            burstComplete: true
        );

        // TerminateSession ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.TERMINATESESSION,
            session: 1,
            offset: 0
        );

        var result = await downloadTask;
        Assert.Equal(fileData, result);
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsOnOpenNak()
    {
        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);

        var downloadTask = client.DownloadFileAsync("nonexistent.txt");

        // ResetSessions ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // OpenFileRO NAK: file not found.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 0,
            offset: 0,
            data: [(byte)MAVLink.MAV_FTP_ERR.FILENOTFOUND]
        );

        await Assert.ThrowsAsync<IOException>(() => downloadTask);
    }

    // -- Helpers --

    private async Task InjectFtpResponseAsync(
        MAVLink.MAV_FTP_OPCODE opcode,
        MAVLink.MAV_FTP_OPCODE reqOpcode,
        byte session,
        uint offset,
        byte[]? data = null,
        bool burstComplete = false
    )
    {
        var payload = new byte[251];
        // seq_number at 0-1 (don't care for responses in these tests).
        payload[2] = session;
        payload[3] = (byte)opcode;
        payload[4] = (byte)(data?.Length ?? 0);
        payload[5] = (byte)reqOpcode;
        payload[6] = burstComplete ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), offset);

        if (data is { Length: > 0 })
            data.AsSpan(0, Math.Min(data.Length, 239)).CopyTo(payload.AsSpan(12));

        var msg = new MAVLink.mavlink_file_transfer_protocol_t
        {
            target_network = 0,
            target_system = 255, // Targeting GCS.
            target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER,
            payload = payload,
        };

        var generator = new MAVLink.MavlinkParse();
        var packet = generator.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.FILE_TRANSFER_PROTOCOL,
            msg,
            sysid: VehicleSysId,
            compid: CompId
        );

        await _connection.Writer.WriteAsync(packet);
        await _connection.Writer.FlushAsync();
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
