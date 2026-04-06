// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Specialized;
using Avalonia.Controls;
using Groundwork.Gui.ViewModels.Widgets;

namespace Groundwork.Gui.Views.Widgets;

/// <summary>
/// Provides the widget-tree mount point. Resolves the inner view for its
/// widget view model and layers anchored overlays on top.
/// </summary>
public class WidgetHost : UserControl
{
    private readonly AnchorPanel _panel = new();
    private readonly ContentControl _innerHost = new();
    private INotifyCollectionChanged? _observedOverlays;

    public WidgetHost()
    {
        Content = _panel;
        _panel.Children.Add(_innerHost);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedOverlays is not null)
            _observedOverlays.CollectionChanged -= OnOverlaysChanged;
        _observedOverlays = null;

        if (DataContext is not WidgetViewModelBase vm)
        {
            ClearOverlays();
            _innerHost.Content = null;
            return;
        }

        _innerHost.Content = vm;
        RebuildOverlays(vm);

        _observedOverlays = vm.Overlays;
        _observedOverlays.CollectionChanged += OnOverlaysChanged;
    }

    private void OnOverlaysChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is WidgetViewModelBase vm)
            RebuildOverlays(vm);
    }

    private void ClearOverlays()
    {
        // _innerHost stays at index 0.
        while (_panel.Children.Count > 1)
            _panel.Children.RemoveAt(_panel.Children.Count - 1);
    }

    private void RebuildOverlays(WidgetViewModelBase vm)
    {
        ClearOverlays();
        foreach (var overlay in vm.Overlays)
        {
            var overlayHost = new WidgetHost { DataContext = overlay.Widget };
            AnchorPanel.SetPlacement(overlayHost, overlay.Placement);
            _panel.Children.Add(overlayHost);
        }
    }
}
