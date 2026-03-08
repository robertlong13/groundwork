// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Vehicles;

namespace Groundwork.Core.Tests.Vehicles;

public class ParamFileTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempPath);

    [Fact]
    public void Load_ParsesStandardValues()
    {
        File.WriteAllText(_tempPath, "PARAM_ONE       1.500000\nPARAM_TWO       42.000000\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Equal(2, result.Count);
        Assert.Equal(1.5, result["PARAM_ONE"].Value);
        Assert.Equal(42.0, result["PARAM_TWO"].Value);
    }

    [Fact]
    public void Load_ParsesHexValues()
    {
        File.WriteAllText(_tempPath, "BITMASK_PARAM   0x1F\nUPPER_HEX       0XFF\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Equal(2, result.Count);
        Assert.Equal(31.0, result["BITMASK_PARAM"].Value);
        Assert.Equal(255.0, result["UPPER_HEX"].Value);
    }

    [Fact]
    public void Load_ParsesScientificNotation()
    {
        File.WriteAllText(_tempPath, "TINY_VAL        1.2E-5\nBIG_VAL         3.0e+8\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Equal(1.2e-5, result["TINY_VAL"].Value);
        Assert.Equal(3.0e+8, result["BIG_VAL"].Value);
    }

    [Fact]
    public void Load_SkipsCommentsAndBlankLines()
    {
        File.WriteAllText(_tempPath, "# comment\n\n  \nPARAM_ONE       1.0\n# another\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Single(result);
    }

    [Fact]
    public void Load_TreatsCommasAsSpaces()
    {
        File.WriteAllText(_tempPath, "PARAM_ONE,1.500000\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Equal(1.5, result["PARAM_ONE"].Value);
    }

    [Fact]
    public void Load_SkipsMalformedLines()
    {
        File.WriteAllText(_tempPath, "ONLY_NAME\nTOO MANY PARTS HERE\nNOT_NUM abc\nGOOD 1.0\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Single(result);
        Assert.Equal(1.0, result["GOOD"].Value);
    }

    [Fact]
    public void Load_UppercasesNames()
    {
        File.WriteAllText(_tempPath, "lower_case 1.0\n");

        var result = ParamFile.Load(_tempPath);

        Assert.True(result.ContainsKey("LOWER_CASE"));
    }

    [Fact]
    public void Load_EntriesHaveNullDefault()
    {
        File.WriteAllText(_tempPath, "PARAM_ONE 1.0\n");

        var result = ParamFile.Load(_tempPath);

        Assert.Null(result["PARAM_ONE"].DefaultValue);
    }

    [Fact]
    public void Save_RoundTripsWithLoad()
    {
        var parameters = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["PARAM_B"] = new(2.5, null),
            ["PARAM_A"] = new(1.0, 1.0),
        };

        ParamFile.Save(_tempPath, parameters);
        var loaded = ParamFile.Load(_tempPath);

        Assert.Equal(2, loaded.Count);
        Assert.True(ParamFile.ValuesEqual(1.0, loaded["PARAM_A"].Value));
        Assert.True(ParamFile.ValuesEqual(2.5, loaded["PARAM_B"].Value));
    }

    [Fact]
    public void Save_WritesAlphabeticalOrder()
    {
        var parameters = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["ZZZ"] = new(3.0, null),
            ["AAA"] = new(1.0, null),
            ["MMM"] = new(2.0, null),
        };

        ParamFile.Save(_tempPath, parameters);
        var lines = File.ReadAllLines(_tempPath);

        Assert.StartsWith("AAA", lines[0]);
        Assert.StartsWith("MMM", lines[1]);
        Assert.StartsWith("ZZZ", lines[2]);
    }

    [Fact]
    public void Save_ReturnsCount()
    {
        var parameters = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new(1.0, null),
            ["B"] = new(2.0, null),
        };

        var count = ParamFile.Save(_tempPath, parameters);

        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.0, 1.0000001, true)]
    [InlineData(1.0, 1.000001, true)]
    [InlineData(1.0, 1.0000011, false)]
    [InlineData(1.0, 1.000002, false)]
    public void ValuesEqual_UsesTolerance(double a, double b, bool expected)
    {
        Assert.Equal(expected, ParamFile.ValuesEqual(a, b));
    }

    [Theory]
    [InlineData("PARAM_ONE", "*", true)]
    [InlineData("PARAM_ONE", "PARAM_ONE", true)]
    [InlineData("PARAM_ONE", "param_one", true)]
    [InlineData("PARAM_ONE", "PARAM_TWO", false)]
    [InlineData("PARAM_ONE", "PARAM_*", true)]
    [InlineData("PARAM_ONE", "*_ONE", true)]
    [InlineData("PARAM_ONE", "PAR*ONE", true)]
    [InlineData("PARAM_ONE", "PAR*TWO", false)]
    [InlineData("SERVO1_FUNCTION", "SERVO*_FUNCTION", true)]
    [InlineData("SERVO1_FUNCTION", "SERVO*_MIN", false)]
    [InlineData("PARAM_ONE", "*ONE*", true)]
    public void MatchesWildcard_MatchesCorrectly(string name, string wildcard, bool expected)
    {
        Assert.Equal(expected, ParamFile.MatchesWildcard(name, wildcard));
    }
}
