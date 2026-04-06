// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Groundwork.Gui.ViewModels.Widgets;

/// <summary>
/// Provides a container widget that splits its area between two children.
/// </summary>
public partial class SplitWidgetViewModel : WidgetViewModelBase
{
    public override string TypeId => "groundwork.split";

    /// <summary>
    /// Gets or sets the first child widget.
    /// </summary>
    [ObservableProperty]
    private WidgetViewModelBase? _first;

    /// <summary>
    /// Gets or sets the second child widget.
    /// </summary>
    [ObservableProperty]
    private WidgetViewModelBase? _second;

    /// <summary>
    /// Gets or sets the split orientation.
    /// </summary>
    [ObservableProperty]
    private Orientation _orientation = Orientation.Horizontal;

    /// <summary>
    /// Gets or sets the split ratio (0-1), representing the proportion
    /// of space allocated to the first child.
    /// </summary>
    [ObservableProperty]
    private double _ratio = 0.5;
}
