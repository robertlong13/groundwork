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
/// Provides arm and disarm commands matching MAVProxy's arm module.
/// </summary>
public sealed class ArmModule
{
    private static readonly string[] ArmingCheckNames =
    [
        "all",
        "baro",
        "compass",
        "gps",
        "ins",
        "params",
        "rc",
        "voltage",
        "battery",
        "airspeed",
        "logging",
        "switch",
        "gps_config",
        "system",
        "mission",
        "rangefinder",
        "unknown16",
        "unknown17",
        "unknown18",
        "unknown19",
        "unknown20",
        "unknown21",
        "unknown22",
        "unknown23",
        "unknown24",
    ];

    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "arm throttle",
            new DelegateCommand(
                "Arm the vehicle",
                "arm throttle",
                (args, ctx) => ArmDisarmAsync(arm: true, force: false, args, ctx)
            )
        );

        commands.Register(
            "arm throttle force",
            new DelegateCommand(
                "Arm (bypass safety checks)",
                "arm throttle force",
                (args, ctx) => ArmDisarmAsync(arm: true, force: true, args, ctx)
            )
        );

        commands.Register(
            "disarm",
            new DelegateCommand(
                "Disarm the vehicle",
                "disarm",
                (args, ctx) => ArmDisarmAsync(arm: false, force: false, args, ctx)
            )
        );

        commands.Register(
            "disarm force",
            new DelegateCommand(
                "Disarm (force)",
                "disarm force",
                (args, ctx) => ArmDisarmAsync(arm: false, force: true, args, ctx)
            )
        );

        commands.Register(
            "arm safetyon",
            new DelegateCommand(
                "Enable safety switch",
                "arm safetyon",
                (args, ctx) => SafetyAsync(on: true, ctx)
            )
        );

        commands.Register(
            "arm safetyoff",
            new DelegateCommand(
                "Disable safety switch",
                "arm safetyoff",
                (args, ctx) => SafetyAsync(on: false, ctx)
            )
        );

        commands.Register(
            "arm prearms",
            new DelegateCommand(
                "Run pre-arm checks",
                "arm prearms",
                (args, ctx) => PrearmsAsync(ctx)
            )
        );

        commands.Register(
            "arm bits",
            new DelegateCommand("List arming check names", "arm bits", (args, ctx) => Bits(ctx))
        );
    }

    private static async Task ArmDisarmAsync(
        bool arm,
        bool force,
        string[] args,
        CommandContext ctx
    )
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        // MAVProxy disarms to component 0 (all components), not just the autopilot.
        var result = arm
            ? await vehicle.ArmAsync(force, ctx.ShutdownToken)
            : await vehicle.DisarmAsync(force, targetComponent: 0, ctx.ShutdownToken);

        var verb = arm ? "Arm" : "Disarm";
        ctx.Output.WriteLine(
            result == MAVLink.MAV_RESULT.ACCEPTED ? $"{verb}ed" : $"{verb} failed: {result}"
        );
    }

    private static async Task SafetyAsync(bool on, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        await vehicle.SetSafetyAsync(on, ctx.ShutdownToken);
        ctx.Output.WriteLine(on ? "Safety ON" : "Safety OFF");
    }

    private static async Task PrearmsAsync(CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var result = await vehicle.SendCommandAsync(
            MAVLink.MAV_CMD.RUN_PREARM_CHECKS,
            ct: ctx.ShutdownToken
        );

        ctx.Output.WriteLine(
            result == MAVLink.MAV_RESULT.ACCEPTED
                ? "Pre-arm checks requested"
                : $"Pre-arm checks failed: {result}"
        );
    }

    private static Task Bits(CommandContext ctx)
    {
        foreach (var name in ArmingCheckNames)
            ctx.Output.WriteLine($"  {name}");

        return Task.CompletedTask;
    }
}
