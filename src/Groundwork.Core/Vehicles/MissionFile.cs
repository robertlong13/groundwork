// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Represents a geographic home position (lat/lon/alt).
/// </summary>
public readonly record struct HomePosition(double Latitude, double Longitude, float Altitude);

/// <summary>
/// Provides read/write for the QGC WPL 110 waypoint file format.
/// </summary>
public static class MissionFile
{
    private const string Header = "QGC WPL 110";

    /// <summary>
    /// Saves a mission to a .waypoints file with home as seq 0.
    /// </summary>
    public static int SaveMission(
        string path,
        IReadOnlyList<MissionItem> items,
        HomePosition home
    ) => Save(path, items, home);

    /// <summary>
    /// Saves fence items to a .waypoints file (no home, items at seq 0).
    /// </summary>
    public static int SaveFence(string path, IReadOnlyList<MissionItem> items) =>
        Save(path, items, null);

    /// <summary>
    /// Saves rally items to a .waypoints file (no home, items at seq 0).
    /// </summary>
    public static int SaveRally(string path, IReadOnlyList<MissionItem> items) =>
        Save(path, items, null);

    /// <summary>
    /// Reads a .waypoints file, separating the home position (seq 0) from
    /// items (seq 1+). Home is detected by seq 0 with cmd WAYPOINT;
    /// fence/rally items at seq 0 are kept as items.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file header is missing or unsupported.</exception>
    public static (List<MissionItem> Items, HomePosition? Home) Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || !lines[0].StartsWith("QGC WPL", StringComparison.Ordinal))
            throw new InvalidDataException("Not a QGC waypoint file: missing header");

        var items = new List<MissionItem>();
        HomePosition? home = null;

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 12)
                continue;

            if (!ushort.TryParse(parts[0], out var seq))
                continue;

            if (
                !byte.TryParse(parts[1], out var current)
                || !byte.TryParse(parts[2], out var frame)
                || !ushort.TryParse(parts[3], out var command)
                || !float.TryParse(
                    parts[4],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var p1
                )
                || !float.TryParse(
                    parts[5],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var p2
                )
                || !float.TryParse(
                    parts[6],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var p3
                )
                || !float.TryParse(
                    parts[7],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var p4
                )
                || !double.TryParse(
                    parts[8],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var lat
                )
                || !double.TryParse(
                    parts[9],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var lon
                )
                || !float.TryParse(
                    parts[10],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var alt
                )
                || !byte.TryParse(parts[11], out var autocontinue)
            )
            {
                continue;
            }

            // Seq 0 with cmd 16 (WAYPOINT) is the home position convention.
            // Fence/rally files may start at seq 0 with actual items
            // (cmd 5001, 5100, etc.) -- those are not home.
            if (seq == 0 && home is null && command == (ushort)MAVLink.MAV_CMD.WAYPOINT)
            {
                home = new HomePosition(lat, lon, alt);
                continue;
            }

            items.Add(
                new MissionItem(
                    (MAVLink.MAV_FRAME)frame,
                    (MAVLink.MAV_CMD)command,
                    p1,
                    p2,
                    p3,
                    p4,
                    (int)(lat * 1e7),
                    (int)(lon * 1e7),
                    alt,
                    autocontinue,
                    current
                )
            );
        }

        return (items, home);
    }

    private static int Save(string path, IReadOnlyList<MissionItem> items, HomePosition? home)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(Header);

        var nextSeq = 0;

        if (home is { } h)
        {
            writer.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "0\t1\t0\t16\t0.00000000\t0.00000000\t0.00000000\t0.00000000\t{0:F8}\t{1:F8}\t{2:F6}\t1",
                    h.Latitude,
                    h.Longitude,
                    h.Altitude
                )
            );
            nextSeq = 1;
        }

        foreach (var item in items)
        {
            writer.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\t{1}\t{2}\t{3}\t{4:F8}\t{5:F8}\t{6:F8}\t{7:F8}\t{8:F8}\t{9:F8}\t{10:F6}\t{11}",
                    nextSeq,
                    item.Current,
                    (byte)item.Frame,
                    (ushort)item.Command,
                    item.Param1,
                    item.Param2,
                    item.Param3,
                    item.Param4,
                    item.Latitude,
                    item.Longitude,
                    item.Altitude,
                    item.Autocontinue
                )
            );
            nextSeq++;
        }

        return items.Count + (home is not null ? 1 : 0);
    }
}
