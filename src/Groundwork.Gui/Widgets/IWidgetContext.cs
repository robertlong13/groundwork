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
using Groundwork.Core.Reactive;

namespace Groundwork.Gui.Widgets;

/// <summary>
/// Defines the context provided to widgets when they attach to the tree.
/// </summary>
public interface IWidgetContext
{
    /// <summary>
    /// Gets the application configuration.
    /// </summary>
    AppConfig Config { get; }

    /// <summary>
    /// Gets the stream registry for subscribing to named data streams.
    /// </summary>
    IStreamRegistry Streams { get; }

    /// <summary>
    /// Gets the scheduler for dispatching observations to the UI thread.
    /// </summary>
    IScheduler Scheduler { get; }

    /// <summary>
    /// Gets the default sample interval for rate-limiting stream observations.
    /// </summary>
    TimeSpan DefaultSampleInterval { get; }
}
