// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Provides parameter metadata resolution through a three-tier cache:
/// in-memory, disk, and CDN (ArduPilot ParameterRepository via jsDelivr).
/// Falls back to autotest.ardupilot.org dev metadata when the versioned
/// CDN path does not exist.
/// </summary>
public sealed class ParamMetadataFetcher
{
    private const string DefaultBaseUrl =
        "https://cdn.jsdelivr.net/gh/ArduPilot/ParameterRepository";

    private const string DefaultFallbackBaseUrl = "https://autotest.ardupilot.org/Parameters";

    private readonly HttpClient _httpClient;
    private readonly ParamMetadataCache _memoryCache;
    private readonly ParamMetadataDiskCache? _diskCache;
    private readonly ILogger _logger;

    /// <summary>
    /// Gets or sets the base URL for the ParameterRepository. Defaults to jsDelivr CDN.
    /// </summary>
    public static string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>
    /// Gets or sets the base URL for dev fallback metadata. Defaults to autotest.ardupilot.org.
    /// </summary>
    public static string FallbackBaseUrl { get; set; } = DefaultFallbackBaseUrl;

    public ParamMetadataFetcher(
        HttpClient httpClient,
        ParamMetadataCache memoryCache,
        ILogger logger,
        ParamMetadataDiskCache? diskCache = null
    )
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _logger = logger;
        _diskCache = diskCache;
    }

    /// <summary>
    /// Builds the cache key for a firmware family and version.
    /// </summary>
    public static string CacheKey(FirmwareFamily family, int major, int minor) =>
        $"{family}-{major}.{minor}";

    /// <summary>
    /// Builds the cache key for dev (latest) metadata for a firmware family.
    /// </summary>
    public static string DevCacheKey(FirmwareFamily family) => $"{family}-latest";

    /// <summary>
    /// Resolves parameter metadata for a firmware version, checking memory cache,
    /// disk cache, then CDN. Disk hits trigger a background revalidation via
    /// conditional GET.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ParamMetadata>?> EnsureAsync(
        FirmwareFamily family,
        int major,
        int minor,
        CancellationToken ct = default
    )
    {
        var key = CacheKey(family, major, minor);

        // 1. In-memory cache.
        var cached = _memoryCache.Get(key);
        if (cached is not null)
            return cached;

        // 2. Disk cache -- load immediately, revalidate in background.
        if (_diskCache is not null)
        {
            try
            {
                var disk = await _diskCache.TryLoadAsync(key, ct).ConfigureAwait(false);
                if (disk is not null)
                {
                    var metadata = await ParseAsync(new MemoryStream(disk.Value.Json), ct)
                        .ConfigureAwait(false);
                    var result = _memoryCache.GetOrAdd(key, metadata);

                    _logger.LogInformation(
                        "Loaded {Count} parameter definitions for {Key} from disk cache",
                        result.Count,
                        key
                    );

                    _ = RevalidateInBackgroundAsync(key, disk.Value.ETag);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read disk cache for {Key}", key);
            }
        }

        // 3. Full CDN fetch.
        return await FetchAndCacheAsync(family, key, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads and parses parameter metadata from the CDN, falling back to
    /// dev metadata from autotest.ardupilot.org when the version is not found.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ParamMetadata>?> FetchAndCacheAsync(
        FirmwareFamily family,
        string key,
        CancellationToken ct
    )
    {
        var url = $"{BaseUrl}/{key}/apm.pdef.json";
        _logger.LogInformation("Fetching parameter metadata from {Url}", url);

        try
        {
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "Version {Key} not in ParameterRepository, falling back to dev",
                    key
                );
                return await FetchDevAsync(family, key, ct).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();

            return await CacheResponseAsync(key, response, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "CDN fetch failed for {Key}, falling back to dev", key);
            return await FetchDevAsync(family, key, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves dev metadata for a firmware family. Checks the dev cache key
    /// in memory and on disk before fetching from autotest.ardupilot.org.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ParamMetadata>?> FetchDevAsync(
        FirmwareFamily family,
        string versionKey,
        CancellationToken ct
    )
    {
        var devKey = DevCacheKey(family);

        // Check memory cache for dev.
        var cached = _memoryCache.Get(devKey);
        if (cached is not null)
        {
            _memoryCache.GetOrAdd(versionKey, cached);
            return cached;
        }

        // Check disk cache for dev -- load immediately, revalidate in background.
        if (_diskCache is not null)
        {
            try
            {
                var disk = await _diskCache.TryLoadAsync(devKey, ct).ConfigureAwait(false);
                if (disk is not null)
                {
                    var metadata = await ParseAsync(new MemoryStream(disk.Value.Json), ct)
                        .ConfigureAwait(false);
                    var result = _memoryCache.GetOrAdd(devKey, metadata);
                    _memoryCache.GetOrAdd(versionKey, result);

                    _logger.LogInformation(
                        "Loaded {Count} parameter definitions for {Key} from dev disk cache",
                        result.Count,
                        devKey
                    );

                    _ = RevalidateDevInBackgroundAsync(family);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read dev disk cache for {Key}", devKey);
            }
        }

        // Fetch from autotest.ardupilot.org.
        return await FetchAndCacheDevAsync(family, versionKey, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads dev metadata from autotest.ardupilot.org and caches it with
    /// the Last-Modified date for future conditional requests.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ParamMetadata>?> FetchAndCacheDevAsync(
        FirmwareFamily family,
        string? versionKey,
        CancellationToken ct
    )
    {
        var devKey = DevCacheKey(family);
        var url = $"{FallbackBaseUrl}/{family}/apm.pdef.json";
        _logger.LogInformation("Fetching dev parameter metadata from {Url}", url);

        try
        {
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var lastModified = response.Content.Headers.LastModified;
            var metadata = await ParseAsync(new MemoryStream(json), ct).ConfigureAwait(false);
            var result = _memoryCache.GetOrAdd(devKey, metadata);

            _logger.LogInformation(
                "Loaded {Count} parameter definitions for {Key}",
                result.Count,
                devKey
            );

            if (versionKey is not null)
                _memoryCache.GetOrAdd(versionKey, result);

            if (_diskCache is not null)
            {
                try
                {
                    await _diskCache
                        .SaveWithDateAsync(devKey, json, lastModified, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write dev disk cache for {Key}", devKey);
                }
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Dev fallback fetch failed for {Family}", family);
            return null;
        }
    }

    /// <summary>
    /// Sends a conditional GET to autotest.ardupilot.org using If-Modified-Since.
    /// Updates disk and memory caches if the content has changed (200).
    /// </summary>
    private async Task RevalidateDevInBackgroundAsync(FirmwareFamily family)
    {
        try
        {
            var devKey = DevCacheKey(family);
            var lastModified = await _diskCache!.TryLoadDateAsync(devKey).ConfigureAwait(false);

            if (lastModified is null)
            {
                // No date sidecar -- do a full fetch to establish one.
                await FetchAndCacheDevAsync(family, versionKey: null, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            var url = $"{FallbackBaseUrl}/{family}/apm.pdef.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.IfModifiedSince = lastModified;

            using var response = await _httpClient
                .SendAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogDebug("Dev parameter metadata for {Key} is up to date", devKey);
                return;
            }

            response.EnsureSuccessStatusCode();

            var json = await response
                .Content.ReadAsByteArrayAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var newLastModified = response.Content.Headers.LastModified;
            var metadata = await ParseAsync(new MemoryStream(json), CancellationToken.None)
                .ConfigureAwait(false);

            _memoryCache.Update(devKey, metadata);

            await _diskCache
                .SaveWithDateAsync(devKey, json, newLastModified, CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation("Revalidated dev parameter metadata for {Key}", devKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background dev revalidation failed for {Family}", family);
        }
    }

    /// <summary>
    /// Parses an HTTP response, populates the memory cache and disk cache.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ParamMetadata>?> CacheResponseAsync(
        string key,
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        var json = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var etag = response.Headers.ETag?.Tag;
        var metadata = await ParseAsync(new MemoryStream(json), ct).ConfigureAwait(false);
        var result = _memoryCache.GetOrAdd(key, metadata);

        _logger.LogInformation("Loaded {Count} parameter definitions for {Key}", result.Count, key);

        if (_diskCache is not null)
        {
            try
            {
                await _diskCache.SaveAsync(key, json, etag, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write disk cache for {Key}", key);
            }
        }

        return result;
    }

    /// <summary>
    /// Sends a conditional GET to the CDN. Updates disk and memory caches if the
    /// content has changed (200). No-op on 304 Not Modified.
    /// </summary>
    private async Task RevalidateInBackgroundAsync(string key, string? etag)
    {
        if (etag is null)
            return;

        try
        {
            var url = $"{BaseUrl}/{key}/apm.pdef.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));

            using var response = await _httpClient
                .SendAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogDebug("Parameter metadata for {Key} is up to date", key);
                return;
            }

            response.EnsureSuccessStatusCode();

            var json = await response
                .Content.ReadAsByteArrayAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var newEtag = response.Headers.ETag?.Tag;
            var metadata = await ParseAsync(new MemoryStream(json), CancellationToken.None)
                .ConfigureAwait(false);

            _memoryCache.Update(key, metadata);

            if (_diskCache is not null)
                await _diskCache
                    .SaveAsync(key, json, newEtag, CancellationToken.None)
                    .ConfigureAwait(false);

            _logger.LogInformation("Revalidated parameter metadata for {Key} from CDN", key);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background revalidation failed for {Key}", key);
        }
    }

    /// <summary>
    /// Parses ArduPilot ParameterRepository JSON into parameter metadata.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, ParamMetadata>> ParseAsync(
        Stream stream,
        CancellationToken ct = default
    )
    {
        using var doc = await JsonDocument
            .ParseAsync(stream, cancellationToken: ct)
            .ConfigureAwait(false);

        var result = new Dictionary<string, ParamMetadata>(StringComparer.OrdinalIgnoreCase);

        // Top-level keys are group prefixes (e.g. "ADSB_", "ARMING_").
        // Each group contains parameter objects keyed by full param name.
        foreach (var group in doc.RootElement.EnumerateObject())
        {
            var groupName = group.Name;

            if (group.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var param in group.Value.EnumerateObject())
            {
                if (param.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var meta = ParseParam(param.Value, groupName);
                result[param.Name] = meta;
            }
        }

        return result;
    }

    private static ParamMetadata ParseParam(JsonElement element, string group)
    {
        return new ParamMetadata
        {
            DisplayName = GetString(element, "DisplayName"),
            Description = GetString(element, "Description"),
            Min = ParseRange(element, "low"),
            Max = ParseRange(element, "high"),
            Increment = ParseFloat(GetString(element, "Increment")),
            Units = GetString(element, "Units"),
            Values = ParseIntStringDict(element, "Values"),
            Bitmask = ParseIntStringDict(element, "Bitmask"),
            RebootRequired = GetString(element, "RebootRequired") == "True",
            ReadOnly = GetString(element, "ReadOnly") == "True",
            Volatile = GetString(element, "Volatile") == "True",
            Group = group,
            Category = GetString(element, "User"),
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static float? ParseRange(JsonElement element, string bound)
    {
        if (!element.TryGetProperty("Range", out var range))
            return null;

        if (range.ValueKind != JsonValueKind.Object)
            return null;

        var str = GetString(range, bound);
        return ParseFloat(str);
    }

    private static float? ParseFloat(string? str) =>
        str is not null
        && float.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? f
            : null;

    private static IReadOnlyDictionary<int, string>? ParseIntStringDict(
        JsonElement element,
        string property
    )
    {
        if (!element.TryGetProperty(property, out var obj))
            return null;

        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<int, string>();

        foreach (var kv in obj.EnumerateObject())
        {
            if (int.TryParse(kv.Name, out var code) && kv.Value.ValueKind == JsonValueKind.String)
                dict[code] = kv.Value.GetString()!;
        }

        return dict.Count > 0 ? dict : null;
    }
}
