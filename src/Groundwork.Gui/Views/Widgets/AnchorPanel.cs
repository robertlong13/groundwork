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
using Groundwork.Gui.Widgets;

namespace Groundwork.Gui.Views.Widgets;

/// <summary>
/// Provides an attached property for overlay placement and positions
/// children using WinForms-style anchor semantics.
/// </summary>
public class AnchorPanel : Panel
{
    public static readonly AttachedProperty<AnchoredPlacement?> PlacementProperty =
        AvaloniaProperty.RegisterAttached<AnchorPanel, Control, AnchoredPlacement?>("Placement");

    public static AnchoredPlacement? GetPlacement(Control element) =>
        element.GetValue(PlacementProperty);

    public static void SetPlacement(Control element, AnchoredPlacement? value) =>
        element.SetValue(PlacementProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(availableSize);

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var placement = GetPlacement(child);
            if (placement is null)
            {
                // No placement = fill parent (base content).
                child.Arrange(new Rect(finalSize));
                continue;
            }

            var anchor = placement.Anchor;
            var margin = placement.Margin;

            // Horizontal axis.
            double x,
                w;
            bool anchorLeft = anchor.HasFlag(Anchor.Left);
            bool anchorRight = anchor.HasFlag(Anchor.Right);

            if (anchorLeft && anchorRight)
            {
                x = margin.Left;
                w = finalSize.Width - margin.Left - margin.Right;
            }
            else if (anchorRight)
            {
                w = placement.Width;
                x = finalSize.Width - margin.Right - w;
            }
            else
            {
                // Default: anchor left (or no horizontal anchor).
                x = margin.Left;
                w = placement.Width;
            }

            // Vertical axis.
            double y,
                h;
            bool anchorTop = anchor.HasFlag(Anchor.Top);
            bool anchorBottom = anchor.HasFlag(Anchor.Bottom);

            if (anchorTop && anchorBottom)
            {
                y = margin.Top;
                h = finalSize.Height - margin.Top - margin.Bottom;
            }
            else if (anchorBottom)
            {
                h = placement.Height;
                y = finalSize.Height - margin.Bottom - h;
            }
            else
            {
                // Default: anchor top (or no vertical anchor).
                y = margin.Top;
                h = placement.Height;
            }

            child.Arrange(new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
        }

        return finalSize;
    }
}
