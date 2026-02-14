// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Channels;

/// <summary>
/// Sends a MAVLink message on a channel.
/// </summary>
public delegate Task SendMessageDelegate(
    MAVLink.MAVLINK_MSG_ID msgId,
    object data,
    CancellationToken ct
);
