// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Console.Commands;

namespace Groundwork.Core.Tests.Commands;

public class CommandRegistryTests
{
    private readonly CommandRegistry _registry = new();

    private sealed class StubCommand(string desc) : ICommand
    {
        public string Description => desc;
        public string Usage => desc;

        public Task ExecuteAsync(string[] args, CommandContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public void Resolve_SingleWord_Matches()
    {
        _registry.Register("status", new StubCommand("status"));

        var result = _registry.Resolve(["status"]);

        Assert.NotNull(result);
        Assert.Equal("status", result.Value.Command.Description);
        Assert.Empty(result.Value.Args);
    }

    [Fact]
    public void Resolve_MultiWord_MatchesLongestPrefix()
    {
        _registry.Register("arm", new StubCommand("arm"));
        _registry.Register("arm throttle", new StubCommand("arm throttle"));

        var result = _registry.Resolve(["arm", "throttle"]);

        Assert.NotNull(result);
        Assert.Equal("arm throttle", result.Value.Command.Description);
        Assert.Empty(result.Value.Args);
    }

    [Fact]
    public void Resolve_MultiWord_RemainingTokensBecomeArgs()
    {
        _registry.Register("mode", new StubCommand("mode"));

        var result = _registry.Resolve(["mode", "GUIDED"]);

        Assert.NotNull(result);
        Assert.Equal("mode", result.Value.Command.Description);
        Assert.Equal(["GUIDED"], result.Value.Args);
    }

    [Fact]
    public void Resolve_ThreeWordPrefix_MatchesOverTwo()
    {
        _registry.Register("arm throttle", new StubCommand("arm throttle"));
        _registry.Register("arm throttle force", new StubCommand("arm throttle force"));

        var result = _registry.Resolve(["arm", "throttle", "force"]);

        Assert.NotNull(result);
        Assert.Equal("arm throttle force", result.Value.Command.Description);
        Assert.Empty(result.Value.Args);
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        _registry.Register("mode", new StubCommand("mode"));

        var result = _registry.Resolve(["MODE", "guided"]);

        Assert.NotNull(result);
        Assert.Equal(["guided"], result.Value.Args);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        _registry.Register("status", new StubCommand("status"));

        Assert.Null(_registry.Resolve(["bogus"]));
    }
}
