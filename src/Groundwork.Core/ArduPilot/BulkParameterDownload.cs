// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Channels;
using Groundwork.Core.Channels.Ftp;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Provides bulk parameter download via FTP (param.pck).
/// </summary>
internal static class BulkParameterDownload
{
    private const string ParamPckPath = "@PARAM/param.pck?withdefaults=1";

    /// <summary>
    /// Downloads all parameters via FTP, calling <paramref name="onParam"/> for each entry.
    /// </summary>
    internal static async Task<int> DownloadViaFtpAsync(
        MavChannel channel,
        byte targetSysId,
        Action<string, ParamEntry> onParam,
        ILogger logger,
        IProgress<ParamDownloadProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        logger.LogInformation("Downloading parameters via FTP (param.pck)");

        var client = new FtpClient(channel, targetSysId, logger);

        IProgress<(int Received, int Total)>? ftpProgress =
            progress != null ? new FtpProgressAdapter(progress) : null;

        var data = await client
            .DownloadFileAsync(ParamPckPath, ftpProgress, ct)
            .ConfigureAwait(false);
        var decoded = ParamPckDecoder.Decode(data);

        foreach (var (name, entry) in decoded)
            onParam(name, entry);

        logger.LogInformation("Downloaded {Count} parameters via FTP", decoded.Count);
        return decoded.Count;
    }

    private sealed class FtpProgressAdapter(IProgress<ParamDownloadProgress> inner)
        : IProgress<(int Received, int Total)>
    {
        public void Report((int Received, int Total) value) =>
            inner.Report(new ParamDownloadProgress(value.Received, value.Total, ViaFtp: true));
    }
}
