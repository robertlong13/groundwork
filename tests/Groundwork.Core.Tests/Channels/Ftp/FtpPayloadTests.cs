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
using Groundwork.Core.Channels.Ftp;

namespace Groundwork.Core.Tests.Channels.Ftp;

public class FtpPayloadTests
{
    [Fact]
    public void Pack_ProducesCorrectHeader()
    {
        var payload = FtpPayload.Pack(
            seqNumber: 0x1234,
            session: 5,
            opcode: MAVLink.MAV_FTP_OPCODE.READFILE,
            size: 239,
            offset: 0xABCD0000
        );

        Assert.Equal(251, payload.Length);
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(payload));
        Assert.Equal(5, payload[2]);
        Assert.Equal((byte)MAVLink.MAV_FTP_OPCODE.READFILE, payload[3]);
        Assert.Equal(239, payload[4]);
        Assert.Equal(0u, (uint)payload[5]); // req_opcode
        Assert.Equal(0u, (uint)payload[6]); // burst_complete
        Assert.Equal(0xABCD0000u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8)));
    }

    [Fact]
    public void Pack_IncludesData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var payload = FtpPayload.Pack(0, 0, MAVLink.MAV_FTP_OPCODE.OPENFILERO, 5, 0, data);

        Assert.Equal(1, payload[12]);
        Assert.Equal(2, payload[13]);
        Assert.Equal(3, payload[14]);
        Assert.Equal(4, payload[15]);
        Assert.Equal(5, payload[16]);
    }

    [Fact]
    public void Unpack_RoundTrips()
    {
        var original = FtpPayload.Pack(
            seqNumber: 42,
            session: 3,
            opcode: MAVLink.MAV_FTP_OPCODE.ACK,
            size: 4,
            offset: 1000
        );

        // Simulate the response fields.
        original[5] = (byte)MAVLink.MAV_FTP_OPCODE.READFILE; // req_opcode
        original[6] = 1; // burst_complete

        var resp = FtpPayload.Unpack(original);

        Assert.Equal(42, resp.SeqNumber);
        Assert.Equal(3, resp.Session);
        Assert.Equal(MAVLink.MAV_FTP_OPCODE.ACK, resp.Opcode);
        Assert.Equal(4, resp.Size);
        Assert.Equal(MAVLink.MAV_FTP_OPCODE.READFILE, resp.ReqOpcode);
        Assert.True(resp.BurstComplete);
        Assert.Equal(1000u, resp.Offset);
        Assert.True(resp.IsAck);
        Assert.False(resp.IsNak);
    }

    [Fact]
    public void NakError_ExtractsErrorCode()
    {
        var payload = FtpPayload.Pack(0, 0, MAVLink.MAV_FTP_OPCODE.NAK, 1, 0);
        payload[12] = (byte)MAVLink.MAV_FTP_ERR.FILENOTFOUND;

        var resp = FtpPayload.Unpack(payload);

        Assert.True(resp.IsNak);
        Assert.Equal(MAVLink.MAV_FTP_ERR.FILENOTFOUND, resp.NakError);
    }

    [Fact]
    public void EncodePath_ProducesAsciiBytes()
    {
        var bytes = FtpPayload.EncodePath("@PARAM/param.pck");
        Assert.Equal("@PARAM/param.pck", System.Text.Encoding.ASCII.GetString(bytes));
    }
}
