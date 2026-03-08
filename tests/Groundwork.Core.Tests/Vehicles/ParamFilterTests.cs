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

public class ParamFilterTests
{
    private static readonly Dictionary<string, ParamEntry> Params = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["ARMING_CHECK"] = new(1.0, 1.0),
        ["ARMING_REQUIRE"] = new(2.0, 1.0),
        ["BATT_MONITOR"] = new(4.0, 0.0),
        ["BATT_CAPACITY"] = new(3300.0, 3300.0),
        ["SERVO1_MIN"] = new(1100.0, 1100.0),
    };

    [Fact]
    public void Apply_NoFilters_ReturnsAll()
    {
        var filter = new ParamFilter();

        var result = filter.Apply(Params);

        Assert.Equal(Params.Count, result.Count);
    }

    [Fact]
    public void Apply_WildcardInclusion_FiltersToMatches()
    {
        var filter = new ParamFilter { Wildcards = ["ARMING_*"] };

        var result = filter.Apply(Params);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("ARMING_CHECK"));
        Assert.True(result.ContainsKey("ARMING_REQUIRE"));
    }

    [Fact]
    public void Apply_WildcardExclusion_RemovesMatches()
    {
        var filter = new ParamFilter { ExcludeWildcards = ["BATT_*"] };

        var result = filter.Apply(Params);

        Assert.Equal(3, result.Count);
        Assert.False(result.ContainsKey("BATT_MONITOR"));
        Assert.False(result.ContainsKey("BATT_CAPACITY"));
    }

    [Fact]
    public void Apply_InclusionAndExclusion_BothApply()
    {
        var filter = new ParamFilter
        {
            Wildcards = ["ARMING_*", "BATT_*"],
            ExcludeWildcards = ["BATT_CAPACITY"],
        };

        var result = filter.Apply(Params);

        Assert.Equal(3, result.Count);
        Assert.False(result.ContainsKey("BATT_CAPACITY"));
        Assert.False(result.ContainsKey("SERVO1_MIN"));
    }

    [Fact]
    public void Apply_ExcludeDefault_SkipsUnchangedParams()
    {
        var filter = new ParamFilter { Exclude = ParamExclude.Default };

        var result = filter.Apply(Params);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("ARMING_REQUIRE"));
        Assert.True(result.ContainsKey("BATT_MONITOR"));
    }

    [Fact]
    public void Apply_ExcludeDefault_ThrowsOnMissingDefault()
    {
        var paramsNoDefault = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["PARAM_A"] = new(1.0, null),
        };
        var filter = new ParamFilter { Exclude = ParamExclude.Default };

        var ex = Assert.Throws<InvalidOperationException>(() => filter.Apply(paramsNoDefault));
        Assert.Equal("PARAM_A", ex.Message);
    }

    [Fact]
    public void Apply_ExcludeReadOnly_SkipsReadOnlyParams()
    {
        var metadata = new Dictionary<string, ParamMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARMING_CHECK"] = new() { ReadOnly = true },
            ["BATT_MONITOR"] = new() { ReadOnly = false },
        };
        var filter = new ParamFilter { Exclude = ParamExclude.ReadOnly, Metadata = metadata };

        var result = filter.Apply(Params);

        Assert.Equal(4, result.Count);
        Assert.False(result.ContainsKey("ARMING_CHECK"));
    }

    [Fact]
    public void Apply_ExcludeVolatile_SkipsVolatileParams()
    {
        var metadata = new Dictionary<string, ParamMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["BATT_MONITOR"] = new() { Volatile = true },
        };
        var filter = new ParamFilter { Exclude = ParamExclude.Volatile, Metadata = metadata };

        var result = filter.Apply(Params);

        Assert.Equal(4, result.Count);
        Assert.False(result.ContainsKey("BATT_MONITOR"));
    }

    [Fact]
    public void Apply_NoMetadata_IgnoresMetadataFlags()
    {
        var filter = new ParamFilter { Exclude = ParamExclude.ReadOnly | ParamExclude.Volatile };

        var result = filter.Apply(Params);

        Assert.Equal(Params.Count, result.Count);
    }

    [Fact]
    public void Apply_ParamNotInMetadata_PassesThrough()
    {
        var metadata = new Dictionary<string, ParamMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARMING_CHECK"] = new() { ReadOnly = true },
        };
        var filter = new ParamFilter { Exclude = ParamExclude.ReadOnly, Metadata = metadata };

        var result = filter.Apply(Params);

        Assert.True(result.ContainsKey("BATT_MONITOR"));
        Assert.True(result.ContainsKey("SERVO1_MIN"));
    }

    [Fact]
    public void Apply_CombinedFlags_ExcludesBoth()
    {
        var metadata = new Dictionary<string, ParamMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARMING_CHECK"] = new() { ReadOnly = true },
            ["SERVO1_MIN"] = new() { Volatile = true },
        };
        var filter = new ParamFilter
        {
            Exclude = ParamExclude.ReadOnly | ParamExclude.Volatile,
            Metadata = metadata,
        };

        var result = filter.Apply(Params);

        Assert.Equal(3, result.Count);
        Assert.False(result.ContainsKey("ARMING_CHECK"));
        Assert.False(result.ContainsKey("SERVO1_MIN"));
    }

    [Fact]
    public void Apply_Reusable_SameFilterDifferentInputs()
    {
        var filter = new ParamFilter { Wildcards = ["ARMING_*"] };

        var result1 = filter.Apply(Params);

        var otherParams = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARMING_CHECK"] = new(0.0, 1.0),
            ["GPS_TYPE"] = new(1.0, 1.0),
        };
        var result2 = filter.Apply(otherParams);

        Assert.Equal(2, result1.Count);
        Assert.Single(result2);
    }
}
