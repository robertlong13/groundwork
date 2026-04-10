// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reactive.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Groundwork.Core.Channels;
using Groundwork.Core.Connections;
using Groundwork.Core.Reactive;
using Groundwork.Core.Vehicles;
using Groundwork.Gui.ViewModels;
using Groundwork.Gui.ViewModels.Widgets;
using Groundwork.Gui.Views;
using Groundwork.Gui.Views.Widgets;
using Groundwork.Gui.Widgets;
using MapControl;
using MapControl.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groundwork.Gui;

public partial class App : Avalonia.Application
{
    private LinkManager? _links;
    private ImageFileCache? _tileCache;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DataTemplates.Add(BuildViewLocator());

        ImageLoader.HttpClient.DefaultRequestHeaders.Add("User-Agent", "Groundwork GCS");
        _tileCache = new ImageFileCache(TileImageLoader.DefaultCacheFolder);
        TileImageLoader.Cache = _tileCache;
        TileImageLoader.DefaultCacheExpiration = TimeSpan.FromDays(90);
        TileImageLoader.MaxCacheExpiration = TimeSpan.FromDays(90);

        var config = AppConfig.Load();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        var vehicleRegistry = new VehicleRegistry(
            loggerFactory,
            new Dictionary<MAVLink.MAV_DATA_STREAM, int>
            {
                [MAVLink.MAV_DATA_STREAM.EXTRA1] = 10,
                [MAVLink.MAV_DATA_STREAM.POSITION] = 5,
                [MAVLink.MAV_DATA_STREAM.EXTENDED_STATUS] = 2,
                [MAVLink.MAV_DATA_STREAM.RC_CHANNELS] = 2,
                [MAVLink.MAV_DATA_STREAM.EXTRA2] = 2,
                [MAVLink.MAV_DATA_STREAM.EXTRA3] = 2,
            }
        );
        var channelRegistry = new MavChannelRegistry();
        var streams = new StreamRegistry();
        _links = new LinkManager(channelRegistry, vehicleRegistry, loggerFactory);

        var widgetContext = new WidgetContext(config, streams);

        // Default streams from the selected vehicle. Bare names
        // (no prefix) since 99% of widgets use these. Registered
        // eagerly; Switch() defers to the real vehicle on discovery.
        var selectedVehicle = vehicleRegistry.Discovered.Take(1).Replay(1);
        selectedVehicle.Connect();

        streams.Register(
            "heartbeat",
            selectedVehicle
                .Select(v => (IObservable<HeartbeatState>)v.CanonicalState.Heartbeat)
                .Switch(),
            "Heartbeat state"
        );
        streams.Register(
            "position",
            selectedVehicle
                .Select(v => (IObservable<PositionState>)v.CanonicalState.Position)
                .Switch(),
            "Position and heading"
        );
        streams.Register(
            "battery",
            selectedVehicle
                .Select(v => (IObservable<BatteryState>)v.CanonicalState.Battery)
                .Switch(),
            "Battery telemetry"
        );

        var vm = new MainWindowViewModel(config, _links, widgetContext);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += async (_, _) =>
            {
                _tileCache?.Dispose();
                if (_links is not null)
                    await _links.DisposeAsync();
            };
        }

        var settingsVm = (SettingsViewModel)vm.Pages[^1];
        _ = ConnectConfiguredLinksAsync(config, _links, settingsVm);

        base.OnFrameworkInitializationCompleted();
    }

    private static ViewLocator BuildViewLocator()
    {
        var locator = new ViewLocator();
        locator.Register<FlightViewModel, FlightView>();
        locator.Register<PlanViewModel, PlanView>();
        locator.Register<ConfigViewModel, ConfigView>();
        locator.Register<SettingsViewModel, SettingsView>();
        locator.Register<SplitWidgetViewModel, SplitWidgetView>();
        locator.Register<TabWidgetViewModel, TabWidgetView>();
        locator.Register<QuickWidgetViewModel, QuickWidgetView>();
        locator.Register<MapWidgetViewModel, MapWidgetView>();
        locator.Register<PlaceholderWidgetViewModel, PlaceholderWidgetView>();
        return locator;
    }

    private static async Task ConnectConfiguredLinksAsync(
        AppConfig config,
        LinkManager links,
        SettingsViewModel settingsVm
    )
    {
        for (var i = 0; i < config.Links.Count; i++)
        {
            try
            {
                var channel = await links.AddAsync(config.Links[i]);
                settingsVm.MarkConnected(i, channel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(
                    "Failed to connect to '{0}': {1}",
                    config.Links[i],
                    ex.Message
                );
            }
        }
    }
}
