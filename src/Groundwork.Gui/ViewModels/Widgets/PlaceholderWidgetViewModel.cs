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

namespace Groundwork.Gui.ViewModels.Widgets;

/// <summary>
/// Provides a simple label widget for testing the widget tree.
/// </summary>
public partial class PlaceholderWidgetViewModel : WidgetViewModelBase
{
    public override string TypeId => "groundwork.placeholder";

    public override string DisplayName => Label;

    /// <summary>
    /// Gets or sets the text label to display.
    /// </summary>
    [ObservableProperty]
    private string _label = "Placeholder";
}
