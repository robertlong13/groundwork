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
using Avalonia.Layout;
using Groundwork.Gui.ViewModels.Widgets;

namespace Groundwork.Gui.Views.Widgets;

public partial class SplitWidgetView : UserControl
{
    public SplitWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SplitWidgetViewModel vm)
            BuildLayout(vm);
    }

    private void BuildLayout(SplitWidgetViewModel vm)
    {
        var grid = this.FindControl<Grid>("SplitGrid")!;
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();

        var first = new WidgetHost { DataContext = vm.First };
        var second = new WidgetHost { DataContext = vm.Second };
        var splitter = new GridSplitter { MinWidth = 3, MinHeight = 3 };

        if (vm.Orientation == Orientation.Horizontal)
        {
            grid.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(vm.Ratio, GridUnitType.Star))
            );
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(1 - vm.Ratio, GridUnitType.Star))
            );

            Grid.SetColumn(first, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);

            splitter.DragCompleted += (_, _) =>
            {
                var a = grid.ColumnDefinitions[0].ActualWidth;
                var b = grid.ColumnDefinitions[2].ActualWidth;
                if (a + b > 0)
                    vm.Ratio = a / (a + b);
            };
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(vm.Ratio, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(
                new RowDefinition(new GridLength(1 - vm.Ratio, GridUnitType.Star))
            );

            Grid.SetRow(first, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);

            splitter.DragCompleted += (_, _) =>
            {
                var a = grid.RowDefinitions[0].ActualHeight;
                var b = grid.RowDefinitions[2].ActualHeight;
                if (a + b > 0)
                    vm.Ratio = a / (a + b);
            };
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
    }
}
