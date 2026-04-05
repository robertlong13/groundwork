// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Groundwork.Gui.ViewModels;

namespace Groundwork.Gui;

/// <summary>
/// Provides a view-model-to-view mapping populated by explicit registration.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _factories = new();

    /// <summary>
    /// Registers a view factory for a view model type.
    /// </summary>
    public void Register<TViewModel, TView>()
        where TViewModel : ViewModelBase
        where TView : Control, new()
    {
        _factories[typeof(TViewModel)] = () => new TView();
    }

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        if (!_factories.TryGetValue(param.GetType(), out var factory))
            return new TextBlock { Text = $"No view registered for {param.GetType().Name}" };

        return factory();
    }

    public bool Match(object? data) => data is ViewModelBase;
}
