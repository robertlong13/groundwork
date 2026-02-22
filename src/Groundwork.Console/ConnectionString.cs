// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;
using System.Net;
using Groundwork.Core.Connections;
using Microsoft.Extensions.Logging;

namespace Groundwork.Console;

/// <summary>
/// Provides parsing of MAVProxy-style connection descriptors into <see cref="IConnection"/> instances.
/// </summary>
/// <remarks>
/// Supported formats: <c>udpin:PORT</c>, <c>udpin:ADDRESS:PORT</c>,
/// <c>path.tlog</c>, <c>path.tlog:SPEED</c>.
/// </remarks>
public static class ConnectionString
{
    public static IConnection Parse(string descriptor, ILoggerFactory loggerFactory)
    {
        // Tlog without speed suffix.
        if (descriptor.EndsWith(".tlog", StringComparison.OrdinalIgnoreCase))
        {
            return new TlogConnection(descriptor, loggerFactory.CreateLogger<TlogConnection>());
        }

        // Tlog with :speed suffix (e.g., flight.tlog:3).
        var lastColon = descriptor.LastIndexOf(':');
        if (lastColon > 0)
        {
            var path = descriptor[..lastColon];
            var speedStr = descriptor[(lastColon + 1)..];

            if (
                path.EndsWith(".tlog", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(
                    speedStr,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var speed
                )
            )
            {
                return new TlogConnection(
                    path,
                    loggerFactory.CreateLogger<TlogConnection>(),
                    speed
                );
            }
        }

        // Scheme:params.
        var schemeEnd = descriptor.IndexOf(':');
        if (schemeEnd < 0)
        {
            throw new FormatException(
                $"Invalid connection string: '{descriptor}'. "
                    + "Expected scheme:params (e.g., udpin:14550) or path.tlog."
            );
        }

        var scheme = descriptor[..schemeEnd].ToLowerInvariant();
        var rest = descriptor[(schemeEnd + 1)..];

        return scheme switch
        {
            "udpin" => ParseUdpIn(rest, loggerFactory),
            _ => throw new FormatException(
                $"Unsupported connection type: '{scheme}'. Supported: udpin, *.tlog"
            ),
        };
    }

    /// <summary>
    /// Parses <c>PORT</c> or <c>ADDRESS:PORT</c>.
    /// </summary>
    private static IConnection ParseUdpIn(string rest, ILoggerFactory loggerFactory)
    {
        var colon = rest.LastIndexOf(':');

        if (colon < 0)
        {
            // udpin:PORT
            if (!int.TryParse(rest, out var port) || port < 1 || port > 65535)
                throw new FormatException($"Invalid port: '{rest}'");

            return new UdpListenConnection(port, loggerFactory.CreateLogger<UdpListenConnection>());
        }

        // udpin:ADDRESS:PORT
        var addressStr = rest[..colon];
        var portStr = rest[(colon + 1)..];

        if (!IPAddress.TryParse(addressStr, out var address))
            throw new FormatException($"Invalid bind address: '{addressStr}'");

        if (!int.TryParse(portStr, out var p) || p < 1 || p > 65535)
            throw new FormatException($"Invalid port: '{portStr}'");

        return new UdpListenConnection(
            address,
            p,
            loggerFactory.CreateLogger<UdpListenConnection>()
        );
    }
}
