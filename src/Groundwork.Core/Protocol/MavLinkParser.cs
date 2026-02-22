// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Protocol;

/// <summary>
/// Reads MAVLink packets from a byte stream using the pymavlink-generated
/// <see cref="MAVLink.MavlinkParse"/> and exposes parsed messages as
/// <see cref="IObservable{MAVLinkMessage}"/>.
/// </summary>
public sealed class MavLinkParser : IDisposable
{
    private readonly ILogger<MavLinkParser> _logger;
    private readonly MAVLink.MavlinkParse _parser = new();
    private readonly Subject<MAVLink.MAVLinkMessage> _messages = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _parseLoop;
    private long _totalMessages;

    public MavLinkParser(Stream source, ILogger<MavLinkParser> logger)
    {
        _logger = logger;
        _parseLoop = Task.Run(() => RunParseLoopAsync(source, _cts.Token));
    }

    /// <summary>
    /// Hot observable of parsed MAVLink messages that completes when the
    /// byte source completes or the parser is disposed.
    /// </summary>
    public IObservable<MAVLink.MAVLinkMessage> Messages => _messages;

    /// <summary>
    /// Gets the number of packets that failed CRC validation.
    /// </summary>
    public int BadCrc => _parser.badCRC;

    /// <summary>
    /// Gets the number of packets with invalid payload length.
    /// </summary>
    public int BadLength => _parser.badLength;

    /// <summary>
    /// Gets the total number of successfully parsed messages.
    /// </summary>
    public long TotalMessages => Interlocked.Read(ref _totalMessages);

    public void Dispose()
    {
        _cts.Cancel();

        // CAUTION: This synchronously blocks until ReadPacket returns.
        // ReadPacket does a blocking Stream.Read, so the stream MUST be
        // closed/completed before calling Dispose -- otherwise this
        // deadlocks.
        try
        {
            _parseLoop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
        _messages.Dispose();
    }

    private async Task RunParseLoopAsync(Stream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = _parser.ReadPacket(stream);

                if (message is null)
                    continue;

                if (ct.IsCancellationRequested)
                    break;

                Interlocked.Increment(ref _totalMessages);
                _messages.OnNext(message);
            }
            catch (EndOfStreamException)
            {
                _logger.LogDebug("End of stream -- byte source completed");
                break;
            }
            catch (TimeoutException)
            {
                // ReadWithTimeout throws this when Read() returns 0 on a
                // non-seekable stream (pipe). For pipe-backed connections,
                // this means the writer completed -- it's real EOF.
                _logger.LogDebug("End of stream -- byte source completed");
                break;
            }
            catch (InvalidOperationException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parse error (bad CRC: {BadCrc}, bad length: {BadLen})",
                    _parser.badCRC,
                    _parser.badLength
                );
            }
        }

        _messages.OnCompleted();
    }
}
