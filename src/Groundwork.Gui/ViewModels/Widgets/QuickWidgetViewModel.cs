// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CommunityToolkit.Mvvm.ComponentModel;
using Groundwork.Core.Vehicles;
using Groundwork.Gui.Widgets;
using ArduPilot = Groundwork.Core.ArduPilot;

namespace Groundwork.Gui.ViewModels.Widgets;

/// <summary>
/// Provides a quick-look telemetry readout with key numeric values.
/// </summary>
public partial class QuickWidgetViewModel : WidgetViewModelBase
{
    public override string TypeId => "groundwork.quick";
    public override string DisplayName => "Quick";

    [ObservableProperty]
    private string _mode = "--";

    [ObservableProperty]
    private bool _armed;

    [ObservableProperty]
    private string _altitude = "--";

    [ObservableProperty]
    private string _heading = "--";

    [ObservableProperty]
    private string _voltage = "--";

    [ObservableProperty]
    private string _current = "--";

    [ObservableProperty]
    private string _batteryRemaining = "--";

    public override void OnAttached(IWidgetContext context)
    {
        base.OnAttached(context);
        SubscribeHeartbeat(context);
        SubscribePosition(context);
        SubscribeBattery(context);
    }

    private void SubscribeHeartbeat(IWidgetContext context)
    {
        Observe(
            context.Streams.Get<HeartbeatState>("heartbeat"),
            hb =>
            {
                Mode = ArduPilot.ModeMap.ModeToName(hb.CustomMode, hb.Type);
                Armed = hb.Armed;
            }
        );
    }

    private void SubscribePosition(IWidgetContext context)
    {
        Observe(
            context.Streams.Get<PositionState>("position"),
            pos =>
            {
                Altitude = $"{pos.AltitudeRel:F1} m";
                Heading = $"{pos.Heading:F0}";
            }
        );
    }

    private void SubscribeBattery(IWidgetContext context)
    {
        Observe(
            context.Streams.Get<BatteryState>("battery"),
            bat =>
            {
                Voltage = $"{bat.Voltage:F1} V";
                Current = $"{bat.Current:F1} A";
                BatteryRemaining = bat.Remaining >= 0 ? $"{bat.Remaining}%" : "--";
            }
        );
    }
}
