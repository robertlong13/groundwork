// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

#if LOSSY_LINK

using Groundwork.Core.Connections;

namespace Groundwork.Console.Commands;

/// <summary>
/// Provides commands for controlling link impairment via <see cref="LossyConnection"/>.
/// </summary>
public sealed class LossyModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "lossy status",
            new DelegateCommand(
                "Show lossy link status",
                "lossy status [index]",
                (args, ctx) => Status(args, ctx)
            )
        );

        commands.Register(
            "lossy uplink",
            new DelegateCommand(
                "Toggle uplink on/off",
                "lossy uplink [index]",
                (args, ctx) => ToggleUplink(args, ctx)
            )
        );

        commands.Register(
            "lossy downlink",
            new DelegateCommand(
                "Toggle downlink on/off",
                "lossy downlink [index]",
                (args, ctx) => ToggleDownlink(args, ctx)
            )
        );

        commands.Register(
            "lossy rxcorrupt",
            new DelegateCommand(
                "Set inbound byte corruption rate (%)",
                "lossy rxcorrupt [index] <percent>",
                (args, ctx) => SetCorrupt(args, ctx)
            )
        );

        commands.Register(
            "lossy rxpacketloss",
            new DelegateCommand(
                "Set inbound corruption rate from target packet loss %",
                "lossy rxpacketloss [index] <percent>",
                (args, ctx) => SetPacketLoss(args, ctx)
            )
        );

        commands.Register(
            "lossy txdrop",
            new DelegateCommand(
                "Set outbound per-packet drop rate (%)",
                "lossy txdrop [index] <percent>",
                (args, ctx) => SetSendDrop(args, ctx)
            )
        );

        commands.Register(
            "lossy latency",
            new DelegateCommand(
                "Set symmetric one-way latency in ms",
                "lossy latency [index] <ms>",
                (args, ctx) => SetLatency(args, ctx)
            )
        );
    }

    private static Task Status(string[] args, CommandContext ctx)
    {
        if (ctx.Links.Count == 0)
        {
            ctx.Output.WriteLine("No active links");
            return Task.CompletedTask;
        }

        int start = 0,
            end = ctx.Links.Count;
        if (args.Length > 0 && int.TryParse(args[0], out var idx))
        {
            if (!ValidIndex(idx, ctx))
                return Task.CompletedTask;
            start = idx;
            end = idx + 1;
        }

        for (var i = start; i < end; i++)
        {
            var lossy = ctx.Links.GetConnection(i) as LossyConnection;
            if (lossy is null)
            {
                ctx.Output.WriteLine($"  {i}: {ctx.Links.GetChannel(i).Name} (not lossy)");
                continue;
            }

            ctx.Output.WriteLine(
                $"  {i}: {lossy.Name}  "
                    + $"uplink={(!lossy.UplinkDown ? "up" : "DOWN")}  "
                    + $"downlink={(!lossy.DownlinkDown ? "up" : "DOWN")}  "
                    + $"rxcorrupt={lossy.CorruptRate:P3}  "
                    + $"txdrop={lossy.SendDropRate:P1}  "
                    + $"latency={lossy.Latency.TotalMilliseconds:F0}ms"
            );
        }

        return Task.CompletedTask;
    }

    private static Task ToggleUplink(string[] args, CommandContext ctx)
    {
        if (!TryGetLossy(args, ctx, out var lossy, out var idx))
            return Task.CompletedTask;

        lossy.UplinkDown = !lossy.UplinkDown;
        ctx.Output.WriteLine($"Link {idx} uplink: {(!lossy.UplinkDown ? "up" : "DOWN")}");
        return Task.CompletedTask;
    }

    private static Task ToggleDownlink(string[] args, CommandContext ctx)
    {
        if (!TryGetLossy(args, ctx, out var lossy, out var idx))
            return Task.CompletedTask;

        lossy.DownlinkDown = !lossy.DownlinkDown;
        ctx.Output.WriteLine($"Link {idx} downlink: {(!lossy.DownlinkDown ? "up" : "DOWN")}");
        return Task.CompletedTask;
    }

    private static Task SetCorrupt(string[] args, CommandContext ctx)
    {
        if (!TryGetLossyAndValue(args, ctx, out var lossy, out var idx, out var value))
            return Task.CompletedTask;

        lossy.CorruptRate = value / 100.0;
        ctx.Output.WriteLine($"Link {idx} rxcorrupt: {lossy.CorruptRate:P3}");
        return Task.CompletedTask;
    }

    private static Task SetPacketLoss(string[] args, CommandContext ctx)
    {
        if (!TryGetLossyAndValue(args, ctx, out var lossy, out var idx, out var value))
            return Task.CompletedTask;

        lossy.SetPacketLossRate(value / 100.0);
        ctx.Output.WriteLine(
            $"Link {idx} target packet loss: {value}% (byte corrupt rate: {lossy.CorruptRate:P4})"
        );
        return Task.CompletedTask;
    }

    private static Task SetLatency(string[] args, CommandContext ctx)
    {
        if (!TryGetLossyAndValue(args, ctx, out var lossy, out var idx, out var value))
            return Task.CompletedTask;

        lossy.Latency = TimeSpan.FromMilliseconds(value);
        ctx.Output.WriteLine($"Link {idx} latency: {value}ms one-way");
        return Task.CompletedTask;
    }

    private static Task SetSendDrop(string[] args, CommandContext ctx)
    {
        if (!TryGetLossyAndValue(args, ctx, out var lossy, out var idx, out var value))
            return Task.CompletedTask;

        lossy.SendDropRate = value / 100.0;
        ctx.Output.WriteLine($"Link {idx} txdrop: {lossy.SendDropRate:P1}");
        return Task.CompletedTask;
    }

    private static bool TryGetLossy(
        string[] args,
        CommandContext ctx,
        out LossyConnection lossy,
        out int index
    )
    {
        lossy = null!;
        index = 0;

        if (args.Length > 0 && int.TryParse(args[0], out index))
        {
            if (!ValidIndex(index, ctx))
                return false;
        }
        else if (ctx.Links.Count == 0)
        {
            ctx.Output.WriteLine("No active links");
            return false;
        }

        if (ctx.Links.GetConnection(index) is not LossyConnection lc)
        {
            ctx.Output.WriteLine($"Link {index} is not a lossy connection");
            return false;
        }

        lossy = lc;
        return true;
    }

    private static bool TryGetLossyAndValue(
        string[] args,
        CommandContext ctx,
        out LossyConnection lossy,
        out int index,
        out double value
    )
    {
        lossy = null!;
        index = 0;
        value = 0;

        // "lossy <cmd> <value>" or "lossy <cmd> <index> <value>"
        if (args.Length >= 2 && int.TryParse(args[0], out index))
        {
            if (!double.TryParse(args[1], out value))
            {
                ctx.Output.WriteLine($"Invalid value: {args[1]}");
                return false;
            }
        }
        else if (args.Length >= 1 && double.TryParse(args[0], out value))
        {
            index = 0;
        }
        else
        {
            ctx.Output.WriteLine("Usage requires a numeric value");
            return false;
        }

        if (!ValidIndex(index, ctx))
            return false;

        if (ctx.Links.GetConnection(index) is not LossyConnection lc)
        {
            ctx.Output.WriteLine($"Link {index} is not a lossy connection");
            return false;
        }

        lossy = lc;
        return true;
    }

    private static bool ValidIndex(int index, CommandContext ctx)
    {
        if (index < 0 || index >= ctx.Links.Count)
        {
            ctx.Output.WriteLine($"Invalid link index: {index}");
            return false;
        }

        return true;
    }
}

#endif
