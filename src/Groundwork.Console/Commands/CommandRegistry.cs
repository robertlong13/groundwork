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
/// Provides a registry that maps command names to <see cref="ICommand"/> implementations.
/// </summary>
/// <remarks>
/// Supports multi-word command prefixes (e.g., "arm throttle").
/// </remarks>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, ICommand command) => _commands[name] = command;

    /// <summary>
    /// Resolves input tokens to a command by matching the longest registered prefix.
    /// </summary>
    /// <returns>The matched command and remaining arguments, or <see langword="null"/> if no match.</returns>
    public (ICommand Command, string[] Args)? Resolve(string[] tokens)
    {
        // Try longest prefix first: "arm throttle force" before "arm throttle".
        for (var len = tokens.Length; len > 0; len--)
        {
            var key = string.Join(' ', tokens[..len]);
            if (_commands.TryGetValue(key, out var command))
                return (command, tokens[len..]);
        }

        return null;
    }

    /// <summary>
    /// Returns all commands whose name starts with the longest matching token prefix.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, ICommand>> FindSubcommands(string[] tokens)
    {
        for (var len = tokens.Length; len > 0; len--)
        {
            var prefix = string.Join(' ', tokens[..len]) + " ";
            var matches = _commands
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matches.Count > 0)
                return matches;
        }

        return [];
    }

    public IReadOnlyDictionary<string, ICommand> Commands => _commands;
}
