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

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Provides a shared cache of parameter metadata, keyed by an opaque string
/// supplied by the fetcher (e.g. "Copter-4.6"). Thread-safe.
/// </summary>
public sealed class ParamMetadataCache
{
    private readonly ConcurrentDictionary<
        string,
        IReadOnlyDictionary<string, ParamMetadata>
    > _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets cached metadata for a firmware key, or <see langword="null"/> if not loaded.
    /// </summary>
    public IReadOnlyDictionary<string, ParamMetadata>? Get(string key) =>
        _cache.TryGetValue(key, out var metadata) ? metadata : null;

    /// <summary>
    /// Stores metadata for a firmware key. Returns the existing value if already present.
    /// </summary>
    public IReadOnlyDictionary<string, ParamMetadata> GetOrAdd(
        string key,
        IReadOnlyDictionary<string, ParamMetadata> metadata
    ) => _cache.GetOrAdd(key, metadata);

    /// <summary>
    /// Replaces metadata for a firmware key unconditionally.
    /// </summary>
    public void Update(string key, IReadOnlyDictionary<string, ParamMetadata> metadata) =>
        _cache[key] = metadata;
}
