// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Channels.Ftp;

/// <summary>
/// Provides MAVFtp file download over a <see cref="MavChannel"/>.
/// </summary>
/// <remarks>
/// Supports burst reads with hybrid gap-fill (response-driven + timer-driven).
/// </remarks>
public sealed class FtpClient
{
    private const int MaxInFlight = 5;
    private static readonly TimeSpan DefaultRetryTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(10);

    private readonly MavChannel _channel;
    private readonly byte _targetSysId;
    private readonly byte _targetCompId;
    private readonly ILogger _logger;
    private readonly TimeSpan _retryTimeout;
    private readonly TimeSpan _stallTimeout;
    private ushort _seqNumber;

    public FtpClient(
        MavChannel channel,
        byte targetSysId,
        ILogger logger,
        byte targetCompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1
    )
        : this(channel, targetSysId, logger, targetCompId, null, null) { }

    internal FtpClient(
        MavChannel channel,
        byte targetSysId,
        ILogger logger,
        byte targetCompId,
        TimeSpan? retryTimeout,
        TimeSpan? stallTimeout
    )
    {
        _channel = channel;
        _targetSysId = targetSysId;
        _targetCompId = targetCompId;
        _logger = logger;
        _retryTimeout = retryTimeout ?? DefaultRetryTimeout;
        _stallTimeout = stallTimeout ?? DefaultStallTimeout;
    }

    /// <summary>
    /// Downloads a file from the autopilot via MAVFtp.
    /// </summary>
    /// <param name="remotePath">The remote file path on the autopilot.</param>
    /// <param name="progress">Reports (bytesReceived, totalBytes) as data arrives.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="IOException">FTP protocol error or NAK.</exception>
    /// <exception cref="TimeoutException">Download stalled with no progress.</exception>
    public async Task<byte[]> DownloadFileAsync(
        string remotePath,
        IProgress<(int Received, int Total)>? progress = null,
        CancellationToken ct = default
    )
    {
        // Filter FTP responses from this target.
        var ftpResponses = _channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.FILE_TRANSFER_PROTOCOL
                && m.sysid == _targetSysId
            )
            .Select(m =>
            {
                var ftp = m.ToStructure<MAVLink.mavlink_file_transfer_protocol_t>();
                return FtpPayload.Unpack(ftp.payload);
            })
            .Do(r =>
                _logger.LogDebug(
                    "FTP rx: op={Op} req={Req} session={S} size={Sz} offset={Off} burst={B} err={Err}",
                    r.Opcode,
                    r.ReqOpcode,
                    r.Session,
                    r.Size,
                    r.Offset,
                    r.BurstComplete,
                    r.IsNak ? r.NakError : MAVLink.MAV_FTP_ERR.NONE
                )
            );

        // 1. Reset sessions.
        await ResetSessionsAsync(ftpResponses, ct).ConfigureAwait(false);

        // 2. Open file.
        var (session, fileSize) = await OpenFileAsync(ftpResponses, remotePath, ct)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "FTP: opened {Path}, session={Session}, size={Size}",
            remotePath,
            session,
            fileSize
        );

        var buffer = new byte[fileSize];
        var tracker = new RangeTracker();

        // 3. Burst read. Returns actual EOF offset if AP signals it.
        var actualSize = await BurstReadAsync(ftpResponses, session, buffer, tracker, progress, ct)
            .ConfigureAwait(false);
        if (actualSize < fileSize)
        {
            _logger.LogDebug(
                "FTP: AP EOF at {Actual}, adjusting from reported {Reported}",
                actualSize,
                fileSize
            );
            fileSize = actualSize;
        }

        // 4. Gap fill.
        if (!tracker.IsComplete(fileSize))
        {
            _logger.LogDebug(
                "FTP: burst complete, {Gaps} gaps remaining",
                tracker.GapCount(fileSize)
            );

            await GapFillAsync(ftpResponses, session, buffer, tracker, fileSize, progress, ct)
                .ConfigureAwait(false);
        }

        // Final progress with corrected total (AP may over-report in OpenFileRO).
        progress?.Report((fileSize, fileSize));

        _logger.LogDebug("FTP: download complete, {Size} bytes", fileSize);

        // 5. Terminate session (best-effort). Only on success -- on failure,
        // the next attempt's ResetSessions cleans up. Sending TerminateSession
        // on failure risks racing with the next attempt on high-latency links.
        try
        {
            await TerminateSessionAsync(ftpResponses, session, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FTP: terminate session failed (non-fatal)");
        }

        return buffer[..fileSize];
    }

    private async Task ResetSessionsAsync(IObservable<FtpResponse> responses, CancellationToken ct)
    {
        var resp = await SendWithRetryAsync(
                responses,
                MAVLink.MAV_FTP_OPCODE.RESETSESSION,
                r => r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.RESETSESSION,
                () => SendFtpAsync(MAVLink.MAV_FTP_OPCODE.RESETSESSION, 0, 0, 0, ct: ct),
                ct
            )
            .ConfigureAwait(false);

        if (resp.IsNak && resp.NakError != MAVLink.MAV_FTP_ERR.NONE)
            throw new IOException($"FTP ResetSessions NAK: {resp.NakError}");
    }

    private async Task<(byte Session, int FileSize)> OpenFileAsync(
        IObservable<FtpResponse> responses,
        string path,
        CancellationToken ct
    )
    {
        var pathBytes = FtpPayload.EncodePath(path);
        var resp = await SendWithRetryAsync(
                responses,
                MAVLink.MAV_FTP_OPCODE.OPENFILERO,
                r => r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.OPENFILERO,
                () =>
                    SendFtpAsync(
                        MAVLink.MAV_FTP_OPCODE.OPENFILERO,
                        0,
                        (byte)pathBytes.Length,
                        0,
                        pathBytes,
                        ct
                    ),
                ct,
                perAttemptTimeout: TimeSpan.FromSeconds(5)
            )
            .ConfigureAwait(false);

        if (resp.IsNak)
            throw new IOException($"FTP OpenFileRO NAK: {resp.NakError}");

        // ACK data: 4-byte file size (LE).
        if (resp.Size < 4)
            throw new IOException("FTP OpenFileRO: ACK missing file size");

        int fileSize = (int)
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(resp.Data);
        return (resp.Session, fileSize);
    }

    private async Task<int> BurstReadAsync(
        IObservable<FtpResponse> responses,
        byte session,
        byte[] buffer,
        RangeTracker tracker,
        IProgress<(int Received, int Total)>? progress,
        CancellationToken ct
    )
    {
        var burstDone = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int eofOffset = buffer.Length;

        using var registration = ct.Register(() => burstDone.TrySetCanceled(ct));

        using var sub = responses
            .Where(r =>
                r.Session == session
                && (
                    r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.BURSTREADFILE
                    || r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.READFILE
                )
            )
            .Subscribe(
                r =>
                {
                    if (r.IsAck && r.Size > 0)
                    {
                        var offset = (int)r.Offset;
                        var length = Math.Min(r.Size, buffer.Length - offset);
                        if (offset >= 0 && offset < buffer.Length && length > 0)
                        {
                            r.Data[..length].CopyTo(buffer.AsSpan(offset));
                            tracker.MarkReceived(offset, length);
                            progress?.Report((tracker.TotalReceived, buffer.Length));
                        }
                    }

                    // Burst ends on burst_complete flag or any NAK.
                    if (r.BurstComplete || r.IsNak)
                    {
                        if (r.IsNak && r.NakError == MAVLink.MAV_FTP_ERR.EOF)
                            Volatile.Write(ref eofOffset, (int)r.Offset);
                        burstDone.TrySetResult();
                    }
                },
                ex => burstDone.TrySetException(ex)
            );

        // Initiate burst read from offset 0.
        await SendFtpAsync(
                MAVLink.MAV_FTP_OPCODE.BURSTREADFILE,
                session,
                (byte)FtpPayload.MaxDataLength,
                0,
                ct: ct
            )
            .ConfigureAwait(false);

        // Wait for burst to complete or timeout.
        using var burstTimeout = new CancellationTokenSource(_retryTimeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            burstTimeout.Token
        );

        try
        {
            await burstDone.Task.WaitAsync(combined.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (burstTimeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Burst timed out -- proceed to gap fill with whatever we received.
            _logger.LogDebug("FTP: burst read timed out, proceeding to gap fill");
        }

        return eofOffset;
    }

    private async Task GapFillAsync(
        IObservable<FtpResponse> responses,
        byte session,
        byte[] buffer,
        RangeTracker tracker,
        int fileSize,
        IProgress<(int Received, int Total)>? progress,
        CancellationToken ct
    )
    {
        var completionSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        using var registration = ct.Register(() => completionSource.TrySetCanceled(ct));

        // Track offsets with requests in flight to avoid duplicate sends.
        var pending = new ConcurrentDictionary<int, byte>();
        int cursor = 0;

        using var sub = responses
            .Where(r =>
                r.Session == session
                && (
                    r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.READFILE
                    || r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.BURSTREADFILE
                )
            )
            .Subscribe(
                r =>
                {
                    if (r.IsAck && r.Size > 0)
                    {
                        var offset = (int)r.Offset;
                        var length = Math.Min(r.Size, buffer.Length - offset);
                        if (offset >= 0 && offset < buffer.Length && length > 0)
                        {
                            r.Data[..length].CopyTo(buffer.AsSpan(offset));
                            tracker.MarkReceived(offset, length);
                            progress?.Report((tracker.TotalReceived, fileSize));
                        }
                    }

                    pending.TryRemove((int)r.Offset, out _);

                    if (tracker.IsComplete(fileSize))
                    {
                        completionSource.TrySetResult();
                        return;
                    }

                    // Response-driven: immediately send the next gap request.
                    if (!ct.IsCancellationRequested)
                        SendGapBatch(session, tracker, fileSize, pending, ref cursor, ct);
                },
                ex => completionSource.TrySetException(ex)
            );

        // Send initial batch of gap requests.
        SendGapBatch(session, tracker, fileSize, pending, ref cursor, ct);

        // Timer-driven: every tick, send more gap requests.
        // If no new data arrives within the stall timeout, abandon.
        using var timer = new PeriodicTimer(_retryTimeout);
        var lastProgressTime = Environment.TickCount64;
        int lastProgress = tracker.TotalReceived;

        while (!completionSource.Task.IsCompleted)
        {
            try
            {
                var timerTask = timer.WaitForNextTickAsync(ct).AsTask();
                await Task.WhenAny(completionSource.Task, timerTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            if (completionSource.Task.IsCompleted || ct.IsCancellationRequested)
                break;

            int currentProgress = tracker.TotalReceived;
            if (currentProgress > lastProgress)
            {
                lastProgress = currentProgress;
                lastProgressTime = Environment.TickCount64;
            }
            else if (
                Environment.TickCount64 - lastProgressTime
                > (long)_stallTimeout.TotalMilliseconds
            )
            {
                throw new TimeoutException(
                    $"FTP gap fill stalled for {_stallTimeout.TotalSeconds}s"
                        + $" ({tracker.TotalReceived}/{fileSize} bytes,"
                        + $" {tracker.GapCount(fileSize)} gaps remaining)"
                );
            }

            // Clear pending so dropped requests can be re-sent.
            pending.Clear();
            SendGapBatch(session, tracker, fileSize, pending, ref cursor, ct);
        }

        await completionSource.Task.ConfigureAwait(false);
    }

    private void SendGapBatch(
        byte session,
        RangeTracker tracker,
        int fileSize,
        ConcurrentDictionary<int, byte> pending,
        ref int cursor,
        CancellationToken ct
    )
    {
        // Snapshot gaps to avoid iterating RangeTracker while it may be
        // mutated by a response arriving on the parser thread.
        foreach (var (offset, length) in tracker.EnumerateGapsFrom(cursor, fileSize).ToList())
        {
            if (pending.Count >= MaxInFlight)
                break;

            // Chunk large gaps into MaxDataLength-sized reads.
            var pos = offset;
            var end = offset + length;

            while (pos < end && pending.Count < MaxInFlight)
            {
                if (!pending.TryAdd(pos, 0))
                {
                    pos += FtpPayload.MaxDataLength;
                    continue;
                }

                var chunkSize = (byte)Math.Min(FtpPayload.MaxDataLength, end - pos);

                _ = SendFtpAsync(
                    MAVLink.MAV_FTP_OPCODE.READFILE,
                    session,
                    chunkSize,
                    (uint)pos,
                    ct: ct
                );

                cursor = pos + chunkSize;
                pos += chunkSize;
            }
        }
    }

    private async Task TerminateSessionAsync(
        IObservable<FtpResponse> responses,
        byte session,
        CancellationToken ct
    )
    {
        var ackTask = responses
            .Where(r => r.ReqOpcode == MAVLink.MAV_FTP_OPCODE.TERMINATESESSION)
            .Take(1)
            .Timeout(TimeSpan.FromSeconds(5))
            .ToTask(ct);

        await SendFtpAsync(MAVLink.MAV_FTP_OPCODE.TERMINATESESSION, session, 0, 0, ct: ct)
            .ConfigureAwait(false);

        await ackTask.ConfigureAwait(false);
    }

    private async Task<FtpResponse> SendWithRetryAsync(
        IObservable<FtpResponse> responses,
        MAVLink.MAV_FTP_OPCODE opcode,
        Func<FtpResponse, bool> filter,
        Func<Task> send,
        CancellationToken ct,
        int maxAttempts = 3,
        TimeSpan? perAttemptTimeout = null
    )
    {
        var timeout = perAttemptTimeout ?? TimeSpan.FromSeconds(3);

        for (var attempt = 1; ; attempt++)
        {
            var ackTask = responses.Where(filter).Take(1).Timeout(timeout).ToTask(ct);
            await send().ConfigureAwait(false);

            try
            {
                return await ackTask.ConfigureAwait(false);
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                _logger.LogDebug(
                    "FTP: {Op} attempt {Attempt} timed out, retrying",
                    opcode,
                    attempt
                );
            }
        }
    }

    private Task SendFtpAsync(
        MAVLink.MAV_FTP_OPCODE opcode,
        byte session,
        byte size,
        uint offset,
        byte[]? data = null,
        CancellationToken ct = default
    )
    {
        var payload = FtpPayload.Pack(
            _seqNumber++,
            session,
            opcode,
            size,
            offset,
            data ?? ReadOnlySpan<byte>.Empty
        );

        var msg = new MAVLink.mavlink_file_transfer_protocol_t
        {
            target_network = 0,
            target_system = _targetSysId,
            target_component = _targetCompId,
            payload = payload,
        };

        return _channel.SendAsync(MAVLink.MAVLINK_MSG_ID.FILE_TRANSFER_PROTOCOL, msg, ct);
    }
}
