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
/// Provides the help command that lists all registered commands.
/// </summary>
public sealed class HelpCommand(CommandRegistry registry) : ICommand
{
    public string Description => "List available commands";

    public string Usage => "help";

    public Task ExecuteAsync(string[] args, CommandContext ctx)
    {
        foreach (var (name, cmd) in registry.Commands.OrderBy(kv => kv.Key))
            ctx.Output.WriteLine($"  {cmd.Usage, -25} {cmd.Description}");

        return Task.CompletedTask;
    }
}
