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
using Groundwork.Core.Vehicles;

namespace Groundwork.Core.Tests.Vehicles;

public class StreamRateControllerTests
{
    private readonly List<(MAVLink.MAVLINK_MSG_ID Id, object Data)> _sent = new();

    private Task RecordSend(MAVLink.MAVLINK_MSG_ID id, object data, CancellationToken ct)
    {
        _sent.Add((id, data));
        return Task.CompletedTask;
    }

    private static readonly IObservable<MAVLink.MAVLinkMessage> NoMessages =
        Observable.Never<MAVLink.MAVLinkMessage>();

    private StreamRateController CreateController(byte sysId = 1)
    {
        var controller = new StreamRateController(sysId);
        controller.AddChannel(RecordSend, NoMessages);
        return controller;
    }

    // -- Default rate tests --

    [Fact]
    public void Constructor_SeedsDefaultRates()
    {
        var defaults = new Dictionary<MAVLink.MAV_DATA_STREAM, int>
        {
            [MAVLink.MAV_DATA_STREAM.POSITION] = 5,
            [MAVLink.MAV_DATA_STREAM.EXTRA1] = 10,
        };

        var ctrl = new StreamRateController(1, defaults);

        Assert.Equal(5, ctrl.StreamRates[MAVLink.MAV_DATA_STREAM.POSITION]);
        Assert.Equal(10, ctrl.StreamRates[MAVLink.MAV_DATA_STREAM.EXTRA1]);
    }

    [Fact]
    public void Constructor_DefaultsSentOnFirstChannel()
    {
        var defaults = new Dictionary<MAVLink.MAV_DATA_STREAM, int>
        {
            [MAVLink.MAV_DATA_STREAM.POSITION] = 5,
            [MAVLink.MAV_DATA_STREAM.EXTRA1] = 10,
        };

        var ctrl = new StreamRateController(1, defaults);
        ctrl.AddChannel(RecordSend, NoMessages);

        Assert.Equal(2, _sent.Count);
        var streams = _sent
            .Select(s =>
            {
                var req = (MAVLink.mavlink_request_data_stream_t)s.Data;
                return ((MAVLink.MAV_DATA_STREAM)req.req_stream_id, (int)req.req_message_rate);
            })
            .ToHashSet();
        Assert.Contains((MAVLink.MAV_DATA_STREAM.POSITION, 5), streams);
        Assert.Contains((MAVLink.MAV_DATA_STREAM.EXTRA1, 10), streams);
    }

    [Fact]
    public void Constructor_NegativeDefaultThrows()
    {
        var defaults = new Dictionary<MAVLink.MAV_DATA_STREAM, int>
        {
            [MAVLink.MAV_DATA_STREAM.POSITION] = -1,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamRateController(1, defaults));
    }

    // -- Stream rate tests --

    [Fact]
    public void SetStreamRate_ImmediatelySends()
    {
        var ctrl = CreateController(sysId: 7);
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);

        var (id, data) = Assert.Single(_sent);
        Assert.Equal(MAVLink.MAVLINK_MSG_ID.REQUEST_DATA_STREAM, id);
        var req = Assert.IsType<MAVLink.mavlink_request_data_stream_t>(data);
        Assert.Equal(7, req.target_system);
        Assert.Equal((byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1, req.target_component);
        Assert.Equal((byte)MAVLink.MAV_DATA_STREAM.POSITION, req.req_stream_id);
        Assert.Equal(5, req.req_message_rate);
        Assert.Equal(1, req.start_stop);
    }

    [Fact]
    public void SetStreamRate_MultipleStreams_EachSendsImmediately()
    {
        var ctrl = CreateController();
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.EXTRA1, 10);
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);

        Assert.Equal(2, _sent.Count);
        var streams = _sent
            .Select(s =>
            {
                var req = (MAVLink.mavlink_request_data_stream_t)s.Data;
                return ((MAVLink.MAV_DATA_STREAM)req.req_stream_id, (int)req.req_message_rate);
            })
            .ToHashSet();
        Assert.Contains((MAVLink.MAV_DATA_STREAM.EXTRA1, 10), streams);
        Assert.Contains((MAVLink.MAV_DATA_STREAM.POSITION, 5), streams);
    }

    [Fact]
    public void SetStreamRate_ZeroStopsStream()
    {
        var ctrl = CreateController();
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 0);

        Assert.Equal(0, ctrl.StreamRates[MAVLink.MAV_DATA_STREAM.POSITION]);

        var req = Assert.IsType<MAVLink.mavlink_request_data_stream_t>(_sent.Last().Data);
        Assert.Equal(0, req.start_stop);
    }

    [Fact]
    public void SetStreamRate_NegativeThrows()
    {
        var ctrl = CreateController();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, -1)
        );
    }

    [Fact]
    public void ClearStreamRate_RemovesFromDictionary()
    {
        var ctrl = CreateController();
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);
        ctrl.ClearStreamRate(MAVLink.MAV_DATA_STREAM.POSITION);

        Assert.Empty(ctrl.StreamRates);
    }

    [Fact]
    public void ClearStreamRate_DoesNotSend()
    {
        var ctrl = CreateController();
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);
        _sent.Clear();

        ctrl.ClearStreamRate(MAVLink.MAV_DATA_STREAM.POSITION);

        Assert.Empty(_sent);
    }

    // -- Message rate tests --

    [Fact]
    public void RequestMessageRate_ImmediatelySends()
    {
        var ctrl = CreateController(sysId: 3);
        ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 10);

        var (id, data) = Assert.Single(_sent);
        Assert.Equal(MAVLink.MAVLINK_MSG_ID.COMMAND_LONG, id);
        var cmd = Assert.IsType<MAVLink.mavlink_command_long_t>(data);
        Assert.Equal(3, cmd.target_system);
        Assert.Equal((byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1, cmd.target_component);
        Assert.Equal((ushort)MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL, cmd.command);
        Assert.Equal((float)(uint)MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, cmd.param1);

        // 10 Hz = 100000 us
        Assert.Equal(100_000f, cmd.param2, 1f);
    }

    [Fact]
    public void RequestMessageRate_HighestWins()
    {
        var ctrl = CreateController();
        ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 5);
        ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 10);

        // Second call re-evaluates effective rate across leases.
        var cmd = Assert.IsType<MAVLink.mavlink_command_long_t>(_sent.Last().Data);

        // Should be 10 Hz = 100000 us, not 5 Hz
        Assert.Equal(100_000f, cmd.param2, 1f);
    }

    [Fact]
    public void DisposeLease_ImmediatelySendsReducedRate()
    {
        var ctrl = CreateController();
        ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 5);
        var high = ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 10);
        _sent.Clear();

        high.Dispose();

        var cmd = Assert.IsType<MAVLink.mavlink_command_long_t>(Assert.Single(_sent).Data);

        // Should fall back to 5 Hz = 200000 us
        Assert.Equal(200_000f, cmd.param2, 1f);
    }

    [Fact]
    public void DisposeLastLease_ImmediatelySendsDisable()
    {
        var ctrl = CreateController();
        var lease = ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 10);
        _sent.Clear();

        lease.Dispose();

        var cmd = Assert.IsType<MAVLink.mavlink_command_long_t>(Assert.Single(_sent).Data);
        Assert.Equal(-1f, cmd.param2);
    }

    // -- Multi-sender tests --

    [Fact]
    public void MultipleChannels_AllReceiveRates()
    {
        var sent2 = new List<(MAVLink.MAVLINK_MSG_ID Id, object Data)>();
        Task RecordSend2(MAVLink.MAVLINK_MSG_ID id, object data, CancellationToken ct)
        {
            sent2.Add((id, data));
            return Task.CompletedTask;
        }

        var ctrl = CreateController();
        ctrl.AddChannel(RecordSend2, NoMessages);
        _sent.Clear();
        sent2.Clear();

        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);

        Assert.Single(_sent);
        Assert.Single(sent2);
    }

    [Fact]
    public void AddChannel_AppliesExistingRates()
    {
        var ctrl = new StreamRateController(1);
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);
        ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 10);

        // Adding a channel should fire-and-forget apply on the new sender.
        ctrl.AddChannel(RecordSend, NoMessages);

        // Both stream rate and message rate should have been sent.
        Assert.Equal(2, _sent.Count);
        Assert.Contains(_sent, s => s.Id == MAVLink.MAVLINK_MSG_ID.REQUEST_DATA_STREAM);
        Assert.Contains(_sent, s => s.Id == MAVLink.MAVLINK_MSG_ID.COMMAND_LONG);
    }

    [Fact]
    public void MultipleChannels_AllReceiveDisables()
    {
        var sent2 = new List<(MAVLink.MAVLINK_MSG_ID Id, object Data)>();
        Task RecordSend2(MAVLink.MAVLINK_MSG_ID id, object data, CancellationToken ct)
        {
            sent2.Add((id, data));
            return Task.CompletedTask;
        }

        var ctrl = CreateController();
        ctrl.AddChannel(RecordSend2, NoMessages);
        var lease = ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 10);
        _sent.Clear();
        sent2.Clear();

        lease.Dispose();

        Assert.Single(_sent);
        Assert.Single(sent2);
        var cmd1 = Assert.IsType<MAVLink.mavlink_command_long_t>(_sent[0].Data);
        var cmd2 = Assert.IsType<MAVLink.mavlink_command_long_t>(sent2[0].Data);
        Assert.Equal(-1f, cmd1.param2);
        Assert.Equal(-1f, cmd2.param2);
    }

    [Fact]
    public void RemoveChannel_StopsReceivingRates()
    {
        var sent2 = new List<(MAVLink.MAVLINK_MSG_ID Id, object Data)>();
        Task RecordSend2(MAVLink.MAVLINK_MSG_ID id, object data, CancellationToken ct)
        {
            sent2.Add((id, data));
            return Task.CompletedTask;
        }

        var ctrl = CreateController();
        ctrl.AddChannel(RecordSend2, NoMessages);
        _sent.Clear();
        sent2.Clear();

        ctrl.RemoveChannel(RecordSend2);
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.POSITION, 5);

        Assert.Single(_sent);
        Assert.Empty(sent2);
    }

    // -- Mixed tests --

    [Fact]
    public void MixedStreamAndMessage_BothSendImmediately()
    {
        var ctrl = CreateController();
        ctrl.SetStreamRate(MAVLink.MAV_DATA_STREAM.EXTRA1, 10);
        ctrl.RequestMessageRate(MAVLink.MAVLINK_MSG_ID.MOUNT_STATUS, 5);

        Assert.Equal(2, _sent.Count);
        Assert.Contains(_sent, s => s.Id == MAVLink.MAVLINK_MSG_ID.REQUEST_DATA_STREAM);
        Assert.Contains(_sent, s => s.Id == MAVLink.MAVLINK_MSG_ID.COMMAND_LONG);
    }
}
