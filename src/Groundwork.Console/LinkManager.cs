// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Channels;
using Groundwork.Core.Connections;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Console;

/// <summary>
/// Provides lifecycle management for connection and channel pairs.
/// </summary>
// TODO: This class currently doubles as a channel registry (Count,
// Channels, GetChannel) because nothing else fills that role yet.
// When multi-connection lands, MavChannelRegistry in Core should
// become the single source of truth for "what channels exist", and
// LinkManager should shrink to just orchestration: parse descriptor,
// open, create channel, register with Core, tear down in the right
// order.
public sealed class LinkManager : IAsyncDisposable
{
    private readonly List<(IConnection Connection, MavChannel Channel)> _links = new();
    private readonly VehicleRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;

    public LinkManager(VehicleRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
    }

    public int Count => _links.Count;

    public MavChannel GetChannel(int index) => _links[index].Channel;

    public IEnumerable<MavChannel> Channels => _links.Select(static l => l.Channel);

    /// <summary>
    /// Parses a connection descriptor, opens the connection, wraps it in
    /// a <see cref="MavChannel"/>, and starts heartbeating for live links.
    /// </summary>
    /// <param name="descriptor">MAVProxy-style connection string (e.g., "udpin:14550").</param>
    /// <returns>The created <see cref="MavChannel"/>.</returns>
    /// <exception cref="FormatException">The descriptor is not a recognized format.</exception>
    public async Task<MavChannel> AddAsync(string descriptor, CancellationToken ct = default)
    {
        var connection = ConnectionString.Parse(descriptor, _loggerFactory);

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var channel = new MavChannel(connection, _registry, _loggerFactory);

        if (connection is not TlogConnection)
            channel.StartHeartbeat();

        _links.Add((connection, channel));
        return channel;
    }

    /// <summary>
    /// Removes and disposes the link at the given index.
    /// </summary>
    /// <param name="index">Zero-based index of the link to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public async Task RemoveAsync(int index)
    {
        if (index < 0 || index >= _links.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var (connection, channel) = _links[index];
        _links.RemoveAt(index);

        // Connection first: closing the connection EOFs the pipe stream,
        // which unblocks the parser's synchronous ReadPacket. If the
        // channel is disposed first, its parser join deadlocks because
        // the stream is still open.
        await connection.DisposeAsync().ConfigureAwait(false);
        channel.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        // Connection before channel -- see RemoveAsync comment.
        for (var i = _links.Count - 1; i >= 0; i--)
        {
            await _links[i].Connection.DisposeAsync().ConfigureAwait(false);
            _links[i].Channel.Dispose();
        }

        _links.Clear();
    }
}
