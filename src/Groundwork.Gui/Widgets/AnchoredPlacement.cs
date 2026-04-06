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

namespace Groundwork.Gui.Widgets;

/// <summary>
/// Represents which edges of a parent an overlay widget is anchored to.
/// Anchoring opposite edges causes the widget to stretch along that axis.
/// </summary>
[Flags]
public enum Anchor
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,
}

/// <summary>
/// Represents the placement of an overlay widget within its parent.
/// </summary>
/// <param name="Anchor">Which parent edges to anchor to.</param>
/// <param name="Width">Initial width in pixels.</param>
/// <param name="Height">Initial height in pixels.</param>
/// <param name="Margin">Distance from anchored edges in pixels.</param>
public record AnchoredPlacement(Anchor Anchor, double Width, double Height, Thickness Margin);
