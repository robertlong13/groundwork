// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Diagnostics;
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
                "Fetch all parameters, or one by name",
                "param fetch [name]",
                (args, ctx) => FetchAsync(args, ctx)
            )
        );

        commands.Register(
            "param fetchlegacy",
            new DelegateCommand(
                "Fetch all parameters via PARAM_REQUEST_LIST",
                "param fetchlegacy",
                (args, ctx) => FetchAllAsync(v => v.DownloadParametersViaStreamAsync, ctx)
            )
        );

        commands.Register(
            "param ftp",
            new DelegateCommand(
                "Fetch all parameters via FTP",
                "param ftp",
                (args, ctx) => FetchAllAsync(v => v.DownloadParametersViaFtpAsync, ctx)
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
            ctx.Output.WriteLine("No parameters cached. Use 'param fetch' to download all.");
            return Task.CompletedTask;
        }

        var pattern = args.Length > 0 ? args[0] : "*";
        var matched = 0;

        foreach (var name in parameters.Keys.OrderBy(k => k, NaturalComparer.Instance))
        {
            if (MatchesGlob(name, pattern))
            {
                var entry = parameters[name];
                var line =
                    entry.DefaultValue.HasValue && entry.DefaultValue.Value != entry.Value
                        ? $"  {name, -16} {FormatValue(entry.Value), -12} (default {FormatValue(entry.DefaultValue.Value)})"
                        : $"  {name, -16} {FormatValue(entry.Value)}";
                ctx.Output.WriteLine(line);
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

    private static Task FetchAsync(string[] args, CommandContext ctx)
    {
        // No args = download all (auto-selects FTP or legacy).
        if (args.Length == 0)
            return FetchAllAsync(v => v.DownloadParametersAsync, ctx);

        return FetchOneAsync(args[0], ctx);
    }

    private static async Task FetchOneAsync(string name, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

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

    private delegate Task<int> DownloadMethod(
        IProgress<ParamDownloadProgress>? progress,
        CancellationToken ct
    );

    private static async Task FetchAllAsync(
        Func<Vehicle, DownloadMethod> methodSelector,
        CommandContext ctx
    )
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var lastReported = 0;
        // Progress<T> posts callbacks via ThreadPool (no SynchronizationContext
        // in console apps), causing races on lastReported and out-of-order
        // output. Use a synchronous IProgress<T> instead.
        IProgress<ParamDownloadProgress> progress = new SyncProgress<ParamDownloadProgress>(p =>
        {
            // FTP reports bytes; legacy reports param count.
            var threshold = p.ViaFtp ? 2_000 : 100;
            if (p.Received == p.Total || p.Received - lastReported >= threshold)
            {
                if (p.ViaFtp)
                    ctx.Output.WriteLine(
                        $"  {p.Received / 1024.0:F1}/{p.Total / 1024.0:F1} KB via FTP"
                    );
                else
                    ctx.Output.WriteLine($"  {p.Received}/{p.Total} via param list");
                lastReported = p.Received;
            }
        });

        try
        {
            var download = methodSelector(vehicle);
            var sw = Stopwatch.StartNew();
            var count = await download(progress, ctx.ShutdownToken);
            sw.Stop();
            ctx.Output.WriteLine($"Downloaded {count} parameters in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"Download failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("Download timed out");
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
    /// Provides synchronous <see cref="IProgress{T}"/> callbacks on the calling thread.
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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
