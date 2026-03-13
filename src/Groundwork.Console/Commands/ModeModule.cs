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
/// Provides flight mode commands matching MAVProxy's mode module.
/// </summary>
public sealed class ModeModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "mode",
            new DelegateCommand("Change flight mode", "mode <name>", ModeAsync)
        );
    }

    private static async Task ModeAsync(string[] args, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        if (args.Length < 1)
        {
            var modes = vehicle.AvailableModes;
            ctx.Output.WriteLine(
                modes.Count > 0
                    ? $"Available modes: {string.Join(", ", modes.Values)}"
                    : "No mode mapping available"
            );
            return;
        }

        // Join args to handle mode names with spaces (e.g., "ALT HOLD").
        var modeName = string.Join(' ', args);

        // Accept numeric mode numbers (e.g., "mode 5").
        uint? modeNum = uint.TryParse(modeName, out var numeric)
            ? numeric
            : vehicle.NameToMode(modeName);

        if (modeNum is null)
        {
            ctx.Output.WriteLine($"Unknown mode: {modeName}");
            return;
        }

        var result = await vehicle.SetModeAsync(modeNum.Value, ctx.ShutdownToken);

        if (result == MAVLink.MAV_RESULT.ACCEPTED)
            ctx.Output.WriteLine($"Mode: {vehicle.ModeToName(modeNum.Value)}");
        else
            ctx.Output.WriteLine($"Mode change to {modeName.ToUpperInvariant()} failed: {result}");
    }
}
