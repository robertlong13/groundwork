// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Console.Commands;

/// <summary>
/// Provides diagnostic commands for channel and vehicle introspection.
/// </summary>
public sealed class DiagModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "diag msgcounts",
            new DelegateCommand(
                "Show per-message receive counts",
                "diag msgcounts",
                (args, ctx) => MsgCounts(ctx)
            )
        );

        commands.Register(
            "diag parser",
            new DelegateCommand("Show parser statistics", "diag parser", (args, ctx) => Parser(ctx))
        );

        commands.Register(
            "diag rates",
            new DelegateCommand(
                "Show configured stream rates",
                "diag rates",
                (args, ctx) => Rates(ctx)
            )
        );
    }

    private static Task MsgCounts(CommandContext ctx)
    {
        var channels = ctx.Links.Channels.ToList();

        if (channels.Count == 0)
        {
            ctx.Output.WriteLine("No active links");
            return Task.CompletedTask;
        }

        foreach (var channel in channels)
        {
            if (channels.Count > 1)
                ctx.Output.WriteLine($"[{channel.Name}]");

            var counts = channel.MessageCounts;

            if (counts.Count == 0)
            {
                ctx.Output.WriteLine("  No messages received");
                continue;
            }

            foreach (var (msgid, count) in counts.OrderByDescending(kv => kv.Value))
            {
                var name =
                    MAVLink.MAVLINK_MESSAGE_INFOS.GetMessageInfo(msgid).name ?? $"UNKNOWN({msgid})";
                ctx.Output.WriteLine($"  {name, -35} {count, 8}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task Parser(CommandContext ctx)
    {
        var channels = ctx.Links.Channels.ToList();

        if (channels.Count == 0)
        {
            ctx.Output.WriteLine("No active links");
            return Task.CompletedTask;
        }

        foreach (var channel in channels)
        {
            if (channels.Count > 1)
                ctx.Output.WriteLine($"[{channel.Name}]");

            var parser = channel.Parser;

            ctx.Output.WriteLine($"  Total messages: {parser.TotalMessages}");
            ctx.Output.WriteLine($"  Bad CRC:        {parser.BadCrc}");
            ctx.Output.WriteLine($"  Bad length:     {parser.BadLength}");
        }

        return Task.CompletedTask;
    }

    private static Task Rates(CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var rates = vehicle.RateController.StreamRates;

        if (rates.Count == 0)
        {
            ctx.Output.WriteLine("No stream rates configured");
            return Task.CompletedTask;
        }

        foreach (var (stream, rateHz) in rates.OrderBy(kv => kv.Key))
            ctx.Output.WriteLine($"  {stream, -25} {rateHz, 3} Hz");

        return Task.CompletedTask;
    }
}
