// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Diagnostics;
using System.Globalization;
using Groundwork.Core.Vehicles;

namespace Groundwork.Console.Commands;

/// <summary>
/// Provides wp, fence, and rally commands sharing the mission item protocol.
/// </summary>
public sealed class WpModule
{
    private const MAVLink.MAV_MISSION_TYPE Mission = MAVLink.MAV_MISSION_TYPE.MISSION;
    private const MAVLink.MAV_MISSION_TYPE Fence = MAVLink.MAV_MISSION_TYPE.FENCE;
    private const MAVLink.MAV_MISSION_TYPE Rally = MAVLink.MAV_MISSION_TYPE.RALLY;

    public void Register(CommandRegistry commands)
    {
        // wp (mission).
        commands.Register(
            "wp list",
            new DelegateCommand(
                "Download and display mission",
                "wp list",
                (a, c) => ListAsync(Mission, c)
            )
        );
        commands.Register(
            "wp load",
            new DelegateCommand(
                "Load mission from file and upload",
                "wp load <file>",
                (a, c) => LoadAsync(Mission, "wp", a, c)
            )
        );
        commands.Register(
            "wp save",
            new DelegateCommand(
                "Download mission and save to file",
                "wp save <file>",
                (a, c) => SaveAsync(Mission, "wp", a, c)
            )
        );
        commands.Register(
            "wp show",
            new DelegateCommand(
                "Display mission file without uploading",
                "wp show <file>",
                (a, c) => Show("wp", a, c)
            )
        );
        commands.Register(
            "wp clear",
            new DelegateCommand(
                "Clear mission on vehicle",
                "wp clear",
                (a, c) => ClearAsync(Mission, c)
            )
        );
        commands.Register(
            "wp ftp",
            new DelegateCommand(
                "Download mission via FTP",
                "wp ftp",
                (a, c) => FtpDownloadAsync(Mission, c)
            )
        );
        commands.Register(
            "wp ftpload",
            new DelegateCommand(
                "Upload mission via FTP",
                "wp ftpload <file>",
                (a, c) => FtpUploadAsync(Mission, "wp", a, c)
            )
        );
        commands.Register(
            "wp savelocal",
            new DelegateCommand(
                "Save cached mission to file",
                "wp savelocal <file>",
                (a, c) => SaveLocal(Mission, "wp", a, c)
            )
        );
        commands.Register(
            "wp set",
            new DelegateCommand(
                "Set current mission item",
                "wp set <seq>",
                (a, c) => SetAsync(a, c)
            )
        );
        commands.Register(
            "wp status",
            new DelegateCommand("Show mission status", "wp status", (a, c) => Status(a, c))
        );

        // fence.
        commands.Register(
            "fence list",
            new DelegateCommand(
                "Download and display fence",
                "fence list",
                (a, c) => ListAsync(Fence, c)
            )
        );
        commands.Register(
            "fence load",
            new DelegateCommand(
                "Load fence from file and upload",
                "fence load <file>",
                (a, c) => LoadAsync(Fence, "fence", a, c)
            )
        );
        commands.Register(
            "fence save",
            new DelegateCommand(
                "Download fence and save to file",
                "fence save <file>",
                (a, c) => SaveAsync(Fence, "fence", a, c)
            )
        );
        commands.Register(
            "fence show",
            new DelegateCommand(
                "Display fence file without uploading",
                "fence show <file>",
                (a, c) => Show("fence", a, c)
            )
        );
        commands.Register(
            "fence clear",
            new DelegateCommand(
                "Clear fence on vehicle",
                "fence clear",
                (a, c) => ClearAsync(Fence, c)
            )
        );
        commands.Register(
            "fence ftp",
            new DelegateCommand(
                "Download fence via FTP",
                "fence ftp",
                (a, c) => FtpDownloadAsync(Fence, c)
            )
        );
        commands.Register(
            "fence ftpload",
            new DelegateCommand(
                "Upload fence via FTP",
                "fence ftpload <file>",
                (a, c) => FtpUploadAsync(Fence, "fence", a, c)
            )
        );
        commands.Register(
            "fence savelocal",
            new DelegateCommand(
                "Save cached fence to file",
                "fence savelocal <file>",
                (a, c) => SaveLocal(Fence, "fence", a, c)
            )
        );

        // rally.
        commands.Register(
            "rally list",
            new DelegateCommand(
                "Download and display rally",
                "rally list",
                (a, c) => ListAsync(Rally, c)
            )
        );
        commands.Register(
            "rally load",
            new DelegateCommand(
                "Load rally from file and upload",
                "rally load <file>",
                (a, c) => LoadAsync(Rally, "rally", a, c)
            )
        );
        commands.Register(
            "rally save",
            new DelegateCommand(
                "Download rally and save to file",
                "rally save <file>",
                (a, c) => SaveAsync(Rally, "rally", a, c)
            )
        );
        commands.Register(
            "rally show",
            new DelegateCommand(
                "Display rally file without uploading",
                "rally show <file>",
                (a, c) => Show("rally", a, c)
            )
        );
        commands.Register(
            "rally clear",
            new DelegateCommand(
                "Clear rally on vehicle",
                "rally clear",
                (a, c) => ClearAsync(Rally, c)
            )
        );
        commands.Register(
            "rally ftp",
            new DelegateCommand(
                "Download rally via FTP",
                "rally ftp",
                (a, c) => FtpDownloadAsync(Rally, c)
            )
        );
        commands.Register(
            "rally ftpload",
            new DelegateCommand(
                "Upload rally via FTP",
                "rally ftpload <file>",
                (a, c) => FtpUploadAsync(Rally, "rally", a, c)
            )
        );
        commands.Register(
            "rally savelocal",
            new DelegateCommand(
                "Save cached rally to file",
                "rally savelocal <file>",
                (a, c) => SaveLocal(Rally, "rally", a, c)
            )
        );
    }

    private static async Task ListAsync(MAVLink.MAV_MISSION_TYPE type, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var progress = new SyncProgress<MissionTransferProgress>(p =>
                ctx.Output.WriteLine($"  {p.Received}/{p.Total}")
            );

            var items = await vehicle.DownloadItemsViaProtocolAsync(
                type,
                progress,
                ctx.ShutdownToken
            );
            sw.Stop();

            ctx.Output.WriteLine(
                $"Downloaded {items.Count} items in {sw.Elapsed.TotalSeconds:F1}s"
            );
            PrintItems(items, ctx);
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("Download timed out");
        }
    }

    private static async Task LoadAsync(
        MAVLink.MAV_MISSION_TYPE type,
        string prefix,
        string[] args,
        CommandContext ctx
    )
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine($"Usage: {prefix} load <filename>");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var filename = args[0].Trim('"');

        List<MissionItem> items;
        try
        {
            (items, _) = MissionFile.Load(filename);
        }
        catch (FileNotFoundException)
        {
            ctx.Output.WriteLine($"File not found: {filename}");
            return;
        }
        catch (InvalidDataException ex)
        {
            ctx.Output.WriteLine($"Invalid file: {ex.Message}");
            return;
        }

        ctx.Output.WriteLine($"Loaded {items.Count} items from {filename}");

        try
        {
            var sw = Stopwatch.StartNew();
            var progress = new SyncProgress<MissionTransferProgress>(p =>
                ctx.Output.WriteLine($"  {p.Received}/{p.Total} sent")
            );

            var result = await vehicle.UploadItemsViaProtocolAsync(
                type,
                items,
                progress,
                ctx.ShutdownToken
            );
            sw.Stop();

            if (result == MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
                ctx.Output.WriteLine(
                    $"Upload accepted ({items.Count} items, {sw.Elapsed.TotalSeconds:F1}s)"
                );
            else
                ctx.Output.WriteLine($"Upload rejected: {result}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("Upload timed out");
        }
    }

    private static async Task SaveAsync(
        MAVLink.MAV_MISSION_TYPE type,
        string prefix,
        string[] args,
        CommandContext ctx
    )
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine($"Usage: {prefix} save <filename>");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var filename = args[0].Trim('"');

        try
        {
            var items = await vehicle.DownloadItemsViaProtocolAsync(type, ct: ctx.ShutdownToken);
            var count = SaveFile(type, filename, items.ToList(), vehicle);
            ctx.Output.WriteLine($"Saved {count} items to {filename}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("Download timed out");
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"Save failed: {ex.Message}");
        }
    }

    private static Task Show(string prefix, string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine($"Usage: {prefix} show <filename>");
            return Task.CompletedTask;
        }

        var filename = args[0].Trim('"');

        List<MissionItem> items;
        HomePosition? home;
        try
        {
            (items, home) = MissionFile.Load(filename);
        }
        catch (FileNotFoundException)
        {
            ctx.Output.WriteLine($"File not found: {filename}");
            return Task.CompletedTask;
        }
        catch (InvalidDataException ex)
        {
            ctx.Output.WriteLine($"Invalid file: {ex.Message}");
            return Task.CompletedTask;
        }

        ctx.Output.WriteLine($"{items.Count} items from {filename}");
        if (home is { } h)
        {
            ctx.Output.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Home: {0:F7}, {1:F7}, {2:F1}m",
                    h.Latitude,
                    h.Longitude,
                    h.Altitude
                )
            );
        }

        PrintItems(items, ctx);

        return Task.CompletedTask;
    }

    private static async Task ClearAsync(MAVLink.MAV_MISSION_TYPE type, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        try
        {
            await vehicle.ClearItemsAsync(type, ctx.ShutdownToken);
            ctx.Output.WriteLine($"{type} cleared");
        }
        catch (InvalidOperationException ex)
        {
            ctx.Output.WriteLine(ex.Message);
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("Clear timed out");
        }
    }

    private static async Task SetAsync(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: wp set <seq>");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        if (!ushort.TryParse(args[0], out var seq))
        {
            ctx.Output.WriteLine($"Invalid sequence number: {args[0]}");
            return;
        }

        try
        {
            var result = await vehicle.SetCurrentMissionItemAsync(seq, ctx.ShutdownToken);
            if (result == MAVLink.MAV_RESULT.ACCEPTED)
                ctx.Output.WriteLine($"Current waypoint set to {seq}");
            else
                ctx.Output.WriteLine($"Set current failed: {result}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("Set current timed out");
        }
    }

    private static Task Status(string[] args, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var mission = vehicle.Mission;
        ctx.Output.WriteLine($"Items: {mission.Count}");
        ctx.Output.WriteLine($"Current: WP {vehicle.CurrentMissionSeq}");
        ctx.Output.WriteLine($"State: {vehicle.MissionState}");

        return Task.CompletedTask;
    }

    private static async Task FtpDownloadAsync(MAVLink.MAV_MISSION_TYPE type, CommandContext ctx)
    {
        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var items = await vehicle.DownloadItemsViaFtpAsync(type, ct: ctx.ShutdownToken);
            sw.Stop();

            ctx.Output.WriteLine(
                $"Downloaded {items.Count} items via FTP in {sw.Elapsed.TotalSeconds:F1}s"
            );
            PrintItems(items, ctx);
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"FTP download failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("FTP download timed out");
        }
    }

    private static async Task FtpUploadAsync(
        MAVLink.MAV_MISSION_TYPE type,
        string prefix,
        string[] args,
        CommandContext ctx
    )
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine($"Usage: {prefix} ftpload <filename>");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var filename = args[0].Trim('"');

        List<MissionItem> items;
        try
        {
            (items, _) = MissionFile.Load(filename);
        }
        catch (FileNotFoundException)
        {
            ctx.Output.WriteLine($"File not found: {filename}");
            return;
        }
        catch (InvalidDataException ex)
        {
            ctx.Output.WriteLine($"Invalid file: {ex.Message}");
            return;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            await vehicle.UploadItemsViaFtpAsync(type, items, ctx.ShutdownToken);
            sw.Stop();

            ctx.Output.WriteLine(
                $"Uploaded {items.Count} items via FTP in {sw.Elapsed.TotalSeconds:F1}s"
            );
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"FTP upload failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("FTP upload timed out");
        }
    }

    private static Task SaveLocal(
        MAVLink.MAV_MISSION_TYPE type,
        string prefix,
        string[] args,
        CommandContext ctx
    )
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine($"Usage: {prefix} savelocal <filename>");
            return Task.CompletedTask;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return Task.CompletedTask;
        }

        var cache = type switch
        {
            MAVLink.MAV_MISSION_TYPE.MISSION => vehicle.Mission,
            MAVLink.MAV_MISSION_TYPE.FENCE => vehicle.Fence,
            MAVLink.MAV_MISSION_TYPE.RALLY => vehicle.Rally,
            _ => [],
        };

        if (cache.Count == 0)
        {
            ctx.Output.WriteLine(
                $"No {prefix} cached. Use '{prefix} list' or '{prefix} ftp' to download."
            );
            return Task.CompletedTask;
        }

        var filename = args[0].Trim('"');

        try
        {
            var count = SaveFile(type, filename, cache.ToList(), vehicle);
            ctx.Output.WriteLine($"Saved {count} items to {filename}");
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"Save failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static void PrintItems(IReadOnlyList<MissionItem> items, CommandContext ctx)
    {
        if (items.Count == 0)
        {
            ctx.Output.WriteLine("No items");
            return;
        }

        ctx.Output.WriteLine();
        ctx.Output.WriteLine(
            $"{"Seq", 4} {"Frame", -8} {"Command", -22} {"P1", 10} {"P2", 10} {"P3", 10} {"P4", 10} {"Lat", 12} {"Lon", 12} {"Alt", 8}"
        );

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var cmdName = Enum.IsDefined((MAVLink.MAV_CMD)item.Command)
                ? item.Command.ToString()
                : $"CMD({(ushort)item.Command})";

            ctx.Output.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,4} {1,-8} {2,-22} {3,10:F2} {4,10:F2} {5,10:F2} {6,10:F2} {7,12:F7} {8,12:F7} {9,8:F1}",
                    i,
                    item.Frame,
                    cmdName,
                    item.Param1,
                    item.Param2,
                    item.Param3,
                    item.Param4,
                    item.Latitude,
                    item.Longitude,
                    item.Altitude
                )
            );
        }
    }

    private static int SaveFile(
        MAVLink.MAV_MISSION_TYPE type,
        string path,
        IReadOnlyList<MissionItem> items,
        Vehicle vehicle
    ) =>
        type switch
        {
            MAVLink.MAV_MISSION_TYPE.MISSION => MissionFile.SaveMission(
                path,
                items,
                new HomePosition(vehicle.HomeLatitude, vehicle.HomeLongitude, vehicle.HomeAltitude)
            ),
            MAVLink.MAV_MISSION_TYPE.FENCE => MissionFile.SaveFence(path, items),
            MAVLink.MAV_MISSION_TYPE.RALLY => MissionFile.SaveRally(path, items),
            _ => 0,
        };

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
