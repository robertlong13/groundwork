// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Connections;

/// <summary>
/// A persistent, protocol-agnostic byte stream connection.
/// Owns transport configuration and manages reconnection.
/// <see cref="BaseStream"/> may block during reconnection gaps.
/// </summary>
public interface IConnection : IAsyncDisposable
{
    /// <summary>
    /// Human-readable identifier for logging (e.g. "UDP:*:14550", "COM3:115200").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Opens the connection and starts receiving bytes. While open,
    /// transport-level failures are handled internally (reconnection).
    /// </summary>
    Task OpenAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes the connection. The <see cref="BaseStream"/> will return EOF.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);

    /// <summary>
    /// Readable byte stream of received data. Blocks until data is available.
    /// May block during reconnection gaps; returns EOF when the connection is
    /// closed or disposed.
    /// </summary>
    Stream BaseStream { get; }

    /// <summary>
    /// Sends bytes over the connection.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
