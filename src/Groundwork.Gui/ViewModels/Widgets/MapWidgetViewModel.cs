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
using MapControl;

namespace Groundwork.Gui.ViewModels.Widgets;

/// <summary>
/// Provides a slippy map widget displaying tile layers and vehicle position.
/// </summary>
public partial class MapWidgetViewModel : WidgetViewModelBase
{
    private const string MapTilerBase = "https://api.maptiler.com/maps";
    private bool _hasAutoCentered;

    public override string TypeId => "groundwork.map";
    public override string DisplayName => "Map";

    [ObservableProperty]
    private Location? _vehicleLocation;

    [ObservableProperty]
    private float _vehicleHeading;

    /// <summary>
    /// Gets or sets the map camera center. Two-way bound to the map control
    /// so user pans persist across view rebuilds.
    /// </summary>
    [ObservableProperty]
    private Location _cameraCenter = new(0, 0);

    /// <summary>
    /// Gets or sets the map zoom level. Two-way bound to the map control.
    /// </summary>
    [ObservableProperty]
    private double _cameraZoom = 2;

    /// <summary>
    /// Gets the tile source URL (MapTiler if key available, otherwise OSM).
    /// </summary>
    [ObservableProperty]
    private string _tileUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>
    /// Gets the tile source name used as the disk cache key.
    /// </summary>
    [ObservableProperty]
    private string _tileUrlName = "OpenStreetMap";

    public override void OnAttached(IWidgetContext context)
    {
        base.OnAttached(context);

        var key = context.Config.EffectiveMapTilerKey;
        if (!string.IsNullOrEmpty(key))
        {
            TileUrl = $"{MapTilerBase}/hybrid-v4/{{z}}/{{x}}/{{y}}.jpg?key={key}";
            TileUrlName = "MapTiler.Hybrid";
        }

        Observe(
            context.Streams.Get<PositionState>("position"),
            pos =>
            {
                VehicleLocation = new Location(pos.Latitude, pos.Longitude);
                VehicleHeading = pos.Heading;

                if (!_hasAutoCentered && (pos.Latitude != 0 || pos.Longitude != 0))
                {
                    CameraCenter = VehicleLocation;
                    CameraZoom = 16;
                    _hasAutoCentered = true;
                }
            }
        );
    }
}
