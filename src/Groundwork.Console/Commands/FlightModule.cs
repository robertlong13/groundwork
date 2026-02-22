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
/// Provides high-level flight commands (takeoff, land).
/// </summary>
public sealed class FlightModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "takeoff",
            new DelegateCommand("Takeoff to altitude", "takeoff <altitude>", TakeoffAsync)
        );

        commands.Register("land", new DelegateCommand("Land the vehicle", "land", LandAsync));
    }

    private static async Task TakeoffAsync(string[] args, CommandContext ctx)
    {
        if (
            args.Length < 1
            || !float.TryParse(
                args[0],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var altitude
            )
        )
        {
            ctx.Output.WriteLine("Usage: takeoff <altitude>");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var result = await vehicle.SendCommandAsync(
            MAVLink.MAV_CMD.TAKEOFF,
            param7: altitude,
            ct: ctx.ShutdownToken
        );

        ctx.Output.WriteLine(
            result == MAVLink.MAV_RESULT.ACCEPTED
                ? $"Takeoff to {altitude} m"
                : $"Takeoff failed: {result}"
        );
    }

    /// <summary>
    /// Sends MAV_CMD_DO_LAND_START to trigger autonomous landing.
    /// </summary>
    private static async Task LandAsync(string[] args, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var result = await vehicle.SendCommandAsync(
            MAVLink.MAV_CMD.DO_LAND_START,
            ct: ctx.ShutdownToken
        );

        ctx.Output.WriteLine(
            result == MAVLink.MAV_RESULT.ACCEPTED ? "Landing" : $"Land failed: {result}"
        );
    }
}
