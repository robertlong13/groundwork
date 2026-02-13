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
    private readonly Subject<MAVLink.MAVLinkMessage> _messages = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _parseLoop;

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

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _parseLoop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _messages.OnCompleted();
        _cts.Dispose();
        _messages.Dispose();
    }

    private async Task RunParseLoopAsync(Stream stream, CancellationToken ct)
    {
        var parser = new MAVLink.MavlinkParse();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = parser.ReadPacket(stream);

                if (message is null)
                    continue;

                if (ct.IsCancellationRequested)
                    break;

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
                    parser.badCRC,
                    parser.badLength
                );
            }
        }
    }
}
