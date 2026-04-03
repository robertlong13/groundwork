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

namespace Groundwork.Core.Channels;

/// <summary>
/// Writes MAVLink packets from one or more channels to a tlog file.
/// </summary>
public sealed class TlogWriter : IDisposable
{
    private readonly FileStream _file;
    private readonly Dictionary<MavChannel, (IDisposable Rx, IDisposable Tx)> _subscriptions =
        new();
    private readonly Lock _lock = new();

    public TlogWriter(string path)
    {
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    /// <summary>
    /// Subscribes to a channel's inbound and outbound packet streams.
    /// </summary>
    public void AddChannel(MavChannel channel)
    {
        var rx = channel.Messages.Subscribe(msg => WritePacket(msg.buffer));
        var tx = channel.TxPackets.Subscribe(WritePacket);

        lock (_lock)
        {
            _subscriptions[channel] = (rx, tx);
        }
    }

    /// <summary>
    /// Unsubscribes from a channel's packet streams.
    /// </summary>
    public void RemoveChannel(MavChannel channel)
    {
        (IDisposable Rx, IDisposable Tx) subs;

        lock (_lock)
        {
            if (!_subscriptions.Remove(channel, out subs))
                return;
        }

        subs.Rx.Dispose();
        subs.Tx.Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (rx, tx) in _subscriptions.Values)
            {
                rx.Dispose();
                tx.Dispose();
            }

            _subscriptions.Clear();
            _file.Flush();
            _file.Dispose();
        }
    }

    private void WritePacket(byte[] packet)
    {
        if (packet is null || packet.Length == 0)
            return;

        // tlog timestamps are microseconds since epoch (big-endian uint64).
        // Millisecond-aligned (trailing 000) -- matches Mission Planner.
        Span<byte> timestamp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(
            timestamp,
            (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000)
        );

        lock (_lock)
        {
            _file.Write(timestamp);
            _file.Write(packet);
        }
    }
}
