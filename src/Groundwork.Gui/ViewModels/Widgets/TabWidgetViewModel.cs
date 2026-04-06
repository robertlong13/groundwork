// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Groundwork.Gui.Widgets;

namespace Groundwork.Gui.ViewModels.Widgets;

/// <summary>
/// Provides a container widget that displays children as tabs.
/// </summary>
public partial class TabWidgetViewModel : WidgetViewModelBase
{
    public override string TypeId => "groundwork.tab";

    public override void OnAttached(IWidgetContext context)
    {
        base.OnAttached(context);
        foreach (var child in Children)
            child.OnAttached(context);
    }

    public override void OnDetached()
    {
        foreach (var child in Children)
            child.OnDetached();
        base.OnDetached();
    }

    /// <summary>
    /// Gets the child widgets displayed as tabs.
    /// </summary>
    public ObservableCollection<WidgetViewModelBase> Children { get; init; } = [];

    /// <summary>
    /// Gets or sets the currently selected child.
    /// </summary>
    [ObservableProperty]
    private WidgetViewModelBase? _selectedChild;
}
