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
using System.Reactive.Threading.Tasks;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Channels;

/// <summary>
/// Provides mission/fence/rally download via MISSION_REQUEST_INT with gap fill.
/// </summary>
internal static class MissionDownload
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(2);
    private const int WindowSize = 5;

    /// <summary>
    /// Downloads all items of the specified mission type via MISSION_REQUEST_LIST / MISSION_REQUEST_INT.
    /// </summary>
    /// <returns>The downloaded items in sequence order.</returns>
    internal static async Task<List<MissionItem>> DownloadAsync(
        MavChannel channel,
        byte targetSysId,
        ILogger logger,
        MAVLink.MAV_MISSION_TYPE missionType = MAVLink.MAV_MISSION_TYPE.MISSION,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        logger.LogInformation("Downloading {Type} via MISSION_REQUEST_LIST", missionType);

        // 1. Request the item count.
        var countTask = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_COUNT && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_mission_count_t>())
            .Where(c => c.mission_type == (byte)missionType)
            .Take(1)
            .Timeout(TimeSpan.FromSeconds(5))
            .ToTask(ct);

        await channel
            .SendAsync(
                MAVLink.MAVLINK_MSG_ID.MISSION_REQUEST_LIST,
                new MAVLink.mavlink_mission_request_list_t
                {
                    target_system = targetSysId,
                    target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
                    mission_type = (byte)missionType,
                },
                ct
            )
            .ConfigureAwait(false);

        var countMsg = await countTask.ConfigureAwait(false);
        var total = countMsg.count;

        logger.LogDebug("Mission count: {Count}", total);
        progress?.Report(new MissionTransferProgress(0, total));

        if (total == 0)
            return [];

        // 2. Request items one at a time with gap fill.
        var items = new MissionItem?[total];
        var received = new bool[total];
        var receivedCount = 0;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => done.TrySetCanceled(ct));

        var itemStream = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_ITEM_INT && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_mission_item_int_t>())
            .Where(m => m.mission_type == (byte)missionType);

        // Track the highest seq we've requested so the sliding window
        // in the subscription doesn't re-request items.
        var highestRequested = -1;

        using var sub = itemStream.Subscribe(
            m =>
            {
                if (m.seq < total)
                {
                    items[m.seq] = MissionItem.FromMavLink(m);

                    if (!Volatile.Read(ref received[m.seq]))
                    {
                        Volatile.Write(ref received[m.seq], true);
                        var count = Interlocked.Increment(ref receivedCount);

                        progress?.Report(new MissionTransferProgress(count, total));

                        if (count >= total)
                        {
                            done.TrySetResult();
                            return;
                        }
                    }

                    // Sliding window: request up to WindowSize items ahead.
                    // The vehicle sends one item per request, so pipelining
                    // requests avoids waiting a full RTT per item.
                    for (
                        ushort next = (ushort)(m.seq + 1);
                        next < total && next <= m.seq + WindowSize;
                        next++
                    )
                    {
                        if (
                            next > Volatile.Read(ref highestRequested)
                            && !Volatile.Read(ref received[next])
                        )
                        {
                            Volatile.Write(ref highestRequested, next);
                            _ = RequestItemAsync(channel, targetSysId, next, missionType, ct);
                        }
                    }
                }
            },
            ex => done.TrySetException(ex)
        );

        // Seed the first WindowSize requests.
        for (ushort i = 0; i < Math.Min((ushort)WindowSize, total); i++)
        {
            Volatile.Write(ref highestRequested, i);
            await RequestItemAsync(channel, targetSysId, i, missionType, ct).ConfigureAwait(false);
        }

        // Wait with gap fill on stall.
        var lastCount = 0;

        while (!done.Task.IsCompleted)
        {
            await Task.WhenAny(done.Task, Task.Delay(StallTimeout, ct)).ConfigureAwait(false);

            if (done.Task.IsCompleted)
                break;

            var currentCount = Volatile.Read(ref receivedCount);

            if (currentCount > lastCount)
            {
                // Still making progress -- request next missing item.
                lastCount = currentCount;
                await RequestNextMissing(channel, targetSysId, received, total, missionType, ct)
                    .ConfigureAwait(false);
                continue;
            }

            // Stalled -- gap fill batch.
            var batch = 0;
            for (ushort i = 0; i < total && batch < 10; i++)
            {
                if (Volatile.Read(ref received[i]))
                    continue;

                await RequestItemAsync(channel, targetSysId, i, missionType, ct)
                    .ConfigureAwait(false);
                batch++;
            }

            logger.LogDebug(
                "Mission gap fill: requested {Batch} items ({Received}/{Total})",
                batch,
                Volatile.Read(ref receivedCount),
                total
            );

            lastCount = currentCount;
        }

        await done.Task.ConfigureAwait(false);

        // Send ACK.
        await channel
            .SendAsync(
                MAVLink.MAVLINK_MSG_ID.MISSION_ACK,
                new MAVLink.mavlink_mission_ack_t
                {
                    target_system = targetSysId,
                    target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
                    type = (byte)MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED,
                    mission_type = (byte)missionType,
                },
                ct
            )
            .ConfigureAwait(false);

        logger.LogInformation("Downloaded {Count} {Type} items", total, missionType);
        return items.Cast<MissionItem>().ToList();
    }

    private static Task RequestItemAsync(
        MavChannel channel,
        byte targetSysId,
        ushort seq,
        MAVLink.MAV_MISSION_TYPE missionType,
        CancellationToken ct
    ) =>
        channel.SendAsync(
            MAVLink.MAVLINK_MSG_ID.MISSION_REQUEST_INT,
            new MAVLink.mavlink_mission_request_int_t
            {
                target_system = targetSysId,
                target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
                seq = seq,
                mission_type = (byte)missionType,
            },
            ct
        );

    private static async Task RequestNextMissing(
        MavChannel channel,
        byte targetSysId,
        bool[] received,
        ushort total,
        MAVLink.MAV_MISSION_TYPE missionType,
        CancellationToken ct
    )
    {
        for (ushort i = 0; i < total; i++)
        {
            if (!Volatile.Read(ref received[i]))
            {
                await RequestItemAsync(channel, targetSysId, i, missionType, ct)
                    .ConfigureAwait(false);
                return;
            }
        }
    }
}
