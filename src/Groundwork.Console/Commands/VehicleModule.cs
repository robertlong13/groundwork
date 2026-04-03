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
/// Provides vehicle discovery and selection commands.
/// </summary>
public sealed class VehicleModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "vehicle list",
            new DelegateCommand(
                "List discovered vehicles",
                "vehicle list",
                (args, ctx) => List(ctx)
            )
        );

        commands.Register(
            "vehicle select",
            new DelegateCommand(
                "Select the active vehicle by index",
                "vehicle select <index>",
                (args, ctx) => Select(args, ctx)
            )
        );

        commands.Register(
            "vehicle link",
            new DelegateCommand(
                "Pin the primary link for the active vehicle",
                "vehicle link <index>",
                (args, ctx) => Link(args, ctx)
            )
        );
    }

    private static Task List(CommandContext ctx)
    {
        var vehicles = ctx.VehicleRegistry.Vehicles.ToList();

        if (vehicles.Count == 0)
        {
            ctx.Output.WriteLine("No vehicles discovered");
            return Task.CompletedTask;
        }

        var current = ctx.CurrentVehicle;

        for (var i = 0; i < vehicles.Count; i++)
        {
            var vehicle = vehicles[i];
            var state = vehicle.CanonicalState;
            var modeName = vehicle.ModeToName(state.CustomMode);
            var marker = vehicle == current ? "*" : " ";
            var armed = state.Armed ? "  Armed" : "";

            ctx.Output.WriteLine(
                $"  {marker} {i}: sysid {vehicle.SysId}  {state.Type}  {modeName}{armed}"
            );

            var channels = ctx.ChannelRegistry.Channels;
            if (channels.Count > 1)
            {
                foreach (var ch in vehicle.Channels)
                {
                    var linkIndex = ctx.ChannelRegistry.IndexOf(ch);
                    var primary = ch == vehicle.PrimaryChannel ? " (primary)" : "";
                    ctx.Output.WriteLine($"      link {linkIndex}: {ch.Name}{primary}");
                }
            }
        }

        return Task.CompletedTask;
    }

    private static Task Select(string[] args, CommandContext ctx)
    {
        var vehicles = ctx.VehicleRegistry.Vehicles.ToList();

        if (args.Length == 0)
        {
            var current = ctx.CurrentVehicle;
            if (current is null)
                ctx.Output.WriteLine("No vehicle selected");
            else
                ctx.Output.WriteLine(
                    $"Active vehicle: {vehicles.IndexOf(current)} (sysid {current.SysId})"
                );
            return Task.CompletedTask;
        }

        if (!int.TryParse(args[0], out var index) || index < 0 || index >= vehicles.Count)
        {
            ctx.Output.WriteLine($"Invalid vehicle index: {args[0]}");
            return Task.CompletedTask;
        }

        var vehicle = vehicles[index];
        ctx.CurrentVehicle = vehicle;
        ctx.Output.WriteLine($"Selected vehicle: {index} (sysid {vehicle.SysId})");
        return Task.CompletedTask;
    }

    private static Task Link(string[] args, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle selected");
            return Task.CompletedTask;
        }

        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: vehicle link <index>");
            return Task.CompletedTask;
        }

        var channels = ctx.ChannelRegistry.Channels;

        if (!int.TryParse(args[0], out var index) || index < 0 || index >= channels.Count)
        {
            ctx.Output.WriteLine($"Invalid link index: {args[0]}");
            return Task.CompletedTask;
        }

        var channel = channels[index];

        if (!vehicle.Channels.Contains(channel))
        {
            ctx.Output.WriteLine($"Link {index} ({channel.Name}) is not connected to this vehicle");
            return Task.CompletedTask;
        }

        vehicle.SetPrimaryChannel(channel);
        ctx.Output.WriteLine($"Pinned primary link: {index} ({channel.Name})");
        return Task.CompletedTask;
    }
}
