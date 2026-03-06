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
/// Provides vehicle status and telemetry display commands.
/// </summary>
public sealed class OverviewModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "overview",
            new DelegateCommand(
                "Show vehicle status overview",
                "overview",
                (args, ctx) => Overview(ctx)
            )
        );
    }

    private static Task Overview(CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var state = vehicle.CanonicalState;
        var modeName = vehicle.ModeToName(state.CustomMode);

        ctx.Output.WriteLine($"Type:     {state.Type}");
        ctx.Output.WriteLine($"Mode:     {modeName}");
        ctx.Output.WriteLine($"Armed:    {(state.Armed ? "YES" : "NO")}");
        ctx.Output.WriteLine($"Position: {state.Latitude:F6}, {state.Longitude:F6}");
        ctx.Output.WriteLine($"Alt:      {state.Altitude:F1} m AGL / {state.AltitudeMsl:F1} m MSL");
        ctx.Output.WriteLine($"Heading:  {state.Heading:F0} deg");
        ctx.Output.WriteLine(
            $"Battery:  {state.BatteryVoltage:F1} V  "
                + $"{state.BatteryCurrent:F1} A  "
                + $"{state.BatteryRemaining}%"
        );

        return Task.CompletedTask;
    }
}
