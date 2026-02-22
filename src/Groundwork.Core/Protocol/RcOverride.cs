// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Protocol;

/// <summary>
/// Provides RC_CHANNELS_OVERRIDE message construction.
/// </summary>
public static class RcOverride
{
    /// <summary>
    /// Gets the number of RC channels supported by RC_CHANNELS_OVERRIDE.
    /// </summary>
    public const int MaxChannels = 18;

    /// <summary>
    /// Builds an RC_CHANNELS_OVERRIDE message from a channel-to-PWM map.
    /// Channels not present in the map default to 0 (release).
    /// </summary>
    /// <param name="sysId">Target system ID.</param>
    /// <param name="overrides">Channel numbers (1-18) mapped to PWM values.</param>
    public static MAVLink.mavlink_rc_channels_override_t BuildMessage(
        byte sysId,
        IReadOnlyDictionary<int, ushort> overrides
    )
    {
        var msg = new MAVLink.mavlink_rc_channels_override_t
        {
            target_system = sysId,
            target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
        };

        foreach (var (ch, val) in overrides)
            SetChannel(ref msg, ch, val);

        return msg;
    }

    private static void SetChannel(
        ref MAVLink.mavlink_rc_channels_override_t msg,
        int channel,
        ushort pwm
    )
    {
        switch (channel)
        {
            case 1:
                msg.chan1_raw = pwm;
                break;
            case 2:
                msg.chan2_raw = pwm;
                break;
            case 3:
                msg.chan3_raw = pwm;
                break;
            case 4:
                msg.chan4_raw = pwm;
                break;
            case 5:
                msg.chan5_raw = pwm;
                break;
            case 6:
                msg.chan6_raw = pwm;
                break;
            case 7:
                msg.chan7_raw = pwm;
                break;
            case 8:
                msg.chan8_raw = pwm;
                break;
            case 9:
                msg.chan9_raw = pwm;
                break;
            case 10:
                msg.chan10_raw = pwm;
                break;
            case 11:
                msg.chan11_raw = pwm;
                break;
            case 12:
                msg.chan12_raw = pwm;
                break;
            case 13:
                msg.chan13_raw = pwm;
                break;
            case 14:
                msg.chan14_raw = pwm;
                break;
            case 15:
                msg.chan15_raw = pwm;
                break;
            case 16:
                msg.chan16_raw = pwm;
                break;
            case 17:
                msg.chan17_raw = pwm;
                break;
            case 18:
                msg.chan18_raw = pwm;
                break;
        }
    }
}
