// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reactive.Linq;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Channels;

/// <summary>
/// Provides parameter download via PARAM_REQUEST_LIST with gap fill.
/// </summary>
internal static class ParamListDownload
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Downloads all parameters via PARAM_REQUEST_LIST with gap fill.
    /// Parameters arrive through the channel's message stream; the caller's
    /// existing PARAM_VALUE subscription handles cache population.
    /// </summary>
    internal static async Task<int> DownloadAsync(
        MavChannel channel,
        byte targetSysId,
        ILogger logger,
        IProgress<ParamDownloadProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        logger.LogInformation("Downloading parameters via PARAM_REQUEST_LIST");

        var receivedIndices = new HashSet<ushort>();
        ushort? expectedCount = null;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => done.TrySetCanceled(ct));

        // Subscribe to track indices for gap detection.
        var paramStream = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_param_value_t>());

        using var sub = paramStream.Subscribe(
            pv =>
            {
                expectedCount ??= pv.param_count;

                // 65535 = index unknown (name-based reads); skip for gap tracking.
                if (pv.param_index != 65535)
                    receivedIndices.Add(pv.param_index);

                progress?.Report(
                    new ParamDownloadProgress(
                        receivedIndices.Count,
                        expectedCount ?? 0,
                        ViaFtp: false
                    )
                );

                if (expectedCount.HasValue && receivedIndices.Count >= expectedCount.Value)
                    done.TrySetResult();
            },
            ex => done.TrySetException(ex)
        );

        // Send initial PARAM_REQUEST_LIST.
        var listMsg = new MAVLink.mavlink_param_request_list_t
        {
            target_system = targetSysId,
            target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
        };

        await channel
            .SendAsync(MAVLink.MAVLINK_MSG_ID.PARAM_REQUEST_LIST, listMsg, ct)
            .ConfigureAwait(false);

        // Wait for the initial flood, then gap fill on stall.
        var lastCount = 0;

        while (!done.Task.IsCompleted)
        {
            await Task.WhenAny(done.Task, Task.Delay(StallTimeout, ct)).ConfigureAwait(false);

            if (done.Task.IsCompleted)
                break;

            var currentCount = receivedIndices.Count;

            if (currentCount > lastCount)
            {
                // Still making progress.
                lastCount = currentCount;
                continue;
            }

            // Stalled.
            if (expectedCount.HasValue)
            {
                await RequestMissingAsync(
                        channel,
                        targetSysId,
                        receivedIndices,
                        expectedCount.Value,
                        logger,
                        ct
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                // Don't know count yet -- re-request the full list.
                logger.LogDebug("No params received, re-requesting list");
                await channel
                    .SendAsync(MAVLink.MAVLINK_MSG_ID.PARAM_REQUEST_LIST, listMsg, ct)
                    .ConfigureAwait(false);
            }

            lastCount = currentCount;
        }

        await done.Task.ConfigureAwait(false);

        logger.LogInformation(
            "Downloaded {Count} parameters via PARAM_REQUEST_LIST",
            receivedIndices.Count
        );
        return expectedCount ?? 0;
    }

    private static async Task RequestMissingAsync(
        MavChannel channel,
        byte targetSysId,
        HashSet<ushort> receivedIndices,
        ushort total,
        ILogger logger,
        CancellationToken ct
    )
    {
        var batch = 0;
        for (ushort i = 0; i < total && batch < 10; i++)
        {
            if (receivedIndices.Contains(i))
                continue;

            var readMsg = new MAVLink.mavlink_param_request_read_t
            {
                target_system = targetSysId,
                target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
                param_index = (short)i,
                param_id = new byte[16],
            };

            await channel
                .SendAsync(MAVLink.MAVLINK_MSG_ID.PARAM_REQUEST_READ, readMsg, ct)
                .ConfigureAwait(false);
            batch++;
        }

        logger.LogDebug(
            "Requested {Batch} missing params ({Received}/{Total})",
            batch,
            receivedIndices.Count,
            total
        );
    }
}
