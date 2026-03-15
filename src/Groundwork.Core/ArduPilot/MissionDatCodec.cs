// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Buffers.Binary;
using Groundwork.Core.Vehicles;

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Provides encode/decode for ArduPilot's @MISSION/*.dat binary format
/// served via AP_Filesystem_Mission.
/// </summary>
internal static class MissionDatCodec
{
    private const ushort Magic = 0x763d;
    private const int HeaderSize = 10;

    // Packed mavlink_mission_item_int_t in MAVLink v2 wire order:
    // 4 floats (16) + 2 ints (8) + 1 float (4) + 2 ushorts (4)
    //   + 4 bytes (4) + 1 byte extension (mission_type) = 38 bytes.
    private const int ItemSize = 38;

    /// <summary>
    /// Decodes an @MISSION/*.dat binary blob into mission items.
    /// </summary>
    internal static List<MissionItem> Decode(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Mission FTP data too short for header");

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0));
        if (magic != Magic)
            throw new InvalidDataException($"Bad mission.dat magic: 0x{magic:X4}");

        var numItems = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));

        var expectedSize = HeaderSize + numItems * ItemSize;
        if (data.Length < expectedSize)
            throw new InvalidDataException(
                $"Mission FTP data too short: {data.Length} < {expectedSize}"
            );

        var items = new List<MissionItem>(numItems);
        for (int i = 0; i < numItems; i++)
        {
            var offset = HeaderSize + i * ItemSize;
            var span = data.AsSpan(offset);

            var param1 = BinaryPrimitives.ReadSingleLittleEndian(span);
            var param2 = BinaryPrimitives.ReadSingleLittleEndian(span[4..]);
            var param3 = BinaryPrimitives.ReadSingleLittleEndian(span[8..]);
            var param4 = BinaryPrimitives.ReadSingleLittleEndian(span[12..]);
            var x = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
            var y = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
            var z = BinaryPrimitives.ReadSingleLittleEndian(span[24..]);
            // span[28..30] = seq (wire index, not stored)
            var command = BinaryPrimitives.ReadUInt16LittleEndian(span[30..]);
            // span[32] = target_system, span[33] = target_component (routing, not stored)
            var frame = span[34];
            var current = span[35];
            var autocontinue = span[36];
            // span[37] = mission_type (extension field, not stored)

            items.Add(
                new MissionItem(
                    (MAVLink.MAV_FRAME)frame,
                    (MAVLink.MAV_CMD)command,
                    param1,
                    param2,
                    param3,
                    param4,
                    x,
                    y,
                    z,
                    autocontinue,
                    current
                )
            );
        }

        return items;
    }

    /// <summary>
    /// Encodes mission items into @MISSION/*.dat binary format.
    /// </summary>
    internal static byte[] Encode(
        IReadOnlyList<MissionItem> items,
        MAVLink.MAV_MISSION_TYPE missionType
    )
    {
        var data = new byte[HeaderSize + items.Count * ItemSize];

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)missionType);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0); // options
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 0); // start_index
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var offset = HeaderSize + i * ItemSize;
            var span = data.AsSpan(offset);

            BinaryPrimitives.WriteSingleLittleEndian(span, item.Param1);
            BinaryPrimitives.WriteSingleLittleEndian(span[4..], item.Param2);
            BinaryPrimitives.WriteSingleLittleEndian(span[8..], item.Param3);
            BinaryPrimitives.WriteSingleLittleEndian(span[12..], item.Param4);
            BinaryPrimitives.WriteInt32LittleEndian(span[16..], item.X);
            BinaryPrimitives.WriteInt32LittleEndian(span[20..], item.Y);
            BinaryPrimitives.WriteSingleLittleEndian(span[24..], item.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(span[28..], (ushort)i);
            BinaryPrimitives.WriteUInt16LittleEndian(span[30..], (ushort)item.Command);
            span[32] = 0; // target_system
            span[33] = 0; // target_component
            span[34] = (byte)item.Frame;
            span[35] = item.Current;
            span[36] = item.Autocontinue;
            span[37] = (byte)missionType;
        }

        return data;
    }
}
