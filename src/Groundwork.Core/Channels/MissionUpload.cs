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
/// Provides mission/fence/rally upload via the MISSION_COUNT / MISSION_ITEM_INT protocol.
/// </summary>
/// <remarks>
/// Reactive vehicle-driven upload: respond to every MISSION_REQUEST with the
/// requested item, finish only on MISSION_ACK(ACCEPTED). Non-ACCEPTED ACKs
/// (like INVALID_SEQUENCE from late duplicate items on lossy links) are
/// ignored -- the vehicle keeps its upload session open and re-requests
/// whatever it actually needs.
/// </remarks>
internal static class MissionUpload
{
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BaseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Uploads items to the vehicle.
    /// </summary>
    /// <returns>The <see cref="MAVLink.MAV_MISSION_RESULT"/> from the vehicle's ACK.</returns>
    internal static async Task<MAVLink.MAV_MISSION_RESULT> UploadAsync(
        MavChannel channel,
        byte targetSysId,
        IReadOnlyList<MissionItem> items,
        ILogger logger,
        MAVLink.MAV_MISSION_TYPE missionType = MAVLink.MAV_MISSION_TYPE.MISSION,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        logger.LogInformation("Uploading {Count} {Type} items", items.Count, missionType);

        var total = (ushort)items.Count;
        var targetCompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

        // Build wire-ready items with seq matching array index.
        // MissionItem has no seq field; it is assigned here for the wire.
        var wireItems = new MAVLink.mavlink_mission_item_int_t[total];
        for (ushort i = 0; i < total; i++)
        {
            wireItems[i] = items[i].ToMavLink(targetSysId, targetCompId, missionType);
            wireItems[i].seq = i;
        }

        // Listen for both MISSION_REQUEST_INT and deprecated MISSION_REQUEST.
        var requestStream = channel
            .Messages.Where(m =>
                m.sysid == targetSysId
                && (
                    m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_REQUEST_INT
                    || m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_REQUEST
                )
            )
            .Select(ExtractRequestSeq);

        // Only finish on ACCEPTED. INVALID_SEQUENCE and other non-fatal
        // NACKs are ignored -- the vehicle keeps its session open.
        var accepted = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_ACK && m.sysid == targetSysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_mission_ack_t>())
            .Where(a => a.mission_type == (byte)missionType)
            .Where(a =>
                (MAVLink.MAV_MISSION_RESULT)a.type
                != MAVLink.MAV_MISSION_RESULT.MAV_MISSION_INVALID_SEQUENCE
            );

        var done = new TaskCompletionSource<MAVLink.MAV_MISSION_RESULT>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = ct.Register(() => done.TrySetCanceled(ct));

        var highestSent = -1;

        // Only respond to requests for seq <= highestSent+1. A request
        // for a higher seq is stale from a previous upload session -- if
        // we respond, the item arrives at the vehicle before MISSION_COUNT
        // and gets rejected with MAV_MISSION_ERROR (!receiving).
        using var reqSub = requestStream.Subscribe(seq =>
        {
            var highest = Volatile.Read(ref highestSent);
            if (seq < total && seq <= highest + 1)
            {
                _ = channel.SendAsync(MAVLink.MAVLINK_MSG_ID.MISSION_ITEM_INT, wireItems[seq], ct);

                if (seq > highest)
                {
                    Volatile.Write(ref highestSent, seq);
                    progress?.Report(new MissionTransferProgress(seq + 1, total));
                }
            }
        });

        // Ignore ACKs that arrive before we've sent any items -- they're
        // from stale items of a previous upload still in flight.
        using var ackSub = accepted.Subscribe(ack =>
        {
            if (Volatile.Read(ref highestSent) >= 0)
                done.TrySetResult((MAVLink.MAV_MISSION_RESULT)ack.type);
        });

        // Send MISSION_COUNT to initiate. Resend if no request arrives
        // (MISSION_COUNT may have been dropped on a lossy link).
        var countMsg = new MAVLink.mavlink_mission_count_t
        {
            target_system = targetSysId,
            target_component = targetCompId,
            count = total,
            mission_type = (byte)missionType,
        };

        await channel
            .SendAsync(MAVLink.MAVLINK_MSG_ID.MISSION_COUNT, countMsg, ct)
            .ConfigureAwait(false);

        for (int countRetry = 0; countRetry < 3; countRetry++)
        {
            await Task.WhenAny(done.Task, Task.Delay(PerItemTimeout, ct)).ConfigureAwait(false);
            if (done.Task.IsCompleted || Volatile.Read(ref highestSent) >= 0)
                break;

            logger.LogDebug(
                "Mission upload: resending MISSION_COUNT (attempt {Attempt})",
                countRetry + 2
            );
            await channel
                .SendAsync(MAVLink.MAVLINK_MSG_ID.MISSION_COUNT, countMsg, ct)
                .ConfigureAwait(false);
        }

        var timeout = BaseTimeout + PerItemTimeout * total;

        var completed = await Task.WhenAny(done.Task, Task.Delay(timeout, ct))
            .ConfigureAwait(false);

        if (completed != done.Task)
            throw new TimeoutException(
                $"Mission upload timed out (sent {Volatile.Read(ref highestSent) + 1}/{total} items)"
            );

        var result = await done.Task.ConfigureAwait(false);

        if (result == MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
            logger.LogInformation("{Type} upload accepted ({Count} items)", missionType, total);
        else
            logger.LogWarning("{Type} upload rejected: {Result}", missionType, result);

        return result;
    }

    private static ushort ExtractRequestSeq(MAVLink.MAVLinkMessage msg)
    {
        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_REQUEST_INT)
            return msg.ToStructure<MAVLink.mavlink_mission_request_int_t>().seq;

        return msg.ToStructure<MAVLink.mavlink_mission_request_t>().seq;
    }
}
