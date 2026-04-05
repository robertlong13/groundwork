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

namespace Groundwork.Gui.ViewModels;

/// <summary>
/// Provides the view model for the main application window.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private PageViewModelBase _currentPage;

    public PageViewModelBase[] Pages { get; } =
    [new FlightViewModel(), new PlanViewModel(), new ConfigViewModel(), new SettingsViewModel()];

    public MainWindowViewModel()
    {
        _currentPage = Pages[0];
    }
}
