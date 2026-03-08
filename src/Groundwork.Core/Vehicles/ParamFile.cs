// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Provides read/write for the ArduPilot .parm file format.
/// </summary>
public static class ParamFile
{
    /// <summary>
    /// Writes parameters to a .parm file.
    /// </summary>
    /// <returns>The number of parameters written.</returns>
    public static int Save(string path, IReadOnlyDictionary<string, ParamEntry> parameters)
    {
        using var writer = new StreamWriter(path);
        var count = 0;

        foreach (var name in parameters.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var value = parameters[name].Value;
            writer.WriteLine(
                string.Format(CultureInfo.InvariantCulture, "{0,-16} {1:F6}", name, value)
            );
            count++;
        }

        return count;
    }

    /// <summary>
    /// Reads parameters from a .parm file.
    /// </summary>
    /// <returns>The parsed parameter entries (no defaults).</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static Dictionary<string, ParamEntry> Load(string path)
    {
        var result = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;

            // MAVProxy treats commas as spaces.
            var normalized = trimmed.Replace(',', ' ');
            var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var name = parts[0];
            var valueStr = parts[1];

            double value;
            if (
                valueStr.Length > 2
                && valueStr[0] == '0'
                && (valueStr[1] == 'x' || valueStr[1] == 'X')
            )
            {
                if (!int.TryParse(valueStr.AsSpan(2), NumberStyles.HexNumber, null, out var hex))
                    continue;
                value = hex;
            }
            else if (
                !double.TryParse(
                    valueStr,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                )
            )
            {
                continue;
            }

            result[name.ToUpperInvariant()] = new ParamEntry(value, null);
        }

        return result;
    }

    /// <summary>
    /// Compares two parameter values with a tolerance matching MAVProxy's mindelta.
    /// </summary>
    public static bool ValuesEqual(double a, double b) => Math.Abs(a - b) <= 0.000001;

    /// <summary>
    /// Matches a parameter name against a simple wildcard pattern (only * is supported).
    /// </summary>
    public static bool MatchesWildcard(string name, string wildcard)
    {
        if (wildcard == "*")
            return true;

        var upper = wildcard.ToUpperInvariant();
        var nameUpper = name.ToUpperInvariant();

        // Simple fnmatch-style: only * wildcards.
        if (!upper.Contains('*'))
            return nameUpper.Equals(upper, StringComparison.Ordinal);

        var parts = upper.Split('*');
        var pos = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;

            var idx = nameUpper.IndexOf(parts[i], pos, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            if (i == 0 && idx != 0)
                return false;

            pos = idx + parts[i].Length;
        }

        if (parts[^1].Length > 0 && !nameUpper.EndsWith(parts[^1], StringComparison.Ordinal))
            return false;

        return true;
    }
}
