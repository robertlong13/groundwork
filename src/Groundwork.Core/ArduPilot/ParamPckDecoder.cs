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
using Groundwork.Core.Vehicles;

namespace Groundwork.Core.ArduPilot;

/// <summary>
/// Decodes the param.pck binary format produced by ArduPilot's
/// <c>@PARAM/param.pck</c> MAVFtp virtual file.
/// </summary>
internal static class ParamPckDecoder
{
    private const ushort MagicNoDefaults = 0x671b;
    private const ushort MagicWithDefaults = 0x671c;
    private const int HeaderSize = 6;

    // param_type values (lower nibble of type/flags byte).
    private const int TypeNone = 0;
    private const int TypeInt8 = 1;
    private const int TypeInt16 = 2;
    private const int TypeInt32 = 3;
    private const int TypeFloat = 4;

    /// <summary>
    /// Decodes a param.pck blob into a parameter dictionary.
    /// </summary>
    /// <exception cref="FormatException">Invalid magic or corrupt data.</exception>
    internal static Dictionary<string, ParamEntry> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new FormatException("param.pck too short for header");

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(data);
        bool withDefaults = magic switch
        {
            MagicNoDefaults => false,
            MagicWithDefaults => true,
            _ => throw new FormatException($"Unknown param.pck magic: 0x{magic:X4}"),
        };

        var numParams = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        // total_params at offset 4 is informational only.

        var result = new Dictionary<string, ParamEntry>(
            numParams,
            StringComparer.OrdinalIgnoreCase
        );
        var pos = HeaderSize;
        var previousName = string.Empty;

        for (int i = 0; i < numParams; i++)
        {
            // Skip pad bytes (zeros used to prevent values crossing read-packet
            // boundaries).
            while (pos < data.Length && data[pos] == 0)
                pos++;

            if (pos >= data.Length)
                throw new FormatException($"param.pck truncated at entry {i}");

            // Byte 1: type (lower nibble) and flags (upper nibble).
            byte typeFlags = data[pos++];
            int paramType = typeFlags & 0x0F;
            int flags = (typeFlags >> 4) & 0x0F;

            if (pos >= data.Length)
                throw new FormatException($"param.pck truncated at entry {i} name header");

            // Byte 2: common_len (lower nibble) and name_len-1 (upper nibble).
            byte nameLens = data[pos++];
            int commonLen = nameLens & 0x0F;
            int nameLen = ((nameLens >> 4) & 0x0F) + 1;

            if (pos + nameLen > data.Length)
                throw new FormatException($"param.pck truncated at entry {i} name");

            // Reconstruct name: prefix from previous + new suffix.
            var name = string.Concat(
                previousName.AsSpan(0, Math.Min(commonLen, previousName.Length)),
                Encoding.ASCII.GetString(data.Slice(pos, nameLen))
            );
            pos += nameLen;
            previousName = name;

            // Read the value.
            int valueSize = TypeSize(paramType);
            if (pos + valueSize > data.Length)
                throw new FormatException($"param.pck truncated at entry {i} value");

            double value = ReadValue(data.Slice(pos, valueSize), paramType);
            pos += valueSize;

            // Read optional default value.
            double? defaultValue = null;
            if (withDefaults)
            {
                bool hasSeparateDefault = (flags & 1) != 0;
                if (hasSeparateDefault)
                {
                    if (pos + valueSize > data.Length)
                        throw new FormatException($"param.pck truncated at entry {i} default");

                    defaultValue = ReadValue(data.Slice(pos, valueSize), paramType);
                    pos += valueSize;
                }
                else
                {
                    // Default equals current value.
                    defaultValue = value;
                }
            }

            result[name] = new ParamEntry(value, defaultValue);
        }

        return result;
    }

    private static int TypeSize(int paramType) =>
        paramType switch
        {
            TypeNone => 0,
            TypeInt8 => 1,
            TypeInt16 => 2,
            TypeInt32 => 4,
            TypeFloat => 4,
            _ => throw new FormatException($"Unknown param type: {paramType}"),
        };

    private static double ReadValue(ReadOnlySpan<byte> data, int paramType) =>
        paramType switch
        {
            TypeNone => 0,
            TypeInt8 => (sbyte)data[0],
            TypeInt16 => BinaryPrimitives.ReadInt16LittleEndian(data),
            TypeInt32 => BinaryPrimitives.ReadInt32LittleEndian(data),
            TypeFloat => BinaryPrimitives.ReadSingleLittleEndian(data),
            _ => throw new FormatException($"Unknown param type: {paramType}"),
        };
}
