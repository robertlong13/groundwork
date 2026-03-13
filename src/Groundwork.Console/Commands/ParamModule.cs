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
using ArduPilot = Groundwork.Core.ArduPilot;

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
                "param show [pattern] [-v]",
                (args, ctx) => Show(args, ctx)
            )
        );

        commands.Register(
            "param help",
            new DelegateCommand(
                "Show metadata for a parameter",
                "param help <name>",
                (args, ctx) => Help(args, ctx)
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

        commands.Register(
            "param save",
            new DelegateCommand(
                "Save parameters to file",
                "param save <filename> [wildcard]",
                (args, ctx) => Save(args, ctx)
            )
        );

        commands.Register(
            "param load",
            new DelegateCommand(
                "Load parameters from file and set on vehicle",
                "param load <filename> [wildcard]",
                (args, ctx) => LoadAsync(args, ctx)
            )
        );

        commands.Register(
            "param savechanged",
            new DelegateCommand(
                "Save parameters that differ from defaults",
                "param savechanged [filename]",
                (args, ctx) => SaveChanged(args, ctx)
            )
        );

        commands.Register(
            "param diff",
            new DelegateCommand(
                "Show parameters differing from defaults or a file",
                "param diff [filename] [wildcard]",
                (args, ctx) => Diff(args, ctx)
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

        // Parse -v flag from anywhere in args.
        var verbose = false;
        var pattern = "*";
        foreach (var arg in args)
        {
            if (arg == "-v")
                verbose = true;
            else
                pattern = arg;
        }

        var filtered = new ParamFilter { Wildcards = [pattern] }.Apply(parameters);
        var metadata = verbose ? vehicle.ParameterMetadata : null;

        foreach (var name in filtered.Keys.OrderBy(k => k, NaturalComparer.Instance))
        {
            var entry = filtered[name];
            var valueStr = FormatValue(entry.Value);
            var line =
                entry.DefaultValue.HasValue && entry.DefaultValue.Value != entry.Value
                    ? $"  {name, -16} {valueStr, -12} (default {FormatValue(entry.DefaultValue.Value)})"
                    : $"  {name, -16} {valueStr}";

            if (metadata is not null && metadata.TryGetValue(name, out var meta))
            {
                var info = FormatValueInfo(meta, entry.Value);
                if (info is not null)
                    line = $"{line, -40} # {info}";
            }

            ctx.Output.WriteLine(line);
        }

        if (filtered.Count == 0)
            ctx.Output.WriteLine($"No parameters matching '{pattern}'");

        return Task.CompletedTask;
    }

    private static Task Help(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: param help <name>");
            return Task.CompletedTask;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var paramName = args[0].ToUpperInvariant();
        var metadata = vehicle.ParameterMetadata;
        if (metadata is null)
        {
            ctx.Output.WriteLine("No parameter metadata available");
            return Task.CompletedTask;
        }

        if (!metadata.TryGetValue(paramName, out var meta))
        {
            ctx.Output.WriteLine($"Parameter '{paramName}' not found in metadata");
            return Task.CompletedTask;
        }

        // Header: name and display name.
        if (meta.DisplayName is not null)
            ctx.Output.WriteLine($"{paramName}: {meta.DisplayName}");
        else
            ctx.Output.WriteLine(paramName);

        // Description.
        if (meta.Description is not null)
        {
            ctx.Output.WriteLine();
            ctx.Output.WriteLine(meta.Description);
        }

        // Fields (range, increment, units, flags).
        var fields = new List<string>();
        if (meta.Min.HasValue || meta.Max.HasValue)
            fields.Add($"Range: {FormatOptional(meta.Min)} .. {FormatOptional(meta.Max)}");
        if (meta.Increment.HasValue)
            fields.Add($"Increment: {meta.Increment.Value}");
        if (meta.Units is not null)
            fields.Add($"Units: {meta.Units}");
        if (meta.ReadOnly)
            fields.Add("ReadOnly");
        if (meta.RebootRequired)
            fields.Add("RebootRequired");
        if (meta.Volatile)
            fields.Add("Volatile");

        if (fields.Count > 0)
        {
            ctx.Output.WriteLine();
            foreach (var field in fields)
                ctx.Output.WriteLine($"  {field}");
        }

        // Current value (if we have params).
        if (vehicle.Parameters.TryGetValue(paramName, out var entry))
        {
            ctx.Output.WriteLine();
            var valueStr = FormatValue(entry.Value);
            var info = FormatValueInfo(meta, entry.Value);
            if (info is not null)
                ctx.Output.WriteLine($"  Current: {valueStr} ({info})");
            else
                ctx.Output.WriteLine($"  Current: {valueStr}");

            if (entry.DefaultValue.HasValue)
                ctx.Output.WriteLine($"  Default: {FormatValue(entry.DefaultValue.Value)}");
        }

        // Values table.
        if (meta.Values is { Count: > 0 })
        {
            ctx.Output.WriteLine();
            ctx.Output.WriteLine("Values:");
            foreach (var (code, label) in meta.Values.OrderBy(kv => kv.Key))
                ctx.Output.WriteLine($"  {code, 6} : {label}");
        }

        // Bitmask table.
        if (meta.Bitmask is { Count: > 0 })
        {
            ctx.Output.WriteLine();
            ctx.Output.WriteLine("Bitmask:");
            foreach (var (bit, label) in meta.Bitmask.OrderBy(kv => kv.Key))
                ctx.Output.WriteLine($"  {bit, 3} : {label}");
        }

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

        // Wildcard: expand against cached params and fetch each match.
        if (args[0].Contains('*'))
            return FetchWildcardAsync(args[0], ctx);

        return FetchOneAsync(args[0], ctx);
    }

    private static async Task FetchWildcardAsync(string pattern, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var matches = vehicle
            .Parameters.Keys.Where(k => ParamFile.MatchesWildcard(k, pattern))
            .OrderBy(k => k, NaturalComparer.Instance)
            .ToList();

        if (matches.Count == 0)
        {
            ctx.Output.WriteLine($"No parameters matching '{pattern}'");
            return;
        }

        foreach (var name in matches)
        {
            try
            {
                var value = await vehicle.FetchParameterAsync(name, ctx.ShutdownToken);
                ctx.Output.WriteLine($"{name} = {FormatValue(value)}");
            }
            catch (ParameterException ex)
            {
                ctx.Output.WriteLine($"Fetch {name} failed: {ex.Message}");
            }
            catch (TimeoutException)
            {
                ctx.Output.WriteLine($"Fetch timed out for '{name}'");
            }
        }
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

    private static Task Save(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: param save <filename> [wildcard]");
            return Task.CompletedTask;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var parameters = vehicle.Parameters;
        if (parameters.Count == 0)
        {
            ctx.Output.WriteLine("No parameters cached");
            return Task.CompletedTask;
        }

        var filename = args[0].Trim('"');
        var wildcard = args.Length > 1 ? args[1] : "*";
        var filtered = new ParamFilter { Wildcards = [wildcard] }.Apply(parameters);

        try
        {
            var count = ParamFile.Save(filename, filtered);
            ctx.Output.WriteLine($"Saved {count} parameters to {filename}");
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"Save failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static async Task LoadAsync(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: param load <filename> [wildcard]");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var filename = args[0].Trim('"');
        var wildcard = args.Length > 1 ? args[1] : "*";

        Dictionary<string, ParamEntry> fileParams;
        try
        {
            var raw = ParamFile.Load(filename);
            fileParams = new ParamFilter
            {
                Wildcards = [wildcard],
                ExcludeWildcards = ArduPilot.ParamLoadExclusions.DefaultWildcards,
                Exclude = ParamExclude.ReadOnly,
                Metadata = vehicle.ParameterMetadata,
            }.Apply(raw);
        }
        catch (FileNotFoundException)
        {
            ctx.Output.WriteLine($"File not found: {filename}");
            return;
        }

        var cached = vehicle.Parameters;
        var changed = 0;
        var skipped = 0;

        foreach (var (name, entry) in fileParams)
        {
            if (!cached.TryGetValue(name, out var existing))
            {
                ctx.Output.WriteLine($"Unknown parameter {name}");
                skipped++;
                continue;
            }

            if (ParamFile.ValuesEqual(existing.Value, entry.Value))
                continue;

            try
            {
                var confirmed = await vehicle.SetParameterAsync(
                    name,
                    entry.Value,
                    ctx.ShutdownToken
                );
                ctx.Output.WriteLine(
                    $"Changed {name, -16} {FormatValue(existing.Value)} -> {FormatValue(confirmed)}"
                );
                changed++;
            }
            catch (ParameterException ex)
            {
                ctx.Output.WriteLine($"Failed to set {name}: {ex.Message}");
                skipped++;
            }
            catch (TimeoutException)
            {
                ctx.Output.WriteLine($"Timeout setting {name}");
                skipped++;
            }
        }

        ctx.Output.WriteLine(
            $"Loaded {fileParams.Count} parameters from {filename} (changed {changed})"
        );
    }

    private static Task SaveChanged(string[] args, CommandContext ctx)
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
            ctx.Output.WriteLine("No parameters cached");
            return Task.CompletedTask;
        }

        var filename = args.Length > 0 ? args[0] : "changed.parm";

        Dictionary<string, ParamEntry> filtered;
        try
        {
            filtered = new ParamFilter { Exclude = ParamExclude.Default }.Apply(parameters);
        }
        catch (InvalidOperationException)
        {
            var anyHaveDefaults = parameters.Values.Any(e => e.DefaultValue.HasValue);
            ctx.Output.WriteLine(
                anyHaveDefaults
                    ? "Defaults missing on some parameters. Fetch via FTP again."
                    : "No defaults available. Fetch parameters via FTP first."
            );
            return Task.CompletedTask;
        }

        try
        {
            var count = ParamFile.Save(filename, filtered);
            if (count == 0)
                ctx.Output.WriteLine("No parameters differ from defaults");
            else
                ctx.Output.WriteLine($"Saved {count} parameters to {filename}");
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"Save failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static Task Diff(string[] args, CommandContext ctx)
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
            ctx.Output.WriteLine("No parameters cached");
            return Task.CompletedTask;
        }

        // No args or first arg is a wildcard: diff against defaults.
        // Otherwise first arg is a filename, optional second arg is wildcard.
        Dictionary<string, double>? defaults;
        var wildcard = "*";

        if (args.Length == 0 || args[0].Contains('*'))
        {
            if (args.Length > 0)
                wildcard = args[0];

            defaults = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, entry) in parameters)
            {
                if (entry.DefaultValue.HasValue)
                    defaults[name] = entry.DefaultValue.Value;
            }

            if (defaults.Count == 0)
            {
                ctx.Output.WriteLine("No defaults available");
                return Task.CompletedTask;
            }
        }
        else
        {
            var filename = args[0].Trim('"');
            if (args.Length > 1)
                wildcard = args[1];

            try
            {
                var fileParams = ParamFile.Load(filename);
                defaults = new Dictionary<string, double>(
                    fileParams.Count,
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (var (name, entry) in fileParams)
                    defaults[name] = entry.Value;
            }
            catch (FileNotFoundException)
            {
                ctx.Output.WriteLine($"File not found: {filename}");
                return Task.CompletedTask;
            }
        }

        var filtered = new ParamFilter { Wildcards = [wildcard] }.Apply(parameters);

        ctx.Output.WriteLine();
        ctx.Output.WriteLine($"{"Parameter", -16} {"Current", 12} {"Default", 12}");

        var metadata = vehicle.ParameterMetadata;
        var count = 0;

        foreach (var name in filtered.Keys.OrderBy(k => k, NaturalComparer.Instance))
        {
            if (!defaults.TryGetValue(name, out var defaultValue))
                continue;

            var current = filtered[name].Value;
            if (ParamFile.ValuesEqual(current, defaultValue))
                continue;

            var line = $"{name, -16} {current, 12:F6} {defaultValue, 12:F6}";

            if (metadata is not null && metadata.TryGetValue(name, out var meta))
            {
                var currentInfo = FormatValueInfo(meta, current);
                var defaultInfo = FormatValueInfo(meta, defaultValue);
                if (currentInfo is not null && defaultInfo is not null)
                    line += $" # {currentInfo} (was {defaultInfo})";
                else if (currentInfo is not null)
                    line += $" # {currentInfo}";
            }

            ctx.Output.WriteLine(line);
            count++;
        }

        if (count == 0)
            ctx.Output.WriteLine("No differences");

        return Task.CompletedTask;
    }

    private static string FormatValue(double value) =>
        value.ToString("F6", CultureInfo.InvariantCulture);

    private static string? FormatValueInfo(ParamMetadata meta, double value)
    {
        // Bitmask: show set flag names.
        if (meta.Bitmask is { Count: > 0 })
        {
            var bits = (int)value;
            var flags = new List<string>();
            var remaining = bits;
            foreach (var (bit, label) in meta.Bitmask.OrderBy(kv => kv.Key))
            {
                if ((bits & (1 << bit)) != 0)
                {
                    flags.Add(label);
                    remaining &= ~(1 << bit);
                }
            }

            for (var i = 0; i < 32; i++)
            {
                if ((remaining & (1 << i)) != 0)
                    flags.Add($"Bit{i}");
            }

            return flags.Count > 0 ? string.Join("|", flags) : null;
        }

        // Enum: resolve code to label.
        if (meta.Values is { Count: > 0 })
        {
            var code = (int)value;
            if (value == code && meta.Values.TryGetValue(code, out var label))
                return label;
        }

        return null;
    }

    private static string FormatOptional(float? value) =>
        value.HasValue ? value.Value.ToString("G7") : "?";

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
