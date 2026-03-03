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
using Groundwork.Core.ArduPilot;
using Groundwork.Core.Vehicles;

namespace Groundwork.Core.Tests.ArduPilot;

public class ParamPckDecoderTests
{
    private const ushort MagicNoDefaults = 0x671b;
    private const ushort MagicWithDefaults = 0x671c;

    [Fact]
    public void DecodesSingleFloatParam_NoDefaults()
    {
        var data = BuildParamPck(
            MagicNoDefaults,
            new ParamBuilder("BATT_MONITOR", ParamType.Float, 4f)
        );

        var result = ParamPckDecoder.Decode(data);

        Assert.Single(result);
        Assert.True(result.ContainsKey("BATT_MONITOR"));
        Assert.Equal(4.0, result["BATT_MONITOR"].Value);
        Assert.Null(result["BATT_MONITOR"].DefaultValue);
    }

    [Fact]
    public void DecodesSingleInt32Param_WithDefaults_SameValue()
    {
        // When flags bit 0 is not set and magic is with-defaults,
        // default equals current value (no separate default encoded).
        var data = BuildParamPck(
            MagicWithDefaults,
            new ParamBuilder("SYSID_THISMAV", ParamType.Int32, 1, hasSeparateDefault: false)
        );

        var result = ParamPckDecoder.Decode(data);

        Assert.Single(result);
        Assert.Equal(1.0, result["SYSID_THISMAV"].Value);
        Assert.Equal(1.0, result["SYSID_THISMAV"].DefaultValue);
    }

    [Fact]
    public void DecodesSingleParam_WithSeparateDefault()
    {
        var data = BuildParamPck(
            MagicWithDefaults,
            new ParamBuilder(
                "BATT_LOW_VOLT",
                ParamType.Float,
                10.5f,
                hasSeparateDefault: true,
                defaultValue: 14.0f
            )
        );

        var result = ParamPckDecoder.Decode(data);

        Assert.Single(result);
        Assert.Equal(10.5, result["BATT_LOW_VOLT"].Value, 5);
        Assert.NotNull(result["BATT_LOW_VOLT"].DefaultValue);
        Assert.Equal(14.0, result["BATT_LOW_VOLT"].DefaultValue!.Value, 5);
    }

    [Fact]
    public void DecodesNameCompression()
    {
        // Two params: "BATT_MONITOR" and "BATT_LOW_VOLT".
        // Second param shares "BATT_" (5 chars) with the first.
        var data = BuildParamPck(
            MagicNoDefaults,
            new ParamBuilder("BATT_MONITOR", ParamType.Float, 4f),
            new ParamBuilder("BATT_LOW_VOLT", ParamType.Float, 10.5f, commonLen: 5)
        );

        var result = ParamPckDecoder.Decode(data);

        Assert.Equal(2, result.Count);
        Assert.Equal(4.0, result["BATT_MONITOR"].Value, 5);
        Assert.Equal(10.5, result["BATT_LOW_VOLT"].Value, 5);
    }

    [Fact]
    public void DecodesInt8Param()
    {
        var data = BuildParamPck(MagicNoDefaults, new ParamBuilder("GPS_TYPE", ParamType.Int8, 1));

        var result = ParamPckDecoder.Decode(data);

        Assert.Equal(1.0, result["GPS_TYPE"].Value);
    }

    [Fact]
    public void DecodesInt16Param()
    {
        var data = BuildParamPck(
            MagicNoDefaults,
            new ParamBuilder("RC1_MIN", ParamType.Int16, 1100)
        );

        var result = ParamPckDecoder.Decode(data);

        Assert.Equal(1100.0, result["RC1_MIN"].Value);
    }

    [Fact]
    public void ThrowsOnBadMagic()
    {
        var data = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 0xBEEF);

        Assert.Throws<FormatException>(() => ParamPckDecoder.Decode(data));
    }

    [Fact]
    public void ThrowsOnTruncatedData()
    {
        Assert.Throws<FormatException>(() => ParamPckDecoder.Decode(new byte[2]));
    }

    [Fact]
    public void DecodesRealParamPck()
    {
        var path = Path.Combine(FindSolutionRoot(), "data", "param.pck");
        var data = File.ReadAllBytes(path);

        var result = ParamPckDecoder.Decode(data);

        // SITL QuadPlane: 1566 params, with-defaults (0x671C).
        Assert.Equal(1566, result.Count);

        // First param: FORMAT_VERSION, int16, value=13.
        Assert.Equal(13.0, result["FORMAT_VERSION"].Value);
        Assert.Equal(13.0, result["FORMAT_VERSION"].DefaultValue);

        // Int8 param.
        Assert.Equal(0.0, result["BATT_MONITOR"].Value);

        // Int32 param with value != default.
        Assert.Equal(65540.0, result["BARO1_DEVID"].Value);
        Assert.Equal(0.0, result["BARO1_DEVID"].DefaultValue);

        // Float param with value != default.
        var compassDec = result["COMPASS_DEC"];
        Assert.Equal(0.2233572155237198, compassDec.Value, 5);
        Assert.Equal(0.0, compassDec.DefaultValue);

        // Last param: QWIK_ENABLE.
        Assert.True(result.ContainsKey("QWIK_ENABLE"));
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Groundwork.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not find solution root");
    }

    // -- Test helpers --

    private enum ParamType : byte
    {
        Int8 = 1,
        Int16 = 2,
        Int32 = 3,
        Float = 4,
    }

    private record ParamBuilder(
        string Name,
        ParamType Type,
        double Value,
        int commonLen = 0,
        bool hasSeparateDefault = false,
        double defaultValue = 0
    );

    private static byte[] BuildParamPck(ushort magic, params ParamBuilder[] entries)
    {
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // Header.
        writer.Write(magic);
        writer.Write((ushort)entries.Length); // num_params
        writer.Write((ushort)entries.Length); // total_params

        var previousName = string.Empty;

        foreach (var entry in entries)
        {
            // type/flags byte.
            int flags = entry.hasSeparateDefault ? 1 : 0;
            byte typeFlags = (byte)((flags << 4) | (byte)entry.Type);
            writer.Write(typeFlags);

            // name_len-1 (upper nibble) and common_len (lower nibble).
            var suffix = entry.Name[entry.commonLen..];
            byte nameLens = (byte)(((suffix.Length - 1) << 4) | entry.commonLen);
            writer.Write(nameLens);

            // Name suffix bytes.
            writer.Write(Encoding.ASCII.GetBytes(suffix));

            // Value.
            WriteValue(writer, entry.Type, entry.Value);

            // Separate default (only if magic is with-defaults AND flag set).
            if (magic == MagicWithDefaults && entry.hasSeparateDefault)
                WriteValue(writer, entry.Type, entry.defaultValue);

            previousName = entry.Name;
        }

        return ms.ToArray();
    }

    private static void WriteValue(BinaryWriter writer, ParamType type, double value)
    {
        switch (type)
        {
            case ParamType.Int8:
                writer.Write((sbyte)value);
                break;
            case ParamType.Int16:
                writer.Write((short)value);
                break;
            case ParamType.Int32:
                writer.Write((int)value);
                break;
            case ParamType.Float:
                writer.Write((float)value);
                break;
        }
    }
}
