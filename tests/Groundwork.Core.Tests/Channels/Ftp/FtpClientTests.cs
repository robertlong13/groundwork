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
using System.Threading.Channels;
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

        // EOF NAK signals end of file.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: (uint)fileData.Length,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
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

        // EOF NAK signals end of file.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: (uint)fileData.Length,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
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

    [Fact]
    public async Task DownloadFileAsync_GapFillRecoversDroppedPacket()
    {
        // 600 bytes across 3 burst packets. The middle packet (offset 239)
        // is dropped during the burst. Gap fill recovers it via READFILE.
        var fileData = new byte[600];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(
            _channel,
            VehicleSysId,
            NullLogger.Instance,
            targetCompId: CompId,
            retryTimeout: TimeSpan.FromMilliseconds(200),
            stallTimeout: null
        );

        var downloadTask = client.DownloadFileAsync("gappy.bin");

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

        // Burst: packet 1 and 3 arrive, packet 2 (offset 239) dropped.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: fileData[..239]
        );

        // Skip fileData[239..478] -- simulating a dropped packet.

        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 478,
            data: fileData[478..],
            burstComplete: true
        );

        // EOF NAK -- server thinks it sent everything, but we dropped a packet.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: (uint)fileData.Length,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
        );

        // Wait for gap fill to request the missing chunk, then respond.
        await WaitForFtpRequestAsync(MAVLink.MAV_FTP_OPCODE.READFILE, 239);

        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.READFILE,
            session: 1,
            offset: 239,
            data: fileData[239..478]
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
    public async Task DownloadFileAsync_ReportsProgress()
    {
        var fileData = new byte[100];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);
        var reports = new List<(int Received, int Total)>();
        var progress = new Progress<(int Received, int Total)>(r => reports.Add(r));

        var downloadTask = client.DownloadFileAsync("progress.bin", progress);

        // ResetSessions ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // OpenFileRO ACK.
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fileData.Length);
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 1,
            offset: 0,
            data: sizeBytes
        );

        // Single burst with all data, burst_complete ends burst phase.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: fileData,
            burstComplete: true
        );

        // EOF NAK signals end of file.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: (uint)fileData.Length,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
        );

        // TerminateSession ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.TERMINATESESSION,
            session: 1,
            offset: 0
        );

        await downloadTask;

        // Progress fires from burst data and from final completion.
        // Progress<T> posts to SynchronizationContext so allow a moment.
        await Task.Delay(50);
        Assert.Contains(reports, r => r.Received == fileData.Length && r.Total == fileData.Length);
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsOnGapFillStall()
    {
        var client = new FtpClient(
            _channel,
            VehicleSysId,
            NullLogger.Instance,
            targetCompId: CompId,
            retryTimeout: TimeSpan.FromMilliseconds(50),
            stallTimeout: TimeSpan.FromMilliseconds(200)
        );

        var downloadTask = client.DownloadFileAsync("gap_stall.bin");

        // ResetSessions ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // OpenFileRO ACK.
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, 500);
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 1,
            offset: 0,
            data: sizeBytes
        );

        // Burst: first chunk only, then EOF. Leaves a gap at [239, 500).
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: new byte[239],
            burstComplete: true
        );

        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 500,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
        );

        // No gap fill responses -- stall timeout should fire.
        await Assert.ThrowsAsync<TimeoutException>(() => downloadTask);
    }

    [Fact]
    public async Task DownloadFileAsync_CancellationStopsDownload()
    {
        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);
        using var cts = new CancellationTokenSource();

        var downloadTask = client.DownloadFileAsync("cancel.bin", ct: cts.Token);

        // ResetSessions ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // OpenFileRO ACK.
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, 1000);
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 1,
            offset: 0,
            data: sizeBytes
        );

        // Cancel mid-burst.
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask);
    }

    [Fact]
    public async Task DownloadFileAsync_ContinuesBurstAfterServerLimit()
    {
        // First burst sends 239 bytes with burst_complete (no EOF; client is
        // expected to trigger a continuation), client sends another burst,
        // server finishes with EOF.
        var fileData = new byte[400];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);

        var downloadTask = client.DownloadFileAsync("big.bin");

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

        // First burst: one packet + burst_complete, no EOF NAK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: fileData[..239],
            burstComplete: true
        );

        // Wait for the client to re-request a burst from offset 239.
        await WaitForFtpRequestAsync(MAVLink.MAV_FTP_OPCODE.BURSTREADFILE, 239);

        // Second burst: delivers the remainder.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 239,
            data: fileData[239..],
            burstComplete: true
        );

        // EOF NAK ends the download.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: (uint)fileData.Length,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
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
    public async Task DownloadFileAsync_HandlesExactMultipleBurstSize()
    {
        // 717 bytes = 3 x 239: ArduPilot bug where burst_complete is never
        // set and EOF NAK offset is one payload short (478 instead of 717).
        var fileData = new byte[239 * 3];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(_channel, VehicleSysId, NullLogger.Instance);

        var downloadTask = client.DownloadFileAsync("exact_multiple.bin");

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

        // Three full-sized burst packets, no burst_complete (AP bug).
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
            data: fileData[239..478]
        );

        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 478,
            data: fileData[478..]
        );

        // Buggy EOF NAK: offset is one payload short, no burst_complete.
        // High-water mark (717) overrides the bogus offset (478).
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 478,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
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
    public async Task DownloadFileAsync_TimeoutRetriesDroppedBurstComplete()
    {
        // Dropped burst_complete and EOF NAK. The burst timeout fires and
        // the loop retries from HighestReceived, recovering without stalling.
        var fileData = new byte[400];
        Random.Shared.NextBytes(fileData);

        var client = new FtpClient(
            _channel,
            VehicleSysId,
            NullLogger.Instance,
            targetCompId: CompId,
            retryTimeout: TimeSpan.FromMilliseconds(200),
            stallTimeout: null
        );

        var downloadTask = client.DownloadFileAsync("dropped.bin");

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

        // First burst: one packet, then silence (burst_complete + NAK dropped).
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: fileData[..239]
        );

        // Wait for the client to retry a burst from offset 239 after timeout.
        await WaitForFtpRequestAsync(MAVLink.MAV_FTP_OPCODE.BURSTREADFILE, 239);

        // Second burst (retry from offset 239): delivers the remainder.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 239,
            data: fileData[239..],
            burstComplete: true
        );

        // EOF NAK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.NAK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: (uint)fileData.Length,
            data: [(byte)MAVLink.MAV_FTP_ERR.EOF]
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
    public async Task DownloadFileAsync_ThrowsOnBurstStall()
    {
        var client = new FtpClient(
            _channel,
            VehicleSysId,
            NullLogger.Instance,
            targetCompId: CompId,
            retryTimeout: TimeSpan.FromMilliseconds(50),
            stallTimeout: TimeSpan.FromMilliseconds(2000)
        );

        var downloadTask = client.DownloadFileAsync("stall.bin");

        // ResetSessions ACK.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.RESETSESSION,
            session: 0,
            offset: 0
        );

        // OpenFileRO ACK.
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, 1000);
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.OPENFILERO,
            session: 1,
            offset: 0,
            data: sizeBytes
        );

        // One burst packet, then silence.
        await InjectFtpResponseAsync(
            MAVLink.MAV_FTP_OPCODE.ACK,
            MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
            session: 1,
            offset: 0,
            data: new byte[239]
        );

        // Verify the client retries a burst (proves the loop is active).
        await WaitForFtpRequestAsync(MAVLink.MAV_FTP_OPCODE.BURSTREADFILE, 239);

        // No more responses -- burst retries stall.
        await Assert.ThrowsAsync<TimeoutException>(() => downloadTask);
    }

    // -- Helpers --

    private async Task WaitForFtpRequestAsync(
        MAVLink.MAV_FTP_OPCODE expectedOpcode,
        uint expectedOffset
    )
    {
        var parser = new MAVLink.MavlinkParse();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (true)
        {
            var packet = await _connection.Sent.ReadAsync(cts.Token);
            var msg = parser.ReadPacket(new MemoryStream(packet));
            if (msg?.msgid != (uint)MAVLink.MAVLINK_MSG_ID.FILE_TRANSFER_PROTOCOL)
                continue;

            var ftp = FtpPayload.Unpack(
                msg.ToStructure<MAVLink.mavlink_file_transfer_protocol_t>().payload
            );

            if (ftp.Opcode == expectedOpcode && ftp.Offset == expectedOffset)
                return;
        }
    }

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
        private readonly Channel<byte[]> _sent = Channel.CreateUnbounded<byte[]>();

        public string Name => "Fake";
        public Stream BaseStream => _pipe.Reader.AsStream();
        public PipeWriter Writer => _pipe.Writer;
        public ChannelReader<byte[]> Sent => _sent.Reader;

        public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken ct = default)
        {
            _pipe.Writer.Complete();
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            _sent.Writer.TryWrite(data.ToArray());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            _sent.Writer.Complete();
            return default;
        }
    }
}
