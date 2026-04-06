// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Reactive;

/// <summary>
/// Defines a registry of named observable streams.
/// </summary>
public interface IStreamRegistry
{
    /// <summary>
    /// Gets a stream by name with strong typing.
    /// </summary>
    IObservable<T> Get<T>(string name);

    /// <summary>
    /// Gets a stream by name as a type-erased observable.
    /// </summary>
    IObservable<object> Get(string name);

    /// <summary>
    /// Registers a named stream.
    /// </summary>
    void Register<T>(string name, IObservable<T> stream, string? description = null);

    /// <summary>
    /// Returns whether a stream with the given name is registered.
    /// </summary>
    bool Contains(string name);

    /// <summary>
    /// Gets metadata for all registered streams.
    /// </summary>
    IReadOnlyList<StreamInfo> Available { get; }
}
