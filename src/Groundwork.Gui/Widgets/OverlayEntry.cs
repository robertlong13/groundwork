// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Gui.ViewModels.Widgets;

namespace Groundwork.Gui.Widgets;

/// <summary>
/// Represents a widget overlaid on its parent with anchored placement.
/// </summary>
public record OverlayEntry(WidgetViewModelBase Widget, AnchoredPlacement Placement);
