// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Groundwork.Gui.ViewModels;
using Groundwork.Gui.Views;

namespace Groundwork.Gui;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DataTemplates.Add(BuildViewLocator());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ViewLocator BuildViewLocator()
    {
        var locator = new ViewLocator();
        locator.Register<FlightViewModel, FlightView>();
        locator.Register<PlanViewModel, PlanView>();
        locator.Register<ConfigViewModel, ConfigView>();
        locator.Register<SettingsViewModel, SettingsView>();
        return locator;
    }
}
