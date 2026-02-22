// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Protocol;

namespace Groundwork.Console.Commands;

/// <summary>
/// Provides RC channel override commands matching MAVProxy's rc module.
/// </summary>
/// <remarks>
/// Active overrides are sent at 10 Hz. Set pwm to 0 to release a channel.
/// </remarks>
public sealed class RcModule
{
    private readonly Dictionary<int, ushort> _overrides = new();
    private readonly Lock _lock = new();
    private readonly CancellationToken _appShutdown;
    private CancellationTokenSource? _senderCts;
    private Task? _senderTask;

    public RcModule(CancellationToken appShutdown)
    {
        _appShutdown = appShutdown;
    }

    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "rc",
            new DelegateCommand(
                "Set RC channel override (0 to release)",
                "rc <channel> <pwm>",
                RcAsync
            )
        );

        commands.Register(
            "rc all",
            new DelegateCommand("Set all RC channels (0 to release)", "rc all <pwm>", RcAllAsync)
        );

        commands.Register(
            "rc clear",
            new DelegateCommand("Release all RC overrides", "rc clear", RcClearAsync)
        );
    }

    private async Task RcAsync(string[] args, CommandContext ctx)
    {
        if (
            args.Length < 2
            || !int.TryParse(args[0], out var channel)
            || !ushort.TryParse(args[1], out var pwm)
        )
        {
            ctx.Output.WriteLine("Usage: rc <channel 1-18> <pwm> (0 to release)");
            PrintActiveOverrides(ctx.Output);
            return;
        }

        if (channel < 1 || channel > RcOverride.MaxChannels)
        {
            ctx.Output.WriteLine("Channel must be 1-18");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        Task? toAwait = null;

        lock (_lock)
        {
            if (pwm == 0)
            {
                _overrides.Remove(channel);
                ctx.Output.WriteLine($"RC {channel} released");
            }
            else
            {
                _overrides[channel] = pwm;
                ctx.Output.WriteLine($"RC {channel} = {pwm}");
            }

            if (_overrides.Count > 0 && _senderTask is null)
                StartSender(ctx);
            else if (_overrides.Count == 0)
                toAwait = StopSender();
        }

        if (toAwait is not null)
            await toAwait.ConfigureAwait(false);
    }

    private async Task RcAllAsync(string[] args, CommandContext ctx)
    {
        if (args.Length < 1 || !ushort.TryParse(args[0], out var pwm))
        {
            ctx.Output.WriteLine("Usage: rc all <pwm> (0 to release all)");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        Task? toAwait = null;

        lock (_lock)
        {
            if (pwm == 0)
            {
                _overrides.Clear();
                toAwait = StopSender();
                ctx.Output.WriteLine("All RC channels released");
            }
            else
            {
                for (var ch = 1; ch <= RcOverride.MaxChannels; ch++)
                    _overrides[ch] = pwm;

                ctx.Output.WriteLine($"All RC channels = {pwm}");

                if (_senderTask is null)
                    StartSender(ctx);
            }
        }

        if (toAwait is not null)
            await toAwait.ConfigureAwait(false);
    }

    private async Task RcClearAsync(string[] args, CommandContext ctx)
    {
        Task? toAwait;

        lock (_lock)
        {
            _overrides.Clear();
            toAwait = StopSender();
        }

        ctx.Output.WriteLine("All RC channels released");

        if (toAwait is not null)
            await toAwait.ConfigureAwait(false);
    }

    private void PrintActiveOverrides(TextWriter output)
    {
        lock (_lock)
        {
            if (_overrides.Count == 0)
            {
                output.WriteLine("No active overrides");
                return;
            }

            foreach (var (ch, val) in _overrides.OrderBy(kv => kv.Key))
                output.WriteLine($"  RC {ch} = {val}");
        }
    }

    private void StartSender(CommandContext ctx)
    {
        _senderCts = CancellationTokenSource.CreateLinkedTokenSource(_appShutdown);
        _senderTask = RunSenderAsync(ctx, _senderCts.Token);
    }

    private Task? StopSender()
    {
        var task = _senderTask;
        var oldCts = _senderCts;
        _senderCts?.Cancel();
        _senderCts = null;
        _senderTask = null;

        // Dispose after the sender exits; disposing while it's still awaiting
        // WaitForNextTickAsync causes ObjectDisposedException.
        if (task is not null && oldCts is not null)
            return task.ContinueWith(_ => oldCts.Dispose(), TaskScheduler.Default);

        oldCts?.Dispose();
        return task;
    }

    private async Task RunSenderAsync(CommandContext ctx, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var vehicle = ctx.CurrentVehicle;
                if (vehicle is null)
                    continue;

                MAVLink.mavlink_rc_channels_override_t msg;
                lock (_lock)
                {
                    if (_overrides.Count == 0)
                        break; // No overrides left.

                    msg = RcOverride.BuildMessage(vehicle.SysId, _overrides);
                }

                try
                {
                    await vehicle
                        .SendAsync(MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE, msg, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Send failure -- retry on next tick.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown or StopSender.
        }
    }
}
