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
using Groundwork.Core.Channels;
using Groundwork.Core.Vehicles;

namespace Groundwork.Console.Commands;

/// <summary>
/// Provides parameter get and set commands.
/// </summary>
public sealed class ParamModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "param show",
            new DelegateCommand(
                "Show cached parameters",
                "param show [pattern]",
                (args, ctx) => Show(args, ctx)
            )
        );

        commands.Register(
            "param set",
            new DelegateCommand(
                "Set a parameter",
                "param set <name> <value>",
                (args, ctx) => SetAsync(args, ctx)
            )
        );

        commands.Register(
            "param fetch",
            new DelegateCommand(
                "Fetch a parameter from the autopilot",
                "param fetch <name>",
                (args, ctx) => FetchAsync(args, ctx)
            )
        );
    }

    private static Task Show(string[] args, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var parameters = vehicle.Parameters;
        if (parameters.Count == 0)
        {
            ctx.Output.WriteLine("No parameters cached. Use 'param fetch <name>' first.");
            return Task.CompletedTask;
        }

        var pattern = args.Length > 0 ? args[0] : "*";
        var matched = 0;

        foreach (var name in parameters.Keys.OrderBy(k => k, NaturalComparer.Instance))
        {
            if (MatchesGlob(name, pattern))
            {
                ctx.Output.WriteLine($"  {name, -16} {FormatValue(parameters[name])}");
                matched++;
            }
        }

        if (matched == 0)
            ctx.Output.WriteLine($"No parameters matching '{pattern}'");

        return Task.CompletedTask;
    }

    private static async Task SetAsync(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: param set <name> <value>");
            return;
        }

        // One arg = show that param (matches MAVProxy behavior).
        if (args.Length == 1)
        {
            await Show(args, ctx);
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var name = args[0];
        var valueStr = args[1];
        double value;
        if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(valueStr.AsSpan(2), NumberStyles.HexNumber, null, out var hex))
            {
                ctx.Output.WriteLine($"Invalid hex value: {valueStr}");
                return;
            }

            value = hex;
        }
        else if (!double.TryParse(valueStr, out value))
        {
            ctx.Output.WriteLine($"Invalid value: {valueStr}");
            return;
        }

        if (!vehicle.Parameters.ContainsKey(name))
        {
            ctx.Output.WriteLine($"Unable to find parameter '{name.ToUpperInvariant()}'");
            return;
        }

        try
        {
            var confirmed = await vehicle.SetParameterAsync(name, value, ctx.ShutdownToken);
            ctx.Output.WriteLine($"{name.ToUpperInvariant()} = {FormatValue(confirmed)}");
        }
        catch (ParameterException ex)
        {
            ctx.Output.WriteLine($"Set failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine($"Set timed out for '{name.ToUpperInvariant()}'");
        }
    }

    private static async Task FetchAsync(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: param fetch <name>");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var name = args[0];

        try
        {
            var value = await vehicle.FetchParameterAsync(name, ctx.ShutdownToken);
            ctx.Output.WriteLine($"{name.ToUpperInvariant()} = {FormatValue(value)}");
        }
        catch (ParameterException ex)
        {
            ctx.Output.WriteLine($"Fetch failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine($"Fetch timed out for '{name.ToUpperInvariant()}'");
        }
    }

    private static string FormatValue(double value) =>
        value.ToString("F6", CultureInfo.InvariantCulture);

    private static bool MatchesGlob(string name, string pattern)
    {
        // Simple glob: * matches any sequence of characters.
        var upper = pattern.ToUpperInvariant();
        if (!upper.Contains('*'))
            return name.Equals(upper, StringComparison.OrdinalIgnoreCase);

        var parts = upper.Split('*');
        var pos = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;

            var idx = name.IndexOf(parts[i], pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            // First segment must match at start.
            if (i == 0 && idx != 0)
                return false;

            pos = idx + parts[i].Length;
        }

        // Last segment must match at end.
        if (parts[^1].Length > 0 && !name.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Compares strings with natural numeric ordering (SERVO1 before SERVO10).
    /// </summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            int ix = 0,
                iy = 0;

            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    // Skip leading zeros within the numeric segment.
                    int sx = ix,
                        sy = iy;
                    while (sx < x.Length && x[sx] == '0')
                        sx++;
                    while (sy < y.Length && y[sy] == '0')
                        sy++;

                    int ex = sx,
                        ey = sy;
                    while (ex < x.Length && char.IsDigit(x[ex]))
                        ex++;
                    while (ey < y.Length && char.IsDigit(y[ey]))
                        ey++;

                    // Longer significant digit span = larger number.
                    int lenDiff = (ex - sx) - (ey - sy);
                    if (lenDiff != 0)
                        return lenDiff;

                    // Same length: compare digits lexicographically.
                    for (int i = sx, j = sy; i < ex; i++, j++)
                    {
                        if (x[i] != y[j])
                            return x[i].CompareTo(y[j]);
                    }

                    ix = ex;
                    iy = ey;
                }
                else
                {
                    int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                    if (cmp != 0)
                        return cmp;
                    ix++;
                    iy++;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
