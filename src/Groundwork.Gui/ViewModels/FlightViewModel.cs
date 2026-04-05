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
/// Provides the view model for the Flight page.
/// </summary>
public class FlightViewModel : PageViewModelBase
{
    public override string Title => "Fly";

    // Material Symbols: Near Me (filled, weight 400, grade 0, optical size 24)
    public override string IconPathData =>
        "M413-413 137-520q-10-4-14.5-12t-4.5-17q0-9 4.5-16t14.5-11l641-241q9-4 17.5-1.5T810-810q6 6 8.5 14.5T817-778L576-137q-4 10-11 14.5t-16 4.5q-9 0-17-4.5T520-137L413-413Z";
}
