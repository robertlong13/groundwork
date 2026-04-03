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
/// Provides the single source of truth for all active <see cref="MavChannel"/>s.
/// </summary>
public class MavChannelRegistry
{
    private readonly List<MavChannel> _channels = new();
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _channels.Count;
            }
        }
    }

    public int IndexOf(MavChannel channel)
    {
        lock (_lock)
        {
            return _channels.IndexOf(channel);
        }
    }

    public void Add(MavChannel channel)
    {
        lock (_lock)
        {
            _channels.Add(channel);
        }
    }

    public void Remove(MavChannel channel)
    {
        lock (_lock)
        {
            _channels.Remove(channel);
        }
    }

    /// <summary>
    /// Gets a snapshot of all active channels.
    /// </summary>
    public IReadOnlyList<MavChannel> Channels
    {
        get
        {
            lock (_lock)
            {
                return _channels.ToList();
            }
        }
    }
}
