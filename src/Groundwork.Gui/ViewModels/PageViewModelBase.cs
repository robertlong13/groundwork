// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Gui.ViewModels;

/// <summary>
/// Provides a base class for top-level page view models.
/// </summary>
public abstract class PageViewModelBase : ViewModelBase
{
    /// <summary>
    /// Gets the icon rail label for this page.
    /// </summary>
    public abstract string Title { get; }

    /// <summary>
    /// Gets the icon path data for the rail icon.
    /// </summary>
    public abstract string IconPathData { get; }
}
