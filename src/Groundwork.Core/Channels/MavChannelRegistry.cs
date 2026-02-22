// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Channels;

/// <summary>
/// TODO: Registry of all active <see cref="MavChannel"/>s. Currently
/// unused -- placeholder for multi-connection. Console's LinkManager
/// fills this role for now. This becomes the Core-level single source
/// of truth when routing, failover, and GUI all need a global channel
/// view.
/// </summary>
public class MavChannelRegistry
{
    private readonly List<MavChannel> _channels = new();

    public void Add(MavChannel channel) => _channels.Add(channel);

    public void Remove(MavChannel channel) => _channels.Remove(channel);

    public IReadOnlyList<MavChannel> Channels => _channels;
}
