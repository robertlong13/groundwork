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
using System.Reactive.Subjects;

namespace Groundwork.Core.Reactive;

/// <summary>
/// Provides a reactive value that exposes both synchronous read access
/// and an observable stream with built-in change deduplication.
/// </summary>
public sealed class ObservableValue<T> : IObservable<T>, IDisposable
{
    private readonly BehaviorSubject<T> _subject;

    public ObservableValue(T initialValue)
    {
        _subject = new BehaviorSubject<T>(initialValue);
    }

    /// <summary>
    /// Gets the current value.
    /// </summary>
    public T Value => _subject.Value;

    /// <summary>
    /// Sets the current value and notifies subscribers if it changed.
    /// </summary>
    internal void Set(T value) => _subject.OnNext(value);

    /// <summary>
    /// Subscribes to value changes. Emissions are deduplicated so
    /// subscribers only observe actual changes, not repeated identical
    /// values from periodic messages.
    /// </summary>
    public IDisposable Subscribe(IObserver<T> observer) =>
        _subject.DistinctUntilChanged().Subscribe(observer);

    public void Dispose() => _subject.Dispose();
}
