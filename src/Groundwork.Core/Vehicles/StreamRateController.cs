// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Channels;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Provides outbound telemetry rate management for a single vehicle.
/// </summary>
public sealed class StreamRateController
{
    private const byte AutopilotCompId = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

    private readonly Lock _lock = new();
    private readonly byte _targetSysId;

    private readonly Dictionary<MAVLink.MAV_DATA_STREAM, int> _streamRates = new();

    private readonly Dictionary<MAVLink.MAVLINK_MSG_ID, HashSet<MessageRateLease>> _messageLeases =
        new();

    private readonly List<ChannelLink> _channels = new();

    public StreamRateController(
        byte targetSysId,
        IReadOnlyDictionary<MAVLink.MAV_DATA_STREAM, int>? defaultStreamRates = null
    )
    {
        _targetSysId = targetSysId;

        if (defaultStreamRates is not null)
        {
            foreach (var (stream, rateHz) in defaultStreamRates)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(rateHz);
                _streamRates[stream] = rateHz;
            }
        }
    }

    /// <summary>
    /// Current per-stream rates (Hz). Presence means the stream is
    /// managed (including 0 = actively stopped). Absence means no opinion.
    /// Returns a snapshot.
    /// </summary>
    public IReadOnlyDictionary<MAVLink.MAV_DATA_STREAM, int> StreamRates
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<MAVLink.MAV_DATA_STREAM, int>(_streamRates);
            }
        }
    }

    /// <summary>
    /// Sets the rate for a bulk data stream and immediately sends the
    /// request to all channels. Last writer wins. Zero stops the stream.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="rateHz"/> is negative.
    /// </exception>
    public void SetStreamRate(MAVLink.MAV_DATA_STREAM stream, int rateHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rateHz);

        SendMessageDelegate[] senders;

        lock (_lock)
        {
            _streamRates[stream] = rateHz;
            senders = [.. _channels.Select(c => c.Sender)];
        }

        var req = new MAVLink.mavlink_request_data_stream_t
        {
            target_system = _targetSysId,
            target_component = AutopilotCompId,
            req_stream_id = (byte)stream,
            req_message_rate = (ushort)rateHz,
            start_stop = (byte)(rateHz > 0 ? 1 : 0),
        };

        foreach (var sender in senders)
            _ = sender(MAVLink.MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req, CancellationToken.None);
    }

    /// <summary>
    /// Removes any configured rate for a stream. Sends nothing.
    /// </summary>
    public void ClearStreamRate(MAVLink.MAV_DATA_STREAM stream)
    {
        lock (_lock)
        {
            _streamRates.Remove(stream);
        }
    }

    /// <summary>
    /// Requests a specific message at the given rate. Multiple consumers
    /// can request the same message; the highest rate wins. Disposing the
    /// returned handle removes this request and immediately sends the
    /// updated rate (or disables the message if no leases remain).
    /// </summary>
    public IDisposable RequestMessageRate(MAVLink.MAVLINK_MSG_ID msgId, int rateHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateHz);

        SendMessageDelegate[] senders;
        float intervalUs;
        MessageRateLease lease;

        lock (_lock)
        {
            if (!_messageLeases.TryGetValue(msgId, out var leases))
            {
                leases = [];
                _messageLeases[msgId] = leases;
            }

            lease = new MessageRateLease(this, msgId, rateHz);
            leases.Add(lease);

            intervalUs = 1e6f / EffectiveMessageRate(msgId);
            senders = [.. _channels.Select(c => c.Sender)];
        }

        FireMessageInterval(senders, msgId, intervalUs);

        return lease;
    }

    /// <summary>
    /// Registers a channel's outbound sender and inbound message stream.
    /// Current rates are sent on the new channel immediately. The message
    /// stream is retained for rate monitoring.
    /// </summary>
    internal void AddChannel(
        SendMessageDelegate sender,
        IObservable<MAVLink.MAVLinkMessage> messages
    )
    {
        KeyValuePair<MAVLink.MAV_DATA_STREAM, int>[] streams;
        (MAVLink.MAVLINK_MSG_ID MsgId, int RateHz)[] msgRates;

        lock (_lock)
        {
            _channels.Add(new ChannelLink(sender, messages));
            streams = _streamRates.ToArray();

            var msgList = new List<(MAVLink.MAVLINK_MSG_ID, int)>();
            foreach (var (msgId, leases) in _messageLeases)
            {
                if (leases.Count == 0)
                    continue;
                msgList.Add((msgId, EffectiveMessageRate(msgId)));
            }

            msgRates = msgList.ToArray();
        }

        foreach (var (stream, rateHz) in streams)
        {
            var req = new MAVLink.mavlink_request_data_stream_t
            {
                target_system = _targetSysId,
                target_component = AutopilotCompId,
                req_stream_id = (byte)stream,
                req_message_rate = (ushort)rateHz,
                start_stop = (byte)(rateHz > 0 ? 1 : 0),
            };
            _ = sender(MAVLink.MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req, CancellationToken.None);
        }

        foreach (var (msgId, rateHz) in msgRates)
            FireMessageInterval([sender], msgId, 1e6f / rateHz);
    }

    /// <summary>
    /// Removes a channel by its sender delegate.
    /// </summary>
    internal void RemoveChannel(SendMessageDelegate sender)
    {
        lock (_lock)
        {
            var index = _channels.FindIndex(l => l.Sender == sender);
            if (index >= 0)
                _channels.RemoveAt(index);
        }
    }

    private int EffectiveMessageRate(MAVLink.MAVLINK_MSG_ID msgId)
    {
        if (!_messageLeases.TryGetValue(msgId, out var leases) || leases.Count == 0)
            return 0;

        var max = 0;
        foreach (var lease in leases)
        {
            if (lease.RateHz > max)
                max = lease.RateHz;
        }

        return max;
    }

    private void RemoveLease(MessageRateLease lease)
    {
        SendMessageDelegate[] senders;
        float newIntervalUs;

        lock (_lock)
        {
            if (!_messageLeases.TryGetValue(lease.MsgId, out var leases))
                return;

            leases.Remove(lease);

            if (leases.Count == 0)
            {
                _messageLeases.Remove(lease.MsgId);
                newIntervalUs = -1f;
            }
            else
            {
                newIntervalUs = 1e6f / EffectiveMessageRate(lease.MsgId);
            }

            senders = [.. _channels.Select(c => c.Sender)];
        }

        FireMessageInterval(senders, lease.MsgId, newIntervalUs);
    }

    private void FireMessageInterval(
        SendMessageDelegate[] senders,
        MAVLink.MAVLINK_MSG_ID msgId,
        float intervalUs
    )
    {
        var cmd = new MAVLink.mavlink_command_long_t
        {
            target_system = _targetSysId,
            target_component = AutopilotCompId,
            command = (ushort)MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL,
            param1 = (float)msgId,
            param2 = intervalUs,
        };

        foreach (var sender in senders)
            _ = sender(MAVLink.MAVLINK_MSG_ID.COMMAND_LONG, cmd, CancellationToken.None);
    }

    private sealed record ChannelLink(
        SendMessageDelegate Sender,
        IObservable<MAVLink.MAVLinkMessage> Messages
    );

    private sealed class MessageRateLease : IDisposable
    {
        private readonly StreamRateController _controller;
        private int _disposed;

        public MessageRateLease(
            StreamRateController controller,
            MAVLink.MAVLINK_MSG_ID msgId,
            int rateHz
        )
        {
            _controller = controller;
            MsgId = msgId;
            RateHz = rateHz;
        }

        public MAVLink.MAVLINK_MSG_ID MsgId { get; }

        public int RateHz { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _controller.RemoveLease(this);
        }
    }
}
