// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Groundwork.Core.Connections;

/// <summary>
/// UDP listen (server) connection. Binds to a local port, receives datagrams,
/// and learns the remote endpoint from the first incoming datagram for replies.
/// </summary>
public sealed class UdpListenConnection : IConnection
{
    private readonly int _port;
    private readonly ILogger<UdpListenConnection> _logger;
    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _cts = new();

    private UdpClient? _client;
    private IPEndPoint? _remoteEndPoint;
    private Task? _receiveLoop;

    public UdpListenConnection(int port, ILogger<UdpListenConnection> logger)
    {
        _port = port;
        _logger = logger;
    }

    public string Name => $"UDP:*:{_port}";

    public Stream BaseStream { get; private set; } = null!;

    public Task OpenAsync(CancellationToken ct = default)
    {
        _client = new UdpClient(_port);
        BaseStream = _pipe.Reader.AsStream();
        _logger.LogInformation("Listening on UDP port {Port}", _port);
        _receiveLoop = RunReceiveLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        _cts.Cancel();
        _client?.Close();

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
        _logger.LogInformation("{Name} closed", Name);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_client is null)
            throw new InvalidOperationException("Connection is not open.");

        if (_remoteEndPoint is null)
            throw new InvalidOperationException("No remote endpoint -- no data received yet.");

        await _client.SendAsync(data, _remoteEndPoint, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _client!.ReceiveAsync(ct).ConfigureAwait(false);

                _remoteEndPoint = result.RemoteEndPoint;
                var memory = _pipe.Writer.GetMemory(result.Buffer.Length);
                result.Buffer.CopyTo(memory);
                _pipe.Writer.Advance(result.Buffer.Length);
                var flushResult = await _pipe.Writer.FlushAsync(ct).ConfigureAwait(false);
                if (flushResult.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean shutdown.
        }
        catch (SocketException ex) when (ct.IsCancellationRequested)
        {
            // UdpClient.Close() can throw SocketException on the pending ReceiveAsync.
            _logger.LogDebug(ex, "Socket closed during receive");
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            // UdpClient disposed while ReceiveAsync was pending.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receive loop error on {Name}", Name);
            await _pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
        }
    }
}
