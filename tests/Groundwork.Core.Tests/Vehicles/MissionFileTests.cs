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

public class MissionFileTests : IDisposable
{
    private static readonly string DataDir = Path.Combine(FindSolutionRoot(), "data");

    private readonly string _tempPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempPath);

    [Fact]
    public void MavProxy_and_MissionPlanner_produce_identical_mission_items()
    {
        var (mpItems, mpHome) = MissionFile.Load(Path.Combine(DataDir, "mavproxy.waypoints"));
        var (plItems, plHome) = MissionFile.Load(Path.Combine(DataDir, "missionplanner.waypoints"));

        Assert.Equal(mpItems.Count, plItems.Count);

        for (int i = 0; i < mpItems.Count; i++)
        {
            var mp = mpItems[i];
            var pl = plItems[i];

            Assert.Equal(mp.Frame, pl.Frame);
            Assert.Equal(mp.Command, pl.Command);
            Assert.Equal(mp.Param1, pl.Param1);
            Assert.Equal(mp.Param2, pl.Param2);
            Assert.Equal(mp.Param3, pl.Param3);
            Assert.Equal(mp.Param4, pl.Param4);
            Assert.Equal(mp.X, pl.X);
            Assert.Equal(mp.Y, pl.Y);
            Assert.Equal(mp.Z, pl.Z);
            Assert.Equal(mp.Autocontinue, pl.Autocontinue);
        }
    }

    [Fact]
    public void Both_formats_extract_home_position()
    {
        var (_, mpHome) = MissionFile.Load(Path.Combine(DataDir, "mavproxy.waypoints"));
        var (_, plHome) = MissionFile.Load(Path.Combine(DataDir, "missionplanner.waypoints"));

        Assert.NotNull(mpHome);
        Assert.NotNull(plHome);

        // Both should have the same lat/lon (home doesn't move).
        Assert.Equal(mpHome.Value.Latitude, plHome.Value.Latitude, 5);
        Assert.Equal(mpHome.Value.Longitude, plHome.Value.Longitude, 5);
        // Altitudes differ (MSL vs different datum) -- just verify non-zero.
        Assert.NotEqual(0, mpHome.Value.Altitude);
        Assert.NotEqual(0, plHome.Value.Altitude);
    }

    [Fact]
    public void Home_is_excluded_from_mission_items()
    {
        var (items, home) = MissionFile.Load(Path.Combine(DataDir, "mavproxy.waypoints"));

        Assert.NotNull(home);
        // All items are mission items (home was stripped).
        Assert.Equal(7, items.Count);
    }

    [Fact]
    public void Save_with_home_produces_seq_zero()
    {
        var home = new HomePosition(-35.363262, 149.165237, 584.0f);
        var items = new List<MissionItem>
        {
            new(
                MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
                MAVLink.MAV_CMD.TAKEOFF,
                0,
                0,
                0,
                0,
                0,
                0,
                50f,
                1
            ),
        };

        MissionFile.SaveMission(_tempPath, items, home);
        var (loaded, loadedHome) = MissionFile.Load(_tempPath);

        Assert.NotNull(loadedHome);
        Assert.Equal(-35.363262, loadedHome.Value.Latitude, 5);
        Assert.Single(loaded);
    }

    [Fact]
    public void SaveFence_has_no_home_and_starts_at_seq_zero()
    {
        var items = new List<MissionItem>
        {
            new(
                MAVLink.MAV_FRAME.GLOBAL,
                MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION,
                5,
                0,
                0,
                0,
                (int)(-35.36 * 1e7),
                (int)(149.16 * 1e7),
                0f,
                1
            ),
        };

        MissionFile.SaveFence(_tempPath, items);
        var (loaded, loadedHome) = MissionFile.Load(_tempPath);

        Assert.Null(loadedHome);
        Assert.Single(loaded);
        Assert.Equal(MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION, loaded[0].Command);
    }

    [Fact]
    public void Load_handles_space_separated_fields()
    {
        File.WriteAllText(_tempPath, "QGC WPL 110\n1 0 3 16 0 0 0 0 -35.361 149.167 80 1\n");

        var (items, _) = MissionFile.Load(_tempPath);

        Assert.Single(items);
        Assert.Equal(MAVLink.MAV_CMD.WAYPOINT, items[0].Command);
    }

    [Fact]
    public void MavProxy_and_MissionPlanner_fence_produce_identical_items()
    {
        var (mpItems, mpHome) = MissionFile.Load(Path.Combine(DataDir, "mavproxy_fence.waypoints"));
        var (plItems, plHome) = MissionFile.Load(
            Path.Combine(DataDir, "missionplanner_fence.waypoints")
        );

        // MAVProxy fence has no home; Mission Planner fence does.
        Assert.Null(mpHome);
        Assert.NotNull(plHome);

        Assert.Equal(mpItems.Count, plItems.Count);

        for (int i = 0; i < mpItems.Count; i++)
        {
            Assert.Equal(mpItems[i].Command, plItems[i].Command);
            // MAVProxy writes 6 decimal places, MP writes 8 -- allow
            // 1e-6 degree tolerance (~0.1m) on the int 1e7 representation.
            Assert.True(
                Math.Abs(mpItems[i].X - plItems[i].X) <= 10,
                $"Fence item {i} X: {mpItems[i].X} vs {plItems[i].X}"
            );
            Assert.True(
                Math.Abs(mpItems[i].Y - plItems[i].Y) <= 10,
                $"Fence item {i} Y: {mpItems[i].Y} vs {plItems[i].Y}"
            );
        }
    }

    [Fact]
    public void MavProxy_and_MissionPlanner_rally_produce_identical_items()
    {
        var (mpItems, mpHome) = MissionFile.Load(Path.Combine(DataDir, "mavproxy_rally.waypoints"));
        var (plItems, plHome) = MissionFile.Load(
            Path.Combine(DataDir, "missionplanner_rally.waypoints")
        );

        Assert.Null(mpHome);
        Assert.NotNull(plHome);

        Assert.Equal(mpItems.Count, plItems.Count);

        for (int i = 0; i < mpItems.Count; i++)
        {
            Assert.Equal(mpItems[i].Command, plItems[i].Command);
            Assert.True(
                Math.Abs(mpItems[i].X - plItems[i].X) <= 10,
                $"Rally item {i} X: {mpItems[i].X} vs {plItems[i].X}"
            );
            Assert.True(
                Math.Abs(mpItems[i].Y - plItems[i].Y) <= 10,
                $"Rally item {i} Y: {mpItems[i].Y} vs {plItems[i].Y}"
            );
        }
    }

    [Fact]
    public void Fence_save_matches_mavproxy_convention()
    {
        // Load MAVProxy fence (no home, starts at seq 0).
        var (items, _) = MissionFile.Load(Path.Combine(DataDir, "mavproxy_fence.waypoints"));

        // Save without home (fence convention).
        MissionFile.SaveFence(_tempPath, items);

        // Reload and verify: no home, items start at seq 0.
        var (reloaded, home) = MissionFile.Load(_tempPath);
        Assert.Null(home);
        Assert.Equal(items.Count, reloaded.Count);
        Assert.All(
            reloaded,
            item => Assert.Equal(MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION, item.Command)
        );
    }

    [Fact]
    public void Rally_save_matches_mavproxy_convention()
    {
        var (items, _) = MissionFile.Load(Path.Combine(DataDir, "mavproxy_rally.waypoints"));

        MissionFile.SaveRally(_tempPath, items);

        var (reloaded, home) = MissionFile.Load(_tempPath);
        Assert.Null(home);
        Assert.Equal(items.Count, reloaded.Count);
        Assert.All(reloaded, item => Assert.Equal(MAVLink.MAV_CMD.RALLY_POINT, item.Command));
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Groundwork.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not find solution root");
    }
}
