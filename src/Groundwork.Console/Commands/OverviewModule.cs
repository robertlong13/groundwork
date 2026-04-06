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
        var hb = state.Heartbeat.Value;
        var pos = state.Position.Value;
        var bat = state.Battery.Value;
        var modeName = vehicle.ModeToName(hb.CustomMode);

        ctx.Output.WriteLine($"Type:     {hb.Type}");
        ctx.Output.WriteLine($"Mode:     {modeName}");
        ctx.Output.WriteLine($"Armed:    {(hb.Armed ? "YES" : "NO")}");
        ctx.Output.WriteLine($"Position: {pos.Latitude:F6}, {pos.Longitude:F6}");
        ctx.Output.WriteLine($"Alt:      {pos.AltitudeRel:F1} m AGL / {pos.AltitudeMsl:F1} m MSL");
        ctx.Output.WriteLine($"Heading:  {pos.Heading:F0} deg");
        ctx.Output.WriteLine($"Battery:  {bat.Voltage:F1} V  {bat.Current:F1} A  {bat.Remaining}%");

        return Task.CompletedTask;
    }
}
