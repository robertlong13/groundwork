// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Provides disk persistence for parameter metadata JSON, with ETag and
/// Last-Modified date sidecars for conditional re-fetch.
/// </summary>
public sealed class ParamMetadataDiskCache
{
    private readonly string _directory;

    public ParamMetadataDiskCache(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Loads the cached JSON bytes and ETag for a firmware key, or <see langword="null"/> if not cached.
    /// </summary>
    public async Task<(byte[] Json, string? ETag)?> TryLoadAsync(
        string key,
        CancellationToken ct = default
    )
    {
        var jsonPath = JsonPath(key);
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllBytesAsync(jsonPath, ct).ConfigureAwait(false);
        var etagPath = ETagPath(key);
        var etag = File.Exists(etagPath)
            ? (await File.ReadAllTextAsync(etagPath, ct).ConfigureAwait(false)).Trim()
            : null;

        return (json, etag);
    }

    /// <summary>
    /// Loads the cached Last-Modified date for a firmware key, or <see langword="null"/> if not stored.
    /// </summary>
    public async Task<DateTimeOffset?> TryLoadDateAsync(string key, CancellationToken ct = default)
    {
        var datePath = DatePath(key);
        if (!File.Exists(datePath))
            return null;

        var text = (await File.ReadAllTextAsync(datePath, ct).ConfigureAwait(false)).Trim();
        return DateTimeOffset.TryParseExact(
            text,
            "R",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var date
        )
            ? date
            : null;
    }

    /// <summary>
    /// Stores JSON bytes and an optional ETag for a firmware key.
    /// </summary>
    public async Task SaveAsync(
        string key,
        byte[] json,
        string? etag,
        CancellationToken ct = default
    )
    {
        Directory.CreateDirectory(_directory);

        await File.WriteAllBytesAsync(JsonPath(key), json, ct).ConfigureAwait(false);

        if (etag is not null)
            await File.WriteAllTextAsync(ETagPath(key), etag, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores JSON bytes and a Last-Modified date for a firmware key.
    /// </summary>
    public async Task SaveWithDateAsync(
        string key,
        byte[] json,
        DateTimeOffset? lastModified,
        CancellationToken ct = default
    )
    {
        Directory.CreateDirectory(_directory);

        await File.WriteAllBytesAsync(JsonPath(key), json, ct).ConfigureAwait(false);

        if (lastModified is not null)
            await File.WriteAllTextAsync(DatePath(key), lastModified.Value.ToString("R"), ct)
                .ConfigureAwait(false);
    }

    private string JsonPath(string key) => Path.Combine(_directory, $"{key}.json");

    private string ETagPath(string key) => Path.Combine(_directory, $"{key}.etag");

    private string DatePath(string key) => Path.Combine(_directory, $"{key}.date");
}
