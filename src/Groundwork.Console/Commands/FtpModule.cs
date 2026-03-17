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
using Groundwork.Core.Channels.Ftp;
using Microsoft.Extensions.Logging;

namespace Groundwork.Console.Commands;

/// <summary>
/// Provides MAVFtp file transfer commands.
/// </summary>
public sealed class FtpModule
{
    public void Register(CommandRegistry commands)
    {
        commands.Register(
            "ftp get",
            new DelegateCommand(
                "Download a file from the autopilot via FTP",
                "ftp get <remote_path> [local_path]",
                (args, ctx) => GetAsync(args, ctx)
            )
        );

        commands.Register(
            "ftp put",
            new DelegateCommand(
                "Upload a file to the autopilot via FTP",
                "ftp put <local_path> [remote_path]",
                (args, ctx) => PutAsync(args, ctx)
            )
        );
    }

    private static async Task GetAsync(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: ftp get <remote_path> [local_path]");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var channel = vehicle.PrimaryChannel;
        if (channel is null)
        {
            ctx.Output.WriteLine("No channel available");
            return;
        }

        var remotePath = args[0];
        var localPath = args.Length > 1 ? args[1] : DefaultLocalName(remotePath);

        var logger = ctx.LoggerFactory.CreateLogger<FtpClient>();
        var client = new FtpClient(channel, vehicle.SysId, logger);

        try
        {
            var sw = Stopwatch.StartNew();
            var data = await client.DownloadFileAsync(remotePath, ct: ctx.ShutdownToken);
            sw.Stop();

            await File.WriteAllBytesAsync(localPath, data, ctx.ShutdownToken);
            ctx.Output.WriteLine(
                $"Downloaded {data.Length} bytes in {sw.Elapsed.TotalSeconds:F1}s -> {localPath}"
            );
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"FTP failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("FTP download timed out");
        }
    }

    private static async Task PutAsync(string[] args, CommandContext ctx)
    {
        if (args.Length == 0)
        {
            ctx.Output.WriteLine("Usage: ftp put <local_path> [remote_path]");
            return;
        }

        var vehicle = ctx.CurrentVehicle;
        if (vehicle is null)
        {
            ctx.Output.WriteLine("No vehicle connected");
            return;
        }

        var channel = vehicle.PrimaryChannel;
        if (channel is null)
        {
            ctx.Output.WriteLine("No channel available");
            return;
        }

        var localPath = args[0].Trim('"');
        var remotePath = args.Length > 1 ? args[1] : Path.GetFileName(localPath);

        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(localPath, ctx.ShutdownToken);
        }
        catch (FileNotFoundException)
        {
            ctx.Output.WriteLine($"File not found: {localPath}");
            return;
        }

        var logger = ctx.LoggerFactory.CreateLogger<FtpClient>();
        var client = new FtpClient(channel, vehicle.SysId, logger);

        try
        {
            var sw = Stopwatch.StartNew();
            await client.UploadFileAsync(remotePath, data, ctx.ShutdownToken);
            sw.Stop();

            ctx.Output.WriteLine(
                $"Uploaded {data.Length} bytes in {sw.Elapsed.TotalSeconds:F1}s -> {remotePath}"
            );
        }
        catch (IOException ex)
        {
            ctx.Output.WriteLine($"FTP failed: {ex.Message}");
        }
        catch (TimeoutException)
        {
            ctx.Output.WriteLine("FTP upload timed out");
        }
    }

    private static string DefaultLocalName(string remotePath)
    {
        // Strip query string (e.g. "@PARAM/param.pck?withdefaults=1" -> "@PARAM/param.pck").
        var path = remotePath.Split('?')[0];
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
    }
}
