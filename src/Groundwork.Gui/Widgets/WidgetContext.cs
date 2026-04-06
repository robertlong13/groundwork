// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Groundwork.Core.Reactive;

namespace Groundwork.Gui.Widgets;

/// <summary>
/// Provides the default widget context using the Avalonia UI thread scheduler.
/// </summary>
public sealed class WidgetContext : IWidgetContext
{
    public WidgetContext(IStreamRegistry streams, TimeSpan? defaultSampleInterval = null)
    {
        Streams = streams;
        DefaultSampleInterval = defaultSampleInterval ?? TimeSpan.FromMilliseconds(33); // ~30fps
    }

    public IStreamRegistry Streams { get; }

    public IScheduler Scheduler { get; } = DispatcherScheduler.Instance;

    public TimeSpan DefaultSampleInterval { get; }
}

/// <summary>
/// Provides an IScheduler that dispatches work to the Avalonia UI thread.
/// </summary>
internal sealed class DispatcherScheduler : IScheduler
{
    public static readonly DispatcherScheduler Instance = new();

    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        var d = new SingleAssignmentDisposable();
        Dispatcher.UIThread.Post(() =>
        {
            if (!d.IsDisposed)
                d.Disposable = action(this, state);
        });
        return d;
    }

    public IDisposable Schedule<TState>(
        TState state,
        TimeSpan dueTime,
        Func<IScheduler, TState, IDisposable> action
    )
    {
        var d = new SingleAssignmentDisposable();
        var timer = new System.Threading.Timer(
            _ =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!d.IsDisposed)
                        d.Disposable = action(this, state);
                }),
            null,
            dueTime,
            Timeout.InfiniteTimeSpan
        );
        return new CompositeDisposable(d, Disposable.Create(() => timer.Dispose()));
    }

    public IDisposable Schedule<TState>(
        TState state,
        DateTimeOffset dueTime,
        Func<IScheduler, TState, IDisposable> action
    )
    {
        return Schedule(state, dueTime - Now, action);
    }
}
