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
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Groundwork.Gui.Widgets;

namespace Groundwork.Gui.ViewModels.Widgets;

/// <summary>
/// Provides a base class for all widget view models.
/// </summary>
public abstract class WidgetViewModelBase : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _subscriptions = new();
    private IWidgetContext? _context;

    /// <summary>
    /// Gets the widget type identifier.
    /// </summary>
    public abstract string TypeId { get; }

    /// <summary>
    /// Gets the display name for this widget instance.
    /// </summary>
    public virtual string DisplayName => TypeId;

    /// <summary>
    /// Gets the minimum size for this widget.
    /// </summary>
    public virtual Size MinimumSize => new(100, 100);

    /// <summary>
    /// Gets the overlay widgets anchored on top of this widget.
    /// </summary>
    public ObservableCollection<OverlayEntry> Overlays { get; init; } = [];

    /// <summary>
    /// Called when the widget is attached to the widget tree.
    /// </summary>
    public virtual void OnAttached(IWidgetContext context)
    {
        _context = context;
        foreach (var overlay in Overlays)
            overlay.Widget.OnAttached(context);
    }

    /// <summary>
    /// Called when the widget is detached from the widget tree.
    /// </summary>
    public virtual void OnDetached()
    {
        foreach (var overlay in Overlays)
            overlay.Widget.OnDetached();
        _subscriptions.Clear();
        _context = null;
    }

    /// <summary>
    /// Subscribes to a stream with automatic rate limiting and UI thread dispatch.
    /// </summary>
    protected IDisposable Observe<T>(IObservable<T> stream, Action<T> onNext)
    {
        if (_context is null)
            throw new InvalidOperationException("Widget is not attached");

        var sub = stream
            .Sample(_context.DefaultSampleInterval)
            .ObserveOn(_context.Scheduler)
            .Subscribe(onNext);
        Track(sub);
        return sub;
    }

    /// <summary>
    /// Tracks a subscription for automatic disposal.
    /// </summary>
    protected void Track(IDisposable subscription) => _subscriptions.Add(subscription);

    public void Dispose()
    {
        OnDetached();
        _subscriptions.Dispose();
    }
}
