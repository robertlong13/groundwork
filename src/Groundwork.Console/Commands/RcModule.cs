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
/// Active overrides are sent at 10 Hz. After clearing, zeros are sent for
/// an additional flush period to ensure the vehicle receives the release.
/// </remarks>
public sealed class RcModule
{
    /// <summary>
    /// Number of extra zero-packets sent after clearing to ensure release on lossy links.
    /// </summary>
    private const int FlushTicks = 10;

    private readonly Dictionary<int, ushort> _overrides = new();
    private readonly Lock _lock = new();
    private readonly CancellationToken _appShutdown;
    private CancellationTokenSource? _senderCts;
    private Task? _senderTask;
    private int _flushRemaining;

    public RcModule(CancellationToken appShutdown)
    {
        _appShutdown = appShutdown;
    }

    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "rc",
            new DelegateCommand(
                "Set RC channel override (0 to release, -1 to ignore)",
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
        if (args.Length < 2 || !int.TryParse(args[0], out var channel))
        {
            ctx.Output.WriteLine("Usage: rc <channel 1-18> <pwm> (0 to release, -1 to ignore)");
            PrintActiveOverrides(ctx.Output);
            return;
        }

        if (!TryParsePwm(args[1], out var pwm))
        {
            ctx.Output.WriteLine($"Invalid PWM value: {args[1]}");
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

        MAVLink.mavlink_rc_channels_override_t msg;

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

            _flushRemaining = FlushTicks;
            EnsureSenderRunning(ctx);
            msg = RcOverride.BuildMessage(vehicle.SysId, _overrides);
        }

        await SendQuietly(vehicle, msg, ctx.ShutdownToken).ConfigureAwait(false);
    }

    private async Task RcAllAsync(string[] args, CommandContext ctx)
    {
        if (!TryParsePwm(args.Length > 0 ? args[0] : "", out var pwm))
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

        MAVLink.mavlink_rc_channels_override_t msg;

        lock (_lock)
        {
            if (pwm == 0)
            {
                _overrides.Clear();
                ctx.Output.WriteLine("All RC channels released");
            }
            else
            {
                for (var ch = 1; ch <= RcOverride.MaxChannels; ch++)
                    _overrides[ch] = pwm;

                ctx.Output.WriteLine($"All RC channels = {pwm}");
            }

            _flushRemaining = FlushTicks;
            EnsureSenderRunning(ctx);
            msg = RcOverride.BuildMessage(vehicle.SysId, _overrides);
        }

        await SendQuietly(vehicle, msg, ctx.ShutdownToken).ConfigureAwait(false);
    }

    private async Task RcClearAsync(string[] args, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;

        MAVLink.mavlink_rc_channels_override_t? msg = null;

        lock (_lock)
        {
            _overrides.Clear();
            _flushRemaining = FlushTicks;
            if (vehicle is not null)
            {
                EnsureSenderRunning(ctx);
                msg = RcOverride.BuildMessage(vehicle.SysId, _overrides);
            }
        }

        ctx.Output.WriteLine("All RC channels released");

        if (msg.HasValue && vehicle is not null)
            await SendQuietly(vehicle, msg.Value, ctx.ShutdownToken).ConfigureAwait(false);
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

    /// <summary>
    /// Parses a PWM value, mapping -1 to 65535 (the RC_CHANNELS_OVERRIDE ignore sentinel).
    /// </summary>
    private static bool TryParsePwm(string text, out ushort pwm)
    {
        if (int.TryParse(text, out var raw))
        {
            if (raw == -1)
                raw = 65535;

            if (raw >= 0 && raw <= 65535)
            {
                pwm = (ushort)raw;
                return true;
            }
        }

        pwm = 0;
        return false;
    }

    /// <summary>
    /// Starts the sender if it is not already running. Must be called under <see cref="_lock"/>.
    /// </summary>
    private void EnsureSenderRunning(CommandContext ctx)
    {
        if (_senderTask is not null && !_senderTask.IsCompleted)
            return;

        _senderCts?.Dispose();
        _senderCts = CancellationTokenSource.CreateLinkedTokenSource(_appShutdown);
        _senderTask = RunSenderAsync(ctx, _senderCts.Token);
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
                    if (_overrides.Count == 0 && _flushRemaining <= 0)
                        break;

                    msg = RcOverride.BuildMessage(vehicle.SysId, _overrides);

                    if (_overrides.Count == 0)
                        _flushRemaining--;
                }

                await SendQuietly(vehicle, msg, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private static async Task SendQuietly(
        Groundwork.Core.Vehicles.Vehicle vehicle,
        MAVLink.mavlink_rc_channels_override_t msg,
        CancellationToken ct
    )
    {
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
            // Send failure -- will retry on next tick.
        }
    }
}
