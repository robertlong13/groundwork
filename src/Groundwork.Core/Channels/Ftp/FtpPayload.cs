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
using System.Text;

namespace Groundwork.Core.Channels.Ftp;

/// <summary>
/// Provides packing and unpacking of the 12-byte FTP header inside the
/// 251-byte FILE_TRANSFER_PROTOCOL payload.
/// </summary>
internal static class FtpPayload
{
    /// <summary>
    /// Maximum bytes of file data per FTP packet.
    /// </summary>
    internal const int MaxDataLength = 239;

    private const int HeaderSize = 12;
    private const int PayloadSize = 251;

    /// <summary>
    /// Packs an FTP request into a 251-byte payload.
    /// </summary>
    internal static byte[] Pack(
        ushort seqNumber,
        byte session,
        MAVLink.MAV_FTP_OPCODE opcode,
        byte size,
        uint offset,
        ReadOnlySpan<byte> data = default
    )
    {
        var payload = new byte[PayloadSize];

        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), seqNumber);
        payload[2] = session;
        payload[3] = (byte)opcode;
        payload[4] = size;
        // payload[5] = req_opcode (0 for requests)
        // payload[6] = burst_complete (0 for requests)
        // payload[7] = padding
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), offset);

        if (data.Length > 0)
            data[..Math.Min(data.Length, MaxDataLength)].CopyTo(payload.AsSpan(HeaderSize));

        return payload;
    }

    /// <summary>
    /// Unpacks a 251-byte FTP payload into a response struct.
    /// </summary>
    internal static FtpResponse Unpack(byte[] payload)
    {
        return new FtpResponse
        {
            SeqNumber = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0)),
            Session = payload[2],
            Opcode = (MAVLink.MAV_FTP_OPCODE)payload[3],
            Size = payload[4],
            ReqOpcode = (MAVLink.MAV_FTP_OPCODE)payload[5],
            BurstComplete = payload[6] != 0,
            Offset = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8)),
            Payload = payload,
        };
    }

    /// <summary>
    /// Encodes a file path into FTP data bytes.
    /// </summary>
    internal static byte[] EncodePath(string path) => Encoding.ASCII.GetBytes(path);
}

/// <summary>
/// Represents an unpacked FTP response.
/// </summary>
internal struct FtpResponse
{
    public ushort SeqNumber;
    public byte Session;
    public MAVLink.MAV_FTP_OPCODE Opcode;
    public byte Size;
    public MAVLink.MAV_FTP_OPCODE ReqOpcode;
    public bool BurstComplete;
    public uint Offset;

    /// <summary>
    /// Gets the raw 251-byte payload. Data starts at offset 12.
    /// </summary>
    public byte[] Payload;

    /// <summary>
    /// Gets the data portion of the response.
    /// </summary>
    public readonly ReadOnlySpan<byte> Data => Payload.AsSpan(12, Size);

    /// <summary>
    /// Gets the NAK error code (first byte of data in a NAK response).
    /// </summary>
    public readonly MAVLink.MAV_FTP_ERR NakError =>
        Opcode == MAVLink.MAV_FTP_OPCODE.NAK && Size > 0
            ? (MAVLink.MAV_FTP_ERR)Payload[12]
            : MAVLink.MAV_FTP_ERR.NONE;

    public readonly bool IsAck => Opcode == MAVLink.MAV_FTP_OPCODE.ACK;
    public readonly bool IsNak => Opcode == MAVLink.MAV_FTP_OPCODE.NAK;
}
