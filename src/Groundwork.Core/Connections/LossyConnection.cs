// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

#if LOSSY_LINK

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace Groundwork.Core.Connections;

/// <summary>
/// Provides an <see cref="IConnection"/> wrapper that injects byte-level
/// corruption, random send drops, symmetric latency, and independent
/// uplink/downlink kill switches.
/// </summary>
/// <remarks>
/// When <see cref="DownlinkDown"/> is set, inbound bytes are actively drained
/// and discarded so recovery sees fresh data, not a backlog. Latency is applied
/// symmetrically to both inbound and outbound data via fire-and-forget queues.
/// </remarks>
public sealed class LossyConnection : IConnection
{
    private readonly IConnection _inner;
    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<(long EnqueueTick, byte[] Data)> _rxQueue = new();
    private readonly ConcurrentQueue<(long EnqueueTick, ReadOnlyMemory<byte> Data)> _txQueue =
        new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private Task? _readLoop;
    private Task? _drainLoop;
    private double _corruptRate;
    private int _corruptThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="LossyConnection"/> class.
    /// </summary>
    /// <param name="inner">The connection to decorate.</param>
    /// <param name="sendDropRate">Probability (0..1) of dropping an outbound send.</param>
    /// <param name="corruptRate">Probability (0..1) of corrupting each inbound byte.</param>
    public LossyConnection(IConnection inner, double sendDropRate = 0.0, double corruptRate = 0.0)
    {
        _inner = inner;
        SendDropRate = sendDropRate;
        CorruptRate = corruptRate;
    }

    public string Name => $"Lossy({_inner.Name})";

    public Stream BaseStream { get; private set; } = null!;

    /// <summary>
    /// Gets or sets the probability (0..1) of dropping an outbound send.
    /// </summary>
    public double SendDropRate { get; set; }

    /// <summary>
    /// Gets or sets the probability (0..1) of corrupting each inbound byte.
    /// </summary>
    public double CorruptRate
    {
        get => _corruptRate;
        set
        {
            _corruptRate = value;
            _corruptThreshold = (int)(value * 1_000_000);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether outbound sends are suppressed.
    /// </summary>
    public bool UplinkDown { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether inbound bytes are drained and discarded.
    /// </summary>
    public bool DownlinkDown { get; set; }

    /// <summary>
    /// Gets or sets the symmetric one-way latency applied to both inbound and outbound data.
    /// Changing this value reschedules all queued entries immediately.
    /// </summary>
    public TimeSpan Latency
    {
        get => _latency;
        set
        {
            _latency = value;
            // Wake the drain loop so it recalculates with the new latency.
            _queueSignal.Release();
        }
    }

    private TimeSpan _latency;

    /// <summary>
    /// Sets <see cref="CorruptRate"/> to achieve a target packet loss probability.
    /// </summary>
    /// <param name="packetLossRate">Desired probability (0..1) that a packet contains at least one corrupted byte.</param>
    /// <param name="avgPacketSize">Average MAVLink packet size in bytes.</param>
    public void SetPacketLossRate(double packetLossRate, int avgPacketSize = 35)
    {
        CorruptRate = 1.0 - Math.Pow(1.0 - packetLossRate, 1.0 / avgPacketSize);
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        await _inner.OpenAsync(ct).ConfigureAwait(false);
        BaseStream = _pipe.Reader.AsStream();
        var token = _cts.Token;
        _readLoop = Task.Run(() => RunReadLoopAsync(token), CancellationToken.None);
        _drainLoop = Task.Run(() => RunDrainLoopAsync(token), CancellationToken.None);
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        _cts.Cancel();
        return _inner.CloseAsync(ct);
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (UplinkDown)
            return Task.CompletedTask;

        if (
            SendDropRate > 0
            && RandomNumberGenerator.GetInt32(0, 10000) < (int)(SendDropRate * 10000)
        )
            return Task.CompletedTask;

        var latency = Latency;
        if (latency > TimeSpan.Zero)
        {
            _txQueue.Enqueue((Environment.TickCount64, data.ToArray()));
            _queueSignal.Release();
            return Task.CompletedTask;
        }

        return _inner.SendAsync(data, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }
        if (_drainLoop is not null)
        {
            try
            {
                await _drainLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
        _queueSignal.Dispose();
        _cts.Dispose();
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads from the inner stream, applies corruption, and either writes
    /// directly to the pipe (no latency) or enqueues for delayed delivery.
    /// </summary>
    private async Task RunReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var innerStream = _inner.BaseStream;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await innerStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                if (DownlinkDown)
                    continue;

                var chunk = buffer.AsMemory(0, read);

                if (_corruptThreshold > 0)
                    CorruptBytes(chunk.Span);

                var latency = Latency;
                if (latency > TimeSpan.Zero)
                {
                    _rxQueue.Enqueue((Environment.TickCount64, chunk.ToArray()));
                    _queueSignal.Release();
                }
                else
                {
                    await WriteToPipeAsync(chunk, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>
    /// Drains both rx and tx delay queues, delivering entries when their
    /// due time arrives. Sleeps precisely until the next due entry or
    /// until signaled by a new enqueue.
    /// </summary>
    private async Task RunDrainLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = Environment.TickCount64;
                var latencyMs = (long)Latency.TotalMilliseconds;
                var nextDue = long.MaxValue;

                // Drain due rx entries.
                while (_rxQueue.TryPeek(out var rxEntry) && rxEntry.EnqueueTick + latencyMs <= now)
                {
                    if (_rxQueue.TryDequeue(out rxEntry) && !DownlinkDown)
                        await WriteToPipeAsync(rxEntry.Data, ct).ConfigureAwait(false);
                }

                if (_rxQueue.TryPeek(out var rxNext))
                    nextDue = Math.Min(nextDue, rxNext.EnqueueTick + latencyMs);

                // Drain due tx entries.
                while (_txQueue.TryPeek(out var txEntry) && txEntry.EnqueueTick + latencyMs <= now)
                {
                    if (_txQueue.TryDequeue(out txEntry) && !UplinkDown)
                        await _inner.SendAsync(txEntry.Data, ct).ConfigureAwait(false);
                }

                if (_txQueue.TryPeek(out var txNext))
                    nextDue = Math.Min(nextDue, txNext.EnqueueTick + latencyMs);

                // Sleep until next due entry or new enqueue signal.
                if (nextDue == long.MaxValue)
                {
                    await _queueSignal.WaitAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    var waitMs = (int)Math.Max(0, nextDue - Environment.TickCount64);
                    await _queueSignal.WaitAsync(waitMs, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async ValueTask WriteToPipeAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var dest = _pipe.Writer.GetMemory(data.Length);
        data.CopyTo(dest);
        _pipe.Writer.Advance(data.Length);
        var flush = await _pipe.Writer.FlushAsync(ct).ConfigureAwait(false);
        if (flush.IsCompleted)
            throw new OperationCanceledException();
    }

    private void CorruptBytes(Span<byte> data)
    {
        int threshold = _corruptThreshold;
        for (int i = 0; i < data.Length; i++)
        {
            if (RandomNumberGenerator.GetInt32(0, 1_000_000) < threshold)
                data[i] ^= (byte)(1 << RandomNumberGenerator.GetInt32(0, 8));
        }
    }
}

#endif
