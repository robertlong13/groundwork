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

namespace Groundwork.Core.Reactive;

/// <summary>
/// Provides a registry of named observable streams.
/// </summary>
public sealed class StreamRegistry : IStreamRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _streams = new();

    public IObservable<T> Get<T>(string name)
    {
        if (!_streams.TryGetValue(name, out var entry))
            throw new KeyNotFoundException($"No stream registered with name '{name}'");

        return (IObservable<T>)entry.TypedStream;
    }

    public IObservable<object> Get(string name)
    {
        if (!_streams.TryGetValue(name, out var entry))
            throw new KeyNotFoundException($"No stream registered with name '{name}'");

        return entry.UntypedStream.Value;
    }

    public void Register<T>(string name, IObservable<T> stream, string? description = null)
    {
        var info = new StreamInfo(name, typeof(T), description);
        var entry = new Entry(
            stream,
            new Lazy<IObservable<object>>(() => stream.Select(x => (object)x!)),
            info
        );
        _streams[name] = entry;
    }

    public bool Contains(string name) => _streams.ContainsKey(name);

    public IReadOnlyList<StreamInfo> Available => _streams.Values.Select(e => e.Info).ToList();

    private sealed record Entry(
        object TypedStream,
        Lazy<IObservable<object>> UntypedStream,
        StreamInfo Info
    );
}
