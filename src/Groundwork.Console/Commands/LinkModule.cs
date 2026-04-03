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
/// Provides link management commands matching MAVProxy's link module.
/// </summary>
public sealed class LinkModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "link list",
            new DelegateCommand("List active links", "link list", (args, ctx) => List(ctx))
        );

        commands.Register(
            "link add",
            new DelegateCommand(
                "Add a new link",
                "link add <descriptor>",
                (args, ctx) => Add(args, ctx)
            )
        );

        commands.Register(
            "link remove",
            new DelegateCommand(
                "Remove a link by index",
                "link remove <index>",
                (args, ctx) => Remove(args, ctx)
            )
        );
    }

    private static Task List(CommandContext ctx)
    {
        var channels = ctx.ChannelRegistry.Channels;

        if (channels.Count == 0)
        {
            ctx.Output.WriteLine("No active links");
            return Task.CompletedTask;
        }

        for (var i = 0; i < channels.Count; i++)
            ctx.Output.WriteLine($"  {i}: {channels[i].Name}");

        return Task.CompletedTask;
    }

    private static async Task Add(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: link add <descriptor>");
            ctx.Output.WriteLine("  e.g., link add udpin:14560");
            return;
        }

        try
        {
            var channel = await ctx.Links.AddAsync(args[0], ctx.ShutdownToken);
            ctx.Output.WriteLine($"Added: {channel.Name}");
        }
        catch (FormatException ex)
        {
            ctx.Output.WriteLine(ex.Message);
        }
    }

    private static async Task Remove(string[] args, CommandContext ctx)
    {
        if (args.Length == 0 || !int.TryParse(args[0], out var index))
        {
            ctx.Output.WriteLine("Usage: link remove <index>");
            return;
        }

        var channels = ctx.ChannelRegistry.Channels;

        if (index < 0 || index >= channels.Count)
        {
            ctx.Output.WriteLine($"Invalid link index: {index}");
            return;
        }

        var name = channels[index].Name;
        var vehicleBefore = ctx.CurrentVehicle;

        await ctx.Links.RemoveAsync(index);
        ctx.Output.WriteLine($"Removed: {name}");

        var vehicleAfter = ctx.CurrentVehicle;
        if (vehicleBefore is not null && vehicleAfter != vehicleBefore)
        {
            if (vehicleAfter is null)
                ctx.Output.WriteLine("Active vehicle disconnected");
            else
                ctx.Output.WriteLine("Active vehicle changed");
        }
    }
}
