// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Console.Commands;

/// <summary>
/// Defines a console command that operates on a vehicle.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Gets the short description shown in help output.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the usage pattern shown in help output (e.g., "mode &lt;name&gt;").
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// Executes the command with the given arguments.
    /// </summary>
    /// <param name="args">Remaining command-line tokens after command resolution.</param>
    /// <param name="ctx">Shared state for the current command session.</param>
    Task ExecuteAsync(string[] args, CommandContext ctx);
}
