// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Represents a parameter value with an optional default.
/// </summary>
public readonly record struct ParamEntry(double Value, double? DefaultValue);

/// <summary>
/// Defines flags for excluding parameters during filtering.
/// </summary>
[Flags]
public enum ParamExclude
{
    None = 0,

    /// <summary>Indicates that parameters matching their default value should be excluded.</summary>
    Default = 1,

    /// <summary>Indicates that parameters marked volatile in metadata should be excluded.</summary>
    Volatile = 2,

    /// <summary>Indicates that parameters marked read-only in metadata should be excluded.</summary>
    ReadOnly = 4,
}

/// <summary>
/// Provides wildcard and metadata-based filtering for parameter dictionaries.
/// </summary>
public sealed class ParamFilter
{
    /// <summary>
    /// Gets or sets the inclusion wildcard patterns. Null means match all.
    /// </summary>
    public IEnumerable<string>? Wildcards { get; set; }

    /// <summary>
    /// Gets or sets the exclusion wildcard patterns. Null means exclude none.
    /// </summary>
    public IEnumerable<string>? ExcludeWildcards { get; set; }

    /// <summary>
    /// Gets or sets the metadata-based exclusion flags.
    /// </summary>
    public ParamExclude Exclude { get; set; }

    /// <summary>
    /// Gets or sets the parameter metadata used to evaluate Volatile and ReadOnly flags.
    /// </summary>
    public IReadOnlyDictionary<string, ParamMetadata>? Metadata { get; set; }

    /// <summary>
    /// Filters the given parameters according to the current filter settings.
    /// </summary>
    public Dictionary<string, ParamEntry> Apply(IReadOnlyDictionary<string, ParamEntry> parameters)
    {
        var result = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, entry) in parameters)
        {
            if (Wildcards is not null && !MatchesAny(name, Wildcards))
                continue;

            if (ExcludeWildcards is not null && MatchesAny(name, ExcludeWildcards))
                continue;

            if ((Exclude & ParamExclude.Default) != 0)
            {
                if (!entry.DefaultValue.HasValue)
                    throw new InvalidOperationException(name);
                if (ParamFile.ValuesEqual(entry.Value, entry.DefaultValue.Value))
                    continue;
            }

            if (Metadata is not null && Metadata.TryGetValue(name, out var meta))
            {
                if ((Exclude & ParamExclude.Volatile) != 0 && meta.Volatile)
                    continue;
                if ((Exclude & ParamExclude.ReadOnly) != 0 && meta.ReadOnly)
                    continue;
            }

            result[name] = entry;
        }

        return result;
    }

    private static bool MatchesAny(string name, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (ParamFile.MatchesWildcard(name, pattern))
                return true;
        }

        return false;
    }
}
