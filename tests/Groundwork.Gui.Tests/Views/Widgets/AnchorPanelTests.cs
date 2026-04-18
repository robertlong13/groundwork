// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Groundwork.Gui.Views.Widgets;
using Groundwork.Gui.Widgets;

namespace Groundwork.Gui.Tests.Views.Widgets;

public class AnchorPanelTests
{
    private const double Width = 400;
    private const double Height = 300;

    private static void Layout(AnchorPanel panel) => ApplyLayout(panel, new Size(Width, Height));

    private static void ApplyLayout(AnchorPanel panel, Size size)
    {
        panel.Measure(size);
        panel.Arrange(new Rect(size));
    }

    private static Control AddChild(AnchorPanel panel, AnchoredPlacement? placement)
    {
        var child = new Border();
        if (placement is not null)
            AnchorPanel.SetPlacement(child, placement);
        panel.Children.Add(child);
        return child;
    }

    [AvaloniaFact]
    public void NoPlacement_FillsParent()
    {
        var panel = new AnchorPanel();
        var child = AddChild(panel, placement: null);

        Layout(panel);

        Assert.Equal(new Rect(0, 0, Width, Height), child.Bounds);
    }

    [AvaloniaFact]
    public void TopLeft_UsesMarginAndPlacementSize()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(Anchor.Top | Anchor.Left, 80, 60, new Thickness(10, 5, 0, 0))
        );

        Layout(panel);

        Assert.Equal(new Rect(10, 5, 80, 60), child.Bounds);
    }

    [AvaloniaFact]
    public void BottomRight_PositionsFromFarEdges()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(Anchor.Bottom | Anchor.Right, 80, 60, new Thickness(0, 0, 10, 5))
        );

        Layout(panel);

        Assert.Equal(new Rect(Width - 10 - 80, Height - 5 - 60, 80, 60), child.Bounds);
    }

    [AvaloniaFact]
    public void LeftAndRight_StretchesHorizontallyBetweenMargins()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(Anchor.Left | Anchor.Right, 0, 60, new Thickness(10, 5, 20, 0))
        );

        Layout(panel);

        Assert.Equal(new Rect(10, 5, Width - 10 - 20, 60), child.Bounds);
    }

    [AvaloniaFact]
    public void TopAndBottom_StretchesVerticallyBetweenMargins()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(Anchor.Top | Anchor.Bottom, 80, 0, new Thickness(0, 5, 0, 20))
        );

        Layout(panel);

        Assert.Equal(new Rect(0, 5, 80, Height - 5 - 20), child.Bounds);
    }

    [AvaloniaFact]
    public void AllAnchors_FillParentMinusMargins()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(
                Anchor.Top | Anchor.Bottom | Anchor.Left | Anchor.Right,
                0,
                0,
                new Thickness(10, 5, 20, 15)
            )
        );

        Layout(panel);

        Assert.Equal(new Rect(10, 5, Width - 10 - 20, Height - 5 - 15), child.Bounds);
    }

    [AvaloniaFact]
    public void NoAnchor_FallsBackToTopLeft()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(Anchor.None, 80, 60, new Thickness(10, 5, 0, 0))
        );

        Layout(panel);

        Assert.Equal(new Rect(10, 5, 80, 60), child.Bounds);
    }

    [AvaloniaFact]
    public void MarginsExceedingParent_ClampStretchedDimensionsToZero()
    {
        var panel = new AnchorPanel();
        var child = AddChild(
            panel,
            new AnchoredPlacement(
                Anchor.Left | Anchor.Right | Anchor.Top | Anchor.Bottom,
                0,
                0,
                new Thickness(300, 200, 300, 200)
            )
        );

        Layout(panel);

        Assert.Equal(new Rect(300, 200, 0, 0), child.Bounds);
    }

    [AvaloniaFact]
    public void MultipleChildren_EachUseTheirOwnPlacement()
    {
        var panel = new AnchorPanel();
        var topLeft = AddChild(
            panel,
            new AnchoredPlacement(Anchor.Top | Anchor.Left, 50, 40, new Thickness(0))
        );
        var bottomRight = AddChild(
            panel,
            new AnchoredPlacement(Anchor.Bottom | Anchor.Right, 50, 40, new Thickness(0))
        );
        var fill = AddChild(panel, placement: null);

        Layout(panel);

        Assert.Equal(new Rect(0, 0, 50, 40), topLeft.Bounds);
        Assert.Equal(new Rect(Width - 50, Height - 40, 50, 40), bottomRight.Bounds);
        Assert.Equal(new Rect(0, 0, Width, Height), fill.Bounds);
    }
}
