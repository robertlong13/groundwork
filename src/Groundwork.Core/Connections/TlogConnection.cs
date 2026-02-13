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
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Connections;

/// <summary>
/// Replays a MAVLink tlog file as a paced byte stream, preserving
/// original inter-message timing at configurable speed. Gaps longer
/// than <see cref="MaxGap"/> are clamped.
/// </summary>
public sealed class TlogConnection : IConnection
{
    /// <summary>
    /// Maximum inter-message delay. Gaps longer than this are clamped.
    /// </summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    private readonly string _filePath;
    private readonly double _speed;
    private readonly ILogger<TlogConnection> _logger;
    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _cts = new();

    private FileStream? _fileStream;
    private Task? _replayLoop;

    public TlogConnection(string filePath, ILogger<TlogConnection> logger, double speed = 1.0)
    {
        _filePath = filePath;
        _logger = logger;
        _speed = speed > 0 ? speed : 1.0;
    }

    public string Name => $"tlog:{Path.GetFileName(_filePath)}";

    public Stream BaseStream { get; private set; } = null!;

    public Task OpenAsync(CancellationToken ct = default)
    {
        _fileStream = File.OpenRead(_filePath);
        BaseStream = _pipe.Reader.AsStream();
        _logger.LogInformation("Replaying {File} at {Speed}x", Path.GetFileName(_filePath), _speed);
        _replayLoop = Task.Run(() => RunReplayLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        _cts.Cancel();

        if (_replayLoop is not null)
        {
            try
            {
                await _replayLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _fileStream?.Dispose();
        _logger.LogInformation("{Name} closed", Name);
    }

    // Silent drop -- a tlog acts like a dead uplink, not an unsupported operation.
    // Downstream timeout/retry logic handles it the same as a real comms failure.
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task RunReplayLoopAsync(CancellationToken ct)
    {
        var parser = new MAVLink.MavlinkParse(hasTimestamp: true);
        DateTime? previousTime = null;
        int lastBadCrc = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = parser.ReadPacket(_fileStream!);

                if (message is null)
                {
                    if (parser.badCRC > lastBadCrc)
                    {
                        _logger.LogWarning(
                            "{Name}: corrupt packet (bad CRC #{Count})",
                            Name,
                            parser.badCRC
                        );
                        lastBadCrc = parser.badCRC;
                    }

                    continue;
                }

                // Pace replay according to original timestamps.
                if (
                    previousTime.HasValue
                    && message.rxtime > previousTime.Value
                    && message.rxtime != DateTime.MinValue
                )
                {
                    var delay = (message.rxtime - previousTime.Value) / _speed;
                    if (delay > MaxGap)
                        delay = MaxGap;
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }

                if (message.rxtime != DateTime.MinValue)
                    previousTime = message.rxtime;

                // Write raw packet bytes to pipe (no timestamp).
                var memory = _pipe.Writer.GetMemory(message.buffer.Length);
                message.buffer.CopyTo(memory);
                _pipe.Writer.Advance(message.buffer.Length);
                var flushResult = await _pipe.Writer.FlushAsync(ct).ConfigureAwait(false);
                if (flushResult.IsCompleted)
                    break;
            }
        }
        catch (EndOfStreamException)
        {
            _logger.LogInformation("{Name}: replay complete", Name);
        }
        catch (TimeoutException)
        {
            // MavlinkParse.ReadWithTimeout throws on short reads at EOF.
            _logger.LogInformation("{Name}: replay complete", Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replay error on {Name}", Name);
        }
        finally
        {
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }
    }
}
