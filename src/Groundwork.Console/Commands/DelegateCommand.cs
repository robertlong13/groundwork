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
/// Provides an <see cref="ICommand"/> wrapper around a delegate.
/// </summary>
public sealed class DelegateCommand(
    string description,
    string usage,
    Func<string[], CommandContext, Task> execute
) : ICommand
{
    public string Description => description;

    public string Usage => usage;

    public Task ExecuteAsync(string[] args, CommandContext ctx) => execute(args, ctx);
}
