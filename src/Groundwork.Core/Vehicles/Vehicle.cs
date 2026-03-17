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
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Security.Cryptography;
using Groundwork.Core.Channels;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Vehicles;

/// <summary>
/// Represents a vehicle identified by hardware UID, holding references to the
/// <see cref="MavChannel"/>s that can reach it.
/// </summary>
public class Vehicle
{
    private readonly HashSet<MavChannel> _channels = new();
    private readonly Dictionary<MavChannel, IDisposable> _paramSubs = new();
    private readonly Dictionary<MavChannel, IDisposable> _missionSubs = new();
    private readonly Dictionary<MavChannel, IDisposable> _homeSubs = new();
    private readonly Dictionary<string, ParamEntry> _parameters = new(
        StringComparer.OrdinalIgnoreCase
    );
    private List<MissionItem> _mission = [];
    private List<MissionItem> _fence = [];
    private List<MissionItem> _rally = [];
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly ArduPilot.ParamMetadataFetcher? _metadataFetcher;

    public Vehicle(
        ulong uid,
        byte sysId,
        MavChannel channel,
        ILoggerFactory loggerFactory,
        IReadOnlyDictionary<MAVLink.MAV_DATA_STREAM, int>? defaultStreamRates = null,
        ArduPilot.ParamMetadataFetcher? metadataFetcher = null
    )
    {
        Uid = uid;
        SysId = sysId;
        _logger = loggerFactory.CreateLogger<Vehicle>();
        RateController = new StreamRateController(sysId, defaultStreamRates);
        _metadataFetcher = metadataFetcher;
        AddChannel(channel);
    }

    public ulong Uid { get; }

    /// <summary>
    /// Gets the MAVLink system ID, constant for a given vehicle across all links.
    /// </summary>
    public byte SysId { get; }

    /// <summary>
    /// Gets the controller for outbound telemetry rate requests on this vehicle.
    /// </summary>
    public StreamRateController RateController { get; }

    /// <summary>
    /// Gets the primary channel for this vehicle, through which outbound commands are routed.
    /// </summary>
    public MavChannel PrimaryChannel
    {
        get
        {
            lock (_lock)
            {
                // TODO: channel selection strategy (manual/auto).
                foreach (var ch in _channels)
                    return ch;
                // Constructor guarantees at least one channel.
                throw new InvalidOperationException("Vehicle has no channels");
            }
        }
    }

    /// <summary>
    /// Gets the canonical vehicle state, delegated from the primary channel.
    /// </summary>
    public VehicleState CanonicalState => PrimaryChannel.GetState(SysId);

    /// <summary>
    /// Gets the channels that can reach this vehicle.
    /// </summary>
    public IReadOnlyCollection<MavChannel> Channels
    {
        get
        {
            lock (_lock)
            {
                return _channels.ToArray();
            }
        }
    }

    /// <summary>
    /// Returns the custom_mode number for a mode name.
    /// </summary>
    /// <returns>The custom_mode number, or <see langword="null"/> if unrecognized.</returns>
    public uint? NameToMode(string name) => ArduPilot.ModeMap.NameToMode(name, CanonicalState.Type);

    /// <summary>
    /// Returns the display name for a custom_mode number.
    /// </summary>
    /// <returns>The mode display name, or "Mode(N)" for unrecognized modes.</returns>
    public string ModeToName(uint customMode) =>
        ArduPilot.ModeMap.ModeToName(customMode, CanonicalState.Type);

    /// <summary>
    /// Gets the parameter cache, populated by fetch, set, and download operations.
    /// </summary>
    public IReadOnlyDictionary<string, ParamEntry> Parameters
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, ParamEntry>(
                    _parameters,
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }
    }

    /// <summary>
    /// Gets the total parameter count last reported by the vehicle in a PARAM_VALUE message.
    /// </summary>
    public int ReportedParameterCount { get; private set; }

    /// <summary>
    /// Gets or sets the shared parameter metadata for this vehicle's firmware version.
    /// Null until metadata has been fetched.
    /// </summary>
    public IReadOnlyDictionary<string, ParamMetadata>? ParameterMetadata { get; set; }

    /// <summary>
    /// Gets the available modes for this vehicle type, keyed by custom_mode number.
    /// </summary>
    public IReadOnlyDictionary<uint, string> AvailableModes =>
        ArduPilot.ModeMap.GetModes(CanonicalState.Type);

    /// <summary>
    /// Gets the cached mission items, populated by download operations.
    /// </summary>
    public IReadOnlyList<MissionItem> Mission
    {
        get
        {
            lock (_lock)
            {
                return _mission.ToList();
            }
        }
    }

    /// <summary>
    /// Gets the cached fence items, populated by download operations.
    /// </summary>
    public IReadOnlyList<MissionItem> Fence
    {
        get
        {
            lock (_lock)
            {
                return _fence.ToList();
            }
        }
    }

    /// <summary>
    /// Gets the cached rally items, populated by download operations.
    /// </summary>
    public IReadOnlyList<MissionItem> Rally
    {
        get
        {
            lock (_lock)
            {
                return _rally.ToList();
            }
        }
    }

    /// <summary>
    /// Gets the sequence number of the currently active mission item,
    /// as last reported by MISSION_CURRENT.
    /// </summary>
    public ushort CurrentMissionSeq { get; private set; }

    /// <summary>
    /// Gets the mission state as last reported by MISSION_CURRENT.
    /// </summary>
    public MAVLink.MISSION_STATE MissionState { get; private set; }

    /// <summary>
    /// Gets the home position latitude in degrees, from HOME_POSITION
    /// messages or downloaded mission seq 0.
    /// </summary>
    public double HomeLatitude { get; private set; }

    /// <summary>
    /// Gets the home position longitude in degrees.
    /// </summary>
    public double HomeLongitude { get; private set; }

    /// <summary>
    /// Gets the home position altitude in meters MSL.
    /// </summary>
    public float HomeAltitude { get; private set; }

    /// <summary>
    /// Arms the vehicle.
    /// </summary>
    /// <param name="force">Bypass pre-arm checks.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> ArmAsync(bool force = false, CancellationToken ct = default) =>
        SendCommandAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            param1: 1f,
            // ArduPilot force-arm: 2989 (spec says 21196 for both arm and disarm).
            param2: force ? 2989f : 0f,
            ct: ct
        );

    /// <summary>
    /// Disarms the vehicle.
    /// </summary>
    /// <param name="force">Force disarm even in flight.</param>
    /// <param name="targetComponent">Target component. Defaults to autopilot.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> DisarmAsync(
        bool force = false,
        byte? targetComponent = null,
        CancellationToken ct = default
    ) =>
        SendCommandAsync(
            MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
            param1: 0f,
            param2: force ? 21196f : 0f,
            targetComponent: targetComponent,
            ct: ct
        );

    /// <summary>
    /// Sets the flight mode via COMMAND_LONG DO_SET_MODE.
    /// </summary>
    /// <param name="customMode">The autopilot-specific custom_mode number.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> SetModeAsync(uint customMode, CancellationToken ct = default) =>
        SendCommandAsync(
            MAVLink.MAV_CMD.DO_SET_MODE,
            param1: (float)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED,
            param2: customMode,
            ct: ct
        );

    /// <summary>
    /// Enables or disables the hardware safety switch via SET_MODE.
    /// </summary>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public Task SetSafetyAsync(bool on, CancellationToken ct = default)
    {
        var msg = new MAVLink.mavlink_set_mode_t
        {
            target_system = SysId,
            base_mode = (byte)MAVLink.MAV_MODE_FLAG_DECODE_POSITION.SAFETY,
            custom_mode = on ? 1u : 0u,
        };

        return SendAsync(MAVLink.MAVLINK_MSG_ID.SET_MODE, msg, ct);
    }

    /// <summary>
    /// Fetches a single parameter by name from the autopilot and updates the cache.
    /// </summary>
    /// <returns>The parameter value.</returns>
    /// <exception cref="Channels.ParameterException">The autopilot reported a PARAM_ERROR.</exception>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No response after all retry attempts.</exception>
    public async Task<double> FetchParameterAsync(string name, CancellationToken ct = default)
    {
        var channel = PrimaryChannel;

        var value = await channel.FetchParameterAsync(SysId, name, ct: ct).ConfigureAwait(false);
        var upperName = name.ToUpperInvariant();

        lock (_lock)
        {
            var existing = _parameters.GetValueOrDefault(upperName);
            _parameters[upperName] = new ParamEntry(value, existing.DefaultValue);
        }

        return value;
    }

    /// <summary>
    /// Sets a parameter by name and updates the cache with the confirmed value.
    /// </summary>
    /// <returns>The confirmed parameter value from the autopilot.</returns>
    /// <exception cref="Channels.ParameterException">The autopilot rejected the value.</exception>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No response after all retry attempts.</exception>
    public async Task<double> SetParameterAsync(
        string name,
        double value,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;

        var confirmed = await channel
            .SetParameterAsync(SysId, name, (float)value, ct: ct)
            .ConfigureAwait(false);
        var upperName = name.ToUpperInvariant();

        lock (_lock)
        {
            var existing = _parameters.GetValueOrDefault(upperName);
            _parameters[upperName] = new ParamEntry(confirmed, existing.DefaultValue);
        }

        return confirmed;
    }

    /// <summary>
    /// Downloads mission items, selecting FTP or standard protocol based on vehicle capabilities.
    /// </summary>
    public Task<IReadOnlyList<MissionItem>> DownloadMissionAsync(
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var hasFtp = CanonicalState.Capabilities.HasFlag(MAVLink.MAV_PROTOCOL_CAPABILITY.FTP);

        return hasFtp
            ? DownloadMissionViaFtpAsync(progress, ct)
            : DownloadMissionViaProtocolAsync(progress, ct);
    }

    /// <summary>
    /// Uploads mission items, selecting FTP or standard protocol based on vehicle capabilities.
    /// </summary>
    public Task UploadMissionAsync(
        IReadOnlyList<MissionItem> items,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var hasFtp = CanonicalState.Capabilities.HasFlag(MAVLink.MAV_PROTOCOL_CAPABILITY.FTP);

        return hasFtp
            ? UploadMissionViaFtpAsync(items, ct)
            : UploadMissionViaProtocolAsync(items, progress, ct);
    }

    /// <summary>
    /// Downloads mission items via the standard protocol.
    /// </summary>
    public Task<IReadOnlyList<MissionItem>> DownloadMissionViaProtocolAsync(
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    ) => DownloadItemsViaProtocolAsync(MAVLink.MAV_MISSION_TYPE.MISSION, progress, ct);

    /// <summary>
    /// Uploads mission items via the standard protocol.
    /// </summary>
    public Task<MAVLink.MAV_MISSION_RESULT> UploadMissionViaProtocolAsync(
        IReadOnlyList<MissionItem> items,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    ) => UploadItemsViaProtocolAsync(MAVLink.MAV_MISSION_TYPE.MISSION, items, progress, ct);

    /// <summary>
    /// Downloads the mission via FTP and populates the cache.
    /// </summary>
    public Task<IReadOnlyList<MissionItem>> DownloadMissionViaFtpAsync(
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    ) => DownloadItemsViaFtpAsync(MAVLink.MAV_MISSION_TYPE.MISSION, progress, ct);

    /// <summary>
    /// Uploads mission items to the vehicle via FTP.
    /// </summary>
    public Task UploadMissionViaFtpAsync(
        IReadOnlyList<MissionItem> items,
        CancellationToken ct = default
    ) => UploadItemsViaFtpAsync(MAVLink.MAV_MISSION_TYPE.MISSION, items, ct);

    /// <summary>
    /// Clears all mission items on the vehicle.
    /// </summary>
    public Task ClearMissionAsync(CancellationToken ct = default) =>
        ClearItemsAsync(MAVLink.MAV_MISSION_TYPE.MISSION, ct);

    /// <summary>
    /// Downloads items of the specified type via the standard protocol.
    /// </summary>
    public async Task<IReadOnlyList<MissionItem>> DownloadItemsViaProtocolAsync(
        MAVLink.MAV_MISSION_TYPE type,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;
        var all = await MissionDownload
            .DownloadAsync(channel, SysId, _logger, type, progress, ct)
            .ConfigureAwait(false);

        var items = type == MAVLink.MAV_MISSION_TYPE.MISSION ? SeparateHomeFromDownload(all) : all;
        SetItemCache(type, items);
        return items;
    }

    /// <summary>
    /// Uploads items of the specified type via the standard protocol.
    /// </summary>
    public async Task<MAVLink.MAV_MISSION_RESULT> UploadItemsViaProtocolAsync(
        MAVLink.MAV_MISSION_TYPE type,
        IReadOnlyList<MissionItem> items,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;
        var toSend = type == MAVLink.MAV_MISSION_TYPE.MISSION ? PrependHome(items) : items;
        var result = await MissionUpload
            .UploadAsync(channel, SysId, toSend, _logger, type, progress, ct)
            .ConfigureAwait(false);

        if (result == MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
            SetItemCache(type, items.ToList());

        return result;
    }

    /// <summary>
    /// Downloads items of the specified type via FTP.
    /// </summary>
    public async Task<IReadOnlyList<MissionItem>> DownloadItemsViaFtpAsync(
        MAVLink.MAV_MISSION_TYPE type,
        IProgress<MissionTransferProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;
        var client = new Channels.Ftp.FtpClient(channel, SysId, _logger);

        IProgress<(int Received, int Total)>? ftpProgress =
            progress != null ? new FtpMissionProgressAdapter(progress) : null;

        var path = FtpPathForType(type);
        var data = await client.DownloadFileAsync(path, ftpProgress, ct).ConfigureAwait(false);
        var all = ArduPilot.MissionDatCodec.Decode(data);
        var items = type == MAVLink.MAV_MISSION_TYPE.MISSION ? SeparateHomeFromDownload(all) : all;

        SetItemCache(type, items);
        _logger.LogInformation("Downloaded {Count} {Type} items via FTP", items.Count, type);
        return items;
    }

    /// <summary>
    /// Uploads items of the specified type via FTP.
    /// </summary>
    public async Task UploadItemsViaFtpAsync(
        MAVLink.MAV_MISSION_TYPE type,
        IReadOnlyList<MissionItem> items,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;
        var client = new Channels.Ftp.FtpClient(channel, SysId, _logger);

        var toSend = type == MAVLink.MAV_MISSION_TYPE.MISSION ? PrependHome(items) : items;
        var path = FtpPathForType(type);
        var data = ArduPilot.MissionDatCodec.Encode(toSend, type);
        await client.UploadFileAsync(path, data, ct).ConfigureAwait(false);

        SetItemCache(type, items.ToList());
        _logger.LogInformation("Uploaded {Count} {Type} items via FTP", items.Count, type);
    }

    /// <summary>
    /// Clears all items of the specified type on the vehicle.
    /// </summary>
    public async Task ClearItemsAsync(MAVLink.MAV_MISSION_TYPE type, CancellationToken ct = default)
    {
        var channel = PrimaryChannel;

        var ackTask = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_ACK && m.sysid == SysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_mission_ack_t>())
            .Where(a => a.mission_type == (byte)type)
            .Take(1)
            .Timeout(TimeSpan.FromSeconds(5))
            .ToTask(ct);

        await channel
            .SendAsync(
                MAVLink.MAVLINK_MSG_ID.MISSION_CLEAR_ALL,
                new MAVLink.mavlink_mission_clear_all_t
                {
                    target_system = SysId,
                    target_component = (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
                    mission_type = (byte)type,
                },
                ct
            )
            .ConfigureAwait(false);

        var ack = await ackTask.ConfigureAwait(false);
        if ((MAVLink.MAV_MISSION_RESULT)ack.type != MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
            throw new InvalidOperationException(
                $"{type} clear rejected: {(MAVLink.MAV_MISSION_RESULT)ack.type}"
            );

        SetItemCache(type, []);
    }

    /// <summary>
    /// Sets the current mission item on the vehicle.
    /// </summary>
    public Task<MAVLink.MAV_RESULT> SetCurrentMissionItemAsync(
        ushort seq,
        CancellationToken ct = default
    ) => SendCommandAsync(MAVLink.MAV_CMD.DO_SET_MISSION_CURRENT, param1: seq, ct: ct);

    /// <summary>
    /// Downloads all parameters, selecting FTP or standard protocol based on vehicle capabilities.
    /// </summary>
    /// <returns>The number of parameters downloaded.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public Task<int> DownloadParametersAsync(
        IProgress<ParamDownloadProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        // Clear before any full download so stale entries (e.g. params hidden
        // after disabling a feature) don't linger and poison the cache.
        lock (_lock)
        {
            _parameters.Clear();
        }

        var hasFtp = CanonicalState.Capabilities.HasFlag(MAVLink.MAV_PROTOCOL_CAPABILITY.FTP);

        return hasFtp
            ? DownloadParametersViaFtpAsync(progress, ct)
            : DownloadParametersViaStreamAsync(progress, ct);
    }

    /// <summary>
    /// Downloads all parameters via FTP (param.pck) and populates the cache.
    /// </summary>
    /// <returns>The number of parameters downloaded.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public async Task<int> DownloadParametersViaFtpAsync(
        IProgress<ParamDownloadProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;

        var download = ArduPilot.BulkParameterDownload.DownloadViaFtpAsync;
        var count = await download(channel, SysId, WriteParam, _logger, progress, ct)
            .ConfigureAwait(false);
        ReportedParameterCount = count;
        return count;

        void WriteParam(string name, ParamEntry entry)
        {
            lock (_lock)
            {
                _parameters[name] = entry;
            }
        }
    }

    /// <summary>
    /// Downloads all parameters via PARAM_REQUEST_LIST and populates the cache.
    /// </summary>
    /// <returns>The number of parameters downloaded.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public async Task<int> DownloadParametersViaStreamAsync(
        IProgress<ParamDownloadProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;

        // Vehicle's existing PARAM_VALUE subscription populates the cache.
        return await ParamListDownload
            .DownloadAsync(channel, SysId, _logger, progress, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a COMMAND_LONG to the autopilot via the primary channel and awaits the matching COMMAND_ACK.
    /// </summary>
    /// <param name="command">The MAVLink command to send.</param>
    /// <param name="timeout">ACK timeout. Defaults to 5 seconds.</param>
    /// <returns>The <see cref="MAVLink.MAV_RESULT"/> from the ACK.</returns>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    /// <exception cref="TimeoutException">No ACK within timeout.</exception>
    public Task<MAVLink.MAV_RESULT> SendCommandAsync(
        MAVLink.MAV_CMD command,
        float param1 = 0,
        float param2 = 0,
        float param3 = 0,
        float param4 = 0,
        float param5 = 0,
        float param6 = 0,
        float param7 = 0,
        byte? targetComponent = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;

        return channel.SendCommandAsync(
            SysId,
            targetComponent ?? (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1,
            command,
            param1,
            param2,
            param3,
            param4,
            param5,
            param6,
            param7,
            timeout,
            ct
        );
    }

    /// <summary>
    /// Sends a typed MAVLink message via the primary channel.
    /// </summary>
    /// <exception cref="InvalidOperationException">No channel available.</exception>
    public Task SendAsync(
        MAVLink.MAVLINK_MSG_ID messageType,
        object data,
        CancellationToken ct = default
    )
    {
        var channel = PrimaryChannel;

        return channel.SendAsync(messageType, data, ct);
    }

    internal void AddChannel(MavChannel channel)
    {
        lock (_lock)
        {
            if (!_channels.Add(channel))
                return;
        }

        var paramSub = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE && m.sysid == SysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_param_value_t>())
            .Subscribe(pv =>
            {
                var name = MavChannel.DecodeParamId(pv.param_id);
                lock (_lock)
                {
                    var existing = _parameters.GetValueOrDefault(name);
                    _parameters[name] = new ParamEntry(pv.param_value, existing.DefaultValue);
                    if (pv.param_count != ushort.MaxValue)
                        ReportedParameterCount = pv.param_count;
                }
            });

        var missionSub = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.MISSION_CURRENT && m.sysid == SysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_mission_current_t>())
            .Subscribe(mc =>
            {
                CurrentMissionSeq = mc.seq;
                if (mc.mission_state != 0)
                    MissionState = (MAVLink.MISSION_STATE)mc.mission_state;
            });

        var homeSub = channel
            .Messages.Where(m =>
                m.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HOME_POSITION && m.sysid == SysId
            )
            .Select(m => m.ToStructure<MAVLink.mavlink_home_position_t>())
            .Subscribe(hp =>
            {
                HomeLatitude = hp.latitude / 1e7;
                HomeLongitude = hp.longitude / 1e7;
                HomeAltitude = hp.altitude / 1000f;
            });

        lock (_lock)
        {
            _paramSubs[channel] = paramSub;
            _missionSubs[channel] = missionSub;
            _homeSubs[channel] = homeSub;
        }

        var vehicleMessages = channel.Messages.Where(m => m.sysid == SysId);
        RateController.AddChannel(channel.SendAsync, vehicleMessages);

        if (ParameterMetadata is null && _metadataFetcher is not null)
            _ = ResolveMetadataAsync();
    }

    private async Task ResolveMetadataAsync()
    {
        try
        {
            var state = CanonicalState;

            var family = ArduPilot.FirmwareFamilyMap.FromMavType(state.Type);
            if (family is null)
                return;

            var version = state.FirmwareVersion;
            if (version == 0)
                return;

            var major = (int)(version >> 24);
            var minor = (int)((version >> 16) & 0xFF);

            var metadata = await _metadataFetcher!
                .EnsureAsync(family.Value, major, minor)
                .ConfigureAwait(false);
            if (metadata is not null)
                ParameterMetadata = metadata;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parameter metadata resolution failed");
        }
    }

    internal void RemoveChannel(MavChannel channel)
    {
        IDisposable? paramSub;
        IDisposable? missionSub;
        IDisposable? homeSub;
        lock (_lock)
        {
            _channels.Remove(channel);
            _paramSubs.Remove(channel, out paramSub);
            _missionSubs.Remove(channel, out missionSub);
            _homeSubs.Remove(channel, out homeSub);
        }

        paramSub?.Dispose();
        missionSub?.Dispose();
        homeSub?.Dispose();
        RateController.RemoveChannel(channel.SendAsync);
    }

    /// <summary>
    /// Computes a deterministic UID from AUTOPILOT_VERSION hardware identifiers.
    /// </summary>
    /// <remarks>
    /// Prefers uid2 (18-byte hardware serial); falls back to uid (uint64) if
    /// uid2 is all zeros. Returns <see langword="null"/> when both are zero,
    /// indicating the autopilot reported no usable hardware identity.
    /// </remarks>
    public static ulong? ComputeUid(ulong uid, byte[] uid2, byte sysId)
    {
        bool hasUid2 = false;
        for (int i = 0; i < uid2.Length; i++)
        {
            if (uid2[i] != 0)
            {
                hasUid2 = true;
                break;
            }
        }

        if (hasUid2)
        {
            Span<byte> input = stackalloc byte[uid2.Length + 1];
            uid2.CopyTo(input);
            input[uid2.Length] = sysId;
            return HashToUlong(input);
        }

        if (uid != 0)
        {
            Span<byte> input = stackalloc byte[9];
            BinaryPrimitives.WriteUInt64LittleEndian(input, uid);
            input[8] = sysId;
            return HashToUlong(input);
        }

        return null;
    }

    private static ulong HashToUlong(ReadOnlySpan<byte> input)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    /// <summary>
    /// Separates the first item (home) from downloaded mission items
    /// and updates the Vehicle home fields.
    /// </summary>
    private List<MissionItem> SeparateHomeFromDownload(List<MissionItem> all)
    {
        if (all.Count > 0)
        {
            var home = all[0];
            HomeLatitude = home.Latitude;
            HomeLongitude = home.Longitude;
            HomeAltitude = home.Altitude;
            return all.GetRange(1, all.Count - 1);
        }

        return all;
    }

    /// <summary>
    /// Prepends the current home position as seq 0 for upload.
    /// </summary>
    private List<MissionItem> PrependHome(IReadOnlyList<MissionItem> items)
    {
        var home = new MissionItem(
            MAVLink.MAV_FRAME.GLOBAL,
            MAVLink.MAV_CMD.WAYPOINT,
            0,
            0,
            0,
            0,
            (int)(HomeLatitude * 1e7),
            (int)(HomeLongitude * 1e7),
            HomeAltitude,
            1,
            1
        );

        var result = new List<MissionItem>(items.Count + 1) { home };
        result.AddRange(items);
        return result;
    }

    private void SetItemCache(MAVLink.MAV_MISSION_TYPE type, List<MissionItem> items)
    {
        lock (_lock)
        {
            switch (type)
            {
                case MAVLink.MAV_MISSION_TYPE.MISSION:
                    _mission = items;
                    break;
                case MAVLink.MAV_MISSION_TYPE.FENCE:
                    _fence = items;
                    break;
                case MAVLink.MAV_MISSION_TYPE.RALLY:
                    _rally = items;
                    break;
            }
        }
    }

    private static string FtpPathForType(MAVLink.MAV_MISSION_TYPE type) =>
        type switch
        {
            MAVLink.MAV_MISSION_TYPE.MISSION => "@MISSION/mission.dat",
            MAVLink.MAV_MISSION_TYPE.FENCE => "@MISSION/fence.dat",
            MAVLink.MAV_MISSION_TYPE.RALLY => "@MISSION/rally.dat",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    private sealed class FtpMissionProgressAdapter(IProgress<MissionTransferProgress> inner)
        : IProgress<(int Received, int Total)>
    {
        public void Report((int Received, int Total) value) =>
            inner.Report(new MissionTransferProgress(value.Received, value.Total));
    }
}
